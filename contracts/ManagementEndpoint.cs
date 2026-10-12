using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace KotorinToys.Integration;

// 共有契約。ユーザー専用、UIスレッド応答、PID指定で別インスタンスへの操作を防ぐ。
internal sealed class ManagementEndpoint : IDisposable
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool AllowSetForegroundWindow(int processId);
    readonly CancellationTokenSource lifetime = new();
    public static string PipeName(string id) => "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + id;
    public static string LaunchOrigin
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--launch-origin");
            return index >= 0 && index + 1 < args.Length && args[index + 1] is "via_kotorintoys" or "direct_task" or "host_manual"
                ? args[index + 1] : "standalone";
        }
    }

    public ManagementEndpoint(string id, Control dispatcher, Action show, Action stop,
        Func<string?>? stopBlockReason = null, Func<object?>? shortcuts = null)
    {
        _ = dispatcher.Handle;
        _ = Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName(id), PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(lifetime.Token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    var line = await reader.ReadLineAsync(deadline.Token) ?? "";
                    string command = line, requestId = "";
                    int? expectedPid = null;
                    if (line.StartsWith("{"))
                    {
                        using var request = JsonDocument.Parse(line);
                        command = request.RootElement.GetProperty("command").GetString() ?? "";
                        if (request.RootElement.TryGetProperty("requestId", out var req)) requestId = req.GetString() ?? "";
                        if (request.RootElement.TryGetProperty("expectedPid", out var pid) && pid.ValueKind == JsonValueKind.Number) expectedPid = pid.GetInt32();
                    }
                    var result = "completed";
                    var detail = "";
                    object? keyState = null;
                    bool stopAccepted = false;
                    var applied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        // タイムアウト後に遅れて届いた操作を実行しない。
                        if (deadline.IsCancellationRequested || lifetime.IsCancellationRequested) return;
                        try
                        {
                            if (expectedPid.HasValue && expectedPid != Environment.ProcessId) { result = "refused"; detail = "対象PIDが変わりました。"; }
                            else if (command == "show") show();
                            else if (command == "stop")
                            {
                                detail = stopBlockReason?.Invoke() ?? "";
                                result = detail.Length == 0 ? "accepted" : "refused";
                                stopAccepted = result == "accepted";
                            }
                            else if (command == "shortcuts")
                            {
                                if (shortcuts == null) { result = "unsupported"; detail = "実効キー情報は未対応です。"; }
                                else keyState = shortcuts();
                            }
                            else if (command != "status") { result = "unsupported"; detail = "未対応の操作です。"; }
                            applied.TrySetResult(true);
                        }
                        catch (Exception) { result = "failed"; detail = "UI操作を完了できませんでした。"; applied.TrySetResult(false); }
                    }));
                    var uiReady = await applied.Task.WaitAsync(deadline.Token);
                    var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
                    var capabilities = shortcuts == null ? new[] { "status", "show", "stop" } : new[] { "status", "show", "stop", "shortcuts" };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new {
                        protocolVersion = 2, requestId, pid = Environment.ProcessId, ready = uiReady,
                        version = version == null ? null : $"{version.Major}.{version.Minor}.{version.Build}",
                        launchOrigin = LaunchOrigin, capabilities, result, detail, shortcuts = keyState
                    }));
                    // 終了要求の受理を返してから、既存の正常終了へ渡す。完了はクライアントがプロセスで確認する。
                    if (stopAccepted && !dispatcher.IsDisposed) dispatcher.BeginInvoke(stop);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or KeyNotFoundException) { }
            }
        });
    }

    public static void ShowExisting(string id)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(1200);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine("status");
            var line = reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            using var response = JsonDocument.Parse(line ?? "");
            var pid = response.RootElement.GetProperty("pid").GetInt32();
            using var process = Process.GetProcessById(pid);
            if (process.SessionId != Process.GetCurrentProcess().SessionId ||
                !string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;
            AllowSetForegroundWindow(pid);
            using var showPipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            showPipe.Connect(1200);
            using var showWriter = new StreamWriter(showPipe) { AutoFlush = true };
            showWriter.WriteLine(JsonSerializer.Serialize(new { command = "show", expectedPid = pid }));
            using var showReader = new StreamReader(showPipe);
            _ = showReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or System.ComponentModel.Win32Exception) { }
    }
    public void Dispose() => lifetime.Cancel();
}
