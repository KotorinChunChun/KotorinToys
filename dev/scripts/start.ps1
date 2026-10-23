param([switch]$LocalCatalog)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$app = Join-Path $projectRoot 'src/KotorinToys/bin/Debug/net10.0-windows10.0.19041.0/KotorinToys.exe'
if (-not (Test-Path $app)) { throw '先に pwsh -File dev/scripts/build.ps1 を実行してください。' }
$arguments = @()
if ($LocalCatalog) {
    $version = 'v' + (Get-Content (Join-Path $projectRoot 'catalog/host.json') -Raw | ConvertFrom-Json).version
    $settingsDirectory = Join-Path $projectRoot ("dev/history/$version/logs/local-catalog-" + [Guid]::NewGuid().ToString('N') + '/settings')
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    $arguments = @('--local-catalog', '--root', ('"' + $projectRoot + '"'), '--settings-dir', ('"' + $settingsDirectory + '"'))
    Write-Output "ローカル開発一覧で起動します。専用設定: $settingsDirectory"
    Write-Output '起動中の本体がある場合は既存画面を表示します。切り替える場合は本体を終了してから再実行してください。'
}
if ($arguments.Count) {
    Start-Process -FilePath $app -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle Normal
} else {
    Start-Process -FilePath $app -WorkingDirectory $projectRoot -WindowStyle Normal
}
