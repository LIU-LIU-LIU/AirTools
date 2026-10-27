$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$localSdk = Join-Path $env:LOCALAPPDATA 'AirTools-Dev\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
& $dotnet build "$repository\tests\AirTools.Tests\AirTools.Tests.csproj" -c Release -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Tests build failed' }
& "$repository\tests\AirTools.Tests\bin\Release\net8.0-windows\AirTools.Tests.exe" "$repository\src\AirTools.Agent\bin\Release\net8.0-windows\AirTools.Agent.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
