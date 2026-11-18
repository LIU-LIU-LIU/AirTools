using System;
using System.IO;

namespace AirTools.Tools.Clipboard.Services;

/// <summary>Bounded diagnostic log; never records clipboard text.</summary>
internal static class PasteDiagnostics
{
    public static void Write(string message)
    {
        try
        {
            var directory = Path.Combine(AirTools.Services.LauncherSettings.DataDirectory, "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "paste.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
