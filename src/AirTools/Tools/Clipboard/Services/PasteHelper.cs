using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AirTools.Tools.Clipboard.Services
{
    public static class PasteHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_LCONTROL = 0xA2;
        private const byte VK_V = 0x56;

        public static IntPtr GetPreviousForegroundWindow() => GetForegroundWindow();

        public static void PasteToTarget(Window window, IntPtr targetHwnd, Action copyToClipboard)
        {
            if (targetHwnd == IntPtr.Zero) return;
            copyToClipboard();
            window.Hide();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    SetForegroundWindow(targetHwnd);
                    var keyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    keyTimer.Tick += (_, _) =>
                    {
                        keyTimer.Stop();
                        keybd_event(VK_LCONTROL, 0, 0, 0);
                        keybd_event(VK_V, 0, 0, 0);
                        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, 0);
                        keybd_event(VK_LCONTROL, 0, KEYEVENTF_KEYUP, 0);
                    };
                    keyTimer.Start();
                }
                catch { }
            };
            timer.Start();
        }
    }
}
