using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirTools.Tools.SystemMonitor.Models;

namespace AirTools.Tools.SystemMonitor
{
    public partial class MonitorSettingsWindow : Window
    {
        private readonly MonitorSettings _settings;
        private readonly Action<MonitorSettings> _onApply;

        public MonitorSettingsWindow(MonitorSettings settings, Action<MonitorSettings> onApply)
        {
            InitializeComponent();
            _settings = settings;
            _onApply = onApply;

            SourceInitialized += (s, e) => SyncThemeFromOwner();

            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).OrderBy(d => d.Name))
            {
                var letter = d.Name.TrimEnd('\\');
                CmbDrive.Items.Add(new ComboBoxItem { Content = letter, Tag = letter.TrimEnd(':') });
            }
            if (CmbDrive.Items.Count == 0)
                CmbDrive.Items.Add(new ComboBoxItem { Content = "C:", Tag = "C" });
            SelectDrive(_settings.DiskDrive);

            CmbNetwork.Items.Add(new ComboBoxItem { Content = "自动选择", Tag = "" });
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                CmbNetwork.Items.Add(new ComboBoxItem { Content = ni.Name, Tag = ni.Id });
            }
            SelectNetwork(_settings.NetworkAdapterId);

            CmbInterval.SelectedIndex = _settings.UpdateIntervalMs switch { 500 => 0, 1000 => 1, 2000 => 2, 3000 => 3, _ => 1 };
            CmbDensity.SelectedIndex = _settings.LayoutDensity == "Relaxed" ? 1 : 0;
            ChkGradient.IsChecked = _settings.UseColorGradient;
            ChkCpu.IsChecked = _settings.ShowCpu;
            ChkMem.IsChecked = _settings.ShowMemory;
            ChkDisk.IsChecked = _settings.ShowDisk;
            ChkNet.IsChecked = _settings.ShowNetwork;
            CmbPosition.SelectedIndex = _settings.Position switch { "BottomLeft" => 1, "TopRight" => 2, "TopLeft" => 3, _ => 0 };
            CmbOrientation.SelectedIndex = _settings.Orientation == "Vertical" ? 1 : 0;
            CmbOpacity.SelectedIndex = _settings.WindowOpacity switch { <= 0.75 => 0, <= 0.82 => 1, <= 0.92 => 2, < 1 => 3, _ => 4 };
        }

        private void SelectDrive(string drive)
        {
            for (var i = 0; i < CmbDrive.Items.Count; i++)
            {
                if (CmbDrive.Items[i] is ComboBoxItem item && (item.Tag?.ToString() ?? "").Equals(drive, StringComparison.OrdinalIgnoreCase))
                {
                    CmbDrive.SelectedIndex = i;
                    return;
                }
            }
            CmbDrive.SelectedIndex = 0;
        }

        private void SelectNetwork(string id)
        {
            for (var i = 0; i < CmbNetwork.Items.Count; i++)
            {
                if (CmbNetwork.Items[i] is ComboBoxItem item && (item.Tag?.ToString() ?? "") == id)
                {
                    CmbNetwork.SelectedIndex = i;
                    return;
                }
            }
            CmbNetwork.SelectedIndex = 0;
        }

        private void SyncThemeFromOwner()
        {
            var owner = Owner;
            while (owner != null && !owner.Resources.Contains("BgDark"))
                owner = owner.Owner;
            if (owner == null) return;
            foreach (var key in new[] { "BgDark", "BgCard", "BgCardHover", "AccentColor", "TextPrimary", "TextSecondary", "TextDim", "BorderColor" })
            {
                if (owner.Resources.Contains(key))
                    Resources[key] = owner.Resources[key];
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var drive = (CmbDrive.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "C";
            var netId = (CmbNetwork.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            var s = new MonitorSettings
            {
                UpdateIntervalMs = int.Parse((CmbInterval.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "1000"),
                DiskDrive = drive,
                NetworkAdapterId = netId,
                LayoutDensity = (CmbDensity.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Compact",
                UseColorGradient = ChkGradient.IsChecked == true,
                ShowCpu = ChkCpu.IsChecked == true,
                ShowMemory = ChkMem.IsChecked == true,
                ShowDisk = ChkDisk.IsChecked == true,
                ShowNetwork = ChkNet.IsChecked == true,
                Position = (CmbPosition.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "BottomRight",
                Orientation = (CmbOrientation.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Horizontal",
                WindowOpacity = double.Parse((CmbOpacity.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0.85")
            };
            s.Save();
            _onApply(s);
            Close();
        }
    }
}
