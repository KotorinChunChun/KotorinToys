param(
    [Parameter(Mandatory)][ValidateSet('Backup','Verify')][string]$Action,
    [Parameter(Mandatory)][string]$BackupDirectory
)
$ErrorActionPreference = 'Stop'
$privateRoot = 'C:\AI\env\KotorinToys\local-upgrades\'
$backup = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
if (-not $backup.StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase) -or $backup -eq $privateRoot.TrimEnd('\')) { throw '専用の非公開退避先が必要です。' }
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$data = Join-Path $env:LOCALAPPDATA 'KotorinToys'
$registration = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\KotorinToys_is1'
$installed = [IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\')
if ($installed -ne (Join-Path $project '.installed/KotorinToys')) { throw '確認した正式導入先と一致しません。' }
function Reservations {
    @(Get-ScheduledTask | Where-Object TaskName -Like 'KotorinToys*' | ForEach-Object {
        $xml = Export-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath
        [pscustomobject]@{ name=$_.TaskName; path=$_.TaskPath; hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($xml))) }
    })
}
if ($Action -eq 'Backup') {
    if (Test-Path -LiteralPath $backup) { throw '既存の退避先は上書きしません。' }
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls.exe $backup /inheritance:r /grant:r "*$($sid):(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE) { throw '非公開退避先のACLを設定できません。' }
    $files = @(Get-ChildItem -LiteralPath $data -File -Recurse | Where-Object {
        $relative = [IO.Path]::GetRelativePath($data,$_.FullName)
        $relative -notmatch '(^|\\)(packages|worker)(\\|$)' -and $_.Extension -ne '.lock' -and
            ($_.Name -match '\.(json|bak)$' -or $_.Name -eq 'claimed')
    } | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($data,$_.FullName)
        $target = Join-Path $backup "data/$relative"
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        $hash = (Get-FileHash -LiteralPath $_.FullName).Hash
        Copy-Item -LiteralPath $_.FullName -Destination $target
        if ((Get-FileHash -LiteralPath $target).Hash -ne $hash -or (Get-FileHash -LiteralPath $_.FullName).Hash -ne $hash) { throw '退避中に設定が変わりました。' }
        [pscustomobject]@{ relative=$relative; hash=$hash; verify=($relative -ne 'tool-catalog-cache.json') }
    })
    if (-not ($files | Where-Object relative -eq 'settings.json')) { throw '本体設定を退避できません。' }
    Copy-Item -LiteralPath $installed -Destination (Join-Path $backup 'previous-program') -Recurse
    $children = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith((Join-Path $project '.installed')+'\',[StringComparison]::OrdinalIgnoreCase) -and $_.ExecutablePath -ne (Join-Path $installed 'KotorinToys.exe') } | Select-Object ProcessId,ExecutablePath)
    [ordered]@{beforeVersion=$registration.DisplayVersion; installed=$installed; data=$data; files=$files; reservations=@(Reservations); children=$children} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'receipt.json') -Encoding utf8
    "退避完了：設定・履歴 $($files.Count) 件、旧本体、子プロセス $($children.Count) 件。秘密値は出力していません。"
} else {
    $receipt = Get-Content -LiteralPath (Join-Path $backup 'receipt.json') -Raw | ConvertFrom-Json
    if ($receipt.installed -ne $installed -or $receipt.data -ne $data) { throw '退避対象と現在の配置が異なります。' }
    $checked = 0
    foreach ($file in $receipt.files | Where-Object verify) {
        if ((Get-FileHash -LiteralPath (Join-Path $data $file.relative)).Hash -ne $file.hash) { throw '設定・履歴の保持を確認できません。値は表示しません。' }
        $checked++
    }
    $now = @(Reservations)
    if ($now.Count -ne @($receipt.reservations).Count) { throw '予約件数が変わりました。' }
    foreach ($task in $receipt.reservations) {
        if (-not ($now | Where-Object { $_.name -eq $task.name -and $_.path -eq $task.path -and $_.hash -eq $task.hash })) { throw '予約定義が変わりました。' }
    }
    foreach ($child in $receipt.children) {
        $running = Get-Process -Id $child.ProcessId -ErrorAction Stop
        if ($running.Path -ne $child.ExecutablePath) { throw '子アプリの継続稼働を確認できません。' }
    }
    $exe = Join-Path $installed 'KotorinToys.exe'
    $version = (Get-Item -LiteralPath $exe).VersionInfo
    if ($registration.DisplayVersion -ne '0.1.7' -or $version.ProductName -ne 'KotorinToys' -or "$($version.FileMajorPart).$($version.FileMinorPart).$($version.FileBuildPart)" -ne '0.1.7') { throw '正式登録と実EXEの版が一致しません。' }
    "保持確認：設定・履歴 $checked 件、予約 $($now.Count) 件、子プロセス $(@($receipt.children).Count) 件。正式導入版0.1.7。"
}
