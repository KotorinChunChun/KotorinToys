using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using KotorinToys;

internal static class ScriptSyncIntegrationTests
{
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder title, int count);
    delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    static IntPtr FindWindow(int pid)
    {
        IntPtr found = 0;
        EnumWindow callback = (window, _) => {
            GetWindowThreadProcessId(window, out var owner);
            var text = new StringBuilder(512); GetWindowText(window, text, text.Capacity);
            if (owner == pid && text.ToString().Contains("(ScriptSync)")) { found = window; return false; }
            return true;
        };
        EnumWindows(callback, 0); GC.KeepAlive(callback); return found;
    }
    internal static string NewDirectory(string root) => Path.Combine(root,"dev","history","v0.1.11","logs","scriptsync-ipc-"+Guid.NewGuid().ToString("N"));
    internal static ToolManager Manager(string root, string directory, ToolCatalogSource? source = null) => new(root, (tool, start) => {
        if (tool.Id != "script-sync") throw new IOException("この試験はScriptSyncだけを起動できます。");
        start.ArgumentList.Add("--settings-dir"); start.ArgumentList.Add(directory);
    }, source);
    public static int Panel()
    {
        var root = KotorinToys.Program.FindRoot(); var directory = NewDirectory(root); Directory.CreateDirectory(directory);
        var tool = ToolCatalog.Load(root).Single(t=>t.Id=="script-sync");
        tool = tool with { ToolRoot = tool.ToolRoot.Replace('\\','/') };
        var settings = new HostSettingsStore(Path.Combine(directory,"host"));
        var source = new ToolCatalogSource(root, settings.DirectoryPath, _ => Task.FromResult(OnlineCatalogTests.Encode(tool)));
        source.Refresh().GetAwaiter().GetResult();
        var manager = Manager(root,Path.Combine(directory,"child"),source);
        if(manager.Status(tool).GetAwaiter().GetResult().State!="停止中")throw new IOException("既存ScriptSyncがあるため隔離画面は開始しません。");
        var startup = new StartupCoordinator(settings,new ReadOnlyGateway(),manager,Environment.ProcessPath!);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var area=Screen.PrimaryScreen!.WorkingArea;
        using var form = new Dashboard(root,settings,startup,toolManager:manager,
            releaseCheck:(_,_,_)=>Task.FromResult(new PublicRelease(null,false,false,"隔離試験。公開先へ接続しません。")),
            openRepository:_=>{},showInstaller:(_,_)=>{},showToolInstaller:(_,_,_)=>{},showSelfUpdater:()=>{},showDevelopmentState:true)
        { StartPosition=FormStartPosition.Manual, Bounds=new(area.Left+area.Width/2,area.Top,area.Width-area.Width/2,area.Height) };
        Console.WriteLine("ScriptSync隔離画面: "+directory);
        try { Application.Run(form); return 0; }
        finally { Task.Run(async()=>{if((await manager.Status(tool)).State=="実行中")await manager.Stop(tool);}).GetAwaiter().GetResult(); }
    }
    sealed class ReadOnlyGateway : IStartupGateway
    {
        public ScheduledTaskState? Read(string name)=>null;
        public ScheduledTaskState Create(TaskPlan plan)=>throw new IOException("隔離試験では予約を変更しません。");
        public ScheduledTaskState Restore(string name,string xml,string securityDescriptor="")=>throw new IOException("隔離試験では予約を変更しません。");
        public void Delete(string name,string fingerprint)=>throw new IOException("隔離試験では予約を変更しません。");
    }
    public static async Task<int> Run()
    {
        var root = KotorinToys.Program.FindRoot(); var directory = NewDirectory(root); Directory.CreateDirectory(directory);
        var manager = Manager(root, directory); var tool = manager.Load().Single(t => t.Id == "script-sync");
        int count = 0; var messages = new List<string>(); int? owned = null;
        void Check(bool ok,string message) { if(!ok) throw new IOException(message); messages.Add("PASS: "+message); Console.WriteLine(messages[^1]); count++; }
        try
        {
            var existing = Process.GetProcessesByName("scriptsync");
            try { Check(existing.Length == 0,"既存ScriptSyncがなく通常作業へ干渉しない"); } finally { foreach(var p in existing)p.Dispose(); }
            Check((await manager.Status(tool)).State == "停止中","起動前は停止中");
            var started = await manager.Start(tool, false); owned = started.Pid;
            Check(started.State == "実行中" && owned.HasValue,"本体ToolManagerから実EXEの起動・準備完了: "+started.Detail);
            using var process = Process.GetProcessById(owned!.Value);
            Check(started.Version == tool.Version && started.ProtocolVersion == 2 && started.LaunchOrigin == "host_manual","4部版・管理契約2・起動経路を確認");
            var window = FindWindow(owned.Value);
            Check(window != 0 && !IsWindowVisible(window),"管理起動直後はメイン画面を表示しない");
            var status = await manager.Send(tool,"status");
            Check(status.Ready && status.Capabilities!.SequenceEqual(["status","show","stop"]),"実状態応答と3操作の能力を確認");
            Check((await manager.Send(tool,"show",owned.Value+1)).Result == "refused" &&
                (await manager.Send(tool,"stop",owned.Value+1)).Result == "refused" &&
                (await manager.Send(tool,"stop")).Result == "refused","誤PIDと対象PID欠落の表示・停止を拒否");
            Check((await manager.Send(tool,"shortcuts",owned)).Result == "unsupported","未対応操作を成功扱いしない");
            Check((await manager.Start(tool,false)).Pid == owned,"起動済みプロセスのPIDを維持");
            Check((await manager.Start(tool,true)).Pid == owned && IsWindowVisible(window),"同じPIDのメイン画面を表示");
            ShowWindow(window,6);
            Check((await manager.Start(tool,true)).Pid == owned && !IsIconic(window),"最小化した同じ画面を復元");
            Check((await manager.Stop(tool)).State == "停止中" && process.HasExited,"停止受理後の実プロセス終了を確認"); owned=null;
            Check(File.Exists(Path.Combine(directory,"config","読みあわせ","window-state.json")),"終了時の画面状態を専用設定へ保存");
            var restarted = await manager.Start(tool,false,"via_kotorintoys"); owned=restarted.Pid;
            Check(restarted.State == "実行中" && restarted.Pid != started.Pid && restarted.LaunchOrigin == "via_kotorintoys","別PIDで再起動し起動経路を反映");
            Check((await manager.Stop(tool)).State == "停止中","再起動した試験プロセスも正常停止"); owned=null;
            var info = new ProcessStartInfo(manager.Executable(tool)) { UseShellExecute=false };
            info.ArgumentList.Add("--settings-dir"); info.ArgumentList.Add(directory);
            using var standalone = Process.Start(info)!; owned=standalone.Id;
            var limit=DateTime.UtcNow.AddSeconds(20); ToolState state;
            do { state=await manager.Status(tool); if(state.State=="実行中")break; await Task.Delay(100); }while(DateTime.UtcNow<limit);
            Check(state.State=="実行中" && state.Pid==owned && state.LaunchOrigin=="standalone" && IsWindowVisible(FindWindow(owned.Value)),"引数なしの通常起動はメイン画面を表示");
            Check((await manager.Stop(tool)).State=="停止中","通常起動にも本体から正常停止を依頼できる"); owned=null;
            Console.WriteLine($"PASS: {count} 件 / {directory}"); return 0;
        }
        catch(Exception error){messages.Add("FAIL: "+error);Console.Error.WriteLine(error);return 1;}
        finally
        {
            if(owned.HasValue)try{var state=await manager.Status(tool);if(state.Pid==owned && state.State=="実行中")messages.Add("後始末: "+(await manager.Stop(tool)).State);}catch(Exception error){messages.Add("正常終了未確認: "+error.Message);}
            File.WriteAllLines(Path.Combine(directory,"result.txt"),messages);
        }
    }
}
