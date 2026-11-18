param([switch]$InteractivePaste)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet build tests/AirTools.Tests/AirTools.Tests.csproj -c Release --tl:off
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    $testArgs = @()
    if ($InteractivePaste) { $testArgs += '--interactive-paste' }
    & './tests/AirTools.Tests/bin/Release/net8.0-windows/win-x64/AirTools.Tests.exe' @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { Pop-Location }
