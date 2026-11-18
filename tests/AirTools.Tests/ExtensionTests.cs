using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

/// <summary>Tests for the extension installer's pure parts (package validation, status parsing). Registration itself needs a real desktop.</summary>
internal static class ExtensionTests
{
    public static void Run(System.Action<bool, string> check, string root)
    {
        var dir = Path.Combine(root, "ext"); Directory.CreateDirectory(dir);
        string Make(string name, params string[] entries)
        {
            var path = Path.Combine(dir, name);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("x"); }
            return path;
        }
        var good = Make("good.zip", "AppxManifest.xml", "AirTools.CommandPalette.exe", "Assets/a.png");
        bool ok = true; try { AirTools.Services.ExtensionInstaller.ValidatePackage(good); } catch { ok = false; }
        check(ok, "有效的扩展包通过校验");
        bool rejected = false; try { AirTools.Services.ExtensionInstaller.ValidatePackage(Make("bad.zip", "readme.txt")); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "缺少清单的压缩包被拒绝");
        check(AirTools.Services.ExtensionInstaller.FindLocalPackage(dir) == null, "目录里没有扩展包时返回空");
        File.Copy(good, Path.Combine(dir, AirTools.Services.ExtensionInstaller.AssetName));
        check(AirTools.Services.ExtensionInstaller.FindLocalPackage(dir) is { } found && found.EndsWith(AirTools.Services.ExtensionInstaller.AssetName), "优先使用 exe 旁边的扩展包");
        var installed = AirTools.Services.ExtensionInstaller.ParseStatus("noise\n{\"ext\":\"0.0.1.0\",\"cmdpal\":\"0.13.1.0\",\"dev\":true}\n");
        check(installed.Installed && installed.HostPresent && installed.DeveloperMode && installed.ExtensionVersion == "0.0.1.0", "解析已安装状态");
        var missing = AirTools.Services.ExtensionInstaller.ParseStatus("{\"ext\":null,\"cmdpal\":null,\"dev\":false}");
        check(!missing.Installed && !missing.HostPresent && !missing.DeveloperMode, "解析未安装状态");
        check(AirTools.Services.ExtensionInstaller.DownloadUrl.StartsWith("https://github.com/") && AirTools.Services.ExtensionInstaller.DownloadUrl.EndsWith(AirTools.Services.ExtensionInstaller.AssetName), "下载地址指向 GitHub Releases");
    }
}
