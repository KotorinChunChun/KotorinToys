using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace KotorinToys;
internal sealed record ToolDefinition(string Id, string Name, string Executable, string Manual);
internal sealed record Catalog(int SchemaVersion, ToolDefinition[] Tools);
internal sealed record ToolState(string State, int? Pid = null, string Detail = "");
internal sealed record Reply(int Pid, bool Ready);

internal sealed class ToolManager(string root)
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    private async Task Show(ToolDefinition tool, ToolState state)
    {
        // ユーザーが操作した本体から対象プロセスへ前面化の権限を渡す。
        if (state.Pid is int pid) AllowSetForegroundWindow(pid);
        await Send(tool, "show");
    }
    private readonly Dictionary<string,int> pendingStops = new();
    public ToolDefinition[] Load()
    {
        var catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(root, "catalog", "tools.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("カタログが空です。");
        if (catalog.SchemaVersion != 1 || catalog.Tools.Length == 0 ||
            catalog.Tools.Select(t => t.Id).Distinct().Count() != catalog.Tools.Length)
            throw new IOException("カタログの形式またはツールIDが不正です。");
        foreach (var t in catalog.Tools) { Resolve(t.Executable); Resolve(t.Manual); }
        return catalog.Tools;
    }
    public string Resolve(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("プロジェクト外のパスは登録できません。");
        return path;
    }
    private async Task<Reply> Send(ToolDefinition tool, string command)
    {
        var name = "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + tool.Id;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(pipe, leaveOpen:true);
        using var writer = new StreamWriter(pipe, leaveOpen:true) { AutoFlush = true };
        await writer.WriteLineAsync(command.AsMemory(), timeout.Token);
        return JsonSerializer.Deserialize<Reply>(await reader.ReadLineAsync(timeout.Token) ?? "",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("応答がありません。");
    }
    public async Task<ToolState> Status(ToolDefinition tool)
    {
        var path = Resolve(tool.Executable);
        bool inaccessible = false;
        var matches = new List<int>();
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            using (p)
            {
                try {
                    if (p.SessionId != Process.GetCurrentProcess().SessionId) continue;
                    if (string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) matches.Add(p.Id);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { inaccessible = true; }
            }
        }
        try
        {
            var reply = await Send(tool, "status");
            using var p = Process.GetProcessById(reply.Pid);
            if (!string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                return new("確認できない", Detail:"別の配置から起動したインスタンスを検出しました。");
            if (pendingStops.TryGetValue(tool.Id, out var stoppingPid) && stoppingPid == reply.Pid)
                return new("停止処理中", reply.Pid, "ツール画面で操作を完了し、必要なら正常停止を再実行してください。");
            return new(reply.Ready ? "実行中" : "起動処理中", reply.Pid);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        {
            if (matches.Count > 0) return new("確認できない", matches[0], "プロセスは存在しますが管理窓口が応答しません。");
            if (inaccessible) return new("確認できない", Detail:"プロセス情報の取得に失敗しました。");
            pendingStops.Remove(tool.Id);
            return new("停止中", Detail:File.Exists(path) ? "" : "実行ファイルがありません。");
        }
    }
    public async Task<ToolState> Start(ToolDefinition tool)
    {
        var state = await Status(tool);
        if (state.State == "実行中") { await Show(tool, state); return state; }
        if (state.State != "停止中") return state;
        var path = Resolve(tool.Executable);
        Process.Start(new ProcessStartInfo(path) { WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true });
        for (int i=0; i<10; i++) { await Task.Delay(250); state=await Status(tool); if(state.State=="実行中") { await Show(tool, state); return state; } }
        return new("確認できない", Detail:"起動後の準備完了を確認できませんでした。");
    }
    public async Task<ToolState> Stop(ToolDefinition tool)
    {
        var state = await Status(tool);
        if (state.State is not ("実行中" or "停止処理中")) return state;
        var reply = await Send(tool, "stop");
        pendingStops[tool.Id] = reply.Pid;
        Process process;
        try { process = Process.GetProcessById(reply.Pid); }
        catch (ArgumentException) { return await Status(tool); }
        using var processLifetime = process;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { return new("停止処理中", reply.Pid, "正常終了を待っています。"); }
        return await Status(tool);
    }
}
