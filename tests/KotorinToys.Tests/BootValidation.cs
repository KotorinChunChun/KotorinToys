using System.Diagnostics;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;
using KotorinToys;

internal sealed record BootPlan(string Sid,DateTimeOffset BeforeBootUtc,string SettingsHash,Dictionary<string,string> DataHashes,TaskOwnership Task);
internal static class BootValidation
{
    static string Sid=>WindowsIdentity.GetCurrent().User!.Value;
    static DateTimeOffset BootUtc=>DateTimeOffset.UtcNow.AddMilliseconds(-Environment.TickCount64);
    static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    static ToolDefinition Host=>new("host","KotorinToys","KotorinToys.exe","README.md",AppId:"KotorinToys",InstalledExecutable:"KotorinToys.exe");
    static Dictionary<string,string> DataHashes(string root)
    {
        var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var files=new Dictionary<string,string>
        {
            ["live-text-marker-settings"]=Path.Combine(local,"LiveTextMarker","settings.json"),
            ["smart-paste-settings"]=Path.Combine(local,"SmartPaste","settings.json"),
            ["original-ss-capture-region"]=Path.Combine(root,"tools","SSCapture","src","SSCapture","bin","Release","net10.0-windows","data","last-region.json")
        };
        var capture=new InstallationDetector().Detect(new("ss-capture","SSCapture","SSCapture.exe","README.md",AppId:"KotorinToys.SSCapture",InstalledExecutable:"SSCapture.exe"));
        if(capture.Directory!=null)
        {
            files["installed-ss-capture-settings"]=Path.Combine(capture.Directory,"data","settings.json");
            files["installed-ss-capture-region"]=Path.Combine(capture.Directory,"data","last-region.json");
        }
        return files.ToDictionary(p=>p.Key,p=>File.Exists(p.Value)?Hash(p.Value):"absent");
    }
    public static void Tests(Action<bool,string> check)
    {
        var before=DateTimeOffset.UtcNow.AddHours(-1);
        check(!NewBoot(before,before.AddMilliseconds(5)),"再起動証跡で同じ起動中の手動試験を合格にしない");
        check(NewBoot(before,before.AddMinutes(5)),"再起動証跡はカーネル開始時刻の更新を要求");
        check(!OwnName("KotorinToys.external",Sid),"再起動検証の片付けで外部予約を扱わない");
        check(OwnName("KotorinToys.Test."+Guid.NewGuid().ToString("N")+"."+Sid+".fixture",Sid),"再起動検証の専用GUID・SID名だけ許可");
    }
    static bool NewBoot(DateTimeOffset before,DateTimeOffset current)=>current>before.AddSeconds(30);
    static bool OwnName(string name,string sid)=>System.Text.RegularExpressions.Regex.IsMatch(name,"^KotorinToys\\.Test\\.[a-f0-9]{32}\\."+System.Text.RegularExpressions.Regex.Escape(sid)+"\\.fixture$");
    public static async Task<int> Prepare()
    {
        var directory=Path.Combine(KotorinToys.Program.FindRoot(),"dev","history","v0.1.0","logs","reboot-preparation-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var result=await InstalledStartupTests.NativeTask("--arm-reboot-test "+StartupCoordinator.Quote(directory));
        Console.WriteLine("準備結果："+directory);
        return result;
    }
    public static async Task<int> ArmWithReceipt(string directory)
    {
        var logs=Path.Combine(KotorinToys.Program.FindRoot(),"dev","history","v0.1.0","logs");
        directory=PathSafety.Resolve(logs,Path.GetRelativePath(logs,Path.GetFullPath(directory)));
        try {var result=await Arm();File.WriteAllText(Path.Combine(directory,"result.txt"),"実サインイン検証予約の準備成功");return result;}
        catch(Exception ex){File.WriteAllText(Path.Combine(directory,"failure.txt"),ex.ToString());return 1;}
    }
    public static async Task<int> Arm()
    {
        var root=KotorinToys.Program.FindRoot();var installed=new InstallationDetector().Detect(Host);
        if(installed.State!="導入済み")throw new IOException("実利用者側の正式本体が必要です。");
        var store=new HostSettingsStore(HostSettingsStore.DefaultDirectory);var settings=store.Load();
        var manager=new ToolManager(root);var gateway=new TaskSchedulerGateway();
        var coordinator=new StartupCoordinator(store,gateway,manager,installed.Executable!);
        if(store.Journal!=null || store.RemovalPending || settings.OwnedTasks.Count!=1 || !settings.OwnedTasks.ContainsKey("host"))throw new IOException("経由3対象・共有予約1件の検証状態が必要です。");
        foreach(var tool in manager.Load())if((await coordinator.State(tool)).ActualState!="登録済み" || settings.StartupModes.GetValueOrDefault(tool.Id)!="via_kotorintoys")throw new IOException("経由予約が一致しません："+tool.Name);
        foreach(var tool in manager.Load())if((await manager.Stop(tool)).State!="停止中")throw new IOException(tool.Name+"を正常終了できません。再起動しません。");
        var dashboard=Host with{Id="dashboard",AppId=""};
        if((await new ToolManager(installed.Directory!).Stop(dashboard)).State!="停止中")throw new IOException("本体を正常終了できません。再起動しません。");
        var identity=Guid.NewGuid().ToString("N");
        var directory=Path.Combine(root,"dev","history","v0.1.0","logs","reboot-validation-"+identity);Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"plan.json");
        var plan=new TaskPlan("KotorinToys.Test."+identity+"."+Sid+".fixture",Environment.ProcessPath!,"--boot-check "+StartupCoordinator.Quote(path),AppContext.BaseDirectory,Sid,"KotorinToys:Test:"+identity);
        var actual=gateway.Create(plan);
        try
        {
            var own=new TaskOwnership(actual.Name,actual.Fingerprint,actual.Xml,actual.SecurityDescriptor);
            File.WriteAllText(path,JsonSerializer.Serialize(new BootPlan(Sid,BootUtc,Hash(Path.Combine(store.DirectoryPath,"settings.json")),DataHashes(root),own),ToolCatalog.Json));
            File.WriteAllText(Path.Combine(directory,"status.txt"),"準備済み。次回サインインで経由3対象・正式版・静かな開始・設定ハッシュを検証し、自分の検証予約だけ照合削除する。");
            Console.WriteLine("実サインイン検証準備："+directory);return 0;
        }
        catch{gateway.Delete(actual.Name,actual.Fingerprint);throw;}
    }
    public static async Task<int> Check(string path)
    {
        var root=KotorinToys.Program.FindRoot();var logs=Path.Combine(root,"dev","history","v0.1.0","logs");
        path=PathSafety.Resolve(logs,Path.GetRelativePath(logs,Path.GetFullPath(path)));
        var plan=JsonSerializer.Deserialize<BootPlan>(File.ReadAllText(path),ToolCatalog.Json)??throw new IOException("再起動計画が空です。");
        if(plan.Sid!=Sid || !OwnName(plan.Task.Name,Sid) || new ScheduledTaskState(plan.Task.Name,plan.Task.Xml,plan.Task.SecurityDescriptor).Fingerprint!=plan.Task.Fingerprint)throw new IOException("再起動検証の所有記録が不一致です。");
        var directory=Path.GetDirectoryName(path)!;var gateway=new TaskSchedulerGateway();
        if(!NewBoot(plan.BeforeBootUtc,BootUtc))
        {File.WriteAllText(Path.Combine(directory,"status.txt"),"未検証：カーネル再起動を確認できない。同じ起動中のタスク手動開始は実サインイン証跡にしない。");return 1;}
        try
        {
            var installed=new InstallationDetector().Detect(Host);if(installed.State!="導入済み")throw new IOException("サインイン側で正式本体を検出できません。");
            var manager=new ToolManager(root);var hostManager=new ToolManager(installed.Directory!);var store=new HostSettingsStore(HostSettingsStore.DefaultDirectory);
            var coordinator=new StartupCoordinator(store,gateway,manager,installed.Executable!);var tools=manager.Load();
            var dashboard=Host with{Id="dashboard",AppId=""};var deadline=DateTime.UtcNow.AddSeconds(90);ToolState[] states=[];
            while(DateTime.UtcNow<deadline)
            {
                states=await Task.WhenAll(new[]{hostManager.Status(dashboard)}.Concat(tools.Select(t=>manager.Status(t))));
                if(states.All(s=>s.State=="実行中" && s.LaunchOrigin=="via_kotorintoys"))break;
                await Task.Delay(500);
            }
            if(states.Length!=4 || states.Any(s=>s.State!="実行中" || s.LaunchOrigin!="via_kotorintoys" || s.Pid==null))throw new IOException("実サインインの本体・3子準備完了と経由開始を確認できません。");
            foreach(var state in states)
            {
                using var process=Process.GetProcessById(state.Pid!.Value);
                if(process.StartTime.ToUniversalTime()<BootUtc.UtcDateTime.AddSeconds(-5) || process.MainWindowHandle!=IntPtr.Zero)throw new IOException("再起動後の新インスタンスまたは静かな開始が不一致です。");
            }
            foreach(var tool in tools)if(manager.Installation(tool).State!="導入済み" || (await coordinator.State(tool)).ActualState!="登録済み")throw new IOException("サインイン後の正式導入・予約条件が不一致です。");
            if(Hash(Path.Combine(store.DirectoryPath,"settings.json"))!=plan.SettingsHash || JsonSerializer.Serialize(DataHashes(root),ToolCatalog.Json)!=JsonSerializer.Serialize(plan.DataHashes,ToolCatalog.Json))throw new IOException("再起動前後の確認対象設定ハッシュが不一致です。");
            File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new {Outcome="pass",ObservedAt=DateTimeOffset.UtcNow,BootUtc,BeforeBootUtc=plan.BeforeBootUtc,Sid,States=states,SettingsHash=plan.SettingsHash,DataHashes=plan.DataHashes},ToolCatalog.Json));
            File.WriteAllText(Path.Combine(directory,"status.txt"),"合格：再起動後の実サインインで本体・3子の経由準備完了・非表示開始・正式導入・管理予約・確認対象設定保持を検証。");return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(directory,"failure.txt"),ex.ToString());File.WriteAllText(Path.Combine(directory,"status.txt"),"失敗：実サインイン検証未完了。failure.txt参照。");return 1;}
        finally
        {
            if(gateway.Read(plan.Task.Name)?.Fingerprint==plan.Task.Fingerprint)gateway.Delete(plan.Task.Name,plan.Task.Fingerprint);
            else File.WriteAllText(Path.Combine(directory,"cleanup.txt"),"検証予約が外部変更または既に不在。自動削除していません。");
        }
    }
}
