using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace KotorinToys;
internal sealed record ToolState(string State, int? Pid = null, string Detail = "", string LaunchOrigin = "unknown",
    string? Version = null, int ProtocolVersion = 0);
internal sealed record Reply(int Pid, bool Ready, int ProtocolVersion = 1, string Result = "", string Detail = "",
    string LaunchOrigin = "unknown", string? Version = null, string[]? Capabilities = null, JsonElement? Shortcuts = null);

internal sealed class ToolManager(string root)
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(int processId);
    readonly InstallationDetector installations = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> operations = new();
    readonly ConcurrentDictionary<string, int> pendingStops = new();
    public string Root => root;
    public ToolDefinition[] Load() => ToolCatalog.Load(root);
    public string Resolve(string relative) => PathSafety.Resolve(root, relative);
    public string DevelopmentRoot(ToolDefinition tool) => string.IsNullOrEmpty(tool.ToolRoot) ? root : Resolve(tool.ToolRoot);
    public string DocumentRoot(ToolDefinition tool) => Installation(tool).Directory ?? DevelopmentRoot(tool);
    public InstallationState Installation(ToolDefinition tool) => installations.Detect(tool);
    public async Task<ToolSnapshot> Snapshot(ToolDefinition tool, StartupState? startup = null, CancellationToken cancellation = default)
    {
        var installed = Installation(tool);
        var state = await Status(tool, cancellation);
        RuntimeState[] runtimes;
        try { runtimes = RuntimeDetector.Detect(tool, Executable(tool)); }
        catch (IOException ex) { runtimes = [new("unknown", "依存情報", "未確認", "基本機能", false, ex.Message)]; }
        return new(tool, installed, runtimes, state, startup ?? new(), DateTimeOffset.UtcNow,
            installed.State == "導入済み" ? "HKCU Inno登録・製品定義・EXE製品名／版" : "正式導入検出と開発配置は別扱い");
    }
    public string Executable(ToolDefinition tool)
    {
        var installed = Installation(tool);
        if (installed.State == "導入済み") return installed.Executable!;
        if (installed.State != "未導入") throw new IOException(installed.Detail);
        return PathSafety.Resolve(DevelopmentRoot(tool), tool.Executable);
    }
    public async Task<Reply> Send(ToolDefinition tool, string command, int? expectedPid = null, CancellationToken cancellation = default)
    {
        var name = "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + tool.Id;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await pipe.ConnectAsync(deadline.Token);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var request = command == "status" ? command : JsonSerializer.Serialize(new { command, expectedPid, requestId = Guid.NewGuid().ToString("N") });
        await writer.WriteLineAsync(request.AsMemory(), deadline.Token);
        return JsonSerializer.Deserialize<Reply>(await reader.ReadLineAsync(deadline.Token) ?? "", ToolCatalog.Json)
            ?? throw new IOException("応答がありません。");
    }
    public async Task<ToolState> Status(ToolDefinition tool, CancellationToken cancellation = default)
    {
        string path;
        try { path = Executable(tool); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return new("確認できない", Detail: ex.Message); }
        bool inaccessible = false;
        var matches = new List<int>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != Process.GetCurrentProcess().SessionId) continue;
                    if (string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) matches.Add(process.Id);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { inaccessible = true; }
            }
        }
        try
        {
            var reply = await Send(tool, "status", cancellation: cancellation);
            using var process = Process.GetProcessById(reply.Pid);
            if (process.SessionId != Process.GetCurrentProcess().SessionId ||
                !string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                return new("確認できない", Detail: "別の配置またはセッションのインスタンスを検出しました。");
            if (reply.ProtocolVersion < 2)
                return new("確認できない", reply.Pid, "旧管理窓口です。準備状態・操作結果を確認するにはツールを更新してください。");
            if (pendingStops.TryGetValue(tool.Id, out var stoppingPid) && stoppingPid == reply.Pid)
                return new("停止処理中", reply.Pid, "正常終了を確認しています。", reply.LaunchOrigin, reply.Version, reply.ProtocolVersion);
            return new(reply.Ready ? "実行中" : "起動処理中", reply.Pid, reply.Detail, reply.LaunchOrigin, reply.Version, reply.ProtocolVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception
            or ArgumentException or InvalidOperationException or JsonException)
        {
            cancellation.ThrowIfCancellationRequested();
            if (matches.Count > 0) return new("確認できない", matches[0], "プロセスは存在しますが管理窓口が応答しません。");
            if (inaccessible) return new("確認できない", Detail: "プロセス情報の取得に失敗しました。");
            pendingStops.TryRemove(tool.Id, out _);
            return new("停止中", Detail: File.Exists(path) ? "" : "実行ファイルがありません。");
        }
    }
    public async Task<ToolState> Start(ToolDefinition tool, bool showWindow = true, string launchReason = "host_manual",
        CancellationToken cancellation = default)
    {
        var gate = operations.GetOrAdd(tool.Id, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(cancellation);
        try
        {
            var state = await Status(tool, cancellation);
            if (state.State == "実行中")
            {
                if (showWindow) await Show(tool, state, cancellation);
                return state;
            }
            if (state.State != "停止中") return state;
            var path = Executable(tool);
            if (!File.Exists(path)) return new("確認できない", Detail: "実行ファイルがありません。先に導入してください。");
            var start = new ProcessStartInfo(path) { WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = false };
            // 起動直後の画面表示は管理窓口の準備完了後に手動要求で行う。
            start.ArgumentList.Add("--startup");
            start.ArgumentList.Add("--launch-origin");
            start.ArgumentList.Add(launchReason);
            using var launched = Process.Start(start) ?? throw new IOException("起動できませんでした。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                while (true)
                {
                    await Task.Delay(250, deadline.Token);
                    state = await Status(tool, deadline.Token);
                    if (state.State == "実行中")
                    {
                        if (showWindow) await Show(tool, state, deadline.Token);
                        return state;
                    }
                    if (launched.HasExited) return new("確認できない", Detail: "起動したプロセスが準備完了前に終了しました。");
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { return new("確認できない", Detail: "起動後の準備完了を30秒以内に確認できませんでした。再起動せず状態を再確認してください。"); }
        }
        finally { gate.Release(); }
    }
    async Task Show(ToolDefinition tool, ToolState state, CancellationToken cancellation)
    {
        if (state.Pid is int pid) AllowSetForegroundWindow(pid);
        var reply = await Send(tool, "show", state.Pid, cancellation);
        if (reply.Result != "completed") throw new IOException(reply.Detail.Length == 0 ? "画面表示を完了できませんでした。" : reply.Detail);
    }
    public async Task<ToolState> Stop(ToolDefinition tool, CancellationToken cancellation = default)
    {
        var gate = operations.GetOrAdd(tool.Id, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(cancellation);
        try
        {
            var state = await Status(tool, cancellation);
            if (state.State is not ("実行中" or "停止処理中")) return state;
            var reply = await Send(tool, "stop", state.Pid, cancellation);
            if (reply.Result != "accepted")
            {
                pendingStops.TryRemove(tool.Id,out _);
                return state with { State = "実行中", Detail = reply.Detail.Length == 0 ? "終了要求は受理されませんでした。" : reply.Detail };
            }
            pendingStops[tool.Id] = reply.Pid;
            Process process;
            try { process = Process.GetProcessById(reply.Pid); }
            catch (ArgumentException) { return await Status(tool, cancellation); }
            using var processLifetime = process;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { return new("停止処理中", reply.Pid, "終了要求を受理しましたが、正常終了をまだ確認できません。"); }
            pendingStops.TryRemove(tool.Id, out _);
            return await Status(tool, cancellation);
        }
        finally { gate.Release(); }
    }
}
