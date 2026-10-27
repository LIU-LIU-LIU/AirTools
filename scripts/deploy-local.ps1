param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('x64','ARM64')][string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$runtime = if ($Architecture -eq 'x64') { 'win-x64' } else { 'win-arm64' }
$layout = Join-Path $repository "dist\build-$Architecture\bin\$Architecture\$Configuration\net10.0-windows10.0.26100.0\$runtime"
$manifest = Join-Path $layout 'AppxManifest.xml'
if (!(Test-Path "$layout\Agent\AirTools.Agent.exe")) { throw 'Run scripts/build.ps1 first so the Agent is included.' }
if (!(Test-Path $manifest)) { throw 'Build the extension first.' }
# Official local development deployment. Does not enable Developer Mode or install a certificate.
$installed = Get-AppxPackage AirTools.CommandPalette
if ($installed -and $installed.InstallLocation.TrimEnd('\') -ne $layout.TrimEnd('\')) {
    if (!$installed.IsDevelopmentMode) { throw 'An installed release uses another location. Uninstall it explicitly before development registration.' }
    # Same-version registration can silently keep the old layout. Preserve app data when relocating.
    Remove-AppxPackage -Package $installed.PackageFullName -PreserveApplicationData
}
Add-AppxPackage -Register $manifest -ForceApplicationShutdown
$registered = Get-AppxPackage AirTools.CommandPalette
if (!$registered -or $registered.InstallLocation.TrimEnd('\') -ne $layout.TrimEnd('\')) {
    throw 'Windows did not register the requested build location.'
}
Start-Process 'x-cmdpal://reload'
Write-Host 'AirTools registered; requested external reload. External reload must be enabled in Command Palette settings.'
