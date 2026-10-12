param([switch]$DownloadOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$version = '7.1.0'
$expectedHash = '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'
$downloadDirectory = Join-Path $projectRoot 'artifacts/dependencies/downloads'
$installer = Join-Path $downloadDirectory "innosetup-$version-x64.exe"
$compilerDirectory = Join-Path $projectRoot "artifacts/dependencies/inno-$version"
$compiler = Join-Path $compilerDirectory 'ISCC.exe'
New-Item -ItemType Directory -Force -Path $downloadDirectory | Out-Null
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    Invoke-WebRequest -Uri "https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-$version-x64.exe" -OutFile $installer
}
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) {
    throw "Inno Setupの署名が有効ではありません: $($signature.Status)"
}
$publisher = $signature.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
if ($publisher -ne 'Pyrsys B.V.') { throw "Inno Setupの発行元が一致しません: $publisher" }
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Inno Setupの固定版SHA-256が一致しません。実行せず、配布元の確認からやり直してください。'
}
if ($DownloadOnly) { Write-Output "署名・発行元・SHA-256の検証成功: $installer"; return }
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $runId = 'inno-prepare-' + [Guid]::NewGuid().ToString('N')
    $logDirectory = Join-Path $projectRoot "dev/history/v0.1.0/logs/$runId"
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS',
        ('/DIR="' + $compilerDirectory + '"'), ('/LOG="' + (Join-Path $logDirectory 'setup.log') + '"'))
    $process = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Inno Setupの導入に失敗しました: $($process.ExitCode)" }
}
# 既存のコンパイラーも固定版と照合する。差し替え・改変された実行ファイルは実行しない。
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
if ($compilerHash -ne 'D06EBD38F38E3CEE60A3C50CC45BD449D77E0BC6A5CABC607EA9886808E4DE1A') { throw 'コンパイラーのSHA-256が一致しません。' }
& $compiler --version
if ($LASTEXITCODE -ne 0) { throw 'コンパイラーの起動確認に失敗しました。' }
Write-Output "コンパイラー: $compiler"
