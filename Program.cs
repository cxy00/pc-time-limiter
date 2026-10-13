using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace CmjLimit
{
    /// <summary>
    /// cmjLimit 主程序：无窗口、无托盘的后台监控，双进程互相守护。
    /// 角色一 Monitor：计时、倒计时显示、到点注销。
    /// 角色二 Guard  ：守护进程，Monitor 被结束就立刻拉起；Monitor 也会拉起 Guard。
    /// </summary>
    internal static class Program
    {
        private const string MonitorMutexName = @"Local\cmjLimit_Monitor";
        private const string GuardMutexName = @"Local\cmjLimit_Guard";

        private const string ArgMonitor = "--monitor";
        private const string ArgGuard = "--guard";

        private static readonly string LogFile = Path.Combine(AppPaths.Dir, "cmjLimit.log");
        private static readonly string StopFile = Path.Combine(AppPaths.Dir, "stop.flag");

        private static LimitFile _ini;
        private static DateTime _iniStamp;
        private static AppConfig _config;
        private static UsageState _state;
        private static int _limitSeconds = 3600;
        private static bool _monitored;
        private static readonly DateTime StartTimeUtc = DateTime.UtcNow;

        private static Timer _tickTimer;
        private static Timer _saveTimer;
        private static Timer _watchTimer;
        private static Timer _logoffTimer;

        private static HudForm _hud;
        private static NoticeForm _warnNotice;
        private static NoticeForm _graceNotice;

        private static bool _locked;
        private static bool _warned;
        private static bool _inGrace;
        private static DateTime _graceEnd = DateTime.MaxValue;
        private static bool _logoffStarted;
        private static bool _stopping;

        private static bool _isMonitor;
        private static string _peerMutexName;
        private static DateTime _lastPeerLaunch = DateTime.MinValue;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log("UI 异常: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log("未处理异常: " + e.ExceptionObject);

            try { Directory.CreateDirectory(AppPaths.Dir); } catch { }

            if (args != null && args.Length > 0)
            {
                switch (args[0].Trim().ToLowerInvariant())
                {
                    case "/stop":
                    case "-stop":
                    case "--stop":
                        RequestStop();
                        return;
                    case "/install":
                    case "-install":
                    case "--install":
                        Log("手动注册自启动: " + Autorun.Register(ArgMonitor));
                        return;
                    case "/uninstall":
                    case "-uninstall":
                    case "--uninstall":
                        Autorun.Unregister();
                        Log("已取消自启动。");
                        return;
                    case "/reset":
                    case "-reset":
                    case "--reset":
                        UsageStore.Save(new UsageState { Date = UsageStore.TodayKey(), UsedSeconds = 0 });
                        Log("已清零今日使用时间。");
                        return;
                }
            }

            _isMonitor = !HasArg(args, ArgGuard);   // 默认是监控进程
            _peerMutexName = _isMonitor ? GuardMutexName : MonitorMutexName;

            LoadIni();

            // ini 里没有这个账户（且也不是默认账户）就不监控
            if (!_monitored)
            {
                Log($"账户 {Environment.UserName} 不在监控名单内（{_ini.FilePath}），退出。");
                return;
            }

            using (var roleMutex = new Mutex(true, _isMonitor ? MonitorMutexName : GuardMutexName, out bool createdNew))
            {
                if (!createdNew)
                {
                    Log($"{RoleName()} 已有实例在运行，本次退出。");
                    return;
                }

                // 随 cmj 账户登录自启动（由监控进程负责注册）
                if (_isMonitor)
                    Autorun.Register(ArgMonitor);

                if (_isMonitor)
                {
                    _state = UsageStore.Load();
                    EnsureToday();
                    Log($"启动监控：配置={_ini.FilePath}，账户={Environment.UserName}，" +
                        $"每日上限={_limitSeconds / 60} 分钟，今日已用={_state.UsedSeconds / 60} 分钟。");

                    // 再次登录（当日已用过时间）：保证本次剩余可用时间不低于 ReloginGraceMinutes
                    if (_state.UsedSeconds > 0)
                    {
                        int remaining = _limitSeconds - _state.UsedSeconds;
                        if (remaining < _config.GraceSeconds)
                            _limitSeconds = _state.UsedSeconds + _config.GraceSeconds;
                    }
                }
                else
                {
                    Log("守护进程启动。");
                }

                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.SessionEnding += (s, e) => SaveState();
                SystemEvents.SessionEnded += (s, e) => SaveState();
                Application.ApplicationExit += (s, e) => SaveState();

                if (_isMonitor)
                {
                    _hud = new HudForm(_config.Position, _config.Opacity, _config.HudFontSize);
                    _hud.Show();
                    UpdateHud();

                    _tickTimer = new Timer { Interval = 1000 };
                    _tickTimer.Tick += OnTick;
                    _tickTimer.Start();

                    _saveTimer = new Timer { Interval = 5000 };
                    _saveTimer.Tick += OnSaveTick;
                    _saveTimer.Start();
                }

                // 互相守护：每 2 秒确认对方还在，不在就拉起
                _watchTimer = new Timer { Interval = 2000 };
                _watchTimer.Tick += OnWatchTick;
                _watchTimer.Start();
                OnWatchTick(null, EventArgs.Empty);

                Application.Run();

                Cleanup();
                Log(RoleName() + " 已停止。");
            }
        }

        private static string RoleName()
        {
            return _isMonitor ? "监控进程" : "守护进程";
        }

        private static bool HasArg(string[] args, string value)
        {
            if (args == null)
                return false;
            foreach (var a in args)
            {
                if (string.Equals(a, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ---------- 计时 ----------

        private static void OnTick(object sender, EventArgs e)
        {
            try
            {
                EnsureToday();

                if (_inGrace)
                {
                    var left = _graceEnd - DateTime.Now;
                    if (left.TotalSeconds <= 0)
                    {
                        DoLogoff("宽限时间已用完，即将注销。");
                        return;
                    }
                    UpdateGraceNotice(left);
                }
                else
                {
                    // 锁屏期间不计时
                    if (_locked && _config.PauseWhenLocked)
                    {
                        UpdateHud();
                        return;
                    }

                    _state.UsedSeconds++;

                    int remain = _limitSeconds - _state.UsedSeconds;

                    if (!_warned && _config.WarnSeconds > 0 && remain > 0 && remain <= _config.WarnSeconds)
                    {
                        _warned = true;
                        ShowWarn(remain);
                    }

                    if (remain <= 0)
                    {
                        DoLogoff("今日使用时间已用完，即将注销。");
                        return;
                    }
                }

                UpdateHud();
            }
            catch (Exception ex)
            {
                Log("计时异常: " + ex.Message);
            }
        }

        private static void OnSaveTick(object sender, EventArgs e)
        {
            try
            {
                SaveState();
                ReloadConfigIfChanged();
            }
            catch (Exception ex)
            {
                Log("保存异常: " + ex.Message);
            }
        }

        private static void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                _locked = true;
                SaveState();
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                _locked = false;
            }
            else if (e.Reason == SessionSwitchReason.SessionLogoff ||
                     e.Reason == SessionSwitchReason.SessionLogon)
            {
                SaveState();
            }
        }

        // ---------- 守护：互相拉起 ----------

        private static void OnWatchTick(object sender, EventArgs e)
        {
            try
            {
                if (_stopping)
                    return;

                if (StopRequested())
                {
                    Log("收到停止标记，退出。");
                    _stopping = true;
                    Application.Exit();
                    return;
                }

                if (!PeerAlive())
                    LaunchPeer();
            }
            catch (Exception ex)
            {
                Log("守护异常: " + ex.Message);
            }
        }

        private static bool PeerAlive()
        {
            try
            {
                using (Mutex.OpenExisting(_peerMutexName))
                {
                    return true;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void LaunchPeer()
        {
            if ((DateTime.Now - _lastPeerLaunch).TotalSeconds < 3)
                return;
            _lastPeerLaunch = DateTime.Now;

            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                    exe = System.Reflection.Assembly.GetEntryAssembly().Location;

                var peerArgs = _isMonitor ? ArgGuard : ArgMonitor;
                Process.Start(new ProcessStartInfo(exe, peerArgs)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                Log(_isMonitor ? "监控进程已拉起守护进程。" : "守护进程已拉起监控进程。");
            }
            catch (Exception ex)
            {
                Log("拉起对方失败: " + ex.Message);
            }
        }

        private static void RequestStop()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Dir);
                File.WriteAllText(StopFile, DateTime.UtcNow.ToString("o"));
                File.SetLastWriteTimeUtc(StopFile, DateTime.UtcNow);
                Log("已写入停止标记，两个进程将在数秒内退出。");
            }
            catch (Exception ex)
            {
                Log("写入停止标记失败: " + ex.Message);
            }
        }

        private static bool StopRequested()
        {
            try
            {
                return File.Exists(StopFile) && File.GetLastWriteTimeUtc(StopFile) > StartTimeUtc;
            }
            catch
            {
                return false;
            }
        }

        // ---------- 额度 / 宽限期 ----------

        private static void EnsureToday()
        {
            var today = UsageStore.TodayKey();
            if (_state != null && _state.Date == today)
                return;

            _state = new UsageState { Date = today, UsedSeconds = 0 };
            _warned = false;
            _inGrace = false;
            _graceEnd = DateTime.MaxValue;
            CloseGraceNotice();
            SaveState();
            Log("新的一天，使用时间已清零。");
        }

        private static void StartGrace()
        {
            _inGrace = true;
            _graceEnd = DateTime.Now.AddSeconds(_config.GraceSeconds);
            Log($"今日额度已用完，进入 {_config.ReloginGraceMinutes} 分钟宽限期。");
            ShowGraceNotice(_graceEnd - DateTime.Now);
        }

        private static void DoLogoff(string message)
        {
            if (_logoffStarted)
                return;
            _logoffStarted = true;

            _inGrace = false;
            _state.UsedSeconds = Math.Max(_state.UsedSeconds, _limitSeconds);
            SaveState();
            Log(message);

            CloseGraceNotice();
            ShowFinalNotice(message);
            UpdateHud();

            // 留出几秒让用户看到提示，然后注销
            _logoffTimer = new Timer { Interval = 4000 };
            _logoffTimer.Tick += (s, e2) =>
            {
                _logoffTimer.Stop();
                NativeMethods.Logoff();
            };
            _logoffTimer.Start();
        }

        // ---------- 倒计时显示 ----------

        private static void UpdateHud()
        {
            if (_hud == null || !_hud.IsHandleCreated || _hud.IsDisposed)
                return;

            if (_logoffStarted)
            {
                _hud.SetText("时间到，即将注销", Color.Firebrick);
                return;
            }

            if (_inGrace)
            {
                var left = _graceEnd - DateTime.Now;
                int total = (int)Math.Max(0, Math.Ceiling(left.TotalSeconds));
                _hud.SetText($"宽限 {total / 60:00}:{total % 60:00}", Color.Firebrick);
                return;
            }

            int remain = Math.Max(0, _limitSeconds - (_state != null ? _state.UsedSeconds : 0));
            bool warn = _config.WarnSeconds > 0 && remain <= _config.WarnSeconds;
            _hud.SetText(
                $"剩余 {remain / 60:00}:{remain % 60:00}",
                warn ? Color.OrangeRed : Color.FromArgb(24, 24, 30));

            if (_locked && _config.PauseWhenLocked)
                _hud.SetText($"剩余 {remain / 60:00}:{remain % 60:00}（已暂停）", Color.DimGray);
        }

        private static void ShowWarn(int remainSeconds)
        {
            int min = (remainSeconds + 59) / 60;
            _warnNotice = new NoticeForm(
                $"今天还能用 {min} 分钟，时间到会自动注销。\r\n" +
                $"{min} minutes left before logging out.",
                Color.OrangeRed, 15, width: 400, height: 140, opacity: 0.5);
            _warnNotice.Show();
        }

        private static void ShowGraceNotice(TimeSpan left)
        {
            if (_graceNotice == null)
            {
                _graceNotice = new NoticeForm("", Color.Firebrick, 0) { ClosableByUser = false };
                _graceNotice.Show();
            }
            UpdateGraceNotice(left);
        }

        private static void UpdateGraceNotice(TimeSpan left)
        {
            if (_graceNotice == null || !_graceNotice.IsHandleCreated || _graceNotice.IsDisposed)
                return;

            int total = (int)Math.Max(0, Math.Ceiling(left.TotalSeconds));
            _graceNotice.SetMessage(
                $"今天的 {_limitSeconds / 60} 分钟已经用完\r\n" +
                $"本次登录宽限剩余 {total / 60:00}:{total % 60:00}\r\n" +
                "时间到将自动注销");
        }

        private static void ShowFinalNotice(string message)
        {
            var form = new NoticeForm(message, Color.Firebrick, 0) { ClosableByUser = false };
            form.Show();
        }

        private static void CloseGraceNotice()
        {
            if (_graceNotice == null)
                return;
            try
            {
                if (!_graceNotice.IsDisposed)
                {
                    _graceNotice.ClosableByUser = true;
                    _graceNotice.Close();
                }
            }
            catch { }
            _graceNotice = null;
        }

        // ---------- 数据 / 退出 ----------

        private static void Cleanup()
        {
            SaveState();

            SystemEvents.SessionSwitch -= OnSessionSwitch;

            if (_tickTimer != null) { _tickTimer.Stop(); _tickTimer.Dispose(); _tickTimer = null; }
            if (_saveTimer != null) { _saveTimer.Stop(); _saveTimer.Dispose(); _saveTimer = null; }
            if (_watchTimer != null) { _watchTimer.Stop(); _watchTimer.Dispose(); _watchTimer = null; }
            if (_logoffTimer != null) { _logoffTimer.Stop(); _logoffTimer.Dispose(); _logoffTimer = null; }

            CloseGraceNotice();
            if (_hud != null)
            {
                try { _hud.Close(); } catch { }
                _hud = null;
            }
        }

        private static void SaveState()
        {
            if (_state == null)
                return;
            try { UsageStore.Save(_state); }
            catch { }
        }

        private static void ReloadConfigIfChanged()
        {
            if (_ini == null || File.GetLastWriteTimeUtc(_ini.FilePath) == _iniStamp)
                return;

            LoadIni();
            Log("cmjLimit.ini 已更新并重新加载：" + _ini);
            _warned = false;

            if (_hud != null)
                _hud.ApplyStyle(_config.Opacity, _config.HudFontSize);

            EnsureToday();

            // 被移出监控名单就停止监控
            if (!_monitored)
            {
                Log("当前账户已不在监控名单内，停止监控。");
                _stopping = true;
                Application.Exit();
                return;
            }

            // 额度调小后可能已经超时
            if (!_inGrace && _state.UsedSeconds >= _limitSeconds)
                StartGrace();
        }

        /// <summary>读取 ini（[users] 与 [sys]），确定当前账户每天的额度与显示配置。</summary>
        private static void LoadIni()
        {
            _ini = LimitFile.Load();
            _iniStamp = _ini.StampUtc;
            _config = AppConfig.FromIni(_ini);

            var user = Environment.UserName;
            var minutes = _ini.For(user);

            if (minutes.HasValue)
            {
                _monitored = true;
                _limitSeconds = minutes.Value * 60;
            }
            else if (string.Equals(user, _config.UserName, StringComparison.OrdinalIgnoreCase))
            {
                // ini 里没有时，退回默认账户与额度
                _monitored = true;
                _limitSeconds = _config.LimitSeconds;
            }
            else
            {
                _monitored = false;
                _limitSeconds = _config.LimitSeconds;
            }
        }

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Dir);
                File.AppendAllText(LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{RoleName()}]  {message}{Environment.NewLine}");
            }
            catch
            {
                // 忽略日志写入失败
            }
        }
    }
}
