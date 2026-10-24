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
Write-Fixture 'dev/managed-app-source-index.json' '{"tools":[{"id":"example","kind":"cargo","path":"Cargo.toml","section":"package"}]}'
Write-Fixture 'tools/Example/Cargo.toml' "[package]`nversion = `"1.2.3`"`n"
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
Write-Fixture 'tools/Example/README.md' '[連携](docs/kotorintoys-integration.md)'
Write-Fixture 'config/tools.json' (@{tools=@($definition)}|ConvertTo-Json -Depth 6)
Write-Fixture 'tools/Example/Cargo.toml' "[package]`nversion = `"1.2.2`"`n"
$source=Run-Case 'source-version'
Check (($source.items[0].findings -join ',').Contains('ソース版 1.2.2')) 'JSON同士が一致してもソース版の不一致を検出する'
Write-Fixture 'tools/Example/Cargo.toml' "[package]`nversion = `"1.2.3`"`n"
Write-Fixture 'tools/Example/docs/kotorintoys-integration.md' '共通管理契約の管理操作は未対応。[欠落](missing.md)'
$link=Run-Case 'broken-link'
Check (($link.items[0].findings -join ',').Contains('参照切れ')) '単独cloneでのローカル参照切れを検出する'
Write-Fixture 'tools/Example/docs/kotorintoys-integration.md' '共通管理契約の管理操作は未対応'
$definition.version='1.2.3.4'
Write-Fixture 'tools/Example/tool.json' ($definition|ConvertTo-Json -Depth 5)
Write-Fixture 'config/tools.json' (@{tools=@($definition)}|ConvertTo-Json -Depth 6)
Write-Fixture 'tools/Example/Cargo.toml' "[package]`nversion = `"1.2.3+4`"`n"
$four=Run-Case 'four-part'
Check ($four.findingCount -eq 0 -and $four.items[0].sourceVersion -eq '1.2.3.4') 'Cargo再配布番号を切り捨てず4成分版で照合する'
Write-Fixture 'dev/managed-app-source-index.json' '{"tools":[{"id":"example","kind":"cargo","path":"../outside.toml","section":"package"}]}'
$escape=Run-Case 'source-path'
Check (($escape.items[0].findings -join ',').Contains('範囲外')) 'ソース版読取りが子リポジトリ外へ出る指定を拒否する'
Write-Fixture 'dev/managed-app-source-index.json' '{"tools":[{"id":"example","kind":"msix-revision","path":"version.json","cargo":"Cargo.toml"}]}'
Write-Fixture 'tools/Example/Cargo.toml' "[package]`nversion = `"1.2.3`"`n"
Write-Fixture 'tools/Example/version.json' '{"applicationVersion":"1.2.3","revision":4}'
$msix=Run-Case 'msix-revision'
Check ($msix.findingCount -eq 0) 'MSIXアプリ版と再配布番号の正本を照合する'
Write-Fixture 'tools/Example/version.json' '{"applicationVersion":"1.2.3","revision":65536}'
$badRevision=Run-Case 'msix-invalid'
Check (($badRevision.items[0].findings -join ',').Contains('再配布番号')) 'MSIXの範囲外の再配布番号を拒否する'
Write-Fixture 'dev/managed-app-source-index.json' '{"tools":[{"id":"example","kind":"msbuild","path":"Example.csproj"}]}'
Write-Fixture 'tools/Example/Example.csproj' '<Project><PropertyGroup><Version>1.2.3.4</Version></PropertyGroup></Project>'
$msbuild=Run-Case 'msbuild'
Check ($msbuild.findingCount -eq 0) 'csprojの製品版を照合する'
"検査器回帰: $checks 件成功。架空の製品だけを使用し、実製品・設定・予約を操作していません。"
