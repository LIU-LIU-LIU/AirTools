using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AirTools.Services;

/// <summary>What Windows currently knows about the extension and its host.</summary>
public sealed record ExtensionStatus(string? ExtensionVersion, string? CommandPaletteVersion, bool DeveloperMode)
{
    public bool Installed => ExtensionVersion != null;
    public bool HostPresent => CommandPaletteVersion != null;
}

/// <summary>
/// Installs / removes the AirTools Command Palette extension. The extension is a thin client of this app, shipped as a
/// zip of an MSIX layout (<see cref="AssetName"/>): next to AirTools.exe in the release zip, or downloaded from the
/// latest GitHub release. It is unpacked into the AirTools data folder and registered with Windows in developer
/// mode, so it keeps working if the AirTools folder is moved or deleted. No certificate, no administrator rights.
/// </summary>
public static class ExtensionInstaller
{
    public const string PackageName = "AirTools.CommandPalette";
    public const string AssetName = "AirTools-Extension-x64.zip";
    public static string DownloadUrl => AirTools.Core.AgentProtocol.Repository + "/releases/latest/download/" + AssetName;
    public static string InstallDirectory => Path.Combine(LauncherSettings.DataDirectory, "Extension");
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>The extension zip sitting next to the executable (portable release), if any.</summary>
    public static string? FindLocalPackage(string? directory = null)
    {
        directory ??= Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var path = Path.Combine(directory, AssetName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Throws unless the zip is an MSIX layout of this extension.</summary>
    public static void ValidatePackage(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.GetEntry("AppxManifest.xml") == null || zip.GetEntry("AirTools.CommandPalette.exe") == null)
            throw new InvalidDataException("扩展包无效：缺少 AppxManifest.xml 或 AirTools.CommandPalette.exe");
    }

    public static ExtensionStatus ParseStatus(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var text = line.Trim();
            if (!text.StartsWith('{')) continue;
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            string? Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return new ExtensionStatus(Get("ext"), Get("cmdpal"), root.TryGetProperty("dev", out var dev) && dev.ValueKind == JsonValueKind.True);
        }
        throw new InvalidDataException("无法读取扩展状态");
    }

    public static async Task<ExtensionStatus> GetStatusAsync(CancellationToken token = default)
    {
        var output = await RunAsync(
            "$e = Get-AppxPackage -Name '" + PackageName + "' | Select-Object -First 1; " +
            "$c = Get-AppxPackage -Name 'Microsoft.CommandPalette' | Select-Object -First 1; " +
            "$d = (Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense; " +
            "[pscustomobject]@{ ext = if ($e) { [string]$e.Version } else { $null }; cmdpal = if ($c) { [string]$c.Version } else { $null }; dev = ($d -eq 1) } | ConvertTo-Json -Compress", token);
        return ParseStatus(output);
    }

    public static async Task<string> InstallAsync(Action<string> progress, string? zipPath = null, CancellationToken token = default)
    {
        if (RuntimeInformation.OSArchitecture != Architecture.X64) throw new NotSupportedException("目前只提供 x64 版本的扩展包");
        await Gate.WaitAsync(token);
        string? downloaded = null;
        try
        {
            progress("正在检查系统…");
            var status = await GetStatusAsync(token);
            if (!status.DeveloperMode)
                throw new InvalidOperationException("需要先开启 Windows“开发人员模式”（设置 → 系统 → 开发者选项）：扩展以未签名包的方式注册，不需要管理员权限或证书。");
            zipPath ??= FindLocalPackage();
            if (zipPath == null) zipPath = downloaded = await DownloadAsync(progress, token);
            progress("正在校验扩展包…");
            ValidatePackage(zipPath);
            progress("正在卸载旧版本…");
            await RemovePackageAsync(token);
            progress("正在解压…");
            if (Directory.Exists(InstallDirectory)) Directory.Delete(InstallDirectory, true);
            ZipFile.ExtractToDirectory(zipPath, InstallDirectory);
            progress("正在向 Windows 注册扩展…");
            await RunAsync("Add-AppxPackage -Register " + Quote(Path.Combine(InstallDirectory, "AppxManifest.xml")) + " -ForceApplicationShutdown", token);
            await ReloadPaletteAsync(token);
            return "已安装。在命令面板中执行 Reload（或重新打开命令面板）后即可看到 AirTools。";
        }
        finally
        {
            if (downloaded != null) { try { File.Delete(downloaded); } catch { } }
            Gate.Release();
        }
    }

    public static async Task<string> UninstallAsync(CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            await RemovePackageAsync(token);
            try { if (Directory.Exists(InstallDirectory)) Directory.Delete(InstallDirectory, true); } catch (IOException) { /* locked by a stale process: harmless leftovers */ }
            await ReloadPaletteAsync(token);
            return "已卸载扩展。历史、快速命令和设置都保留在 AirTools 中。";
        }
        finally { Gate.Release(); }
    }

    private static Task RemovePackageAsync(CancellationToken token) => RunAsync(
        "Get-Process -Name 'AirTools.CommandPalette' -ErrorAction SilentlyContinue | Stop-Process -Force; " +
        "$p = Get-AppxPackage -Name '" + PackageName + "'; if ($p) { $p | Remove-AppxPackage -PreserveApplicationData }", token);

    private static async Task ReloadPaletteAsync(CancellationToken token)
    {
        // Needs "external reload" enabled in Command Palette; harmless otherwise.
        try { await RunAsync("Start-Process 'x-cmdpal://reload'", token); } catch (InvalidOperationException) { }
    }

    private static async Task<string> DownloadAsync(Action<string> progress, CancellationToken token)
    {
        progress("正在从 GitHub 下载扩展包…");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AirTools");
        using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new FileNotFoundException($"GitHub 上还没有发布扩展包。请从项目的 Releases 页面下载 {AssetName}，放到 AirTools.exe 同一目录后再点安装。");
        response.EnsureSuccessStatusCode();
        var temp = Path.Combine(Path.GetTempPath(), "AirTools-" + Guid.NewGuid().ToString("N") + ".zip");
        long total = response.Content.Headers.ContentLength ?? 0, read = 0; int lastPercent = -1;
        await using (var source = await response.Content.ReadAsStreamAsync(token))
        await using (var target = File.Create(temp))
        {
            var buffer = new byte[81920]; int n;
            while ((n = await source.ReadAsync(buffer, token)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, n), token); read += n;
                if (total > 0 && (int)(read * 100 / total) is var percent && percent != lastPercent) { lastPercent = percent; progress($"正在下载扩展包… {percent}%"); }
            }
        }
        return temp;
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>Runs a script in Windows PowerShell 5.1 (the one with the Appx module). Throws with the script's own message on failure.</summary>
    private static async Task<string> RunAsync(string script, CancellationToken token)
    {
        var wrapped = "$ProgressPreference='SilentlyContinue'; $ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
                      "try { " + script + " } catch { [Console]::Out.WriteLine('ERR:' + ($_.Exception.Message -replace '\\s+', ' ')); exit 1 }";
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-OutputFormat", "Text", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped)) })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("无法启动 PowerShell");
        var output = process.StandardOutput.ReadToEndAsync(token); var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var text = (await output).Trim();
        if (process.ExitCode != 0)
        {
            foreach (var line in text.Split('\n')) if (line.StartsWith("ERR:", StringComparison.Ordinal)) throw new InvalidOperationException(line[4..].Trim());
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(await error) ? "PowerShell 返回错误代码 " + process.ExitCode : (await error).Trim());
        }
        return text;
    }
}
