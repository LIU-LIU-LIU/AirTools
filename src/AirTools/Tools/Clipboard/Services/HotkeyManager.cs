using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AirTools.Tools.Clipboard.Services
{
    public class HotkeyManager : IDisposable
    {
        private IntPtr _hwnd;
        private HwndSource? _hwndSource;
        private bool _disposed;
        private int _hotkeyId = 0;

        private const int WM_HOTKEY = 0x0312;

        public const uint MOD_NONE = 0x0000;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        public const uint VK_V = 0x56;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event EventHandler? HotkeyPressed;

        public bool Register(Window window, uint modifiers, uint virtualKey)
        {
            var helper = new WindowInteropHelper(window);
            _hwnd = helper.Handle;
            if (_hwnd == IntPtr.Zero) return false;

            if (_hwndSource == null)
            {
                _hwndSource = HwndSource.FromHwnd(_hwnd);
                _hwndSource?.AddHook(WndProc);
            }

            if (_hotkeyId != 0)
                UnregisterHotKey(_hwnd, _hotkeyId);

            _hotkeyId = 9000;
            return RegisterHotKey(_hwnd, _hotkeyId, modifiers, virtualKey);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hwnd != IntPtr.Zero && _hotkeyId != 0)
                UnregisterHotKey(_hwnd, _hotkeyId);
            _hwndSource?.RemoveHook(WndProc);
        }
    }
}
