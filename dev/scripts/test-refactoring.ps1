[CmdletBinding()]
param(
    [ValidateSet('All','Host','LiveTextMarker','SmartPaste','SSCapture')][string]$Target='All',
    [ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version='v0.1.0'
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot)
$runId='refactoring-'+[Guid]::NewGuid().ToString('N')
$logs=Join-Path $projectRoot "dev/history/$Version/logs/$runId"
$artifacts=Join-Path $projectRoot "artifacts/refactoring/$runId"
New-Item -ItemType Directory -Path $logs,$artifacts | Out-Null
function Invoke-Checked([string]$Label,[string]$Executable,[string[]]$Arguments) {
    & $Executable @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $logs "$Label.log") | Out-Host
    if($LASTEXITCODE -ne 0){throw "$Label が終了コード $LASTEXITCODE で失敗しました。ログ：$logs"}
}
Push-Location $projectRoot
try {
    if($Target -in @('All','Host')) {
        Invoke-Checked 'host-build' 'dotnet' @('build','tests/KotorinToys.Tests/KotorinToys.Tests.csproj','--artifacts-path',"$artifacts/host",'--nologo')
        Invoke-Checked 'host-test' "$artifacts/host/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe" @('--scheduler','--host-lifetime')
        Invoke-Checked 'maintenance-build' 'dotnet' @('build','src/KotorinToys.Maintenance/KotorinToys.Maintenance.csproj','--artifacts-path',"$artifacts/maintenance",'--nologo')
    }
    if($Target -in @('All','LiveTextMarker')) {
        Invoke-Checked 'livetext-build' 'dotnet' @('build','tools/LiveTextMarker/source/LiveTextMarker.csproj','-c','Release','-p:Platform=x64','--artifacts-path',"$artifacts/livetext",'--nologo')
        Invoke-Checked 'livetext-test-publish' 'dotnet' @('publish','tools/LiveTextMarker/source/tests/GeometryTests.csproj','-c','Release','-r','win-x64','--self-contained','true','-o',"$artifacts/livetext-tests",'--nologo')
        Invoke-Checked 'livetext-test' "$artifacts/livetext-tests/GeometryTests.exe" @()
    }
    if($Target -in @('All','SmartPaste')) {
        Invoke-Checked 'smartpaste-test-publish' 'dotnet' @('publish','tools/SmartPaste/SmartPaste.Tests/SmartPaste.Tests.csproj','-c','Release','-r','win-x64','--self-contained','true','-o',"$artifacts/smartpaste-tests",'--nologo')
        Invoke-Checked 'smartpaste-test' "$artifacts/smartpaste-tests/SmartPaste.Tests.exe" @()
        Invoke-Checked 'smartpaste-worker-build' 'dotnet' @('build','tools/SmartPaste/SmartPaste.Ocr/SmartPaste.Ocr.csproj','-c','Release','--artifacts-path',"$artifacts/smartpaste-worker",'--nologo')
    }
    if($Target -in @('All','SSCapture')) {
        Invoke-Checked 'sscapture-build' 'dotnet' @('build','tools/SSCapture/tests/SSCapture.Tests/SSCapture.Tests.csproj','-c','Release','--artifacts-path',"$artifacts/sscapture",'--nologo')
        Invoke-Checked 'sscapture-test' "$artifacts/sscapture/bin/SSCapture.Tests/release/SSCapture.Tests.exe" @()
    }
    Write-Output "回帰検証成功：$Target / $logs"
} finally { Pop-Location }
