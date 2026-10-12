param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$targets = @(
    @{Id='live-text-marker'; Path='tools/LiveTextMarker/source/out/LiveTextMarker/LiveTextMarker.exe'},
    @{Id='smart-paste'; Path='tools/SmartPaste/release/SmartPaste.exe'},
    @{Id='ss-capture'; Path='tools/SSCapture/src/SSCapture/bin/Release/net10.0-windows/SSCapture.exe'}
)
foreach ($target in $targets) {
    $exePath = [IO.Path]::GetFullPath((Join-Path $projectRoot $target.Path))
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', ('KotorinToys.'+$sid+'.'+$target.Id), [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(2000)
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $true, 1024, $true)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
        $writer.AutoFlush = $true
        $writer.WriteLine('status')
        $reply = $reader.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(2)).GetAwaiter().GetResult() | ConvertFrom-Json
        $instance = Get-Process -Id $reply.pid
        if ($instance.SessionId -ne [Diagnostics.Process]::GetCurrentProcess().SessionId -or $instance.Path -ne $exePath) { throw '対象の実パス・セッションが一致しません。' }
        $reader.Dispose(); $writer.Dispose(); $pipe.Dispose()
        $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', ('KotorinToys.'+$sid+'.'+$target.Id), [IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(2000)
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $true, 1024, $true)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
        $writer.AutoFlush = $true
        $writer.WriteLine('stop')
        $response = $reader.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(2)).GetAwaiter().GetResult()
        if (!$instance.WaitForExit(10000)) { throw '正常終了を確認できません。更新しません。' }
        Write-Output ($target.Id+': 正常終了確認')
    } finally {
        if ($reader) { $reader.Dispose() }
        if ($writer) { $writer.Dispose() }
        $pipe.Dispose()
    }
}
$hostPath = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src/KotorinToys/bin/Debug/net10.0-windows/KotorinToys.exe'))
foreach ($instance in @(Get-Process -Name KotorinToys -ErrorAction SilentlyContinue)) {
    if ($instance.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId -and $instance.Path -eq $hostPath) {
        if (!$instance.CloseMainWindow() -or !$instance.WaitForExit(10000)) { throw '旧本体の正常終了を確認できません。更新しません。' }
        Write-Output '旧本体: 正常終了確認'
    }
}
