[CmdletBinding(DefaultParameterSetName = 'Hook')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Test')][guid]$SessionId,
    [Parameter(Mandatory, ParameterSetName = 'Test')][guid]$TurnId,
    [Parameter(Mandatory, ParameterSetName = 'Test')][ValidateNotNullOrEmpty()][string]$CliExecutable,
    [Parameter(ParameterSetName = 'Test')][string[]]$CliArguments = @(),
    [Parameter(Mandatory, ParameterSetName = 'Test')][ValidateNotNullOrEmpty()][string]$GuiCheck
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path

function Get-TestDirectory([guid]$Session, [guid]$Turn) {
    $catalog = Get-Content -LiteralPath (Join-Path $projectRoot 'catalog/host.json') -Raw | ConvertFrom-Json
    $version = [version]::Parse($catalog.version).ToString()
    Join-Path $projectRoot "dev/history/v$version/logs/computer-use-cli-$($Session.ToString('N'))-$($Turn.ToString('N'))"
}

function Get-SourceCommit {
    $commit = & git -C $projectRoot rev-parse --verify HEAD
    if ($LASTEXITCODE -ne 0) { throw 'テスト対象のコミットを確認できません。' }
    $commit
}

if ($PSCmdlet.ParameterSetName -eq 'Test') {
    if ([string]::IsNullOrWhiteSpace($GuiCheck)) { throw '実画面でしか確認できない項目を指定してください。' }
    $directory = Get-TestDirectory $SessionId $TurnId
    $receiptPath = Join-Path $directory 'passed.json'
    $logPath = Join-Path $directory 'cli.log'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    # 再試験に失敗したときは以前の合格記録を再利用させない。
    [IO.File]::Delete($receiptPath)
    $sourceCommit = Get-SourceCommit
    $command = Get-Command -Name $CliExecutable -CommandType Application -ErrorAction Stop | Select-Object -First 1
    & $command.Source @CliArguments *> $logPath
    if ($LASTEXITCODE -ne 0) { throw "CLI検証が終了コード $LASTEXITCODE で失敗しました。ログ $logPath" }
    if ((Get-SourceCommit) -ne $sourceCommit) { throw 'CLI検証中に採用コミットが変わりました。再実行してください。' }
    @{
        sourceCommit = $sourceCommit
        guiCheck = $GuiCheck.Trim()
        executable = $command.Name
        exitCode = 0
        passedAt = [DateTimeOffset]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8
    Write-Output "CLI検証に合格しました。残る実画面確認 $GuiCheck"
    Write-Output "ログ $logPath"
    return
}

if (!$IsWindows) { return }
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
if ($request.hook_event_name -ne 'PreToolUse') { return }
# CUAはブラウザーとネイティブで同じツール名を使う。ネイティブを開く入口だけを対象にする。
if ($request.tool_input.code -notmatch '\bcua\s*\.\s*(?:getApp|computer\s*\.\s*launch_app)\s*\(') { return }

try {
    $session = [guid]::Parse($request.session_id)
    $turn = [guid]::Parse($request.turn_id)
    $directory = Get-TestDirectory $session $turn
    $receiptPath = Join-Path $directory 'passed.json'
    if ((Test-Path -LiteralPath $receiptPath -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $directory 'cli.log') -PathType Leaf)) {
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
        if ($receipt.exitCode -eq 0 -and $receipt.sourceCommit -eq (Get-SourceCommit) -and
            ![string]::IsNullOrWhiteSpace($receipt.guiCheck)) { return }
    }
    $reason = "対象機能をCLIで検証してからWindowsアプリを開いてください。dev/scripts/confirm-cli-before-computer-use.ps1 に -SessionId $session -TurnId $turn と CLIコマンド、実画面でしか確認できない項目を渡します。docs/testing.md の手順を参照してください。"
} catch {
    $reason = 'CLI検証の記録を照合できません。現在のセッションとターンで検証をやり直してください。'
}
@{ hookSpecificOutput = @{
    hookEventName = 'PreToolUse'
    permissionDecision = 'deny'
    permissionDecisionReason = $reason
} } | ConvertTo-Json -Compress
