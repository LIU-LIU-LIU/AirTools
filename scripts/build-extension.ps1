<#
  Builds the AirTools Command Palette extension (MSIX layout) and registers it for local development.
  The extension is only a thin client of AirTools.exe; it bundles no background process.
  Usage:  ./scripts/build-extension.ps1            # build
          ./scripts/build-extension.ps1 -Register  # build + register in dev mode + ask Command Palette to reload
#>
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('x64','ARM64')][string]$Architecture = 'x64',
    [switch]$Register
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$localSdk = Join-Path $env:LOCALAPPDATA 'AirTools-Dev\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$runtime = if ($Architecture -eq 'x64') { 'win-x64' } else { 'win-arm64' }
$buildRoot = Join-Path $repository "dist\extension-$Architecture"
& $dotnet build "$repository\src\AirTools.CommandPalette\AirTools.CommandPalette.csproj" -c $Configuration -p:Platform=$Architecture "-p:BaseOutputPath=$buildRoot\bin\" -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Extension build failed (needs .NET 10 SDK)' }
$layout = Join-Path $buildRoot "bin\$Architecture\$Configuration\net10.0-windows10.0.26100.0\$runtime"
$manifest = Join-Path $layout 'AppxManifest.xml'
if (!(Test-Path $manifest)) { throw "AppxManifest.xml not found in $layout" }
Write-Host "Extension layout: $layout"
if ($Register) {
    $installed = Get-AppxPackage -Name AirTools.CommandPalette
    if ($installed) { Remove-AppxPackage -Package $installed.PackageFullName -PreserveApplicationData }
    Add-AppxPackage -Register $manifest -ForceApplicationShutdown
    Start-Process 'x-cmdpal://reload'
    Write-Host 'Registered. In Command Palette run "Reload" if AirTools does not refresh.'
}
