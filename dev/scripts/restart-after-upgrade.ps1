param(
    [Parameter(Mandatory)][string]$PlanPath,
    [ValidateRange(30,300)][int]$DelaySeconds=60
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot)
$version=(Get-Content -LiteralPath (Join-Path $projectRoot 'catalog/host.json') -Raw | ConvertFrom-Json).version
$planFile=[IO.Path]::GetFullPath($PlanPath)
$logs=[IO.Path]::GetFullPath((Join-Path $projectRoot "dev/history/v$version/logs"))
$directory=Split-Path $planFile
if((Split-Path $directory) -ne $logs -or (Split-Path $directory -Leaf) -notmatch '^reboot-upgrade-[a-f0-9]{32}$' -or (Split-Path $planFile -Leaf) -ne 'plan.json'){
    throw '現行版の専用再起動計画ではありません。'
}
$testExe=Join-Path $projectRoot 'artifacts/update-reboot-013/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe'
try {
    if(-not (Test-Path -LiteralPath $planFile -PathType Leaf)){throw '再起動前記録がありません。'}
    Start-Sleep -Seconds $DelaySeconds
    & $testExe --upgrade-reboot-ready $planFile *> (Join-Path $directory 'restart-preflight.log')
    if($LASTEXITCODE -ne 0){throw '再起動直前の照合に失敗しました。再起動しません。'}
    [ordered]@{requestedAt=[DateTimeOffset]::UtcNow;version=$version;force=$false;delaySeconds=$DelaySeconds} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'restart-request.json') -Encoding utf8
    # 正の/tは強制終了を伴うため使わない。保存確認でWindowsが中断したら未完了のまま残す。
    & "$env:SystemRoot\System32\shutdown.exe" /r /t 0 /d p:4:1 /c 'KotorinToys 0.1.3 更新後の自動起動検証'
    if($LASTEXITCODE -ne 0){throw "Windowsの再起動要求が失敗しました：$LASTEXITCODE"}
} catch {
    $_.Exception.Message | Set-Content -LiteralPath (Join-Path $directory 'restart-failure.txt') -Encoding utf8
    exit 1
}
