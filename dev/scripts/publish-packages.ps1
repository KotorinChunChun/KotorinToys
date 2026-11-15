param(
    [ValidateSet('KotorinToys','LiveTextMarker','SmartPaste','SSCapture','CapchunScreen','ChulipVideo','ChunapTool','EveryDB','OperationRecorder','OfficeAssist','EchoType')]
    [string[]]$Products = @('KotorinToys','LiveTextMarker','SmartPaste'),
    [switch]$ExcludeFFmpeg,
    [ValidatePattern('^v\d+\.\d+\.\d+$')][string]$RecordVersion
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
. (Join-Path $PSScriptRoot 'distribution-products.ps1')
Assert-CommonDistributionProducts $Products
. (Join-Path $PSScriptRoot 'runtime-pack-validation.ps1')
function Get-SnapshotRevision([string]$Folder = '') {
    $manifest = Join-Path $projectRoot 'release-source.json'
    if (-not (Test-Path -LiteralPath $manifest)) { return $null }
    $source = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    if ($source.commit -isnot [string] -or $source.commit -cnotmatch '^[a-f0-9]{40}$' -or $source.children -isnot [array]) {
        throw '採用ソース台帳の親コミットまたは子一覧が不正です。'
    }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($child in $source.children) {
        if ($child.path -isnot [string] -or $child.path -cnotmatch '^tools/[A-Za-z0-9][A-Za-z0-9._-]*$' -or
            $child.commit -isnot [string] -or $child.commit -cnotmatch '^[a-f0-9]{40}$' -or -not $paths.Add($child.path)) {
            throw '採用ソース台帳の子パス・コミットまたは一意性が不正です。'
        }
    }
    if (-not $Folder) { return $source.commit }
    $children = @($source.children | Where-Object { $_.path -ceq "tools/$Folder" })
    if ($children.Count -ne 1) { throw "採用ソース台帳に対象の子コミットがありません：tools/$Folder" }
    return $children[0].commit
}
# 隔離ソースの祖先Git情報を使わず、発行前に全対象の採用コミットを確定する。
$maintenanceRevision = Get-SnapshotRevision
foreach ($selected in $Products | Select-Object -Unique) {
    [void](Get-SnapshotRevision (Get-DistributionProduct $selected).Folder)
}
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
function Assert-SharedFramework([string]$Output, [string]$Framework) {
    $config = Get-ChildItem -LiteralPath $Output -Filter '*.runtimeconfig.json' | Select-Object -First 1
    if (!$config) { throw "共有ランタイムの設定がありません：$Output" }
    $options = (Get-Content -LiteralPath $config.FullName -Raw | ConvertFrom-Json).runtimeOptions
    $references = @($options.framework; $options.frameworks) | Where-Object { $_ -and $_.name -eq $Framework -and $_.version -like '10.0.*' }
    if (!$references -or $options.includedFrameworks -or
        (Get-ChildItem -LiteralPath $Output -File | Where-Object Name -In @('coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll'))) {
        throw ".NET 10の共有ランタイムを参照する構成ではありません：$Output"
    }
}
function Publish-Application([string]$Project, [string]$Output, [string]$Label, [string[]]$Extra = @(),
    [switch]$ConsoleRuntime) {
    $revisionProduct = ($Label -split '\.', 2)[0]
    $revision = Get-SnapshotRevision (Get-DistributionProduct $revisionProduct).Folder
    $revisionArguments = @(if ($revision) { "-p:SourceRevisionId=$revision" })
    $selfContained = if ($revisionProduct -eq 'EchoType') { 'false' } else { 'true' }
    & dotnet publish (Join-Path $projectRoot $Project) -c Release -r win-x64 --self-contained $selfContained `
        --artifacts-path (Join-Path $staging "build/$Label") -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $Output --nologo @Extra @revisionArguments 2>&1 | Tee-Object -FilePath (Join-Path $logDirectory "$Label.log") | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Label の発行に失敗しました。" }
    if ($selfContained -eq 'false') {
        $framework = if ($ConsoleRuntime) { 'Microsoft.NETCore.App' } else { 'Microsoft.WindowsDesktop.App' }
        Assert-SharedFramework $Output $framework
        return
    }
    $runtimeFiles = @('coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll')
    if (-not $ConsoleRuntime) { $runtimeFiles += 'System.Windows.Forms.dll' }
    Assert-Files $Output $runtimeFiles
    $runtimeConfig = Get-ChildItem -LiteralPath $Output -Filter '*.runtimeconfig.json' | Select-Object -First 1
    if ($null -eq $runtimeConfig) { throw "$Label のランタイム設定がありません。" }
    $runtimeOptions = (Get-Content -LiteralPath $runtimeConfig.FullName -Raw | ConvertFrom-Json).runtimeOptions
    $minimumFrameworks = if ($ConsoleRuntime) { 1 } else { 2 }
    if ($runtimeOptions.framework -or $runtimeOptions.frameworks -or $runtimeOptions.includedFrameworks.Count -lt $minimumFrameworks) {
        throw "$Label が自己完結型になっていません。"
    }
    Copy-RuntimeLicenses $Output $Label
}
function Copy-RuntimeLicenses([string]$Output, [string]$Label) {
    $runtimeConfig = Get-ChildItem -LiteralPath $Output -Filter '*.runtimeconfig.json' | Select-Object -First 1
    if (-not $runtimeConfig) { throw 'ランタイム設定がありません。' }
    $runtimeOptions = (Get-Content -LiteralPath $runtimeConfig.FullName -Raw | ConvertFrom-Json).runtimeOptions
    $runtimeLicenses = Join-Path $Output 'runtime-licenses'
    New-Item -ItemType Directory -Path $runtimeLicenses | Out-Null
    Copy-Required $dotnetLicense $runtimeLicenses
    Copy-Required $dotnetNotices $runtimeLicenses
    $assetsFile = Get-ChildItem -LiteralPath (Join-Path $staging "build/$Label/obj") -Filter project.assets.json -Recurse | Select-Object -First 1
    $assets = Get-Content -LiteralPath $assetsFile.FullName -Raw | ConvertFrom-Json
    foreach ($framework in $runtimeOptions.includedFrameworks) {
        $packageId = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
        Assert-RuntimePackVersion $assets $packageId $framework.version
        $runtimePackage = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ "$packageId/$($framework.version)" } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $runtimePackage) { throw "ランタイムの配布通知がありません：$packageId" }
        $notices = Join-Path $runtimeLicenses $framework.name
        New-Item -ItemType Directory -Path $notices -Force | Out-Null
        $licenses = @(Get-ChildItem -LiteralPath $runtimePackage -File | Where-Object Name -Match '^LICENSE(\.TXT)?$')
        if (-not $licenses.Count) { throw "ランタイムのライセンスがありません：$packageId" }
        foreach ($license in $licenses) { Copy-Required $license.FullName $notices }
        $thirdParty = Join-Path $runtimePackage 'THIRD-PARTY-NOTICES.TXT'
        if (Test-Path -LiteralPath $thirdParty -PathType Leaf) { Copy-Required $thirdParty $notices }
    }
}
function Copy-EchoTypeVcRuntime([string]$Output) {
    # ONNX Runtimeが必要とするx64 CRTを、Visual Studioの再配布用ファイルからアプリの隣へ配置する。
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    $buildTools = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -or !$buildTools) { throw 'Visual StudioのC++ x64ビルドツールが見つかりません。' }
    $redist = Get-ChildItem -Path "$buildTools/VC/Redist/MSVC/*/x64/Microsoft.VC143.CRT" -Directory |
        Sort-Object { [version]$_.Parent.Parent.Name } -Descending | Select-Object -First 1
    if (!$redist) { throw 'Visual C++の再配布用x64 CRTが見つかりません。' }
    $files = @('msvcp140.dll', 'msvcp140_1.dll', 'vcruntime140.dll', 'vcruntime140_1.dll')
    Assert-Files $redist.FullName $files
    $receipt = foreach ($file in $files) {
        $source = Join-Path $redist.FullName $file
        $signature = Get-AuthenticodeSignature -LiteralPath $source
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
            throw "Visual C++再配布ファイルの発行元を確認できません：$file"
        }
        Copy-Required $source $Output
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath (Join-Path $Output $file) -Algorithm SHA256).Hash -ne $sourceHash) {
            throw "Visual C++再配布ファイルのコピーが一致しません：$file"
        }
        [pscustomobject]@{ file = $file; version = (Get-Item -LiteralPath $source).VersionInfo.FileVersion;
            source = "VC/Redist/MSVC/$($redist.Parent.Parent.Name)/x64/Microsoft.VC143.CRT"; sha256 = $sourceHash.ToLowerInvariant() }
    }
    $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logDirectory 'EchoType-vc-runtime.json') -Encoding utf8
}
$maintenanceRevisionArguments = @(if ($maintenanceRevision) { "-p:SourceRevisionId=$maintenanceRevision" })
function Publish-Maintenance([string]$Label, [string]$SelfContained) {
    $destination = Join-Path $staging $Label
    & dotnet publish (Join-Path $projectRoot 'src/KotorinToys.Maintenance/KotorinToys.Maintenance.csproj') -c Release -r win-x64 --self-contained $SelfContained --artifacts-path (Join-Path $staging "build/$Label") -p:DebugType=None -p:DebugSymbols=false -o $destination --nologo @maintenanceRevisionArguments 2>&1 | Tee-Object -FilePath (Join-Path $logDirectory "$Label.log") | Out-Host
    if ($LASTEXITCODE -ne 0) { throw '単体削除用メンテナンスの発行に失敗しました。' }
    Assert-Files $destination @('KotorinToys.Maintenance.exe')
    if ($SelfContained -eq 'false') { Assert-SharedFramework $destination 'Microsoft.NETCore.App' }
    else {
        Assert-Files $destination @('coreclr.dll','hostfxr.dll','hostpolicy.dll')
        Copy-RuntimeLicenses $destination $Label
    }
    return $destination
}
if ($Products -contains 'EchoType') { $echoMaintenance = Publish-Maintenance 'echo-maintenance' 'false' }
if ($Products | Where-Object { $_ -ne 'EchoType' }) { $maintenanceOutput = Publish-Maintenance 'maintenance' 'true' }
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
            foreach ($name in @('LiveTextMarker','SmartPaste','SSCapture','ShortcutKeyCounter','CapchunScreen','ChulipVideo','ChunapTool','HttpSelector','EveryDB','VoiceMixerMicRouter','VoiceMixerSpkRouter','OperationRecorder','ScriptSync','OfficeAssist','EchoType')) {
                $definitionOutput = Join-Path $output "tools/$name"
                New-Item -ItemType Directory -Path $definitionOutput -Force | Out-Null
                Copy-Required (Join-Path $projectRoot "tools/$name/tool.json") $definitionOutput
                Copy-Required (Join-Path $projectRoot "tools/$name/README.md") $definitionOutput
                if ($name -in @('ShortcutKeyCounter','CapchunScreen','ChulipVideo','ChunapTool','HttpSelector','EveryDB','VoiceMixerMicRouter','VoiceMixerSpkRouter','OperationRecorder','ScriptSync','OfficeAssist','EchoType')) {
                    # 同梱READMEから辿れる説明だけを含める。子EXE・利用者DBは含めない。
                    Copy-Required (Join-Path $projectRoot "tools/$name/docs") $definitionOutput
                }
                if ($name -eq 'OperationRecorder') {
                    Copy-Required (Join-Path $projectRoot 'tools/OperationRecorder/THIRD-PARTY-NOTICES.md') $definitionOutput
                    $manualOutput = Join-Path $definitionOutput 'operation-recorder'
                    New-Item -ItemType Directory -Path $manualOutput -Force | Out-Null
                    Copy-Required (Join-Path $projectRoot 'tools/OperationRecorder/docs/USERS_GUIDE.md') (Join-Path $manualOutput 'MANUAL.md')
                    foreach ($document in @('config/shortcuts.json','Assets/app.ico')) {
                        $documentOutput = Join-Path $definitionOutput (Split-Path $document -Parent)
                        New-Item -ItemType Directory -Path $documentOutput -Force | Out-Null
                        Copy-Required (Join-Path $projectRoot "tools/OperationRecorder/$document") $documentOutput
                    }
                }
                if ($name -eq 'ScriptSync') {
                    # ソース収録用の定義と説明だけ。同ツールの配布形式は署名付きMSIXのまま維持する。
                    foreach ($document in @('config/shortcuts.json','src-tauri/icons/icon.ico','dev/SHORTCUT_KEYS.md')) {
                        $documentOutput = Join-Path $definitionOutput (Split-Path $document -Parent)
                        New-Item -ItemType Directory -Path $documentOutput -Force | Out-Null
                        Copy-Required (Join-Path $projectRoot "tools/ScriptSync/$document") $documentOutput
                    }
                }
            }
        }
        'EveryDB' {
            $toolRoot = Join-Path $projectRoot 'tools/EveryDB'
            $nativeTarget = Join-Path $staging 'build/EveryDB-native'
            & (Join-Path $toolRoot 'dev/scripts/build-release-EveryDB-kotorintoys.ps1') -TargetDirectory $nativeTarget 2>&1 | Tee-Object -FilePath (Join-Path $logDirectory 'EveryDB.log') | Out-Host
            Copy-Required (Join-Path $nativeTarget 'release/everydb.exe') (Join-Path $output 'EveryDB.exe')
            New-Item -ItemType Directory -Path (Join-Path $output 'samples') | Out-Null
            Copy-Required (Join-Path $toolRoot 'src-tauri/resources/samples/customers.csv') (Join-Path $output 'samples')
            Copy-Required (Join-Path $toolRoot 'LICENSE.md') $output
            Copy-Required (Join-Path $toolRoot 'licenses') $output
            Assert-Files $output @('EveryDB.exe','samples/customers.csv','LICENSE.md','licenses/THIRD_PARTY_NOTICES.txt')
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
        'EchoType' {
            Publish-Application $identity.Project $output $product
            Publish-Application 'tools/EchoType/src/EchoType.Captions/EchoType.Captions.csproj' (Join-Path $output 'Captions') 'EchoType.Captions'
            Publish-Application 'tools/EchoType/src/EchoType.WindowsSpeech/EchoType.WindowsSpeech.csproj' (Join-Path $output 'WindowsSpeech') 'EchoType.WindowsSpeech' -ConsoleRuntime
            Copy-EchoTypeVcRuntime $output
            Copy-Required (Join-Path $projectRoot 'tools/EchoType/LICENSE') $output
            Assert-Files $output @('CaptionDll/CaptionDllHost.exe','Captions/EchoType.Captions.exe',
                'WindowsSpeech/EchoType.WindowsSpeech.exe','setup-asr.ps1','setup-haas.ps1',
                'Asr/worker.py','Asr/requirements.txt','Asr/requirements-qwen.txt','Asr/requirements-fast.txt',
                'Asr/requirements-cuda.txt','Asr/silero_vad_v6.onnx','Asr/whisper_setup.py','Asr/whisper-models.json')
        }
        { $_ -in @('CapchunScreen','ChulipVideo','ChunapTool','OperationRecorder','OfficeAssist') } {
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
            New-Item -ItemType Directory -Path $configurationOutput -Force | Out-Null
            Copy-Required (Join-Path $configuration 'shortcuts.json') $configurationOutput
        }
    }
    Assert-Files $output @("$product.exe", 'README.md')
    if (-not $identity.Native) { Assert-Files $output @("$product.runtimeconfig.json") }
    if ($product -in @('CapchunScreen','ChulipVideo','ChunapTool') -and @(Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Name -in @('ffmpeg.exe','ffprobe.exe')).Count) { throw '外部FFmpegが配布物へ混入しています。' }
    if ($product -eq 'EchoType') { Copy-Required $echoMaintenance (Join-Path $output 'maintenance') }
    else { Copy-Required $maintenanceOutput $output }
    if (Test-Path -LiteralPath (Join-Path $output 'data')) { throw '利用者データが配布対象へ混入しています。' }
    & (Join-Path $PSScriptRoot 'test-public-payload-security.ps1') -Payload $output -ReportDirectory (Join-Path $logDirectory "$product-security")
    $files = @(Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/');length=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
    $manifest = [ordered]@{schemaVersion=1;product=$product;status='staged-not-installed';files=$files}
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staging "$product.files.json") -Encoding utf8
}
Write-Output "パッケージ発行完了（導入・削除・公開は未実施）: $staging"
