$ErrorActionPreference = 'Stop'
function Get-SecurityGitleaks {
    $root = Split-Path (Split-Path $PSScriptRoot)
    $cache = Join-Path $root 'artifacts/dependencies/security/gitleaks-8.30.1'
    $scanner = Join-Path $cache 'gitleaks.exe'
    $expectedExe = '17157e2ee8b76fc8b1d8bee607a250e34b8a8023c8bc81822d4b5ee4d78fcb7c'
    if (-not (Test-Path -LiteralPath $scanner)) {
        New-Item -ItemType Directory -Force -Path $cache | Out-Null
        $zip = Join-Path $cache 'gitleaks_8.30.1_windows_x64.zip'
        Invoke-WebRequest 'https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_windows_x64.zip' -OutFile $zip -TimeoutSec 60
        if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'd29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e') { throw '検査器ZIPのSHA-256が不一致です。実行しません。' }
        Expand-Archive -LiteralPath $zip -DestinationPath $cache -Force
    }
    if ((Get-FileHash -LiteralPath $scanner -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedExe) { throw '検査器EXEのSHA-256が不一致です。実行しません。' }
    return $scanner
}
