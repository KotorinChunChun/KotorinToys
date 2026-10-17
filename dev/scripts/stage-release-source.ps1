param([string]$Ref = 'HEAD')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$commit = (& git -C $projectRoot rev-parse "$Ref^{commit}").Trim()
if ($LASTEXITCODE -or $commit -notmatch '^[a-f0-9]{40}$') { throw '公開元コミットを解決できません。' }
$output = Join-Path $projectRoot ('artifacts/release-sources/' + $commit.Substring(0,12) + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$archive = Join-Path $output 'parent.zip'
& git -C $projectRoot archive --format=zip --output=$archive $commit
if ($LASTEXITCODE) { throw '本体の採用ソースを抽出できません。' }
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $output, [Text.Encoding]::UTF8, $false)
$adopted = @()
foreach ($line in (& git -C $projectRoot ls-tree $commit tools/)) {
    if ($line -notmatch '^160000 commit ([a-f0-9]{40})\s+(tools/[A-Za-z0-9._-]+)$') { continue }
    $childCommit = $Matches[1]; $relative = $Matches[2]
    $repository = Join-Path $projectRoot $relative
    $target = Join-Path $output $relative
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $childArchive = Join-Path $output ((Split-Path $relative -Leaf) + '.zip')
    & git -c "safe.directory=$repository" -C $repository archive --format=zip --output=$childArchive $childCommit
    if ($LASTEXITCODE) { throw "採用した子ソースがありません：$relative/$childCommit" }
    [IO.Compression.ZipFile]::ExtractToDirectory($childArchive, $target, [Text.Encoding]::UTF8, $false)
    $adopted += [ordered]@{ path=$relative; commit=$childCommit }
}
if (-not $adopted.Count) { throw '子の採用コミットがありません。' }
[ordered]@{ commit=$commit; children=$adopted } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'release-source.json') -Encoding utf8
Write-Output "採用コミットだけの配布入力：$output"
