# AirTools 发布脚本 - 统一输出到 dist/ 目录
# 用法: .\publish.ps1 或 .\publish.ps1 -SelfContainedOnly 或 .\publish.ps1 -FrameworkDependentOnly

param(
    [switch]$SelfContainedOnly,
    [switch]$FrameworkDependentOnly
)

$ErrorActionPreference = "Stop"
$proj = "src/AirTools/AirTools.csproj"

if (-not $FrameworkDependentOnly) {
    Write-Host "发布自包含版 (dist/self-contained/) ..." -ForegroundColor Cyan
    dotnet publish $proj -c Release -o dist/self-contained
    Write-Host "完成: dist/self-contained/AirTools.exe (~63MB)" -ForegroundColor Green
}

if (-not $SelfContainedOnly) {
    Write-Host "发布框架依赖版 (dist/framework-dependent/) ..." -ForegroundColor Cyan
    dotnet publish $proj -c Release -p:SelfContained=false -p:RuntimeIdentifier=win-x64 -p:PublishSingleFile=true -o dist/framework-dependent
    Write-Host "完成: dist/framework-dependent/AirTools.exe (~250KB)" -ForegroundColor Green
}

Write-Host "`n发布产物:" -ForegroundColor Yellow
Get-ChildItem dist -Recurse -File | ForEach-Object { Write-Host "  $($_.FullName.Replace((Get-Location).Path + '\', ''))  $([math]::Round($_.Length/1KB, 1)) KB" }
