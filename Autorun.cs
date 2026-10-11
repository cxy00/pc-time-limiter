using System;
using System.IO;
using Microsoft.Win32;

namespace CmjLimit
{
    /// <summary>把本程序注册到当前用户的“启动”里（HKCU Run），实现登录自启动。</summary>
    internal static class Autorun
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "cmjLimit";

        private static string CommandLine(string arguments)
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                path = System.Reflection.Assembly.GetEntryAssembly().Location;
            var cmd = "\"" + path + "\"";
            if (!string.IsNullOrWhiteSpace(arguments))
                cmd += " " + arguments.Trim();
            return cmd;
        }

        public static bool IsRegistered()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    var value = key != null ? key.GetValue(ValueName) as string : null;
                    return !string.IsNullOrEmpty(value);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>注册自启动；已注册且与当前路径一致时不重复写入。</summary>
        public static bool Register(string arguments = null)
        {
            try
            {
                var cmd = CommandLine(arguments);
                if (IsRegistered())
                {
                    using (var exist = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    {
                        var value = exist != null ? exist.GetValue(ValueName) as string : null;
                        if (string.Equals(value, cmd, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ??
                                 Registry.CurrentUser.CreateSubKey(RunKeyPath, true))
                {
                    key.SetValue(ValueName, cmd, RegistryValueKind.String);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void Unregister()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null && key.GetValue(ValueName) != null)
                        key.DeleteValue(ValueName, false);
                }
            }
            catch
            {
                // 忽略
            }
        }
    }
}
