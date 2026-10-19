$ErrorActionPreference = 'Stop'
foreach ($project in @('KotorinToys','KotorinToys.Maintenance')) {
    [xml]$definition = Get-Content -LiteralPath (Join-Path $PSScriptRoot "../../src/$project/$project.csproj") -Raw
    $runtime = @($definition.Project.PropertyGroup.RuntimeFrameworkVersion | Where-Object { $_ })
    if ($runtime.Count -ne 1 -or [version]$runtime[0].InnerText -lt [version]'10.0.13' -or $runtime[0].Condition -ne "'`$(SelfContained)' == 'true'") { throw '本体の自己完結配布は.NET 10.0.13以上を明示してください。' }
}
. (Join-Path $PSScriptRoot 'distribution-products.ps1')
foreach ($file in @('distribution-products.ps1','publish-packages.ps1','build-installers.ps1','publish-releases.ps1','stage-release-source.ps1')) {
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
Write-Output '配布スクリプト5件の構文・固定配布先・公開対象外製品の拒否：成功'
