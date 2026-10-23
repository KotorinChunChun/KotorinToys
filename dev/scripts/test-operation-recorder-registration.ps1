#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$version = 'v' + (Get-Content (Join-Path $projectRoot 'catalog/host.json') -Raw | ConvertFrom-Json).version
$run = Join-Path $projectRoot ("dev/history/$version/logs/operation-recorder-registration-" + [guid]::NewGuid().ToString('N'))
$output = Join-Path $run 'host-documents'
New-Item -ItemType Directory -Path $output -Force | Out-Null

# 製品発行を行わず、本番スクリプトの本体説明書同梱部分を実行する。
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'publish-packages.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw '配布スクリプトの構文が不正です。' }
$copy = $ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Copy-Required'}, $true)
if (-not $copy) { throw '本番の必須ファイルコピー関数がありません。' }
. ([scriptblock]::Create($copy.Extent.Text))
function Publish-Application { param($Project, $Output, $Label, $Extra) }
function Assert-Files { param($Root, $RelativePaths) }
$selection = $ast.Find({param($node) $node -is [Management.Automation.Language.SwitchStatementAst] -and $node.Condition.Extent.Text -eq '$product'}, $true)
$hostClause = @($selection.Clauses | Where-Object { $_.Item1.Value -eq 'KotorinToys' })
if ($hostClause.Count -ne 1) { throw '本体の同梱処理が一意ではありません。' }
$body = $hostClause[0].Item2.Extent.Text.Trim()
& ([scriptblock]::Create($body.Substring(1, $body.Length - 2)))

$definitionOutput = Join-Path $output 'tools/OperationRecorder'
foreach ($relative in @('tool.json','README.md','THIRD-PARTY-NOTICES.md','docs/kotorintoys-integration.md','operation-recorder/MANUAL.md','config/shortcuts.json','Assets/app.ico')) {
    $source = Join-Path $projectRoot "tools/OperationRecorder/$relative"
    $actual = Join-Path $definitionOutput $relative
    if (-not (Test-Path -LiteralPath $actual -PathType Leaf) -or (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $actual).Hash) {
        throw "同梱漏れ・不一致: OperationRecorder/$relative"
    }
}
if (@(Get-ChildItem -LiteralPath $definitionOutput -File -Recurse | Where-Object Extension -in @('.exe','.dll','.pdb','.db','.sqlite','.key','.pfx')).Count) {
    throw 'OperationRecorderの実行物・利用者データが本体説明書へ混入しています。'
}
foreach ($markdown in Get-ChildItem -LiteralPath $definitionOutput -Filter '*.md' -File -Recurse) {
    $content = Get-Content -LiteralPath $markdown.FullName -Raw
    foreach ($link in [regex]::Matches($content, '\]\(([^)]+)\)')) {
        $target = $link.Groups[1].Value
        if ($target -match '^(https?://|#)') { continue }
        $relativeTarget = ($target -split '#', 2)[0]
        if (-not (Test-Path -LiteralPath (Join-Path $markdown.DirectoryName $relativeTarget))) {
            throw "本体同梱説明書のリンク切れ: $($markdown.Name) -> $target"
        }
    }
}
$buildTokens = $null; $buildErrors = $null
$build = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'build.ps1'), [ref]$buildTokens, [ref]$buildErrors)
if ($buildErrors.Count) { throw '開発ビルドスクリプトの構文が不正です。' }
$commands = @($build.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'dotnet'}, $true) |
    Where-Object { $_.CommandElements.Extent.Text -contains 'tools/OperationRecorder/OperationRecorderApp.csproj' })
if ($commands.Count -ne 1 -or $commands[0].CommandElements.Extent.Text -notcontains 'tools/OperationRecorder/artifacts/management') {
    throw 'OperationRecorderの専用管理ビルド指定がありません。'
}
'PASS: OperationRecorderの説明書・定義・キー一覧・アイコンの同梱、説明書リンク、開発ビルド指定。子EXEの発行・起動・公開なし。'
"検証出力: $run"
