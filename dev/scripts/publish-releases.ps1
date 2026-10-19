param(
    [Parameter(Mandatory)][string]$CatalogPath,
    [ValidateSet('KotorinToys','LiveTextMarker','SmartPaste','SSCapture','CapchunWindow','ChulipVideo','ChunapTool','EveryDB')][string[]]$Products=@('LiveTextMarker','SmartPaste','SSCapture'),
    [ValidatePattern('^v\d+\.\d+\.\d+$')][string]$RecordVersion
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot)
. (Join-Path $PSScriptRoot 'distribution-products.ps1')
if (-not $RecordVersion) { $RecordVersion = 'v' + (Get-Content (Join-Path $projectRoot 'catalog/host.json') -Raw | ConvertFrom-Json).version }
$catalogFile=[IO.Path]::GetFullPath($CatalogPath)
$allowed=[IO.Path]::GetFullPath((Join-Path $projectRoot "dev/history/$RecordVersion/logs"))+[IO.Path]::DirectorySeparatorChar
if(-not $catalogFile.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw '版別ログ内の配布台帳だけ公開できます。'}
$catalog=Get-Content -LiteralPath $catalogFile -Raw | ConvertFrom-Json
if($catalog.schemaVersion -ne 1 -or $catalog.distributionStatus -ne 'unpublished'){throw '正式ビルドのローカル台帳が必要です。'}
$ghExe=Join-Path $env:ProgramFiles 'GitHub CLI/gh.exe'
if(-not (Test-Path -LiteralPath $ghExe)){throw 'GitHub CLIがありません。'}
$account=& $ghExe api user --jq '.login'
if($LASTEXITCODE -ne 0 -or $account -ne 'KotorinChunChun'){throw 'KotorinChunChunの認証が必要です。'}
$directory=Split-Path $catalogFile
if($Products -contains 'KotorinToys'){
    # 公開config・候補・GitHub資産の不一致を、最初のRelease作成より前に拒否する。
    & (Join-Path $PSScriptRoot 'test-release-readiness.ps1') -ProjectRoot $projectRoot -RecordVersion $RecordVersion -Online
}
$published=@()
# 複数対象の後半が不正でも、前半だけを先に公開しない。
foreach($name in $Products | Select-Object -Unique){
    $entries=@($catalog.packages | Where-Object name -eq $name)
    if($entries.Count -ne 1){throw "公開前検査：対象が一意ではありません：$name"}
    $entry=$entries[0]; $identity=Get-DistributionProduct $name
    if($entry.id -ne $identity.Id -or $entry.appId -ne $identity.AppId -or $entry.version -notmatch '^\d+\.\d+\.\d+$' -or
       $entry.file -ne "$name-$($entry.version)-setup.exe" -or $entry.signature -ne 'unsigned-local-build' -or $entry.sha256 -notmatch '^[a-f0-9]{64}$'){
        throw '公開前検査：配布製品・版・形式が不正です。'
    }
    $candidate=Join-Path $directory $entry.file
    if((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256){throw '公開前検査：配布物のハッシュが不一致です。'}
    $repoPath=if($identity.Id -eq 'host'){$projectRoot}else{Join-Path $projectRoot "tools/$($identity.Folder)"}
    $commit=git -c "safe.directory=$repoPath" -C $repoPath rev-parse HEAD
    if($LASTEXITCODE){throw '公開前検査：採用コミットがありません。'}
    $remote=git -c "safe.directory=$repoPath" -C $repoPath ls-remote origin refs/heads/main
    if($LASTEXITCODE -or ($remote -split '\s+')[0] -ne $commit){throw '公開前検査：採用コミットが公開mainと一致しません。'}
}
foreach($name in $Products | Select-Object -Unique){
    $package=@($catalog.packages | Where-Object name -eq $name)
    if($package.Count -ne 1){throw "対象が一意ではありません：$name"}
    $package=$package[0]
    $identity=Get-DistributionProduct $name
    $id=$identity.Id
    $appId=$identity.AppId
    if($package.id -ne $id -or $package.appId -ne $appId -or $package.version -notmatch '^\d+\.\d+\.\d+$' -or $package.file -ne "$name-$($package.version)-setup.exe" -or $package.signature -ne 'unsigned-local-build' -or $package.sha256 -notmatch '^[a-f0-9]{64}$'){throw '配布製品・版・形式が不正です。'}
    $file=Join-Path $directory $package.file
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256){throw '公開直前のハッシュが一致しません。'}
    $repoPath=if($id -eq 'host'){$projectRoot}else{Join-Path $projectRoot "tools/$($identity.Folder)"}
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
    if($name -eq 'KotorinToys'){$notes+=" 本体の『本体アップデート』から新しいReleasesを照合・取得し、本体だけを正常終了・更新・再起動します。開発版は明示選択が必要です。初回はこの機能を含む版の手動導入が必要です。子ツールの実行状態と設定・履歴・自動起動予約は保持します。対応する子ツールの導入・更新はメイン一覧から操作します。"}
    if($name -eq 'KotorinToys'){$notes+=" メイン一覧からインストール・アップデート・条件付き修復・アンインストールを操作できます。理由は詳細セルから確認でき、表示の絞り込みと保存に対応します。一括操作・FFmpeg・配布台帳は用途別タブに分離しています。"}
    if($name -in @('CapchunWindow','ChulipVideo')){$notes+=" FFmpegは同梱しません。公式取得案内 https://ffmpeg.org/download.html を確認し、各アプリの設定で既存のffmpeg.exeを指定してください。"}
    if($name -eq 'CapchunWindow'){$notes+=" 仮想カメラのUnity Captureドライバーは別途必要です。入力記録は画面に映る機密情報やpassword入力を保護する保証ではありません。機密入力を含む録画・操作記録には使用しないでください。"}
    $releaseAssets = @($file, $shaFile, $manifest)
    if ($name -eq 'EveryDB') {
        $notes += ' EveryDBはネイティブ実装で、.NETはメンテナンス用にのみ同梱します。WebView2 Runtimeが必要です。本体0.1.5以降で導入・更新・削除に対応します。旧MSI／NSISの登録を自動移行せず、既存DBは保持します。'
        foreach ($license in @('LICENSE.md','licenses/THIRD_PARTY_NOTICES.txt')) {
            $copy = Join-Path $directory (Split-Path $license -Leaf)
            Copy-Item -LiteralPath (Join-Path $repoPath $license) -Destination $copy
            $releaseAssets += $copy
            "$((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant())  $(Split-Path $copy -Leaf)" | Add-Content -LiteralPath $shaFile -Encoding utf8
        }
    }
    & $ghExe release create "v$($package.version)" @releaseAssets --repo "KotorinChunChun/$($identity.Repository)" --target $commit --title "$name $($package.version) 開発版" --notes $notes --prerelease
    if($LASTEXITCODE -ne 0){throw 'Release作成が失敗しました。既存assetの上書き・再公開は自動実行しません。'}
    $published+= [ordered]@{package=$package;length=(Get-Item -LiteralPath $file).Length}
}
[ordered]@{schemaVersion=1;packages=$published} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $directory 'published-releases.json') -Encoding utf8
Write-Output "公開済み固定カタログ：$(Join-Path $directory 'published-releases.json')"
