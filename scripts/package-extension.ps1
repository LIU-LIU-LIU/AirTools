<#
  Packages the Command Palette extension as AirTools-Extension-x64.zip: a plain MSIX *layout* (AppxManifest.xml at the
  root) that AirTools.exe unpacks and registers in developer mode. Build with:  ./scripts/package-extension.ps1
  Output: dist/release/AirTools-Extension-x64.zip
#>
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
& "$PSScriptRoot\build-extension.ps1" -Configuration $Configuration
$layout = Join-Path $repository "dist\extension-x64\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64"
$staging = Join-Path $repository 'dist\extension-staging'
$out = Join-Path $repository 'dist\release'
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging, $out | Out-Null
# Runtime layout only: no symbols, no build leftovers.
Get-ChildItem $layout -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
    $target = Join-Path $staging $_.FullName.Substring($layout.Length).TrimStart('\')
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target -Force
}
Copy-Item (Join-Path $repository 'THIRD_PARTY_NOTICES.md') $staging -Force
$zip = Join-Path $out 'AirTools-Extension-x64.zip'
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$staging\*" -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force
"{0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)
