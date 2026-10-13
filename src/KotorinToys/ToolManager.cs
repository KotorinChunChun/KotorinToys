using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace KotorinToys;
internal sealed partial class ToolManager(string root)
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
        try
        {
            runtimes = RuntimeDetector.Detect(tool, Executable(tool));
        }
        catch (IOException ex)
        {
            runtimes = [new("unknown", "依存情報", "未確認", "基本機能", false, ex.Message)];
        }

        return new(tool, installed, runtimes, state, startup ?? new(), DateTimeOffset.UtcNow, installed.State == "導入済み" ? "HKCU Inno登録・製品定義・EXE製品名／版" : "正式導入検出と開発配置は別扱い");
    }

    public string Executable(ToolDefinition tool)
    {
        var installed = Installation(tool);
        if (installed.State == "導入済み")
            return installed.Executable!;
        if (installed.State != "未導入")
            throw new IOException(installed.Detail);
        return PathSafety.Resolve(DevelopmentRoot(tool), tool.Executable);
    }
}
