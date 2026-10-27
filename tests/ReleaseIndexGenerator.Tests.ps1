param([Parameter(Mandatory)][string]$FixtureRoot)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
$fixture = [IO.Path]::GetFullPath($FixtureRoot)
if (-not $fixture.StartsWith((Join-Path $projectRoot 'dev/history') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $fixture -notmatch '[\\/]logs[\\/]') { throw '試験出力先は版別logs内に指定してください。' }
New-Item -ItemType Directory -Force -Path $fixture, (Join-Path $fixture 'responses') | Out-Null
$responses = Join-Path $fixture 'responses'
$config = Join-Path $fixture 'tools.json'
$output = Join-Path $fixture 'index.json'
$json = [ordered]@{ schemaVersion = 1; tools = @(
    @{ id = 'voice-mixer-spk-router'; distributionStatus = 'published'; repositoryUrl = 'https://github.com/KotorinChunChun/VoiceMixerSpkRouter' },
    @{ id = 'shortcut-key-counter'; distributionStatus = 'unpublished'; repositoryUrl = 'https://github.com/KotorinChunChun/ShortcutKeyCounter' },
    @{ id = 'private-tool'; distributionStatus = 'unpublished'; repositoryUrl = 'https://example.com/private' }
) }
$json | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $config -Encoding utf8
$release = @(@{ tag_name = 'v1.0.11'; draft = $false; prerelease = $true; published_at = '2026-10-27T13:59:13Z';
    body = '一覧へ含めない情報'; assets = @(@{ name = 'a.exe'; state = 'uploaded'; size = 4; digest = 'sha256:test';
        browser_download_url = 'https://github.com/KotorinChunChun/VoiceMixerSpkRouter/releases/download/v1.0.11/a.exe' }) })
ConvertTo-Json -InputObject $release -Depth 10 | Set-Content -LiteralPath (Join-Path $responses 'VoiceMixerSpkRouter.json') -Encoding utf8
$many = @(1..100 | ForEach-Object { $release[0] })
ConvertTo-Json -InputObject $many -Depth 10 | Set-Content -LiteralPath (Join-Path $responses 'ShortcutKeyCounter.json') -Encoding utf8
& (Join-Path $projectRoot 'dev/scripts/update-release-index.ps1') -OutputPath $output -ConfigPath $config -FixtureDirectory $responses
$result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
$spk = @($result.repositories | Where-Object name -eq 'VoiceMixerSpkRouter')
$manyResult = @($result.repositories | Where-Object name -eq 'ShortcutKeyCounter')
$missing = @($result.repositories | Where-Object name -eq 'KotorinToys')
if ($result.schemaVersion -ne 1 -or @($result.repositories).Count -ne 3 -or $spk.Count -ne 1 -or -not $spk[0].available -or
    $spk[0].releases[0].tag_name -ne 'v1.0.11' -or $spk[0].releases[0].PSObject.Properties.Name -contains 'body' -or
    $manyResult.Count -ne 1 -or $manyResult[0].available -or $missing.Count -ne 1 -or $missing[0].available) {
    throw '公開一覧の取得・失敗・抽出条件が一致しません。'
}
$json.tools[0].repositoryUrl = 'https://example.com/not-github'
$json | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $config -Encoding utf8
$rejected = $false
try { & (Join-Path $projectRoot 'dev/scripts/update-release-index.ps1') -OutputPath $output -ConfigPath $config -FixtureDirectory $responses }
catch { $rejected = $true }
if (-not $rejected) { throw '許可外の公開先を拒否しませんでした。' }
Write-Output 'PASS: 公開一覧の生成・失敗時の扱い・対象外情報・公開先を確認しました。'
