param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [ValidateSet('KotorinToys','LiveTextMarker','SmartPaste','SSCapture','CapchunWindow','ChulipVideo','ChunapTool','EveryDB')]
    [string[]]$Products=@('KotorinToys','LiveTextMarker','SmartPaste'),
    [ValidatePattern('^v\d+\.\d+\.\d+$')][string]$RecordVersion
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot)
. (Join-Path $PSScriptRoot 'distribution-products.ps1')
Assert-CommonDistributionProducts $Products
if (-not $RecordVersion) { $RecordVersion = 'v' + (Get-Content (Join-Path $projectRoot 'catalog/host.json') -Raw | ConvertFrom-Json).version }
$packageRoot=[IO.Path]::GetFullPath($PackageDirectory)
$allowedRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/packages')) + [IO.Path]::DirectorySeparatorChar
if (-not $packageRoot.StartsWith($allowedRoot,[StringComparison]::OrdinalIgnoreCase)) { throw '専用パッケージ出力以外はコンパイルしません。' }
& (Join-Path $PSScriptRoot 'prepare-inno.ps1') -RecordVersion $RecordVersion
$compiler=Join-Path $projectRoot 'artifacts/dependencies/inno-7.1.0/ISCC.exe'
$runId='installers-'+[Guid]::NewGuid().ToString('N')
$output=Join-Path $projectRoot "dev/history/$RecordVersion/logs/$runId"
New-Item -ItemType Directory -Path $output | Out-Null
$packages=@()
foreach($product in $Products | Select-Object -Unique) {
    $payload=Join-Path $packageRoot $product
    & (Join-Path $PSScriptRoot 'test-public-payload-security.ps1') -Payload $payload -ReportDirectory (Join-Path $output "$product-security")
    $definition=Get-Content -LiteralPath (Join-Path $payload 'tool.json') -Raw | ConvertFrom-Json
    $manifest=Get-Content -LiteralPath (Join-Path $packageRoot "$product.files.json") -Raw | ConvertFrom-Json
    foreach($entry in $manifest.files){
        $file=[IO.Path]::GetFullPath((Join-Path $payload $entry.path))
        if(-not $file.StartsWith($payload+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'ファイル台帳の範囲外参照です。' }
        if((Get-Item -LiteralPath $file).Length -ne $entry.length -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256){throw "配布入力のハッシュが不一致です：$product/$($entry.path)"}
    }
    $identity = Get-DistributionProduct $product
    $expectedId=$identity.AppId
    if($definition.name -ne $product -or $definition.appId -ne $expectedId -or $definition.version -notmatch '^\d+\.\d+\.\d+$'){throw '製品定義が不正です。'}
    if(@(Get-ChildItem -LiteralPath $payload -File -Recurse).Count -ne $manifest.files.Count){throw '台帳にない追加ファイルがあります。'}
    $exeInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload "$product.exe"))
    if ($exeInfo.ProductName -ne $product -or "$($exeInfo.FileMajorPart).$($exeInfo.FileMinorPart).$($exeInfo.FileBuildPart)" -ne $definition.version) { throw '配布EXEの製品名・版が定義と一致しません。' }
    $arguments=@('--quiet-progress',"--define=ProductName=$product","--define=ProductAppId=$($definition.appId)","--define=ProductVersion=$($definition.version)","--define=Payload=$payload","--define=OutputDirectory=$output",(Join-Path $projectRoot 'installers/product.iss'))
    & $compiler @arguments 2>&1 | Tee-Object -FilePath (Join-Path $output "$product-compile.log") | Out-Host
    if($LASTEXITCODE -ne 0){throw "$product のインストーラーコンパイルに失敗しました。"}
    $filename="$product-$($definition.version)-setup.exe"
    $packages += [ordered]@{id=$definition.id;name=$product;version=$definition.version;appId=$definition.appId;file=$filename;sha256=(Get-FileHash -LiteralPath (Join-Path $output $filename) -Algorithm SHA256).Hash.ToLowerInvariant();signature='unsigned-local-build'}
}
[ordered]@{schemaVersion=1;distributionStatus='unpublished';packages=$packages} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'packages.json') -Encoding utf8
Write-Output "インストーラーと固定ハッシュ台帳: $output"
