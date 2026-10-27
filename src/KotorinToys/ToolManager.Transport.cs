using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace KotorinToys;

internal sealed partial class ToolManager
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    public async Task<Reply> Send(ToolDefinition tool, string command, int? expectedPid = null, CancellationToken cancellation = default)
    {
        var pipeId = tool.Msix is { } msix ? tool.Id + "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(msix.FamilyName))).ToLowerInvariant() : tool.Id;
        var name = "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + pipeId;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await pipe.ConnectAsync(deadline.Token);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || serverPid == 0)
            throw new IOException("管理窓口のプロセスを確認できません。");
        if (expectedPid is { } expected && serverPid != expected)
            throw new IOException("管理窓口のプロセスが変わりました。操作を中止します。");
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true)
        {
            AutoFlush = true
        };
        var request = command == "status" ? command : JsonSerializer.Serialize(new { command, expectedPid, requestId = Guid.NewGuid().ToString("N") });
        await writer.WriteLineAsync(request.AsMemory(), deadline.Token);
        var reply = JsonSerializer.Deserialize<Reply>(await reader.ReadLineAsync(deadline.Token) ?? "", ToolCatalog.Json) ?? throw new IOException("応答がありません。");
        if (reply.Pid != serverPid) throw new IOException("管理窓口と応答のプロセスが一致しません。");
        return reply;
    }
}
