$ErrorActionPreference = 'Stop'
foreach ($project in @('KotorinToys','KotorinToys.Maintenance')) {
    [xml]$definition = Get-Content -LiteralPath (Join-Path $PSScriptRoot "../../src/$project/$project.csproj") -Raw
    $runtime = @($definition.Project.ItemGroup.FrameworkReference | Where-Object { $_.RuntimeFrameworkVersion })
    if ($runtime.Count -ne 1 -or [version]$runtime[0].RuntimeFrameworkVersion -lt [version]'10.0.13' -or $runtime[0].Condition -ne "'`$(SelfContained)' == 'true'" -or $runtime[0].Update -notlike '*Microsoft.NETCore.App*') { throw '本体の自己完結配布は対象FrameworkReferenceに.NET 10.0.13以上を明示してください。' }
}
. (Join-Path $PSScriptRoot 'distribution-products.ps1')
foreach ($file in @('distribution-products.ps1','publish-packages.ps1','build-installers.ps1','publish-releases.ps1','stage-release-source.ps1','test-release-readiness.ps1')) {
    $tokens = $null; $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file), [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw "$file のPowerShell構文が不正です。" }
}
$cap = Get-DistributionProduct 'CapchunWindow'
if ($cap.Folder -ne 'CapchunScreen' -or $cap.AppId -ne 'KotorinToys.CapchunScreen' -or $cap.Repository -ne 'CapchunWindow') { throw 'CapchunWindowの固定識別が不正です。' }
if ((Get-DistributionProduct 'ChulipVideo').Repository -ne 'ChulipVideo-CSharp') { throw '別実装へ公開する設定です。' }
$every = Get-DistributionProduct 'EveryDB'
if ($every.Id -ne 'every-db' -or $every.AppId -ne 'KotorinToys.EveryDB' -or $every.Repository -ne 'EveryDB' -or -not $every.Native) { throw 'EveryDBのネイティブ配布識別が不正です。' }
$rejected = $false
try { Get-DistributionProduct 'ShortcutKeyCounter' | Out-Null } catch { $rejected = $true }
if (-not $rejected) { throw '今回公開しない製品を許可してしまいます。' }
Assert-CommonDistributionProducts @('KotorinToys','LiveTextMarker','SmartPaste','CapchunWindow','ChulipVideo','ChunapTool','EveryDB')
$capture = Get-DistributionProduct 'SSCapture'
if ($capture.Id -ne 'ss-capture' -or $capture.AppId -ne 'KotorinToys.SSCapture' -or $capture.PackagingScript -ne 'tools/SSCapture/scripts/package.ps1') { throw 'SSCaptureの固定識別と専用配布入口が不正です。' }
foreach ($file in @('publish-packages.ps1','build-installers.ps1','publish-releases.ps1')) {
    $script = Join-Path $PSScriptRoot $file
    $text = Get-Content -LiteralPath $script -Raw
    if ($text -notmatch "(?m)^\. \(Join-Path \`$PSScriptRoot 'distribution-products.ps1'\)\r?\nAssert-CommonDistributionProducts \`$Products") { throw "$file の共通読込直後に専用配布製品の事前検査がありません。" }
    $ast = [Management.Automation.Language.Parser]::ParseFile($script, [ref]$null, [ref]$null)
    $defaults = $ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Products' }
    if ($defaults.DefaultValue.Extent.Text -match 'SSCapture') { throw "$file の既定対象に専用配布製品が含まれています。" }
    $arguments = @{ Products = @('KotorinToys','SSCapture') }
    if ($file -eq 'build-installers.ps1') { $arguments.PackageDirectory = 'unused-fixture-path' }
    if ($file -eq 'publish-releases.ps1') { $arguments.CatalogPath = 'unused-fixture-path' }
    $message = ''
    try { & $script @arguments | Out-Null } catch { $message = $_.Exception.Message }
    if ($message -notlike '*SSCapture*専用配布*tools/SSCapture/scripts/package.ps1*') { throw "$file が副作用前に専用入口へ誘導しませんでした：$message" }
}
Write-Output '配布スクリプト6件の構文・固定配布先・汎用7製品・SSCapture専用経路の副作用前拒否3件：成功'
