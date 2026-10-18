param([Parameter(Mandatory)][string]$PackageDirectory,[string]$Ref='HEAD')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot)
$inputRoot=[IO.Path]::GetFullPath($PackageDirectory)
$allowed=[IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/packages'))+[IO.Path]::DirectorySeparatorChar
if(-not $inputRoot.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw '専用パッケージ配下の入力が必要です。'}
$inputPayload=Join-Path $inputRoot 'KotorinToys'
$manifest=Get-Content -LiteralPath (Join-Path $inputRoot 'KotorinToys.files.json') -Raw | ConvertFrom-Json
if($manifest.product -ne 'KotorinToys' -or $manifest.schemaVersion -ne 1){throw '本体のファイル台帳が必要です。'}
$commit=(& git -C $projectRoot rev-parse "$Ref^{commit}").Trim()
if($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$'){throw '採用した親コミットを特定できません。'}
$childLine=& git -C $projectRoot ls-tree $commit -- tools/HttpSelector
if($LASTEXITCODE -ne 0 -or $childLine -notmatch '^160000 commit ([a-f0-9]{40})\s+tools/HttpSelector$'){throw '採用したHttpSelectorコミットを特定できません。'}
$childCommit=$Matches[1]
$version=(Get-Content -LiteralPath (Join-Path $inputPayload 'tool.json') -Raw | ConvertFrom-Json).version
$runId='public-payload-'+[Guid]::NewGuid().ToString('N')
$output=Join-Path $projectRoot "artifacts/packages/$runId"
$payload=Join-Path $output 'KotorinToys'
$logs=Join-Path $projectRoot "dev/history/v$version/logs/$runId"
New-Item -ItemType Directory -Path $payload,$logs | Out-Null
$kept=0
foreach($entry in $manifest.files){
    $source=[IO.Path]::GetFullPath((Join-Path $inputPayload $entry.path))
    $target=[IO.Path]::GetFullPath((Join-Path $payload $entry.path))
    if(-not $source.StartsWith($inputPayload+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
       -not $target.StartsWith($payload+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '台帳の範囲外参照です。'}
    if((Get-Item -LiteralPath $source).Length -ne $entry.length -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256){throw "入力台帳のハッシュが不一致です：$($entry.path)"}
    if($entry.path.Replace('\','/').StartsWith('tools/HttpSelector/')){continue}
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256){throw 'コピー後のハッシュ不一致です。'}
    $kept++
}
$archive=Join-Path $logs 'adopted-documents.zip'
& git -C (Join-Path $projectRoot 'tools/HttpSelector') archive --format=zip "--output=$archive" $childCommit README.md tool.json docs
if($LASTEXITCODE -ne 0){throw '採用した子の資料だけを抽出できません。'}
$childOutput=Join-Path $payload 'tools/HttpSelector'
New-Item -ItemType Directory -Path $childOutput -Force | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($archive,$childOutput)
if(@(Get-ChildItem -LiteralPath $childOutput -File -Recurse | Where-Object Extension -in @('.exe','.dll','.pfx','.pem','.key','.db','.sqlite')).Count){throw '子の実行物・利用者データ・秘密鍵が混入しています。'}
$files=@(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object{
    [ordered]@{path=[IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/');length=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
[ordered]@{schemaVersion=1;product='KotorinToys';status='staged-not-installed';files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'KotorinToys.files.json') -Encoding utf8
[ordered]@{parentCommit=$commit;adoptedHttpSelector=$childCommit;unchangedFiles=$kept;files=$files.Count;installerInput=$output} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logs 'receipt.json') -Encoding utf8
Write-Output "公開用入力：$output"
Write-Output "実行物を含む$kept 件の既存ファイルはハッシュ不変。HttpSelector資料は採用コミットだけから抽出。"
