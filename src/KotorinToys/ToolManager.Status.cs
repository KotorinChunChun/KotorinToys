using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace KotorinToys;

internal sealed partial class ToolManager
{
    public async Task<ToolState> Status(ToolDefinition tool, CancellationToken cancellation = default)
    {
        if (tool.Msix != null && Installation(tool).State == "未導入")
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(tool.InstalledExecutable)))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId == Process.GetCurrentProcess().SessionId)
                            return new("確認できない", process.Id, "MSIX未導入ですが同名プロセスがあります。開発版などを正常終了してから導入してください。");
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                    { return new("確認できない", Detail: "同名プロセスの確認に失敗しました：" + ex.Message); }
                }
            }
            return new("停止中", Detail: "署名付きMSIXは未導入です。");
        }
        string path;
        try
        {
            var installed = Installation(tool);
            path = installed is { State: "修復が必要", Repairable: true, Executable: not null } ? installed.Executable : Executable(tool);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new("確認できない", Detail: ex.Message);
        }

        bool inaccessible = false;
        var matches = new List<int>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != Process.GetCurrentProcess().SessionId)
                        continue;
                    if (string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                        matches.Add(process.Id);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    inaccessible = true;
                }
            }
        }

        try
        {
            var reply = await Send(tool, "status", cancellation: cancellation);
            using var process = Process.GetProcessById(reply.Pid);
            if (process.SessionId != Process.GetCurrentProcess().SessionId || !string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                return new("確認できない", Detail: "別の配置またはセッションのインスタンスを検出しました。");
            if (tool.Msix is { } msix && !MsixInstallation.MatchesProcess(process, msix))
                return new("確認できない", Detail: "実プロセスのMSIX Package Familyが一致しません。");
            if (reply.ProtocolVersion < 2)
                return new("確認できない", reply.Pid, "旧管理窓口です。準備状態・操作結果を確認するにはツールを更新してください。");
            if (pendingStops.TryGetValue(tool.Id, out var stoppingPid) && stoppingPid == reply.Pid)
                return new("停止処理中", reply.Pid, "正常終了を確認しています。", reply.LaunchOrigin, reply.Version, reply.ProtocolVersion);
            return new(reply.Ready ? "実行中" : "起動処理中", reply.Pid, reply.Detail, reply.LaunchOrigin, reply.Version, reply.ProtocolVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or JsonException)
        {
            cancellation.ThrowIfCancellationRequested();
            if (matches.Count > 0)
                return new("確認できない", matches[0], "プロセスは存在しますが管理窓口が応答しません。");
            if (inaccessible)
                return new("確認できない", Detail: "プロセス情報の取得に失敗しました。");
            pendingStops.TryRemove(tool.Id, out _);
            return new("停止中", Detail: File.Exists(path) ? "" : "実行ファイルがありません。");
        }
    }
}
