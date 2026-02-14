using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace AirTools.Services
{
    /// <summary>
    /// 主题服务（所有工具共用）
    /// </summary>
    public static class ThemeService
    {
        private const string PersonalizeRegPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightTheme = "AppsUseLightTheme";

        public static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegPath);
                var value = key?.GetValue(AppsUseLightTheme);
                if (value is int intValue) return intValue == 0;
            }
            catch { }
            return true;
        }

        public static void ApplyTheme(ResourceDictionary resources, bool darkMode)
        {
            if (darkMode)
            {
                var windowGlass = new LinearGradientBrush();
                windowGlass.StartPoint = new Point(0, 0);
                windowGlass.EndPoint = new Point(1, 1);
                windowGlass.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0x15, 0x1A, 0x26), 0));
                windowGlass.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0x1C, 0x20, 0x30), 1));
                resources["WindowGlass"] = windowGlass;
                resources["BgDark"] = new SolidColorBrush(Color.FromArgb(0xB0, 0x1A, 0x1D, 0x2B));
                resources["BgCard"] = new SolidColorBrush(Color.FromArgb(0x8C, 0x2B, 0x30, 0x40));
                resources["BgCardHover"] = new SolidColorBrush(Color.FromArgb(0xB0, 0x3D, 0x43, 0x58));
                resources["BgInput"] = new SolidColorBrush(Color.FromArgb(0x80, 0x20, 0x25, 0x33));
                resources["AccentColor"] = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
                resources["AccentHover"] = new SolidColorBrush(Color.FromRgb(0x74, 0xC7, 0xEC));
                resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
                resources["TextSecondary"] = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8));
                resources["TextDim"] = new SolidColorBrush(Color.FromRgb(0x8B, 0x90, 0xAA));
                resources["DangerColor"] = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                resources["SuccessColor"] = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
                resources["PinColor"] = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
                resources["BorderColor"] = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
                resources["MonitorBg"] = new SolidColorBrush(Color.FromArgb(0xCC, 0x28, 0x28, 0x28));
                resources["MonitorBorder"] = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
                resources["MonitorCpuColor"] = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)); // 深色主题用浅色
                resources["MonitorMemColor"] = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
                resources["MonitorDiskColor"] = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
                resources["MonitorNetColor"] = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
                resources["CanvasBg"] = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x0C));
            }
            else
            {
                var windowGlass = new LinearGradientBrush();
                windowGlass.StartPoint = new Point(0, 0);
                windowGlass.EndPoint = new Point(1, 1);
                windowGlass.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, 0xF6, 0xF8, 0xFC), 0));
                windowGlass.GradientStops.Add(new GradientStop(Color.FromArgb(0xC8, 0xE8, 0xEE, 0xF9), 1));
                resources["WindowGlass"] = windowGlass;
                resources["BgDark"] = new SolidColorBrush(Color.FromArgb(0xE8, 0xF8, 0xFA, 0xFF));
                resources["BgCard"] = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF));
                resources["BgCardHover"] = new SolidColorBrush(Color.FromArgb(0xEE, 0xF2, 0xF6, 0xFF));
                resources["BgInput"] = new SolidColorBrush(Color.FromArgb(0xEE, 0xF8, 0xFA, 0xFF));
                resources["AccentColor"] = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                resources["AccentHover"] = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
                resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0x1D, 0x25, 0x36));
                resources["TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x69));
                resources["TextDim"] = new SolidColorBrush(Color.FromRgb(0x71, 0x7C, 0x91));
                resources["DangerColor"] = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                resources["SuccessColor"] = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                resources["PinColor"] = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                resources["BorderColor"] = new SolidColorBrush(Color.FromArgb(0x90, 0xB9, 0xC7, 0xDD));
                resources["MonitorBg"] = new SolidColorBrush(Color.FromArgb(0xEE, 0xF2, 0xF6, 0xFF));
                resources["MonitorBorder"] = new SolidColorBrush(Color.FromRgb(0xB9, 0xC7, 0xDD));
                resources["MonitorCpuColor"] = new SolidColorBrush(Color.FromRgb(0x0D, 0x72, 0x33)); // 浅色主题用深色
                resources["MonitorMemColor"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF));
                resources["MonitorDiskColor"] = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));
                resources["MonitorNetColor"] = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));
                resources["CanvasBg"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
            }
        }
    }
}
