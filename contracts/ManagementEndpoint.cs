using System.IO.Pipes;
using System.Text.Json;
using System.Security.Principal;

namespace KotorinToys.Integration;

// ユーザー単位の管理窓口。UI操作はツールのUIスレッドで実行する。
internal sealed class ManagementEndpoint : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    public static string PipeName(string id) => "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + id;
    public ManagementEndpoint(string id, Control dispatcher, Action show, Action stop)
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
                    using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    requestDeadline.CancelAfter(TimeSpan.FromSeconds(3));
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    var command = await reader.ReadLineAsync(requestDeadline.Token);
                    if (command is not ("status" or "show" or "stop")) { await writer.WriteLineAsync("invalid"); continue; }
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { pid = Environment.ProcessId, ready = true }));
                    if (command != "status")
                        dispatcher.BeginInvoke(command == "show" ? show : stop);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (InvalidOperationException) { }
            }
        });
    }
    public void Dispose() { lifetime.Cancel(); }
}
