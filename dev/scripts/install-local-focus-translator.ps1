param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$child = Join-Path $root 'tools/FocusTranslator'
$definition = Get-Content (Join-Path $child 'tool.json') -Raw | ConvertFrom-Json
if ($definition.id -ne 'focus-translator' -or $definition.version -ne '0.5.6') { throw '対象の製品定義が一致しません。' }
$compiler = Join-Path $root 'artifacts/dependencies/inno-7.1.0/ISCC.exe'
$executable = Join-Path $child $definition.executable
$product = (Get-Item -LiteralPath $executable).VersionInfo
if ($product.ProductName -ne 'FocusTranslator' -or "$($product.FileMajorPart).$($product.FileMinorPart).$($product.FileBuildPart)" -ne $definition.version) { throw '実EXEの製品名・版が一致しません。' }
$run = Join-Path $root ('dev/history/v0.1.11/logs/focus-translator-local-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $run 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
Copy-Item -LiteralPath $executable -Destination (Join-Path $payload 'FocusTranslator.exe')
Copy-Item -LiteralPath (Join-Path (Split-Path $executable) 'DirectML.dll') -Destination $payload
foreach ($file in @('tool.json', 'README.md', 'FocusTranslator_icon_32x32.ico')) { Copy-Item -LiteralPath (Join-Path $child $file) -Destination $payload }
Copy-Item -LiteralPath (Join-Path $child 'docs') -Destination $payload -Recurse
& $compiler ('/DPayload=' + $payload) ('/DOutputDirectory=' + $run) (Join-Path $child 'packaging/focus-translator-local.iss') | Out-File (Join-Path $run 'build.log')
if ($LASTEXITCODE) { throw 'ローカル導入用setupの作成に失敗しました。' }
$installer = Join-Path $run 'FocusTranslator-0.5.6-setup.exe'
if ($PrepareOnly) { Write-Output $installer; return }
$destination = Join-Path $root '.installed/FocusTranslator'
$process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/NOCLOSEAPPLICATIONS','/NORESTARTAPPLICATIONS', ('/DIR="' + $destination + '"'), ('/LOG="' + (Join-Path $run 'install.log') + '"')) -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "ローカル導入に失敗しました。終了コード: $($process.ExitCode)" }
# 公開一覧とは分離し、既存ローカル定義を保持する。本体の通常の読込検証を通す。
$localPath = Join-Path $env:LOCALAPPDATA 'KotorinToys/local-tools.json'
$local = if (Test-Path -LiteralPath $localPath) { Get-Content -LiteralPath $localPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{schemaVersion=1;tools=@()} }
if ($local.schemaVersion -ne 1) { throw 'ローカル一覧の形式が未対応です。' }
if (Test-Path -LiteralPath $localPath) { Copy-Item -LiteralPath $localPath -Destination (Join-Path $run 'local-tools-before.json') }
$entry = Get-Content (Join-Path $child 'tool.json') -Raw | ConvertFrom-Json
$entry.PSObject.Properties.Remove('schemaVersion')
$entry | Add-Member -NotePropertyName toolRoot -NotePropertyValue 'tools/FocusTranslator'
$entry | Add-Member -NotePropertyName shortcuts -NotePropertyValue 'config/shortcuts.json'
$entry.repositoryUrl = ''
$entry.executable = 'FocusTranslator.exe'
$local.tools = @($local.tools | Where-Object { $_.id -ne $entry.id }) + @($entry)
$temporary = Join-Path (Split-Path $localPath) ('local-tools-' + [Guid]::NewGuid().ToString('N') + '.tmp')
[IO.Directory]::CreateDirectory((Split-Path $localPath)) | Out-Null
[IO.File]::WriteAllText($temporary, ($local | ConvertTo-Json -Depth 20))
Move-Item -LiteralPath $temporary -Destination $localPath -Force
Write-Output "FocusTranslator $($definition.version) をローカル一覧へ登録しました: $destination"
Write-Output "記録: $run"
