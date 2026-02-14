using System;
using System.IO;
using System.Text.Json;

namespace AirTools.Services
{
    /// <summary>
    /// 全局主题设置（所有工具共用）
    /// </summary>
    public class AppThemeSettings
    {
        /// <summary>0=跟随系统, 1=浅色, 2=深色</summary>
        public int ThemeMode { get; set; } = 0;

        public static AppThemeSettings Load()
        {
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<AppThemeSettings>(File.ReadAllText(path)) ?? new AppThemeSettings();
            }
            catch { }
            return new AppThemeSettings();
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
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirTools");
            return Path.Combine(appData, "settings.json");
        }
    }
}
