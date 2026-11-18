using System;
using System.IO;
using System.Text.Json;

namespace AirTools.Tools.SystemMonitor.Models
{
    public class MonitorSettings
    {
        public int UpdateIntervalMs { get; set; } = 1000;
        public bool ShowCpu { get; set; } = true;
        public bool ShowMemory { get; set; } = true;
        public bool ShowDisk { get; set; } = true;
        public string DiskDrive { get; set; } = "C";
        public bool ShowNetwork { get; set; } = true;
        public string NetworkAdapterId { get; set; } = "";
        public double WindowOpacity { get; set; } = 0.85;
        public string Position { get; set; } = "BottomRight";
        public string Orientation { get; set; } = "Horizontal";
        public string LayoutDensity { get; set; } = "Compact";
        public bool UseColorGradient { get; set; } = true;
        /// <summary>Position after the user dragged the bar (screen DIPs). Null = use the <see cref="Position"/> corner.</summary>
        public double? Left { get; set; }
        public double? Top { get; set; }

        public static MonitorSettings Load()
        {
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(path)) ?? new MonitorSettings();
            }
            catch { }
            return new MonitorSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(GetSettingsPath());
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(GetSettingsPath(), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private static string GetSettingsPath()
        {
            // Same folder as before by default (%LOCALAPPDATA%\AirTools); now also honours AIRTOOLS_DATA_DIRECTORY.
            return Path.Combine(AirTools.Services.LauncherSettings.DataDirectory, "SystemMonitor", "settings.json");
        }
    }
}
