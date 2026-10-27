$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot) 'dev/scripts/runtime-pack-validation.ps1')
$assets = '{"project":{"frameworks":{"net10.0":{"downloadDependencies":[{"name":"Microsoft.NETCore.App.Runtime.win-x64","version":"[10.0.14, 10.0.14]"},{"name":"Microsoft.WindowsDesktop.App.Runtime.win-x64","version":"[10.0.13, 10.0.13]"}]}}}}' | ConvertFrom-Json
Assert-RuntimePackVersion $assets 'microsoft.netcore.app.runtime.win-x64' '10.0.14'
$rejected = $false
try { Assert-RuntimePackVersion $assets 'microsoft.windowsdesktop.app.runtime.win-x64' '10.0.14' } catch { $rejected = $true }
if (-not $rejected) { throw '表示版と実際のWindows Desktopパックの不一致を拒否しませんでした。' }
$rejected = $false
try { Assert-RuntimePackVersion $assets 'missing.runtime.win-x64' '10.0.14' } catch { $rejected = $true }
if (-not $rejected) { throw '解決されたパックがない場合を拒否しませんでした。' }
Write-Output 'PASS: 解決されたランタイムの一致・不一致・欠落を確認しました。'
