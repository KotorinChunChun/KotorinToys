param(
    [ValidateSet('KotorinToys','LiveTextMarker','SmartPaste','SSCapture','CapchunWindow','ChulipVideo','ChunapTool')]
    [string[]]$Products = @('KotorinToys','LiveTextMarker','SmartPaste','SSCapture'),
    [switch]$ExcludeFFmpeg,
    [ValidatePattern('^v\d+\.\d+\.\d+$')][string]$RecordVersion
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
. (Join-Path $PSScriptRoot 'distribution-products.ps1')
if (-not $RecordVersion) { $RecordVersion = 'v' + (Get-Content (Join-Path $projectRoot 'catalog/host.json') -Raw | ConvertFrom-Json).version }
$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop
$dotnetRoot = Split-Path $dotnetCommand.Source
$dotnetLicense = Join-Path $dotnetRoot 'LICENSE.txt'
$dotnetNotices = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'
if (-not (Test-Path -LiteralPath $dotnetLicense -PathType Leaf) -or -not (Test-Path -LiteralPath $dotnetNotices -PathType Leaf)) {
    throw '.NET配布元のライセンス・第三者通知が見つかりません。パッケージの生成を中止しました。'
}
$runId = 'packages-' + [Guid]::NewGuid().ToString('N')
$staging = Join-Path $projectRoot "artifacts/packages/$runId"
$logDirectory = Join-Path $projectRoot "dev/history/$RecordVersion/logs/$runId"
New-Item -ItemType Directory -Path $staging, $logDirectory | Out-Null
function Copy-Required([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source)) { throw "配布に必要なファイルがありません: $Source" }
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
}
function Assert-Files([string]$Root, [string[]]$RelativePaths) {
    foreach ($relative in $RelativePaths) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $relative) -PathType Leaf)) {
            throw "パッケージ検証失敗: $Root / $relative"
        }
    }
}
function Publish-Application([string]$Project, [string]$Output, [string]$Label, [string[]]$Extra = @()) {
    & dotnet publish (Join-Path $projectRoot $Project) -c Release -r win-x64 --self-contained true `
        --artifacts-path (Join-Path $staging "build/$Label") -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $Output --nologo @Extra 2>&1 | Tee-Object -FilePath (Join-Path $logDirectory "$Label.log") | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Label の自己完結型発行に失敗しました。" }
    Assert-Files $Output @('coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','System.Windows.Forms.dll')
    $runtimeConfig = Get-ChildItem -LiteralPath $Output -Filter '*.runtimeconfig.json' | Select-Object -First 1
    if ($null -eq $runtimeConfig) { throw "$Label のランタイム設定がありません。" }
    $runtimeOptions = (Get-Content -LiteralPath $runtimeConfig.FullName -Raw | ConvertFrom-Json).runtimeOptions
    if ($runtimeOptions.framework -or $runtimeOptions.frameworks -or $runtimeOptions.includedFrameworks.Count -lt 2) {
        throw "$Label が自己完結型になっていません。"
    }
    $runtimeLicenses = Join-Path $Output 'runtime-licenses'
    New-Item -ItemType Directory -Path $runtimeLicenses | Out-Null
    Copy-Required $dotnetLicense $runtimeLicenses
    Copy-Required $dotnetNotices $runtimeLicenses
    $assetsFile = Get-ChildItem -LiteralPath (Join-Path $staging "build/$Label/obj") -Filter project.assets.json -Recurse | Select-Object -First 1
    $assets = Get-Content -LiteralPath $assetsFile.FullName -Raw | ConvertFrom-Json
    foreach ($framework in $runtimeOptions.includedFrameworks) {
        $packageId = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
        $runtimePackage = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ "$packageId/$($framework.version)" } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $runtimePackage) { throw "ランタイムの配布通知がありません：$packageId" }
        $notices = Join-Path $runtimeLicenses $framework.name
        New-Item -ItemType Directory -Path $notices -Force | Out-Null
        foreach ($file in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) { Copy-Required (Join-Path $runtimePackage $file) $notices }
    }
}
$maintenanceOutput = Join-Path $staging 'maintenance'
& dotnet publish (Join-Path $projectRoot 'src/KotorinToys.Maintenance/KotorinToys.Maintenance.csproj') -c Release -r win-x64 --self-contained true --artifacts-path (Join-Path $staging 'build/maintenance') -p:DebugType=None -p:DebugSymbols=false -o $maintenanceOutput --nologo 2>&1 | Tee-Object -FilePath (Join-Path $logDirectory 'maintenance.log') | Out-Host
if ($LASTEXITCODE -ne 0) { throw '単体削除用メンテナンスの発行に失敗しました。' }
Assert-Files $maintenanceOutput @('KotorinToys.Maintenance.exe','coreclr.dll','hostfxr.dll','hostpolicy.dll')
foreach ($product in $Products | Select-Object -Unique) {
    $identity = Get-DistributionProduct $product
    $output = Join-Path $staging $product
    New-Item -ItemType Directory -Path $output | Out-Null
    switch ($product) {
        'KotorinToys' {
            Publish-Application 'src/KotorinToys/KotorinToys.csproj' $output $product
            Copy-Required (Join-Path $projectRoot 'catalog') $output
            # configはcsprojからも発行される。配布時の自己完結した初期一覧として必須にする。
            Assert-Files $output @('config/tools.json')
            Copy-Required (Join-Path $projectRoot 'README.md') $output
            Copy-Required (Join-Path $projectRoot 'docs') $output
            Copy-Required (Join-Path $projectRoot 'catalog/host.json') (Join-Path $output 'tool.json')
            foreach ($name in @('LiveTextMarker','SmartPaste','SSCapture','ShortcutKeyCounter','CapchunScreen','ChulipVideo','ChunapTool','HttpSelector','EveryDB')) {
                $definitionOutput = Join-Path $output "tools/$name"
                New-Item -ItemType Directory -Path $definitionOutput -Force | Out-Null
                Copy-Required (Join-Path $projectRoot "tools/$name/tool.json") $definitionOutput
                Copy-Required (Join-Path $projectRoot "tools/$name/README.md") $definitionOutput
                if ($name -in @('ShortcutKeyCounter','CapchunScreen','ChulipVideo','ChunapTool','HttpSelector','EveryDB')) {
                    # 同梱READMEから辿れる説明だけを含める。子EXE・利用者DBは含めない。
                    Copy-Required (Join-Path $projectRoot "tools/$name/docs") $definitionOutput
                }
            }
        }
        'LiveTextMarker' {
            Publish-Application 'tools/LiveTextMarker/source/LiveTextMarker.csproj' $output $product @('-p:Platform=x64')
            Copy-Required (Join-Path $projectRoot 'tools/LiveTextMarker/source/licenses') $output
            Copy-Required (Join-Path $projectRoot 'tools/LiveTextMarker/source/LICENSE') $output
            Copy-Required (Join-Path $projectRoot 'tools/LiveTextMarker/source/THIRD-PARTY-NOTICES.md') $output
            Assert-Files $output @('tessdata/jpn.traineddata','tessdata/eng.traineddata','x64/tesseract50.dll','x64/leptonica-1.82.0.dll')
        }
        'SmartPaste' {
            Publish-Application 'tools/SmartPaste/SmartPaste/SmartPaste.csproj' $output $product
            $worker = Join-Path $output 'ocr'
            Publish-Application 'tools/SmartPaste/SmartPaste.Ocr/SmartPaste.Ocr.csproj' $worker 'SmartPaste.Ocr'
            Assert-Files $worker @('SmartPaste.Ocr.exe','SmartPaste.Ocr.runtimeconfig.json')
            New-Item -ItemType Directory -Path (Join-Path $output 'licenses') | Out-Null
            Copy-Required (Join-Path $projectRoot 'tools/LiveTextMarker/source/licenses/dotnet-LICENSE.txt') (Join-Path $output 'licenses')
            Copy-Required (Join-Path $projectRoot 'tools/LiveTextMarker/source/licenses/windowsdesktop-LICENSE.txt') (Join-Path $output 'licenses')
            Copy-Required (Join-Path $projectRoot 'tools/LiveTextMarker/source/licenses/dotnet-THIRD-PARTY-NOTICES.txt') (Join-Path $output 'licenses')
        }
        'SSCapture' {
            $mediaRoot = Join-Path $projectRoot 'tools/SSCapture/.tools/ffmpeg/ffmpeg-9.0.2-essentials_build'
            if(-not $ExcludeFFmpeg){Assert-Files $mediaRoot @('bin/ffmpeg.exe','bin/ffprobe.exe','LICENSE','README.txt')}
            Publish-Application 'tools/SSCapture/src/SSCapture/SSCapture.csproj' $output $product
            if(-not $ExcludeFFmpeg){
                $mediaOutput = Join-Path $output 'tools'
                New-Item -ItemType Directory -Path $mediaOutput | Out-Null
                foreach ($file in @('bin/ffmpeg.exe','bin/ffprobe.exe','LICENSE','README.txt')) {
                    Copy-Required (Join-Path $mediaRoot $file) $mediaOutput
                }
            }
            Copy-Required (Join-Path $projectRoot 'tools/SSCapture/licenses/NAudio-LICENSE.txt') $output
            Copy-Required (Join-Path $projectRoot 'tools/SSCapture/THIRD_PARTY.md') $output
        }
        { $_ -in @('CapchunWindow','ChulipVideo','ChunapTool') } {
            Publish-Application $identity.Project $output $product
            $licenses = Join-Path $projectRoot "tools/$($identity.Folder)/licenses"
            if (Test-Path -LiteralPath $licenses -PathType Container) { Copy-Required $licenses $output }
        }
    }
    if ($product -ne 'KotorinToys') {
        $toolRoot = Join-Path $projectRoot "tools/$($identity.Folder)"
        Copy-Required (Join-Path $toolRoot 'tool.json') $output
        Copy-Required (Join-Path $toolRoot 'README.md') $output
        $documentation = Join-Path $toolRoot 'docs'
        if (Test-Path -LiteralPath $documentation -PathType Container) { Copy-Required $documentation $output }
        $configuration = Join-Path $toolRoot 'config'
        if (Test-Path -LiteralPath (Join-Path $configuration 'shortcuts.json') -PathType Leaf) {
            $configurationOutput = Join-Path $output 'config'
            New-Item -ItemType Directory -Path $configurationOutput | Out-Null
            Copy-Required (Join-Path $configuration 'shortcuts.json') $configurationOutput
        }
    }
    Assert-Files $output @("$product.exe", "$product.runtimeconfig.json", 'README.md')
    if ($product -in @('CapchunWindow','ChulipVideo','ChunapTool') -and @(Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Name -in @('ffmpeg.exe','ffprobe.exe')).Count) { throw '外部FFmpegが配布物へ混入しています。' }
    Copy-Required $maintenanceOutput $output
    if (Test-Path -LiteralPath (Join-Path $output 'data')) { throw '利用者データが配布対象へ混入しています。' }
    $files = @(Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/');length=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
    $manifest = [ordered]@{schemaVersion=1;product=$product;status='staged-not-installed';files=$files}
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staging "$product.files.json") -Encoding utf8
}
Write-Output "パッケージ発行完了（導入・削除・公開は未実施）: $staging"
