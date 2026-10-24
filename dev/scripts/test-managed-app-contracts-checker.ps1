$ErrorActionPreference = 'Stop'
$project = Split-Path (Split-Path $PSScriptRoot)
$version = (Get-Content (Join-Path $project 'catalog/host.json') -Raw | ConvertFrom-Json).version
$root = Join-Path $project "dev/history/v$version/logs/contract-checker-$([Guid]::NewGuid().ToString('N'))"
function Write-Fixture([string]$Path, [string]$Text) {
    $file = Join-Path $root $Path
    [IO.Directory]::CreateDirectory((Split-Path $file)) | Out-Null
    [IO.File]::WriteAllText($file,$Text,[Text.UTF8Encoding]::new($false))
}
$definition = [ordered]@{ schemaVersion=1;id='example';name='Example';version='1.2.3';appId='KotorinToys.Example';installedExecutable='Example.exe';manual='docs/kotorintoys-integration.md';readme='README.md';capabilities=@('status','show','stop','silent-start') }
Write-Fixture 'catalog/host.json' '{"version":"0.1.12"}'
Write-Fixture 'catalog/tools.json' '{"tools":[{"id":"example","name":"Example","definition":"tools/Example/tool.json"}]}'
Write-Fixture 'tools/Example/tool.json' ($definition|ConvertTo-Json -Depth 5)
Write-Fixture 'tools/Example/README.md' '[連携](docs/kotorintoys-integration.md)'
Write-Fixture 'tools/Example/docs/kotorintoys-integration.md' '--startup --launch-origin 設定・停止と共通管理契約'
$child = Join-Path $root 'tools/Example'
& git init --quiet $child
& git -C $child add -- tool.json
& git -C $child -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m fixture
if($LASTEXITCODE -ne 0){throw '架空の独立Gitを作成できません。'}
$checks=0
function Check([bool]$Valid,[string]$Label){if(-not $Valid){throw "FAIL: $Label"};$script:checks++;"PASS: $Label"}
function Run-Case([string]$Name){
    $path=Join-Path $root "dev/history/v0.1.12/logs/results/$Name.json"
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-managed-app-contracts.ps1') -ProjectRoot $root -PassName regression -ResultPath $path -AllowFindings | Out-Null
    if($LASTEXITCODE -ne 0){throw "$Name の検査自体が失敗しました。"}
    return (Get-Content $path -Raw|ConvertFrom-Json)
}
Write-Fixture 'config/tools.json' (@{tools=@($definition)}|ConvertTo-Json -Depth 6)
$ok=Run-Case 'valid'
Check ($ok.toolCount -eq 1 -and $ok.findingCount -eq 0) '一致する能力の配列を不一致としない'
Write-Fixture 'tools/Example/docs/kotorintoys-integration.md' '--startup 設定・停止と共通管理契約'
$missing=Run-Case 'missing-origin'
Check (($missing.items[0].findings -join ',').Contains('--launch-origin')) '共通引数の説明欠落を検出する'
$definition.capabilities=@()
Write-Fixture 'tools/Example/tool.json' ($definition|ConvertTo-Json -Depth 5)
Write-Fixture 'config/tools.json' (@{tools=@($definition)}|ConvertTo-Json -Depth 6)
Write-Fixture 'tools/Example/docs/kotorintoys-integration.md' '共通管理契約の管理操作は未対応'
$candidate=Run-Case 'candidate'
Check ($candidate.findingCount -eq 0 -and -not $candidate.items[0].managed) '候補のみの製品を架空の能力追加で埋めない'
$public=[ordered]@{};foreach($key in $definition.Keys){$public[$key]=$definition[$key]};$public.version='1.2.4'
Write-Fixture 'config/tools.json' (@{tools=@($public)}|ConvertTo-Json -Depth 6)
$mismatch=Run-Case 'version'
Check (($mismatch.items[0].findings -join ',').Contains('version')) '定義の版不一致を検出する'
Write-Fixture 'tools/Example/README.md' '入口なし'
$readme=Run-Case 'readme'
Check (($readme.items[0].findings -join ',').Contains('README')) '管理連携ガイドへの導線欠落を検出する'
"検査器回帰: $checks 件成功。架空の製品だけを使用し、実製品・設定・予約を操作していません。"
