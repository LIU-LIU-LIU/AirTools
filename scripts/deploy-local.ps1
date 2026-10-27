param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('x64','ARM64')][string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$runtime = if ($Architecture -eq 'x64') { 'win-x64' } else { 'win-arm64' }
$layout = Join-Path $repository "src\AirTools.CommandPalette\bin\$Architecture\$Configuration\net10.0-windows10.0.26100.0\$runtime"
$manifest = Join-Path $layout 'AppxManifest.xml'
if (!(Test-Path "$layout\Agent\AirTools.Agent.exe")) { throw 'Run scripts/build.ps1 first so the Agent is included.' }
if (!(Test-Path $manifest)) { throw 'Build the extension first.' }
# Official local development deployment. Does not enable Developer Mode or install a certificate.
Add-AppxPackage -Register $manifest -ForceApplicationShutdown
Write-Host 'AirTools registered. Run Reload in Command Palette, then search AirTools.'
