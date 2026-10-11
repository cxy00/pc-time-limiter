using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CmjLimit
{
    /// <summary>
    /// 统一的 ini 配置文件（默认 %LOCALAPPDATA%\cmjLimit\config.ini，或用 C:\ProgramData\cmjLimit\config.ini 集中管理）。
    /// 分区：
    ///   [users]  每行一个账户：用户名=每天可用时长，如 cmj=1h / lyy=1h30m
    ///   [sys]    显示与其它逻辑配置（warn / grace / position / opacity / hudfont / pauselocked 等）
    /// 以 ; 或 # 开头为注释，空行忽略。
    /// </summary>
    internal sealed class LimitFile
    {
        private static readonly string GlobalPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "cmjLimit", "cmjLimit.ini");

        private static readonly string UserPath = System.IO.Path.Combine(AppPaths.Dir, "cmjLimit.ini");

        private const string DefaultContent =
            "; cmjLimit 配置文件\r\n" +
            "[users]\r\n" +
            "; 每行一个账户：用户名=每天允许使用的时长\r\n" +
            "; 时长写法：1h / 1h30m / 90m / 1.5h / 1:30 / 90（纯数字=分钟）\r\n" +
            "cmj=1h\r\n" +
            "lyy=1h30m\r\n" +
            "\r\n" +
            "[sys]\r\n" +
            "; 提前多少分钟弹窗提醒\r\n" +
            "warn=5m\r\n" +
            "; 当天用完后再次登录的宽限时长\r\n" +
            "grace=5m\r\n" +
            "; 悬浮窗位置：TopRight / TopLeft / BottomRight / BottomLeft\r\n" +
            "position=TopRight\r\n" +
            "; 整体不透明度 0~1（越小越透）\r\n" +
            "opacity=0.82\r\n" +
            "; 悬浮窗字号（磅）\r\n" +
            "hudfont=13\r\n" +
            "; 锁屏时是否暂停计时\r\n" +
            "pauselocked=true\r\n";

        private readonly Dictionary<string, int> _users = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _sys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string FilePath { get; private set; }
        public DateTime StampUtc { get; private set; }

        public IReadOnlyDictionary<string, int> Users => _users;
        public IReadOnlyDictionary<string, string> Sys => _sys;

        public static LimitFile Load()
        {
            var file = new LimitFile();
            file.Reload();
            return file;
        }

        private void Reload()
        {
            var path = File.Exists(GlobalPath) ? GlobalPath : UserPath;

            if (!File.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    File.WriteAllText(path, DefaultContent, Encoding.UTF8);
                }
                catch
                {
                    // 创建失败就用内存里的默认配置
                    _users["cmj"] = 60;
                    FilePath = path;
                    StampUtc = DateTime.MinValue;
                    return;
                }
            }

            FilePath = path;
            try { StampUtc = File.GetLastWriteTimeUtc(path); }
            catch { StampUtc = DateTime.MinValue; }

            _users.Clear();
            _sys.Clear();

            string section = "";
            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                        continue;

                    if (line[0] == '[' && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                        continue;
                    }

                    int idx = line.IndexOf('=');
                    if (idx <= 0)
                        continue;

                    var key = line.Substring(0, idx).Trim();
                    var value = line.Substring(idx + 1).Trim();
                    if (key.Length == 0)
                        continue;

                    if (string.Equals(section, "users", StringComparison.OrdinalIgnoreCase))
                    {
                        int minutes = ParseMinutes(value);
                        if (minutes > 0)
                            _users[key] = minutes;
                    }
                    else if (string.Equals(section, "sys", StringComparison.OrdinalIgnoreCase))
                    {
                        _sys[key] = value;
                    }
                }
            }
            catch
            {
                // 解析失败时保留已解析的部分
            }

            if (_users.Count == 0)
                _users["cmj"] = 60;
        }

        /// <summary>查询当前账户每天的额度（分钟）；未配置返回 null。</summary>
        public int? For(string user)
        {
            if (string.IsNullOrWhiteSpace(user))
                return null;

            if (_users.TryGetValue(user.Trim(), out int minutes))
                return minutes;

            int slash = user.IndexOf('\\');
            if (slash >= 0 && slash < user.Length - 1)
            {
                if (_users.TryGetValue(user.Substring(slash + 1), out minutes))
                    return minutes;
            }

            return null;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var kv in _users)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('m');
            }
            return sb.ToString();
        }

        /// <summary>把 1h / 1h30m / 90m / 1.5h / 1:30 / 90 解析为分钟。</summary>
        public static int ParseMinutes(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return -1;

            var s = raw.Trim().ToLowerInvariant();

            if (s.Contains(":"))
            {
                var parts = s.Split(':');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double hh) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double mm))
                {
                    return Clamp(hh * 60 + mm);
                }
            }

            double total = 0;
            bool matched = false;

            var h = Regex.Match(s, @"(\d+(?:\.\d+)?)\s*h");
            if (h.Success)
            {
                total += double.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture) * 60;
                matched = true;
            }

            var m = Regex.Match(s, @"(\d+(?:\.\d+)?)\s*m\b");
            if (m.Success)
            {
                total += double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                matched = true;
            }

            if (!matched)
            {
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double plain))
                    total = plain;
                else
                    return -1;
            }

            return Clamp(total);
        }

        private static int Clamp(double minutes)
        {
            var v = (int)Math.Round(minutes);
            if (v < 1) v = 1;
            if (v > 1440) v = 1440;
            return v;
        }
    }
}
