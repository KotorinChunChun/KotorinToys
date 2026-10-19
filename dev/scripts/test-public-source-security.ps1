param(
    [string]$Output = 'dev/history/v0.1.7/logs/audit-20261019',
    [switch]$SkipLocalHistory
)
$ErrorActionPreference = 'Stop'
$project = Split-Path (Split-Path $PSScriptRoot)
$outputPath = [IO.Path]::GetFullPath((Join-Path $project $Output))
if (-not $outputPath.StartsWith((Join-Path $project 'dev/history/v0.1.7/logs/').Replace('/', '\'), [StringComparison]::OrdinalIgnoreCase)) { throw '出力は対象版のlogs内に限定します。' }
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
. (Join-Path $PSScriptRoot 'get-security-tools.ps1')
$scanner = Get-SecurityGitleaks
$rules = Join-Path $PSScriptRoot 'security-audit.rules.toml'
$targets = @([pscustomobject]@{ name = 'KotorinToys'; toolRoot = '.'; repositoryUrl = 'https://github.com/KotorinChunChun/KotorinToys' }) + @((Get-Content (Join-Path $project 'config/tools.json') -Raw | ConvertFrom-Json).tools)
$receipts = @()
foreach ($target in $targets) {
    if ($target.repositoryUrl -notmatch '^https://github.com/KotorinChunChun/([A-Za-z0-9][A-Za-z0-9.-]{0,99})$') { throw '固定所有者以外の監査は実行しません。' }
    $repository = $Matches[1]
    if ($target.toolRoot -ne '.' -and ($target.toolRoot -notmatch '^tools/[A-Za-z0-9][A-Za-z0-9._-]*$')) { throw 'ローカル走査先は収録ツール直下に限定します。' }
    $itemOutput = Join-Path $outputPath $repository
    New-Item -ItemType Directory -Force -Path $itemOutput | Out-Null
    $info = gh api "repos/KotorinChunChun/$repository" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $info.visibility -ne 'public') { throw "$repository の公開先を確認できません。" }
    $releasePages = gh api --paginate --slurp "repos/KotorinChunChun/$repository/releases?per_page=100" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "$repository の公開一覧を取得できません。" }
    $releases = @($releasePages | ForEach-Object { $_ } | Where-Object { -not $_.draft })
    $releaseInfo = @($releases | ForEach-Object { [pscustomobject]@{ tag = $_.tag_name; publishedAt = $_.published_at; prerelease = $_.prerelease; assets = @($_.assets | Select-Object name,size,digest,browser_download_url) } })
    $releaseInfo | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $itemOutput 'public-releases.json') -Encoding utf8
    $mirror = Join-Path $itemOutput 'public.git'
    if (-not (Test-Path -LiteralPath $mirror)) {
        & git -c credential.interactive=false clone --quiet --mirror ($target.repositoryUrl + '.git') $mirror
    } else { & git -C $mirror fetch --quiet origin }
    if ($LASTEXITCODE -ne 0) { throw "$repository の公開履歴を取得できません。" }
    $head = & git -C $mirror rev-parse ('refs/heads/' + $info.default_branch)
    $commits = & git -C $mirror rev-list --all --count
    $archive = Join-Path $itemOutput 'public-head.zip'
    $snapshot = Join-Path $itemOutput ('public-head-' + $head.Substring(0,12))
    if (-not (Test-Path -LiteralPath $snapshot)) {
        & git -C $mirror archive --format=zip "--output=$archive" $head
        if ($LASTEXITCODE -ne 0) { throw "$repository の公開HEADを抽出できません。" }
        Expand-Archive -LiteralPath $archive -DestinationPath $snapshot
    }
    $findings = @()
    $scans = @([pscustomobject]@{ scope = 'public-history'; root = $mirror })
    if (-not $SkipLocalHistory) { $scans += [pscustomobject]@{ scope = 'local-history'; root = [IO.Path]::GetFullPath((Join-Path $project $target.toolRoot)) } }
    foreach ($scan in $scans) {
        $report = Join-Path $itemOutput ($scan.scope + '-redacted.json')
        $errorLog = Join-Path $itemOutput ($scan.scope + '-scanner-errors.log')
        $configCount = [Environment]::GetEnvironmentVariable('GIT_CONFIG_COUNT', 'Process')
        $index = if ([string]::IsNullOrEmpty($configCount)) { 0 } else { [int]$configCount }
        $keyName = "GIT_CONFIG_KEY_$index"; $valueName = "GIT_CONFIG_VALUE_$index"
        $oldKey = [Environment]::GetEnvironmentVariable($keyName, 'Process'); $oldValue = [Environment]::GetEnvironmentVariable($valueName, 'Process')
        try {
            [Environment]::SetEnvironmentVariable('GIT_CONFIG_COUNT', [string]($index + 1), 'Process')
            [Environment]::SetEnvironmentVariable($keyName, 'safe.directory', 'Process')
            [Environment]::SetEnvironmentVariable($valueName, $scan.root.Replace('\','/'), 'Process')
            & $scanner git $scan.root --log-opts=--all --config $rules --gitleaks-ignore-path $outputPath --ignore-gitleaks-allow --redact=100 --no-banner --no-color --log-level error --timeout 600 --report-format json --report-path $report 2> $errorLog
            $scanExit = $LASTEXITCODE
        } finally {
            [Environment]::SetEnvironmentVariable('GIT_CONFIG_COUNT', $configCount, 'Process')
            [Environment]::SetEnvironmentVariable($keyName, $oldKey, 'Process')
            [Environment]::SetEnvironmentVariable($valueName, $oldValue, 'Process')
        }
        if ((Test-Path -LiteralPath $errorLog) -and (Get-Item -LiteralPath $errorLog).Length -gt 0) { throw "$repository / $($scan.scope) の走査エラーがあります。成功として扱いません。" }
        if ($scanExit -notin @(0,1)) { throw "$repository / $($scan.scope) の秘密検査が未完了です（exit=$scanExit）。" }
        if (Test-Path -LiteralPath $report) {
            $rows = @(Get-Content -LiteralPath $report -Raw | ConvertFrom-Json)
            foreach ($row in $rows) { $findings += [pscustomobject]@{scope=$scan.scope;rule=$row.RuleID;file=$row.File;line=$row.StartLine;commit=$row.Commit} }
        }
    }
    $files = @(& git -C $mirror ls-tree -r --name-only $head)
    $sensitiveNames = @($files | Where-Object { $_ -match '(^|/)(\.env($|\.(?!example$))|id_(rsa|dsa|ed25519)($|\.)|credentials\.json$|secrets\.json$)|\.(pfx|p12|key|pem|db|sqlite|sqlite3)$' })
    $receipt = [pscustomobject]@{repository=$repository;folder=$target.toolRoot;visibility=$info.visibility;branch=$info.default_branch;head=$head;commits=[int]$commits;files=$files.Count;releases=$releases.Count;assets=@($releases.assets).Count;assetBytes=($releases.assets | Measure-Object size -Sum).Sum;suspiciousFiles=$sensitiveNames;findings=$findings}
    $receipts += $receipt
    $receipts | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'source-progress.json') -Encoding utf8
    [pscustomobject]@{repo=$repository;commits=[int]$commits;files=$files.Count;releases=$releases.Count;findings=$findings.Count;suspiciousFiles=$sensitiveNames.Count} | ConvertTo-Json -Compress
}
$receipts | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'source-summary.partial.json') -Encoding utf8
Move-Item -LiteralPath (Join-Path $outputPath 'source-summary.partial.json') -Destination (Join-Path $outputPath 'source-summary.json') -Force
if (@($receipts | Where-Object { $_.findings.Count -or $_.suspiciousFiles.Count }).Count) { throw '公開ソースに要対応の検出があります。値は表示せずマスク済み台帳を確認してください。' }
Write-Output '公開ソースとGit到達可能履歴の完全マスク検査を完了しました。検出値は表示していません。'
