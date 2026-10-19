#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$run = Join-Path $projectRoot ('dev/history/v0.1.7/logs/voice-registration-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$output = Join-Path $run 'host-documents'
New-Item -ItemType Directory -Path $output | Out-Null

# 製品EXEを発行せず、本番スクリプトの本体向け同梱処理だけを実行する。
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

$checked = 0
foreach ($name in @('VoiceMixerMicRouter','VoiceMixerSpkRouter')) {
    foreach ($relative in @('tool.json','README.md','docs/kotorintoys-integration.md')) {
        $source = Join-Path $projectRoot "tools/$name/$relative"
        $actual = Join-Path $output "tools/$name/$relative"
        if (-not (Test-Path -LiteralPath $actual -PathType Leaf) -or (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $actual).Hash) { throw "同梱漏れ・不一致: $name/$relative" }
        $checked++
    }
}
if (@(Get-ChildItem -LiteralPath (Join-Path $output 'tools') -File -Recurse | Where-Object Extension -in @('.exe','.dll','.pdb','.db','.sqlite','.key','.pfx')).Count) { throw '子の実行物・利用者データが本体説明書へ混入しています。' }
$buildTokens = $null; $buildErrors = $null
$build = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'build.ps1'), [ref]$buildTokens, [ref]$buildErrors)
if ($buildErrors.Count) { throw '開発ビルドスクリプトの構文が不正です。' }
foreach ($manifest in @('tools/VoiceMixerMicRouter/src/src-tauri/Cargo.toml','tools/VoiceMixerSpkRouter/Cargo.toml')) {
    $commands = @($build.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'cargo'}, $true) | Where-Object { $_.CommandElements.Extent.Text -contains $manifest })
    if ($commands.Count -ne 1 -or $commands[0].CommandElements.Extent.Text -notcontains '--locked') { throw "固定依存の開発ビルド漏れ: $manifest" }
    $checked++
}
"PASS: 定義・README・管理説明書6件とネイティブビルド指定2件（計${checked}件）。子EXEの発行・起動・公開・設定変更なし。"
"検証出力: $run"
