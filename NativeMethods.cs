using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CmjLimit
{
    internal static class NativeMethods
    {
        private const int EWX_LOGOFF = 0x00000000;
        private const int EWX_FORCE = 0x00000004;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ExitWindowsEx(int uFlags, int dwReason);

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        /// <summary>把窗口重新钉在“永远置顶”层，防止被其它置顶窗口盖住。</summary>
        public static void SetTopMost(IntPtr hWnd)
        {
            try
            {
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch
            {
                // 忽略
            }
        }

        /// <summary>注销当前登录用户。</summary>
        public static void Logoff()
        {
            try
            {
                if (ExitWindowsEx(EWX_LOGOFF, 0) != 0)
                    return;
            }
            catch
            {
                // 继续尝试备用方式
            }

            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "/l /f")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch
            {
                // 无法注销时忽略
            }
        }
    }
}
