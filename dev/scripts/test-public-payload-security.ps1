param(
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$ReportDirectory
)
$ErrorActionPreference = 'Stop'
$project = Split-Path (Split-Path $PSScriptRoot)
$root = [IO.Path]::GetFullPath($Payload).TrimEnd('\')
$reports = [IO.Path]::GetFullPath($ReportDirectory)
if (-not $root.StartsWith($project + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not $reports.StartsWith($project + '\dev\history\', [StringComparison]::OrdinalIgnoreCase)) { throw '検査入力はプロジェクト内、出力は版別記録内に限定します。' }
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw '配布入力が存在しません。' }
if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '配布入力のルートにリンクを指定できません。' }
$items = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint -or
    @($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw '配布入力にリンクを含めません。' }
$files = @($items | Where-Object { -not $_.PSIsContainer })
if (-not $files.Count) { throw '空の配布入力を成功にしません。' }
$forbidden = @($files | Where-Object {
    $relative = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/')
    $_.Name -match '^\.env($|\.(?!example$))|credentials\.json$|secrets\.json$|\.(pfx|p12|key|pem|pdb|db|sqlite|sqlite3)$' -or
    $relative -match '(^|/)(\.git|private|temp|node_modules)(/|$)'
} | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) })
New-Item -ItemType Directory -Force -Path $reports | Out-Null
$forbidden | ConvertTo-Json | Set-Content (Join-Path $reports 'forbidden-paths.json') -Encoding utf8
if ($forbidden.Count) { throw "秘密保管・実DB・デバッグ情報等の禁止ファイルを検出しました（$($forbidden.Count)件）。内容は表示しません。" }
$runtimeChecks = @()
$metadata = @{}
foreach ($configuration in @($files | Where-Object Name -Like '*.runtimeconfig.json')) {
    $options = (Get-Content -LiteralPath $configuration.FullName -Raw | ConvertFrom-Json).runtimeOptions
    foreach ($framework in @($options.includedFrameworks)) {
        if (-not $framework) { continue }
        $version = [version]$framework.version
        $channel = "$($version.Major).$($version.Minor)"
        if ($channel -notin @('8.0','10.0')) { throw 'サポート対象外の同梱.NETランタイムです。' }
        if (-not $metadata.ContainsKey($channel)) {
            $metadata[$channel] = Invoke-RestMethod "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$channel/releases.json" -TimeoutSec 30
        }
        $latest = [version]$metadata[$channel].'latest-runtime'
        $runtimeChecks += [pscustomobject]@{ framework=$framework.name; version=$version.ToString(); latest=$latest.ToString() }
        if ($version -lt $latest) { throw '同梱.NETランタイムが公式の最新修正版より古いため配布を中止します。' }
    }
}
$runtimeChecks | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $reports 'runtime-checks.json') -Encoding utf8
. (Join-Path $PSScriptRoot 'get-security-tools.ps1')
$scanner = Get-SecurityGitleaks
$errorLog = Join-Path $reports 'scanner-errors.log'
$report = Join-Path $reports 'secrets-redacted.json'
& $scanner dir $root --config (Join-Path $PSScriptRoot 'security-audit.rules.toml') --gitleaks-ignore-path $reports --ignore-gitleaks-allow --redact=100 --no-banner --no-color --log-level error --report-format json --report-path $report 2> $errorLog
if ($LASTEXITCODE -ne 0 -or (Get-Item $errorLog).Length -gt 0) { throw '秘密情報の検出または未完了のため配布を中止します。秘密値は表示しません。' }
[pscustomobject]@{ files=$files.Count; findings=0; scanner='gitleaks-8.30.1'; completedUtc=[DateTime]::UtcNow.ToString('O') } | ConvertTo-Json | Set-Content (Join-Path $reports 'receipt.json') -Encoding utf8
Write-Output "配布入力の秘密検査成功（$($files.Count)ファイル、検出0件）。バイナリ内部・外部サービスの安全性保証ではありません。"
