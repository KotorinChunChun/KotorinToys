param([string]$ProjectRoot, [ValidatePattern('^\d+\.\d+\.\d+$')][string]$RecordVersion = '0.1.7')
$ErrorActionPreference = 'Stop'
$project = if ($ProjectRoot) { [IO.Path]::GetFullPath($ProjectRoot) } else { Split-Path (Split-Path $PSScriptRoot) }
$base = Join-Path $project ("dev/history/v$RecordVersion/logs/security-gates-" + [guid]::NewGuid().ToString('N'))
$gate = Join-Path $project 'dev/scripts/test-public-payload-security.ps1'
New-Item -ItemType Directory -Force -Path $base | Out-Null
$count = 0
function Test-Case([string]$Name, [string]$File, [string]$Content, [bool]$ExpectedPass, [string]$ExpectedReason = '') {
    $payload = Join-Path $base "$Name/payload"
    New-Item -ItemType Directory -Force -Path $payload | Out-Null
    [IO.File]::WriteAllText((Join-Path $payload $File), $Content)
    $passed = $true
    try { & $gate -Payload $payload -ReportDirectory (Join-Path $base "$Name/reports") | Out-Null }
    catch {
        $passed = $false
        if ($ExpectedReason -and -not $_.Exception.Message.Contains($ExpectedReason)) { throw "想定外の理由で拒否しました: $Name" }
    }
    if ($passed -ne $ExpectedPass) { throw "配布安全ゲートの期待不一致: $Name" }
    $script:count++
    Write-Output "PASS: $Name"
}
Test-Case 'clean' 'README.md' '公開可能な説明だけを含むテストです。' $true
Test-Case 'public-shortcuts' 'shortcuts.json' '{"keys":"Ctrl+Shift+F10","otherKeys":"Ctrl+Shift+F12"}' $true
foreach ($extension in @('pfx','p12','pem','key','pdb','db','sqlite','sqlite3')) {
    Test-Case "reject-$extension" "fixture.$extension" '実データではない架空入力' $false
}
Test-Case 'reject-dotenv' '.env' '架空設定' $false
# 実際の認証情報ではない。実行ごとに無効な文字列を生成し、値は表示しない。
$fake = 'gh' + 'p_' + [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N').Substring(0,4)
Test-Case 'reject-synthetic-token' 'fixture.txt' ('github_token = "' + $fake + '"') $false '秘密情報の検出'
Test-Case 'reject-token-near-shortcuts' 'shortcuts.json' ('{"keys":"Ctrl+Shift+F10","github_token":"' + $fake + '"}') $false '秘密情報の検出'
$secretReport = Get-Content (Join-Path $base 'reject-synthetic-token/reports/secrets-redacted.json') -Raw | ConvertFrom-Json
if (@($secretReport | Where-Object { $_.RuleID -eq 'github-pat' }).Count -ne 1) { throw '架空トークンの検出結果を確認できません。' }
$obsolete = '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}'
Test-Case 'reject-obsolete-runtime' 'fixture.runtimeconfig.json' $obsolete $false '公式の最新修正版より古い'
Write-Output "配布安全ゲート $count 件成功"
