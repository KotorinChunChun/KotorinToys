param([Parameter(Mandatory)][string]$FixtureRoot)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
$fixture = [IO.Path]::GetFullPath($FixtureRoot)
if (-not $fixture.StartsWith((Join-Path $projectRoot 'dev/history') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $fixture -notmatch '[\\/]logs[\\/]') { throw '試験出力先は版別logs内に指定してください。' }
if (Test-Path -LiteralPath $fixture) { throw '既存の試験フォルダーは使用しません。' }
New-Item -ItemType Directory -Path $fixture | Out-Null
$remote = Join-Path $fixture 'remote.git'
$checkout = Join-Path $fixture 'checkout'
$runner = Join-Path $fixture 'runner'
New-Item -ItemType Directory -Path $checkout, $runner | Out-Null
git init --bare --initial-branch=main $remote | Out-Null
if ($LASTEXITCODE -ne 0) { throw '試験用リポジトリを初期化できません。' }
git -C $checkout init --initial-branch=main | Out-Null
git -C $checkout config user.name 'test'
git -C $checkout config user.email 'test@example.invalid'
Set-Content -LiteralPath (Join-Path $checkout 'README.md') -Value 'test' -Encoding utf8
git -C $checkout add README.md
git -C $checkout commit -m '試験用の初期状態' | Out-Null
git -C $checkout remote add origin $remote
git -C $checkout push origin main | Out-Null
if ($LASTEXITCODE -ne 0 -or (git -C $checkout remote get-url origin) -ne $remote) { throw '試験用の公開先が不正です。' }

$lines = Get-Content -LiteralPath (Join-Path $projectRoot '.github/workflows/release-index.yml')
$step = [Array]::IndexOf($lines, '      - name: 一覧専用ブランチへ保存')
if ($step -lt 0) { throw '一覧を保存するworkflowの手順がありません。' }
$run = $step + 1
while ($run -lt $lines.Count -and $lines[$run] -ne '        run: |') { $run++ }
if ($run -ge $lines.Count) { throw 'workflowの実行内容がありません。' }
$body = @()
for ($i = $run + 1; $i -lt $lines.Count -and $lines[$i].StartsWith('          '); $i++) { $body += $lines[$i].Substring(10) }
if ($body.Count -eq 0) { throw 'workflowの実行内容が空です。' }
$code = [scriptblock]::Create(($body -join "`n"))
$previousRunner = $env:RUNNER_TEMP
try {
    $env:RUNNER_TEMP = $runner
    Set-Content -LiteralPath (Join-Path $runner 'release-index.json') -Value '{"fixture":1}' -Encoding utf8
    Push-Location $checkout
    try {
        & $code
        $first = git --git-dir=$remote show release-index:config/release-index.json
        if ($LASTEXITCODE -ne 0 -or $first -notmatch '"fixture":1') { throw '初回公開結果が一致しません。' }
        & $code
        Set-Content -LiteralPath (Join-Path $runner 'release-index.json') -Value '{"fixture":2}' -Encoding utf8
        & $code
        $second = git --git-dir=$remote show release-index:config/release-index.json
        if ($LASTEXITCODE -ne 0 -or $second -notmatch '"fixture":2') { throw '公開情報の更新結果が一致しません。' }
    } finally { Pop-Location }
} finally { $env:RUNNER_TEMP = $previousRunner }
Write-Output 'PASS: workflowの初回作成・変更なし・通常更新をローカルリポジトリで確認しました。'
