using System;
using System.IO;

namespace CmjLimit
{
    internal static class AppPaths
    {
        public static readonly string Dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cmjLimit");

        public static readonly string ConfigFile = System.IO.Path.Combine(Dir, "config.json");
        public static readonly string UsageFile = System.IO.Path.Combine(Dir, "usage.json");
    }

    /// <summary>
    /// 从 config.ini 的 [sys] 段读取显示与逻辑配置，全部带默认值。
    /// 用户额度在 LimitFile 的 [users] 段。
    /// </summary>
    internal sealed class AppConfig
    {
        // [users] 里没有的账户时，退回的默认账户名
        public string UserName { get; private set; } = "cmj";

        // 退回默认账户时的每日分钟数
        public int DailyLimitMinutes { get; private set; } = 40;

        // 剩余多少分钟时弹窗提醒
        public int WarnMinutes { get; private set; } = 5;

        // 当天用完后再次登录的宽限分钟数
        public int ReloginGraceMinutes { get; private set; } = 7;

        // 悬浮窗整体不透明度
        public double Opacity { get; private set; } = 0.82;

        // 悬浮窗字号（磅）
        public double HudFontSize { get; private set; } = 13;

        // 悬浮窗位置
        public string Position { get; private set; } = "TopRight";

        // 锁屏时暂停计时
        public bool PauseWhenLocked { get; private set; } = true;

        public int LimitSeconds => Math.Max(1, DailyLimitMinutes) * 60;
        public int WarnSeconds => Math.Max(0, WarnMinutes) * 60;
        public int GraceSeconds => Math.Max(0, ReloginGraceMinutes) * 60;

        public static AppConfig FromIni(LimitFile ini)
        {
            var sys = ini.Sys;
            var cfg = new AppConfig();

            cfg.UserName = GetString(sys, "defaultuser", cfg.UserName);

            var daily = GetMinutes(sys, "daily", cfg.DailyLimitMinutes);
            if (daily > 0) cfg.DailyLimitMinutes = daily;

            cfg.WarnMinutes = GetMinutes(sys, "warn", cfg.WarnMinutes);
            cfg.ReloginGraceMinutes = GetMinutes(sys, "grace", cfg.ReloginGraceMinutes);
            cfg.Opacity = GetDouble(sys, "opacity", cfg.Opacity);
            cfg.HudFontSize = GetDouble(sys, "hudfont", cfg.HudFontSize);
            cfg.Position = GetString(sys, "position", cfg.Position);
            cfg.PauseWhenLocked = GetBool(sys, "pauselocked", cfg.PauseWhenLocked);

            cfg.Normalize();
            return cfg;
        }

        private static string GetString(System.Collections.Generic.IReadOnlyDictionary<string, string> sys, string key, string def)
        {
            if (sys.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
            return def;
        }

        private static int GetMinutes(System.Collections.Generic.IReadOnlyDictionary<string, string> sys, string key, int def)
        {
            if (sys.TryGetValue(key, out var v))
            {
                var m = LimitFile.ParseMinutes(v);
                if (m > 0)
                    return m;
            }
            return def;
        }

        private static double GetDouble(System.Collections.Generic.IReadOnlyDictionary<string, string> sys, string key, double def)
        {
            if (sys.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d))
            {
                return d;
            }
            return def;
        }

        private static bool GetBool(System.Collections.Generic.IReadOnlyDictionary<string, string> sys, string key, bool def)
        {
            if (sys.TryGetValue(key, out var v))
            {
                var s = v.Trim().ToLowerInvariant();
                if (s == "true" || s == "1" || s == "yes" || s == "on") return true;
                if (s == "false" || s == "0" || s == "no" || s == "off") return false;
            }
            return def;
        }

        private void Normalize()
        {
            if (string.IsNullOrWhiteSpace(UserName)) UserName = "cmj";
            if (DailyLimitMinutes <= 0) DailyLimitMinutes = 40;
            if (WarnMinutes < 0) WarnMinutes = 5;
            if (ReloginGraceMinutes < 0) ReloginGraceMinutes = 7;
            if (Opacity < 0.2) Opacity = 0.2;
            if (Opacity > 1) Opacity = 1;
            if (HudFontSize < 8) HudFontSize = 8;
            if (HudFontSize > 28) HudFontSize = 28;
            if (string.IsNullOrWhiteSpace(Position)) Position = "TopRight";
        }
    }
}
