using System;
using System.IO;
using Microsoft.Win32;

namespace AirTools.Services
{
    /// <summary>
    /// Windows 开机自启 - 仅注册 AirTools
    /// </summary>
    public static class StartupService
    {
        private const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "AirTools";

        public static string GetExecutablePath()
        {
            return Environment.ProcessPath ?? AppDomain.CurrentDomain.BaseDirectory + "AirTools.exe";
        }

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegPath, false);
                var value = key?.GetValue(AppName) as string;
                return !string.IsNullOrEmpty(value) && File.Exists(value?.Trim('"'));
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enable)
        {
            try
            {
                var exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    return false;

                using var key = Registry.CurrentUser.OpenSubKey(RegPath, true);
                if (key == null) return false;

                if (enable)
                {
                    var path = exePath.Contains(" ") ? $"\"{exePath}\"" : exePath;
                    key.SetValue(AppName, path);
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
