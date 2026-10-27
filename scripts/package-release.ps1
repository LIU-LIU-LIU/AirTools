param(
    [ValidateSet('x64','ARM64')][string]$Architecture = 'x64',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$manifest = [xml](Get-Content "$repository\src\AirTools.CommandPalette\Package.appxmanifest" -Raw -Encoding UTF8)
$subject = $manifest.Package.Identity.Publisher
$version = $manifest.Package.Identity.Version
if (!$SkipBuild) { & "$PSScriptRoot\build.ps1" -Architecture $Architecture; if ($LASTEXITCODE -ne 0) { throw 'Build failed' } }

$msix = Get-ChildItem "$repository\dist\package-$Architecture" -Recurse -Filter "*_$Architecture.msix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (!$msix) { throw 'MSIX not found. Run without -SkipBuild.' }
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe | Where-Object FullName -match '\\x64\\' | Sort-Object FullName -Descending | Select-Object -First 1
if (!$signtool) { throw 'signtool.exe not found. Install the Windows SDK.' }

$keyDirectory = Join-Path $env:LOCALAPPDATA 'AirTools-Dev\signing'
New-Item -ItemType Directory -Force $keyDirectory | Out-Null
$pfx = Join-Path $keyDirectory 'AirTools.pfx'
$passwordFile = Join-Path $keyDirectory 'AirTools.pfx.password'
if (!(Test-Path $pfx)) {
    $bytes = New-Object byte[] 18; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); $password = [Convert]::ToBase64String($bytes)
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature -FriendlyName 'AirTools' `
        -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    Export-PfxCertificate -Cert $certificate -FilePath $pfx -Password (ConvertTo-SecureString $password -AsPlainText -Force) | Out-Null
    Set-Content $passwordFile $password -NoNewline
    Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)"
    Write-Host "Created signing certificate in $keyDirectory (keep it; never commit it)."
}
$password = Get-Content $passwordFile -Raw

$output = Join-Path $repository "dist\release-$Architecture"
Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $output | Out-Null
$name = "AirTools-$version-$Architecture"
$signed = Join-Path $output "$name.msix"
Copy-Item $msix.FullName $signed
& $signtool.FullName sign /fd SHA256 /f $pfx /p $password $signed
if ($LASTEXITCODE -ne 0) { throw 'Signing failed' }

$certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2($pfx, $password)
Export-Certificate -Cert $certificate -FilePath (Join-Path $output 'AirTools.cer') | Out-Null

$install = @"
`$ErrorActionPreference = 'Stop'
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File ``"`$PSCommandPath``""
    return
}
`$here = Split-Path `$PSCommandPath
Import-Certificate -FilePath "`$here\AirTools.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Add-AppxPackage -Path "`$here\$name.msix"
Write-Host 'AirTools installed. In Command Palette run "Reload" and search AirTools.'
Read-Host 'Press Enter to close'
"@
Set-Content (Join-Path $output 'Install.ps1') $install -Encoding UTF8
Get-FileHash $signed -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $name.msix" } | Set-Content (Join-Path $output 'SHA256SUMS.txt')
Write-Host "Release files: $output"
Get-ChildItem $output | Select-Object Name,Length

