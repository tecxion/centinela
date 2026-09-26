# Downloads FFmpeg 9.0 LGPL shared build (BtbN) into <repo>/ffmpeg/bin.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'ffmpeg'
$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n9.0-latest-win64-lgpl-shared-9.0.zip'
$zip = Join-Path $env:TEMP 'camarawin-ffmpeg.zip'
$tmp = Join-Path $env:TEMP 'camarawin-ffmpeg'

Invoke-WebRequest $url -OutFile $zip
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
Expand-Archive $zip $tmp
$bin = Get-ChildItem $tmp -Recurse -Directory -Filter bin | Select-Object -First 1
New-Item -ItemType Directory -Force (Join-Path $dest 'bin') | Out-Null
Copy-Item (Join-Path $bin.FullName '*.dll') (Join-Path $dest 'bin') -Force
$license = Get-ChildItem $tmp -Recurse -Filter 'LICENSE*' | Select-Object -First 1
if ($license) { Copy-Item $license.FullName (Join-Path $dest 'LICENSE.txt') -Force }
Remove-Item $zip, $tmp -Recurse -Force
Write-Host "FFmpeg DLLs in $dest\bin"
