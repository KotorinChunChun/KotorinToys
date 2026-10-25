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
                    var (command, requestId, expectedPid) = ParseRequest(await ReadRequestAsync(reader, deadline.Token));
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
                    // 応答を読まないクライアントが単一の受付枠を占有し続けないよう、書込みにも期限を設ける。
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new {
                        protocolVersion = 2, requestId, pid = Environment.ProcessId, ready = uiReady,
                        version = version == null ? null : $"{version.Major}.{version.Minor}.{version.Build}",
                        launchOrigin = LaunchOrigin, capabilities, result, detail, shortcuts = keyState
                    }).AsMemory(), deadline.Token);
                    // 終了要求の受理を返してから、既存の正常終了へ渡す。完了はクライアントがプロセスで確認する。
                    if (stopAccepted && !dispatcher.IsDisposed) dispatcher.BeginInvoke(stop);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    // 1件の不正な要求や想定外の失敗で受付ループを終了させない。連続失敗時は少し待って再試行する。
                    Debug.WriteLine($"管理要求を処理できませんでした: {ex.GetType().Name}");
                    try { await Task.Delay(RetryDelay, lifetime.Token); }
                    catch (OperationCanceledException) { }
                }
            }
        });
    }

    internal const int MaximumRequestLength = 4096;
    static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    // 改行までの1要求を読む。上限を超える要求は全体を読まずに拒否する。
    internal static async Task<string> ReadRequestAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var line = new System.Text.StringBuilder();
        var buffer = new char[256];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) return line.ToString();
            int end = Array.IndexOf(buffer, '\n', 0, count);
            line.Append(buffer, 0, end >= 0 ? end : count);
            if (line.Length > MaximumRequestLength) throw new InvalidDataException("管理要求が長すぎます。");
            if (end >= 0) return line.ToString().TrimEnd('\r');
        }
    }

    // 文字列のみの旧形式と、JSONの新形式を解釈する。期待PIDが整数でなければ一致しない値として扱う。
    internal static (string Command, string RequestId, int? ExpectedPid) ParseRequest(string line)
    {
        if (!line.StartsWith('{')) return (line, "", null);
        using var request = JsonDocument.Parse(line);
        var command = request.RootElement.GetProperty("command").GetString() ?? "";
        var requestId = request.RootElement.TryGetProperty("requestId", out var req) ? req.GetString() ?? "" : "";
        int? expectedPid = null;
        if (request.RootElement.TryGetProperty("expectedPid", out var pid) && pid.ValueKind == JsonValueKind.Number)
            expectedPid = pid.TryGetInt32(out var value) ? value : 0;
        return (command, requestId, expectedPid);
    }

    // 既存画面を表示できたときはnull、できなかったときは利用者へ伝える理由を返す。
    public static string? ShowExisting(string id)
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
            var problem = ExistingInstanceProblem(process.SessionId == Process.GetCurrentProcess().SessionId,
                process.MainModule?.FileName, Environment.ProcessPath);
            if (problem is not null) return problem;
            AllowSetForegroundWindow(pid);
            using var showPipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            showPipe.Connect(1200);
            // 読み書きの後始末でpipeを閉じる順序に依存しないよう、pipe自体の破棄はusingに任せる。
            using var showWriter = new StreamWriter(showPipe, leaveOpen: true) { AutoFlush = true };
            showWriter.WriteLine(JsonSerializer.Serialize(new { command = "show", expectedPid = pid }));
            using var showReader = new StreamReader(showPipe, leaveOpen: true);
            var shown = showReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            using var showResponse = JsonDocument.Parse(shown ?? "");
            var result = showResponse.RootElement.GetProperty("result").GetString();
            return result == "completed" ? null
                : "起動中の画面を表示できませんでした。" + (showResponse.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : "");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or JsonException or InvalidOperationException or ArgumentException
            or KeyNotFoundException or FormatException or System.ComponentModel.Win32Exception)
        {
            return "起動中の同じツールが応答しません。タスクマネージャーで終了してから、起動し直してください。";
        }
    }

    // 実行ファイルが別の場所（ポータブル版とインストール版など）なら前面化せず、その旨を返す。
    internal static string? ExistingInstanceProblem(bool sameSession, string? runningPath, string? ownPath)
    {
        if (!sameSession) return "別のサインインセッションで同じツールが起動中です。";
        if (runningPath is not null && string.Equals(runningPath, ownPath, StringComparison.OrdinalIgnoreCase)) return null;
        return "別の場所から起動した同じツールが実行中です。起動中のものを終了してから、起動し直してください。\n\n"
            + $"起動中: {runningPath ?? "不明"}\n今回: {ownPath ?? "不明"}";
    }

    public void Dispose() => lifetime.Cancel();
}
