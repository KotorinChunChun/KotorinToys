using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace KotorinToys;

internal sealed partial class ToolManager
{
    sealed record ReplacementProcess(int Pid, string Executable, DateTime StartedAtUtc);

    static ReplacementProcess[] ReplacementProcesses(ToolDefinition tool)
    {
        var names = new[] { tool.Executable, tool.InstalledExecutable }
            .Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var found = new Dictionary<int, ReplacementProcess>();
        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name!);
            try
            {
                foreach (var process in processes)
                {
                    if (process.SessionId != Process.GetCurrentProcess().SessionId) continue;
                    var path = ToolLocations.LocalExecutable(process.MainModule?.FileName);
                    found[process.Id] = new(process.Id, path, process.StartTime.ToUniversalTime());
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return found.Values.ToArray();
    }

    // 同じ製品の管理窓口、OSが返すサーバーPID、実行名、セッションと開始時刻を照合する。
    public async Task<ToolState> StatusForReplacement(ToolDefinition tool, CancellationToken cancellation = default)
    {
        try
        {
            var processes = ReplacementProcesses(tool);
            if (processes.Length > 1) return new("確認できない", Detail: "同名のアプリが複数動いています。対象を確認してから終了してください。");
            if (processes.Length == 0)
            {
                var state = await Status(tool, cancellation);
                return state.State == "停止中" ? state : new("確認できない", Detail: state.Detail.Length == 0 ? "実行中のアプリを照合できません。" : state.Detail);
            }
            var process = processes[0];
            var reply = await Send(tool, "status", process.Pid, cancellation);
            if (reply.ProtocolVersion < 2 || !reply.Ready || tool.Capabilities?.Contains("stop") != true)
                return new("確認できない", process.Pid, "実行中のアプリが正常終了の管理連携に対応していません。アプリ側で終了してから再実行してください。",
                    RunningExecutable: process.Executable, ProcessStartedAtUtc: process.StartedAtUtc);
            using var checkedProcess = Process.GetProcessById(process.Pid);
            if (checkedProcess.SessionId != Process.GetCurrentProcess().SessionId ||
                checkedProcess.StartTime.ToUniversalTime() != process.StartedAtUtc ||
                !string.Equals(checkedProcess.MainModule?.FileName, process.Executable, StringComparison.OrdinalIgnoreCase))
                return new("確認できない", Detail: "実行中のアプリが確認中に変わりました。もう一度操作してください。");
            var actual = Installation(tool);
            if (tool.Msix is { } msix && MsixPolicy.Installed(actual) &&
                string.Equals(actual.Executable, process.Executable, StringComparison.OrdinalIgnoreCase) && !MsixInstallation.MatchesProcess(checkedProcess, msix))
                return new("確認できない", Detail: "実行中のMSIX製品識別が一致しません。");
            return new("実行中", process.Pid, "実行中のアプリを正常終了して入れ替えます。", reply.LaunchOrigin, reply.Version,
                reply.ProtocolVersion, process.Executable, actual.Executable, process.StartedAtUtc);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or
            Win32Exception or OperationCanceledException or JsonException or NotSupportedException)
        {
            return new("確認できない", Detail: "実行中のアプリを安全に確認できません。アプリ側で終了してから再実行してください。" +
                (string.IsNullOrWhiteSpace(error.Message) ? "" : " " + error.Message));
        }
    }

    public async Task<ToolState> StopForReplacement(ToolDefinition tool, ToolState expected, CancellationToken cancellation = default)
    {
        var gate = operations.GetOrAdd(tool.Id, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(cancellation);
        try
        {
            var current = await StatusForReplacement(tool, cancellation);
            if (current.State != "実行中" || current.Pid != expected.Pid || current.ProcessStartedAtUtc == null ||
                current.ProcessStartedAtUtc != expected.ProcessStartedAtUtc ||
                !string.Equals(current.RunningExecutable, expected.RunningExecutable, StringComparison.OrdinalIgnoreCase))
                return new("確認できない", Detail: "実行中のアプリが変わったか、終了対象を確認できません。もう一度操作してください。");
            using var process = Process.GetProcessById(current.Pid!.Value);
            if (process.StartTime.ToUniversalTime() != current.ProcessStartedAtUtc ||
                !string.Equals(process.MainModule?.FileName, current.RunningExecutable, StringComparison.OrdinalIgnoreCase))
                return new("確認できない", Detail: "終了対象のアプリが変わりました。操作を中止します。");
            var reply = await Send(tool, "stop", current.Pid, cancellation);
            if (reply.Result != "accepted") return current with { Detail = reply.Detail.Length == 0 ? "アプリが正常終了を拒否しました。" : reply.Detail };
            pendingStops[tool.Id] = current.Pid.Value;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { return new("停止処理中", current.Pid, "終了要求を受理しましたが、正常終了をまだ確認できません。"); }
            pendingStops.TryRemove(tool.Id, out _);
            var after = await StatusForReplacement(tool, cancellation);
            return after.State == "停止中" ? after : new("確認できない", Detail: "終了後に別のアプリを検出しました。インストールを中止します。" + after.Detail);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or Win32Exception or JsonException)
        { return new("確認できない", Detail: "アプリを正常終了できません：" + error.Message); }
        finally { gate.Release(); }
    }
}
