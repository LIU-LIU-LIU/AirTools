# AirTools 发布脚本 - 统一输出到 dist/ 目录
# 用法: .\publish.ps1 或 .\publish.ps1 -SelfContainedOnly 或 .\publish.ps1 -FrameworkDependentOnly

param(
    [switch]$SelfContainedOnly,
    [switch]$FrameworkDependentOnly
)

$ErrorActionPreference = "Stop"
$proj = "src/AirTools/AirTools.csproj"
Set-Location $PSScriptRoot
if ($SelfContainedOnly -and $FrameworkDependentOnly) { throw 'Choose only one publish mode' }
function Copy-LegalFiles([string]$output) {
    Copy-Item LICENSE,NOTICE -Destination $output -Force
    Copy-Item licenses -Destination $output -Recurse -Force
    $assets = Get-Content 'src/AirTools/obj/project.assets.json' -Raw | ConvertFrom-Json -AsHashtable
    foreach ($packageRoot in $assets.packageFolders.Keys) {
        foreach ($library in $assets.libraries.Values) {
            if ($library.type -ne 'package') { continue }
            $folder = Join-Path $packageRoot $library.path
            Get-ChildItem $folder -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY.NOTICES)(\.|$)' } | ForEach-Object {
                $destination = Join-Path $output ('licenses/' + ($library.path -replace '[/\\]', '-') + '-' + $_.Name)
                Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
            }
        }
        $runtimeConfig = Get-Content 'src/AirTools/bin/Release/net8.0-windows/win-x64/AirTools.runtimeconfig.json' -Raw | ConvertFrom-Json
        foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
            $package = if ($framework.name -eq 'Microsoft.NETCore.App') { 'microsoft.netcore.app.runtime.win-x64' } else { 'microsoft.windowsdesktop.app.runtime.win-x64' }
            $folder = Join-Path $packageRoot "$package/$($framework.version)"
            Get-ChildItem $folder -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY.NOTICES)(\.|$)' } | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $output "licenses/$package-$($framework.version)-$($_.Name)") -Force
            }
        }
    }
    $hash = Get-FileHash -LiteralPath (Join-Path $output 'AirTools.exe') -Algorithm SHA256
    "$($hash.Hash)  AirTools.exe" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
}
function Prepare-Output([string]$output) {
    $target = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $output))
    $allowed = @('dist/self-contained', 'dist/framework-dependent') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $_)) }
    if ($target -notin $allowed) { throw "Unexpected publish target: $target" }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    New-Item -ItemType Directory -Force $target | Out-Null
}

if (-not $FrameworkDependentOnly) {
    Write-Host "发布自包含版 (dist/self-contained/) ..." -ForegroundColor Cyan
    Prepare-Output 'dist/self-contained'
    dotnet publish $proj -c Release -o dist/self-contained
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed' }
    Copy-LegalFiles 'dist/self-contained'
    Write-Host "完成: dist/self-contained/AirTools.exe" -ForegroundColor Green
}

if (-not $SelfContainedOnly) {
    Write-Host "发布框架依赖版 (dist/framework-dependent/) ..." -ForegroundColor Cyan
    Prepare-Output 'dist/framework-dependent'
    dotnet publish $proj -c Release -p:SelfContained=false -p:RuntimeIdentifier=win-x64 -p:PublishSingleFile=true -o dist/framework-dependent
    if ($LASTEXITCODE -ne 0) { throw 'Framework-dependent publish failed' }
    Copy-LegalFiles 'dist/framework-dependent'
    Write-Host "完成: dist/framework-dependent/AirTools.exe" -ForegroundColor Green
}

Write-Host "`n发布产物:" -ForegroundColor Yellow
foreach ($output in $(if ($SelfContainedOnly) { @('dist/self-contained') } elseif ($FrameworkDependentOnly) { @('dist/framework-dependent') } else { @('dist/self-contained', 'dist/framework-dependent') })) {
    if (Test-Path $output) { Get-ChildItem $output -File | ForEach-Object { Write-Host "  $($_.FullName)  $([math]::Round($_.Length/1KB, 1)) KB" } }
}
