param([string]$Output = 'dev/history/v0.1.7/logs/audit-20261019', [string]$SevenZip)
$ErrorActionPreference = 'Stop'
$project = Split-Path (Split-Path $PSScriptRoot)
$base = [IO.Path]::GetFullPath((Join-Path $project $Output))
if (-not $base.StartsWith((Join-Path $project 'dev/history/v0.1.7/logs/').Replace('/', '\'), [StringComparison]::OrdinalIgnoreCase)) { throw '出力は対象版logsに限定します。' }
. (Join-Path $PSScriptRoot 'get-security-tools.ps1')
$scanner = Get-SecurityGitleaks
$unpacker = Join-Path $base 'tools/innounp-2.71/innounp.exe'
if (-not (Test-Path -LiteralPath $unpacker) -or (Get-FileHash -LiteralPath $unpacker -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'aecc0d7b1a2669711cd86d50061bb68911faa832ca3c69d4b1d2deb735cb1fcd') { throw '公式固定版InnoUnp 2.71.2を準備してください。検査器が未検証なら実行しません。' }
$summary = @()
foreach ($repo in @(Get-Content (Join-Path $base 'source-summary.json') -Raw | ConvertFrom-Json)) {
    $repoBase = Join-Path $base $repo.repository
    foreach ($release in @(Get-Content (Join-Path $repoBase 'public-releases.json') -Raw | ConvertFrom-Json)) {
        if ($release.tag -notmatch '^v[0-9]+(\.[0-9]+){2,3}$') { throw '公開tagが不正です。' }
        $directory = Join-Path $repoBase ('assets-' + $release.tag)
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        foreach ($asset in $release.assets) {
            if ([IO.Path]::GetFileName($asset.name) -ne $asset.name -or $asset.name -match '[:\\/]' -or $asset.size -gt 512MB) { throw 'asset名・容量が不正です。' }
            $expectedUrl = 'https://github.com/KotorinChunChun/' + $repo.repository + '/releases/download/' + $release.tag + '/' + $asset.name
            if ($asset.browser_download_url -ne $expectedUrl) { throw '固定所有者・tag以外のassetです。' }
            $path = Join-Path $directory $asset.name
            if (-not (Test-Path -LiteralPath $path)) { Invoke-WebRequest $expectedUrl -OutFile $path -TimeoutSec 300 }
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ((Get-Item -LiteralPath $path).Length -ne $asset.size -or ($asset.digest -and $asset.digest -ne ('sha256:' + $hash))) { throw 'assetの容量・digestが不一致です。' }
            $kind = [IO.Path]::GetExtension($asset.name).ToLowerInvariant()
            $payload = Join-Path $directory ($asset.name + '.unpacked')
            $status = 'metadata-verified'
            if ($kind -in @('.zip','.msix')) {
                if (-not (Test-Path -LiteralPath $payload)) { [IO.Compression.ZipFile]::ExtractToDirectory($path, $payload) }
                $status = 'payload-extracted'
            } elseif ($kind -eq '.exe' -and $asset.name -match '-setup\.exe$') {
                New-Item -ItemType Directory -Force -Path $payload | Out-Null
                & $unpacker -x -m -y "-d$payload" $path *> (Join-Path $directory ($asset.name + '.unpack.log'))
                if ($LASTEXITCODE -eq 0) { $status = 'payload-extracted' } else { $status = 'unpack-unsupported' }
            } elseif ($kind -in @('.exe','.msi')) { $status = 'unpack-unsupported' }
            if ($status -eq 'unpack-unsupported') {
                $seven = $SevenZip
                if (-not $seven) {
                    $command = Get-Command 7z -CommandType Application -ErrorAction SilentlyContinue
                    $seven = if ($command) { $command.Source } else { Join-Path $env:USERPROFILE 'scoop/apps/7zip/current/7z.exe' }
                }
                if (-not (Test-Path -LiteralPath $seven)) { throw '非実行で展開できる7-Zipが必要です。未走査を成功にしません。' }
                $payload = $path + '.7z-unpacked'
                & $seven x -y "-o$payload" $path *> ($path + '.7z.log')
                if ($LASTEXITCODE -ne 0) { throw '旧配布物の非実行展開に失敗しました。' }
                $status = 'payload-extracted'
            }
            $files = if ($status -eq 'payload-extracted') { @(Get-ChildItem -LiteralPath $payload -File -Recurse) } else { @() }
            if ($status -eq 'payload-extracted' -and -not $files.Count) { throw '配布物の空の展開結果を成功にしません。' }
            $prohibited = @($files | Where-Object { $_.Name -match '^\.env($|\.(?!example$))|credentials\.json$|secrets\.json$|\.(pfx|p12|key|pem|pdb|db|sqlite|sqlite3)$' } | ForEach-Object { [IO.Path]::GetRelativePath($payload,$_.FullName) })
            $findings = @()
            if ($status -eq 'payload-extracted') {
                $report = Join-Path $directory ($asset.name + '.secrets-redacted.json')
                $errors = Join-Path $directory ($asset.name + '.scanner-errors.log')
                & $scanner dir $payload --config (Join-Path $PSScriptRoot 'security-audit.rules.toml') --gitleaks-ignore-path $base --ignore-gitleaks-allow --redact=100 --no-banner --log-level error --report-format json --report-path $report 2> $errors
                if ($LASTEXITCODE -notin @(0,1) -or (Get-Item $errors).Length -gt 0) { throw 'payloadの秘密検査が未完了です。' }
                if (Test-Path $report) { $findings = @(Get-Content $report -Raw | ConvertFrom-Json | Select-Object RuleID,File,StartLine) }
            }
            $signature = if ($kind -in @('.exe','.msi','.msix')) { (Get-AuthenticodeSignature -LiteralPath $path).Status.ToString() } else { $null }
            $item = [pscustomobject]@{repository=$repo.repository;tag=$release.tag;name=$asset.name;size=$asset.size;sha256=$hash;digestConfirmed=([bool]$asset.digest);signature=$signature;inspection=$status;files=$files.Count;prohibited=$prohibited;findings=$findings}
            $summary += $item
            $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $base 'release-progress.json') -Encoding utf8
            [pscustomobject]@{repo=$repo.repository;tag=$release.tag;asset=$asset.name;status=$status;files=$files.Count;flagged=$prohibited.Count;secrets=$findings.Count} | ConvertTo-Json -Compress
        }
    }
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $base 'release-summary.partial.json') -Encoding utf8
Move-Item -LiteralPath (Join-Path $base 'release-summary.partial.json') -Destination (Join-Path $base 'release-summary.json') -Force
if (@($summary | Where-Object { $_.prohibited.Count -or $_.findings.Count -or $_.inspection -eq 'unpack-unsupported' }).Count) {
    throw '公開資産に禁止ファイル・秘密検出または未走査があります。値を表示せず台帳を確認してください。'
}
