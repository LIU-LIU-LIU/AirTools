using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AirTools.Tools.Clipboard.Models;

namespace AirTools.Tools.Clipboard
{
    public partial class ClipboardSettingsWindow : Window
    {
        private readonly ClipboardAppSettings _settings;
        private readonly Action<ClipboardAppSettings> _onApply;
        private bool _capturingHotkey;

        public ClipboardSettingsWindow(ClipboardAppSettings settings, Action<ClipboardAppSettings> onApply)
        {
            InitializeComponent();
            _settings = settings;
            _onApply = onApply;
            UpdateHotkeyDisplay();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SyncThemeFromOwner();
        }

        public void SyncThemeFromOwner()
        {
            if (Owner == null) return;
            foreach (var key in new[] { "WindowGlass", "BgDark", "BgCard", "BgCardHover", "AccentColor", "TextPrimary", "TextSecondary", "TextDim", "BorderColor", "DangerColor", "SuccessColor", "PinColor", "BgInput", "AccentHover" })
            {
                if (Owner.Resources.Contains(key))
                    Resources[key] = Owner.Resources[key];
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (_capturingHotkey) return;
            _capturingHotkey = true;
            BtnHotkey.Content = "请按下快捷键...";
            BtnHotkey.Background = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A));
            PreviewKeyDown += OnCaptureKeyDown;
            Focusable = true;
            Focus();
        }

        private void OnCaptureKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key;
            if (key == Key.System) key = e.SystemKey;

            if (key == Key.Escape) { EndCapture(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
            if (key == Key.LWin || key == Key.RWin) return;

            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

            if (!ctrl && !shift && !alt)
            {
                MessageBox.Show("请至少按住一个修饰键（Ctrl / Shift / Alt）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var charKey = KeyToString(key);
            if (string.IsNullOrEmpty(charKey))
            {
                MessageBox.Show("不支持的按键，请使用字母或数字", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _settings.CtrlModifier = ctrl;
            _settings.ShiftModifier = shift;
            _settings.AltModifier = alt;
            _settings.WinModifier = false;
            _settings.HotkeyChar = charKey;
            _settings.Save();
            _onApply(_settings);
            EndCapture();
        }

        private static string KeyToString(Key key)
        {
            if (key >= Key.A && key <= Key.Z) return ((char)('A' + (key - Key.A))).ToString();
            if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return ((char)('0' + (key - Key.NumPad0))).ToString();
            return key switch
            {
                Key.OemComma => ",", Key.OemPeriod => ".", Key.Oem1 => ";", Key.Oem2 => "/", Key.Oem3 => "`",
                Key.OemMinus => "-", Key.OemPlus => "=", Key.Oem4 => "[", Key.Oem5 => "\\", Key.Oem6 => "]",
                Key.Oem7 => "'", Key.Space => " ",
                _ => ""
            };
        }

        private void EndCapture()
        {
            PreviewKeyDown -= OnCaptureKeyDown;
            _capturingHotkey = false;
            BtnHotkey.Background = (Brush)FindResource("AccentColor");
            UpdateHotkeyDisplay();
        }

        private void UpdateHotkeyDisplay() => BtnHotkey.Content = _settings.GetHotkeyDisplay();

        private void BtnOk_Click(object sender, RoutedEventArgs e) => Close();
    }
}
