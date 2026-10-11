param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$app = Join-Path $projectRoot 'src/KotorinToys/bin/Debug/net10.0-windows/KotorinToys.exe'
if (-not (Test-Path $app)) { throw '先に pwsh -File dev/scripts/build.ps1 を実行してください。' }
Start-Process -FilePath $app -WorkingDirectory $projectRoot -WindowStyle Normal
