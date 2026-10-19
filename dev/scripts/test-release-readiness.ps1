param(
    [string]$ProjectRoot,
    [switch]$Online,
    [ValidatePattern('^v\d+\.\d+\.\d+$')][string]$RecordVersion
)
$ErrorActionPreference='Stop'
$project=if($ProjectRoot){[IO.Path]::GetFullPath($ProjectRoot)}else{Split-Path (Split-Path $PSScriptRoot)}
if(-not $RecordVersion){$RecordVersion='v'+(Get-Content (Join-Path $project 'catalog/host.json') -Raw|ConvertFrom-Json).version}
$run=Join-Path $project "dev/history/$RecordVersion/logs/readiness-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $run -Force|Out-Null
$artifacts=Join-Path $project 'artifacts/readiness-check'
& dotnet build (Join-Path $project 'tests/KotorinToys.Tests/KotorinToys.Tests.csproj') --artifacts-path $artifacts --nologo *> (Join-Path $run 'build.log')
if($LASTEXITCODE){throw '公開前検査用プログラムのビルドに失敗しました。版別ログを確認してください。'}
$test=Join-Path $artifacts 'bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe'
$arguments=@('--check-release-readiness',(Join-Path $run 'readiness.json'))
if($Online){$arguments+='--online'}
& $test @arguments *> (Join-Path $run 'check.log')
$result=$LASTEXITCODE
Get-Content (Join-Path $run 'check.log') | Select-Object -Last 3
"公開前検査の記録：$run"
if($result){throw '定義・公開config・配布物の不一致または未照合があります。公開は開始しません。'}
if(-not $Online){'静的検査のみ成功。実公開の一致を保証するものではありません。'}
