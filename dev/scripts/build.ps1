param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
Push-Location $projectRoot
try {
    dotnet publish tools/LiveTextMarker/source/LiveTextMarker.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o tools/LiveTextMarker/source/out/LiveTextMarker --nologo
    if ($LASTEXITCODE) { throw 'LiveTextMarkerのビルドに失敗しました。' }
    dotnet publish tools/SmartPaste/SmartPaste/SmartPaste.csproj -c Release -r win-x64 --self-contained true -o tools/SmartPaste/release --nologo
    if ($LASTEXITCODE) { throw 'SmartPasteのビルドに失敗しました。' }
    dotnet build tools/SSCapture/src/SSCapture/SSCapture.csproj -c Release --nologo
    if ($LASTEXITCODE) { throw 'SSCaptureのビルドに失敗しました。' }
    & pwsh -ExecutionPolicy Bypass -File tools/ShortcutKeyCounter/dev/scripts/build-debug-ShortcutKeyCounter-all.ps1
    if ($LASTEXITCODE) { throw 'ShortcutKeyCounterのビルドに失敗しました。' }
    & cargo build --manifest-path tools/HttpSelector/Cargo.toml --target-dir tools/HttpSelector/target/management
    if ($LASTEXITCODE) { throw 'HttpSelectorの管理連携ビルドに失敗しました。' }
    & (Join-Path $projectRoot 'tools/EveryDB/dev/scripts/build-debug-EveryDB-kotorintoys.ps1')
    if ($LASTEXITCODE) { throw 'EveryDBの管理連携ビルドに失敗しました。' }
    & cargo build --locked --manifest-path tools/VoiceMixerMicRouter/src/src-tauri/Cargo.toml --bin voicemixer-mic
    if ($LASTEXITCODE) { throw 'VoiceMixerMicRouterの管理連携ビルドに失敗しました。' }
    dotnet build src/KotorinToys/KotorinToys.csproj -c Debug --nologo
    if ($LASTEXITCODE) { throw 'KotorinToysのビルドに失敗しました。' }
    foreach ($name in @('CapchunScreen','ChulipVideo','ChunapTool')) {
        dotnet build "tools/$name/src/$name/$name.csproj" -c Debug --artifacts-path "tools/$name/artifacts/management" --nologo
        if ($LASTEXITCODE) { throw "$name の管理連携ビルドに失敗しました。" }
    }
} finally { Pop-Location }
