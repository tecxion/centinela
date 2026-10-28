# Builds dist\Centinela-<version>-windows-x64.exe (self-contained .NET publish + FFmpeg, packed by Inno Setup 6).
# Usage: tools\build-installer.ps1 [-Version x.y.z]   (default: <Version> of Directory.Build.props)
# Unsigned: Windows SmartScreen warns the first time (see README).
param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not (Test-Path (Join-Path $root 'ffmpeg\bin\avcodec-*.dll'))) {
    throw 'Faltan las DLL de FFmpeg en ffmpeg\bin. Ejecuta antes: tools\get-ffmpeg.ps1'
}

$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
              "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'Falta Inno Setup 6: winget install JRSoftware.InnoSetup' }

if (-not $Version) {
    $Version = (Select-String -Path (Join-Path $root 'Directory.Build.props') -Pattern '<Version>(.*)</Version>').Matches[0].Groups[1].Value
}
if (-not $Version) { throw 'No se encuentra <Version> en Directory.Build.props.' }

$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'windows-x64'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish (Join-Path $root 'src\Centinela.Desktop') -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish ha fallado.' }

& $iscc "/DAppVersion=$Version" "/DSourceDir=$publish" "/DOutputDir=$dist" (Join-Path $root 'packaging\windows\Centinela.iss')
if ($LASTEXITCODE) { throw 'Inno Setup ha fallado.' }

Remove-Item $publish -Recurse -Force
Write-Host "Instalador creado: dist\Centinela-$Version-windows-x64.exe"
