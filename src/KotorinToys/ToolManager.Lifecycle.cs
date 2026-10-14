using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace KotorinToys;

internal sealed partial class ToolManager
{
    public async Task<ToolState> Start(ToolDefinition tool, bool showWindow = true, string launchReason = "host_manual", CancellationToken cancellation = default)
    {
        var gate = operations.GetOrAdd(tool.Id, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(cancellation);
        try
        {
            var state = await Status(tool, cancellation);
            if (state.State == "実行中")
            {
                if (showWindow)
                    await Show(tool, state, cancellation);
                return state;
            }

            if (state.State != "停止中")
                return state;
            var path = Executable(tool);
            if (!File.Exists(path))
                return new("確認できない", Detail: "実行ファイルがありません。先に導入してください。");
            var start = new ProcessStartInfo(path)
            {
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = false
            };
            // 起動直後の画面表示は管理窓口の準備完了後に手動要求で行う。
            start.ArgumentList.Add("--startup");
            start.ArgumentList.Add("--launch-origin");
            start.ArgumentList.Add(launchReason);
            // 隔離連携試験用の起動設定。通常運用では指定せず、カタログから任意引数を受け入れない。
            configureStart?.Invoke(tool, start);
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
                        if (showWindow)
                            await Show(tool, state, deadline.Token);
                        return state;
                    }

                    if (launched.HasExited)
                        return new("確認できない", Detail: "起動したプロセスが準備完了前に終了しました。");
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                return new("確認できない", Detail: "起動後の準備完了を30秒以内に確認できませんでした。再起動せず状態を再確認してください。");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    async Task Show(ToolDefinition tool, ToolState state, CancellationToken cancellation)
    {
        if (state.Pid is int pid)
            AllowSetForegroundWindow(pid);
        var reply = await Send(tool, "show", state.Pid, cancellation);
        if (reply.Result != "completed")
            throw new IOException(reply.Detail.Length == 0 ? "画面表示を完了できませんでした。" : reply.Detail);
    }

    public async Task<ToolState> Stop(ToolDefinition tool, CancellationToken cancellation = default)
    {
        var gate = operations.GetOrAdd(tool.Id, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(cancellation);
        try
        {
            var state = await Status(tool, cancellation);
            if (state.State is not ("実行中" or "停止処理中"))
                return state;
            var reply = await Send(tool, "stop", state.Pid, cancellation);
            if (reply.Result != "accepted")
            {
                pendingStops.TryRemove(tool.Id, out _);
                return state with
                {
                    State = "実行中",
                    Detail = reply.Detail.Length == 0 ? "終了要求は受理されませんでした。" : reply.Detail
                };
            }

            pendingStops[tool.Id] = reply.Pid;
            Process process;
            try
            {
                process = Process.GetProcessById(reply.Pid);
            }
            catch (ArgumentException)
            {
                return await Status(tool, cancellation);
            }

            using var processLifetime = process;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                return new("停止処理中", reply.Pid, "終了要求を受理しましたが、正常終了をまだ確認できません。");
            }

            pendingStops.TryRemove(tool.Id, out _);
            return await Status(tool, cancellation);
        }
        finally
        {
            gate.Release();
        }
    }
}
