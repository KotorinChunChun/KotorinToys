param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$child = Join-Path $projectRoot 'tools/MdVisualizer'
$run = Join-Path $projectRoot ('dev/history/v0.1.11/logs/mdvisualizer-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
Import-Module (Join-Path $child 'scripts/NodeRuntime.psm1')
$runtime = Get-MdVisualizerNodeRuntime -ProjectRoot $child
$previousPath = $env:PATH
$previousCargo = $env:CARGO_HOME
$previousRustup = $env:RUSTUP_HOME
$previousTarget = $env:CARGO_TARGET_DIR
try {
    $env:PATH = "$($runtime.NodeRoot);$($runtime.CargoBin);$previousPath"
    $env:CARGO_HOME = $runtime.CargoHome
    $env:RUSTUP_HOME = $runtime.RustupHome
    $env:CARGO_TARGET_DIR = Join-Path $child 'src-tauri/target/kotorintoys'
    if (-not $NoBuild) {
        Push-Location $child
        try {
            Invoke-MdVisualizerNpm -ProjectRoot $child -Arguments @('test', '--', 'src/App.test.ts', 'src/lib/management.test.ts') *> (Join-Path $run 'frontend-tests.log')
            Invoke-MdVisualizerNpm -ProjectRoot $child -Arguments @('run', 'build') *> (Join-Path $run 'frontend-build.log')
            cargo test --locked --offline --manifest-path src-tauri/Cargo.toml *> (Join-Path $run 'rust-tests.log')
            if ($LASTEXITCODE) { throw 'Rust試験に失敗しました。' }
            cargo build --locked --offline --features tauri/custom-protocol --manifest-path src-tauri/Cargo.toml *> (Join-Path $run 'desktop-build.log')
            if ($LASTEXITCODE) { throw 'MdVisualizerのビルドに失敗しました。' }
        } finally { Pop-Location }
        dotnet build (Join-Path $projectRoot 'tests/KotorinToys.Tests/KotorinToys.Tests.csproj') -c Debug --artifacts-path (Join-Path $projectRoot 'artifacts/mdvisualizer') *> (Join-Path $run 'host-build.log')
        if ($LASTEXITCODE) { throw '本体試験のビルドに失敗しました。' }
    }
    $test = Join-Path $projectRoot 'artifacts/mdvisualizer/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe'
    Push-Location $projectRoot
    try {
        & $test --md-visualizer-registration-tests *> (Join-Path $run 'registration.log')
        if ($LASTEXITCODE) { throw '登録試験に失敗しました。' }
        & $test --md-visualizer-ipc *> (Join-Path $run 'ipc.log')
        if ($LASTEXITCODE) { throw '実プロセス連携試験に失敗しました。既存起動の有無も確認してください。' }
    } finally { Pop-Location }
    Write-Output "MdVisualizerの登録・管理連携試験に成功しました。ログ: $run"
} finally {
    $env:PATH = $previousPath
    $env:CARGO_HOME = $previousCargo
    $env:RUSTUP_HOME = $previousRustup
    $env:CARGO_TARGET_DIR = $previousTarget
}
