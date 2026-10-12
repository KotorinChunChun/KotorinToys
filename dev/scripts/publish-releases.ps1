param(
    [Parameter(Mandatory)][string]$CatalogPath,
    [ValidateSet('KotorinToys','LiveTextMarker','SmartPaste','SSCapture')][string[]]$Products=@('LiveTextMarker','SmartPaste','SSCapture')
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot)
$catalogFile=[IO.Path]::GetFullPath($CatalogPath)
$allowed=[IO.Path]::GetFullPath((Join-Path $projectRoot 'dev/history/v0.1.0/logs'))+[IO.Path]::DirectorySeparatorChar
if(-not $catalogFile.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw '版別ログ内の配布台帳だけ公開できます。'}
$catalog=Get-Content -LiteralPath $catalogFile -Raw | ConvertFrom-Json
if($catalog.schemaVersion -ne 1 -or $catalog.distributionStatus -ne 'unpublished'){throw '正式ビルドのローカル台帳が必要です。'}
$ghExe=Join-Path $env:ProgramFiles 'GitHub CLI/gh.exe'
if(-not (Test-Path -LiteralPath $ghExe)){throw 'GitHub CLIがありません。'}
$account=& $ghExe api user --jq '.login'
if($LASTEXITCODE -ne 0 -or $account -ne 'KotorinChunChun'){throw 'KotorinChunChunの認証が必要です。'}
$directory=Split-Path $catalogFile
$published=@()
foreach($name in $Products | Select-Object -Unique){
    $package=@($catalog.packages | Where-Object name -eq $name)
    if($package.Count -ne 1){throw "対象が一意ではありません：$name"}
    $package=$package[0]
    $id=switch($name){'KotorinToys'{'host'} 'LiveTextMarker'{'live-text-marker'} 'SmartPaste'{'smart-paste'} 'SSCapture'{'ss-capture'}}
    $appId=if($id -eq 'host'){'KotorinToys'}else{'KotorinToys.'+$name}
    if($package.id -ne $id -or $package.appId -ne $appId -or $package.version -notmatch '^\d+\.\d+\.\d+$' -or $package.file -ne "$name-$($package.version)-setup.exe" -or $package.signature -ne 'unsigned-local-build' -or $package.sha256 -notmatch '^[a-f0-9]{64}$'){throw '配布製品・版・形式が不正です。'}
    $file=Join-Path $directory $package.file
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256){throw '公開直前のハッシュが一致しません。'}
    $repoPath=if($id -eq 'host'){$projectRoot}else{Join-Path $projectRoot "tools/$name"}
    $commit=git -c "safe.directory=$repoPath" -C $repoPath rev-parse HEAD
    if($LASTEXITCODE -ne 0){throw '採用コミットがありません。'}
    $remote=git -c "safe.directory=$repoPath" -C $repoPath ls-remote origin refs/heads/main
    if($LASTEXITCODE -ne 0 -or ($remote -split '\s+')[0] -ne $commit){throw '公開前に採用コミットをpushしてください。'}
    $shaFile=Join-Path $directory "$name-SHA256SUMS.txt"
    "$($package.sha256)  $($package.file)" | Set-Content -LiteralPath $shaFile -Encoding utf8
    $package.signature='unsigned-public-build'
    $manifest=Join-Path $directory "$name-packages.json"
    [ordered]@{schemaVersion=1;distributionStatus='published';packages=@($package)} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifest -Encoding utf8
    $notes="Windows 11 x64向けの未署名の開発版（prerelease）です。.NETランタイム同梱。設定・保存データは保持します。SHA256SUMSで配布物を確認できます。全機能の受入完了版ではありません。"
    if($name -eq 'SSCapture'){$notes+=" FFmpeg／FFprobe・OneOCR外部ファイルは同梱しません。録画にはFFmpeg公式の取得案内 https://ffmpeg.org/download.html を参照し、ffmpeg.exeとffprobe.exeを導入先のtoolsへ別途配置してください。"}
    if($name -eq 'KotorinToys'){$notes+=" 収録3ツールは導入・削除画面からGitHub Releasesの固定版を取得し、製品・版・容量・SHA-256を照合して導入できます。"}
    & $ghExe release create "v$($package.version)" $file $shaFile $manifest --repo "KotorinChunChun/$name" --target $commit --title "$name $($package.version) 開発版" --notes $notes --prerelease
    if($LASTEXITCODE -ne 0){throw 'Release作成が失敗しました。既存assetの上書き・再公開は自動実行しません。'}
    $published+= [ordered]@{package=$package;length=(Get-Item -LiteralPath $file).Length}
}
[ordered]@{schemaVersion=1;packages=$published} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $directory 'published-releases.json') -Encoding utf8
Write-Output "公開済み固定カタログ：$(Join-Path $directory 'published-releases.json')"
