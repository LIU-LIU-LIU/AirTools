param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('x64','ARM64')][string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$localSdk = Join-Path $env:LOCALAPPDATA 'AirTools-Dev\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$runtime = if ($Architecture -eq 'x64') { 'win-x64' } else { 'win-arm64' }
$agent = Join-Path $repository "dist\agent-$Architecture"
$package = Join-Path $repository "dist\package-$Architecture"
& $dotnet publish "$repository\src\AirTools.Agent\AirTools.Agent.csproj" -c $Configuration -r $runtime --self-contained true -o $agent -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Agent publish failed' }
& $dotnet build "$repository\src\AirTools.CommandPalette\AirTools.CommandPalette.csproj" -c $Configuration -p:Platform=$Architecture "-p:AgentPublishDirectory=$agent" -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false "-p:AppxPackageDir=$package\" -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Extension package build failed' }
Write-Host "Package output: $package"
Get-ChildItem $package -Recurse -Filter *.msix | Select-Object FullName,Length
