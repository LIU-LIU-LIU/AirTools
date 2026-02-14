using System;
using System.Windows;
using System.Windows.Input;
using AirTools.Services;

namespace AirTools
{
    public partial class SettingsWindow : Window
    {
        private readonly AppThemeSettings _settings;
        private readonly Action<AppThemeSettings> _onApply;

        public SettingsWindow(AppThemeSettings settings, Action<AppThemeSettings> onApply)
        {
            InitializeComponent();
            _settings = settings;
            _onApply = onApply;

            CmbTheme.SelectedIndex = _settings.ThemeMode;
            ChkStartup.IsChecked = StartupService.IsEnabled();
            SourceInitialized += (s, e) => SyncThemeFromOwner();
        }

        private void SyncThemeFromOwner()
        {
            if (Owner == null) return;
            foreach (var key in new[] { "BgDark", "BgCard", "BgCardHover", "AccentColor", "TextPrimary", "TextSecondary", "TextDim", "BorderColor" })
            {
                if (Owner.Resources.Contains(key))
                    Resources[key] = Owner.Resources[key];
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void ChkStartup_Changed(object sender, RoutedEventArgs e)
        {
            StartupService.SetEnabled(ChkStartup.IsChecked == true);
        }

        private void CmbTheme_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbTheme.SelectedIndex < 0) return;
            _settings.ThemeMode = CmbTheme.SelectedIndex;
            _settings.Save();
            _onApply(_settings);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void BtnOk_Click(object sender, RoutedEventArgs e) => Close();
    }
}
