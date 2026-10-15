using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace KotorinToys;

internal sealed partial class ToolManager
{
    public async Task<Reply> Send(ToolDefinition tool, string command, int? expectedPid = null, CancellationToken cancellation = default)
    {
        var pipeId = tool.Msix is { } msix ? tool.Id + "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(msix.FamilyName))).ToLowerInvariant() : tool.Id;
        var name = "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + pipeId;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await pipe.ConnectAsync(deadline.Token);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true)
        {
            AutoFlush = true
        };
        var request = command == "status" ? command : JsonSerializer.Serialize(new { command, expectedPid, requestId = Guid.NewGuid().ToString("N") });
        await writer.WriteLineAsync(request.AsMemory(), deadline.Token);
        return JsonSerializer.Deserialize<Reply>(await reader.ReadLineAsync(deadline.Token) ?? "", ToolCatalog.Json) ?? throw new IOException("応答がありません。");
    }
}
