param(
    [string]$ProjectRoot = (Split-Path (Split-Path $PSScriptRoot)),
    [ValidateSet('first','second','regression')][string]$PassName = 'second',
    [string]$ResultPath,
    [switch]$AllowFindings
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$catalog = Get-Content -LiteralPath (Join-Path $ProjectRoot 'catalog/tools.json') -Raw | ConvertFrom-Json
$online = (Get-Content -LiteralPath (Join-Path $ProjectRoot 'config/tools.json') -Raw | ConvertFrom-Json).tools
$rows = [Collections.Generic.List[object]]::new()
foreach ($entry in $catalog.tools) {
    $definitionPath = [IO.Path]::GetFullPath((Join-Path $ProjectRoot $entry.definition))
    if (-not $definitionPath.StartsWith($ProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '定義が対象ルートの外です。' }
    $root = Split-Path $definitionPath
    $definition = Get-Content -LiteralPath $definitionPath -Raw | ConvertFrom-Json
    $findings = [Collections.Generic.List[string]]::new()
    $managed = @('status','show','stop','silent-start' | Where-Object { $_ -notin $definition.capabilities }).Count -eq 0
    if ($definition.id -ne $entry.id -or $definition.name -ne $entry.name) { $findings.Add('親と子のID／製品名が不一致') }
    foreach ($key in @('readme','manual')) {
        $relative = $definition.$key
        $path = if ($relative) { [IO.Path]::GetFullPath((Join-Path $root $relative)) } else { '' }
        if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { $findings.Add("$key の正本文書がない／範囲外") }
    }
    $guidePath = Join-Path $root 'docs/kotorintoys-integration.md'
    $guide = if (Test-Path -LiteralPath $guidePath) { Get-Content -LiteralPath $guidePath -Raw } else { '' }
    if (-not $guide) { $findings.Add('管理連携ガイドがない') }
    elseif ($managed) {
        foreach ($term in @('--startup','--launch-origin','設定','停止')) {
            if (-not $guide.Contains($term)) { $findings.Add("管理連携ガイドに $term の説明がない") }
        }
    } elseif ($guide -notmatch '未対応|未実装') { $findings.Add('候補のみの能力制限がガイドにない') }
    if ($guide -and $guide -notmatch 'KotorinToys/blob/|KotorinToys/tree/|管理アプリの追加・更新手順|共通管理契約') { $findings.Add('共通規約への案内がない') }
    $readme = if (Test-Path -LiteralPath (Join-Path $root 'README.md')) { Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw } else { '' }
    if (-not $readme.Contains('kotorintoys-integration.md')) { $findings.Add('READMEから管理連携ガイドへ到達できない') }
    $public = @($online | Where-Object id -eq $entry.id)
    if ($public.Count -ne 1) { $findings.Add('配信用configに一意の登録がない') }
    else {
        foreach ($key in @('name','appId','version','installedExecutable')) {
            if ($public[0].$key -ne $definition.$key) { $findings.Add("配信用configの $key が子定義と不一致") }
        }
        if ((($public[0].capabilities | Sort-Object) -join ',') -cne (($definition.capabilities | Sort-Object) -join ',')) { $findings.Add('配信用configの能力宣言が子定義と不一致') }
    }
    $head = & git -c "safe.directory=$root" -C $root rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0) { throw "独立Gitを確認できません: $($entry.id)" }
    $rows.Add([ordered]@{ id=$entry.id; name=$definition.name; version=$definition.version; head=$head; managed=$managed; findings=@($findings); behavior='静的検査と別に対象の引数・既存インスタンス・版の回帰結果を記録する' })
}
$findingCount = @($rows | ForEach-Object { $_.findings }).Count
$report = [ordered]@{ pass=$PassName; utc=[DateTimeOffset]::UtcNow.ToString('o'); toolCount=$rows.Count; findingCount=$findingCount; items=$rows }
if (-not $ResultPath) {
    $version = (Get-Content -LiteralPath (Join-Path $ProjectRoot 'catalog/host.json') -Raw | ConvertFrom-Json).version
    $ResultPath = Join-Path $ProjectRoot "dev/history/v$version/logs/contract-audit-$([Guid]::NewGuid().ToString('N'))/$PassName.json"
}
$result = [IO.Path]::GetFullPath($ResultPath)
if (-not $result.StartsWith((Join-Path $ProjectRoot 'dev/history') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $result -notmatch '[\\/]logs[\\/]') { throw '結果は対象版のlogs配下へ指定してください。' }
if (Test-Path -LiteralPath $result) { throw '既存の点検結果は上書きしません。' }
[IO.Directory]::CreateDirectory((Split-Path $result)) | Out-Null
[IO.File]::WriteAllText($result, ($report | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
foreach ($row in $rows) { "{0}: {1} / {2}" -f $row.name,$row.version,($(if($row.findings.Count){$row.findings -join '、'}else{'静的整合OK（動作確認とは別）'})) }
"$($rows.Count)製品 / 指摘 $findingCount 件 / $result"
if ($findingCount -and -not $AllowFindings) { exit 1 }
