param(
    [Parameter(Mandatory)][string]$CatalogPath,
    [ValidateSet('KotorinToys','LiveTextMarker','SmartPaste','SSCapture')]
    [string[]]$Products=@('KotorinToys','LiveTextMarker','SmartPaste','SSCapture'),
    [switch]$CustomValidationDirectory,
    [ValidatePattern('^v\d+\.\d+\.\d+$')][string]$RecordVersion
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot)
if (-not $RecordVersion) { $RecordVersion = 'v' + (Get-Content (Join-Path $projectRoot 'catalog/host.json') -Raw | ConvertFrom-Json).version }
$catalogFile=[IO.Path]::GetFullPath($CatalogPath)
$catalog=Get-Content -LiteralPath $catalogFile -Raw | ConvertFrom-Json
if($catalog.schemaVersion -ne 1 -or $catalog.distributionStatus -ne 'unpublished'){throw '未対応のローカル配布台帳です。'}
$runId='install-'+[Guid]::NewGuid().ToString('N')
$logs=Join-Path $projectRoot "dev/history/$RecordVersion/logs/$runId"
New-Item -ItemType Directory -Path $logs | Out-Null
$dataPaths=@{
 'LiveTextMarker'=Join-Path $env:LOCALAPPDATA 'LiveTextMarker/settings.json'
 'SmartPaste'=Join-Path $env:LOCALAPPDATA 'SmartPaste/settings.json'
 'SSCaptureSettings'=Join-Path $projectRoot 'tools/SSCapture/src/SSCapture/bin/Release/net10.0-windows/data/settings.json'
 'SSCaptureRegion'=Join-Path $projectRoot 'tools/SSCapture/src/SSCapture/bin/Release/net10.0-windows/data/last-region.json'
 'Host'=Join-Path $env:LOCALAPPDATA 'KotorinToys/settings.json'
}
$hashes=@{}
foreach($entry in $dataPaths.GetEnumerator()) {
 if(Test-Path -LiteralPath $entry.Value -PathType Leaf){$hashes[$entry.Key]=(Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash}
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logs 'data-hashes-before.json') -Encoding utf8
foreach($product in $Products | Select-Object -Unique){
 $packages=@($catalog.packages | Where-Object {$_.name -eq $product})
 if($packages.Count -ne 1){throw "配布物が特定できません：$product"}
 $package=$packages[0]
 if($package.file -ne ($product+'-'+$package.version+'-setup.exe') -or $package.version -notmatch '^\d+\.\d+\.\d+$' -or $package.signature -ne 'unsigned-local-build'){throw '未確認の配布定義です。'}
 $installer=Join-Path (Split-Path $catalogFile) $package.file
 if((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256){throw '配布物のSHA-256が一致しません。'}
 $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/RESTARTEXITCODE=3010','/NOCLOSEAPPLICATIONS','/NOFORCECLOSEAPPLICATIONS','/NORESTARTAPPLICATIONS',('/LOG="'+(Join-Path $logs "$product-setup.log")+'"'))
 if($CustomValidationDirectory){$arguments += '/DIR="'+(Join-Path $projectRoot ".installed/$product")+'"'}
 $process=Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
 if($process.ExitCode -ne 0){throw "$product の導入処理が未完了です：終了コード $($process.ExitCode)。自動再起動はしません。"}
 $appId=if($product -eq 'KotorinToys'){'KotorinToys'}else{'KotorinToys.'+$product}
 $registration=Get-ItemProperty -LiteralPath ('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$appId+'_is1')
 $executable=Join-Path $registration.InstallLocation ($product+'.exe')
 $version=(Get-Item -LiteralPath $executable).VersionInfo
 if($version.ProductName -ne $product -or "$($version.FileMajorPart).$($version.FileMinorPart).$($version.FileBuildPart)" -ne $package.version){throw '導入後の実EXEの製品・版が一致しません。'}
 "$product : 正式導入・製品・版照合成功 $($package.version)"
}
foreach($entry in $hashes.GetEnumerator()){
 if((Get-FileHash -LiteralPath $dataPaths[$entry.Key] -Algorithm SHA256).Hash -ne $entry.Value){throw "既存設定のハッシュが変更されています：$($entry.Key)"}
}
"既存設定・保存領域の既知ファイル $($hashes.Count) 件：ハッシュ一致"
"導入ログ：$logs"
