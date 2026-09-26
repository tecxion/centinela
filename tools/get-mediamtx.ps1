# Downloads the latest mediamtx (MIT) for Windows into <repo>/tools/bin. Used only by tests.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'tools\bin'
$release = Invoke-RestMethod 'https://api.github.com/repos/bluenviron/mediamtx/releases/latest'
$asset = $release.assets | Where-Object name -like '*windows_amd64.zip' | Select-Object -First 1
$zip = Join-Path $env:TEMP $asset.name
Invoke-WebRequest $asset.browser_download_url -OutFile $zip
New-Item -ItemType Directory -Force $dest | Out-Null
Expand-Archive $zip $dest -Force
Remove-Item $zip
Write-Host "mediamtx $($release.tag_name) in $dest"
