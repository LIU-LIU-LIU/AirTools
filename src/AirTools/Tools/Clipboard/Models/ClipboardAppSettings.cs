using System;
using System.IO;
using System.Text.Json;
using AirTools.Tools.Clipboard.Services;

namespace AirTools.Tools.Clipboard.Models
{
    public class ClipboardAppSettings
    {
        public bool CtrlModifier { get; set; } = true;
        public bool ShiftModifier { get; set; } = true;
        public bool AltModifier { get; set; }
        public bool WinModifier { get; set; }
        public string HotkeyChar { get; set; } = "V";

        public static ClipboardAppSettings Load()
        {
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<ClipboardAppSettings>(json) ?? new ClipboardAppSettings();
                }
            }
            catch { }
            return new ClipboardAppSettings();
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
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirTools", "Clipboard");
            return Path.Combine(appData, "settings.json");
        }

        public string GetHotkeyDisplay()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (CtrlModifier) parts.Add("Ctrl");
            if (ShiftModifier) parts.Add("Shift");
            if (AltModifier) parts.Add("Alt");
            parts.Add(HotkeyChar.ToUpperInvariant());
            return string.Join("+", parts);
        }

        public uint GetModifiers()
        {
            uint mod = HotkeyManager.MOD_NOREPEAT;
            if (CtrlModifier) mod |= HotkeyManager.MOD_CONTROL;
            if (ShiftModifier) mod |= HotkeyManager.MOD_SHIFT;
            if (AltModifier) mod |= HotkeyManager.MOD_ALT;
            return mod;
        }

        public uint GetVirtualKey()
        {
            if (string.IsNullOrEmpty(HotkeyChar)) return HotkeyManager.VK_V;
            var c = char.ToUpperInvariant(HotkeyChar[0]);
            return (uint)c;
        }
    }
}
