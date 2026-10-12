using System.Diagnostics;
using System.Text.Json;
using KotorinToys;

internal static class InstalledStartupTests
{
    public static async Task<int> HostCycle(string catalog)
    {
        var root=KotorinToys.Program.FindRoot();var manager=new ToolManager(root);var tools=manager.Load();
        var host=new ToolDefinition("host","KotorinToys","KotorinToys.exe","README.md",AppId:"KotorinToys",InstalledExecutable:"KotorinToys.exe");
        var installed=new InstallationDetector().Detect(host);if(installed.State!="導入済み" || installed.Uninstaller==null)throw new IOException("正式本体が必要です。");
        var store=new HostSettingsStore(HostSettingsStore.DefaultDirectory);var before=store.Load();
        var gateway=new TaskSchedulerGateway();var coordinator=new StartupCoordinator(store,gateway,manager,installed.Executable!);
        var run=Path.Combine(root,"dev","history","v0.1.0","logs","host-removal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
        try
        {
            var childStates=await Task.WhenAll(tools.Select(t=>manager.Status(t)));
            if(childStates.Any(s=>s.State!="実行中"))throw new IOException("本体だけの削除試験には3子の実行中が必要です。");
            var direct=await coordinator.SetMode(tools[2],"direct_task");if(!direct.Success)throw new IOException(direct.Detail);
            var own=store.Load().OwnedTasks[tools[2].Id];
            var start=new ProcessStartInfo(installed.Uninstaller){UseShellExecute=false,CreateNoWindow=true};
            foreach(var arg in new[]{"/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART"})start.ArgumentList.Add(arg);
            using(var process=Process.Start(start)??throw new IOException("削除開始失敗")){await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException("本体削除の終了コード："+process.ExitCode);}
            if(new InstallationDetector().Detect(host).State!="未導入")throw new IOException("本体削除後の登録が残っています。");
            var after=store.Load();
            if(after.StartupModes.GetValueOrDefault(tools[0].Id)!="none" || after.StartupModes.GetValueOrDefault(tools[1].Id)!="none" || after.StartupModes.GetValueOrDefault(tools[2].Id)!="direct_task" || gateway.Read(own.Name)?.Fingerprint!=own.Fingerprint)throw new IOException("本体削除で直接予約保持・経由解除が不一致です。");
            for(int i=0;i<tools.Length;i++)if(manager.Installation(tools[i]).State!="導入済み" || (await manager.Status(tools[i])).Pid!=childStates[i].Pid)throw new IOException("本体削除が子の正式導入または実行中PIDを変更しました。");
            var restored=await new InstallerAdapter(manager).Install(host,catalog,installed.Directory);if(restored.State!="成功")throw new IOException(restored.Detail);
            foreach(var tool in tools){var result=await coordinator.SetMode(tool,before.StartupModes.GetValueOrDefault(tool.Id,"none"));if(!result.Success)throw new IOException(result.Detail);}
            if(before.HostStartup){var result=await coordinator.SetHost(true,before.ShowDashboardAtLogon);if(!result.Success)throw new IOException(result.Detail);}
            File.WriteAllText(Path.Combine(run,"result.txt"),"本体だけの正式削除・再導入：3子の正式導入と同じ実行中PID、直接予約、保存設定を保持。経由予約解除と元方式復元に成功。");return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(run,"failure.txt"),ex.ToString());throw;}
    }
    public static async Task<int> Cycle(string catalog)
    {
        var root=KotorinToys.Program.FindRoot();var manager=new ToolManager(root);var adapter=new InstallerAdapter(manager);
        InstallerAdapter.Load(catalog);
        var store=new HostSettingsStore(HostSettingsStore.DefaultDirectory);var before=store.Load();
        var run=Path.Combine(root,"dev","history","v0.1.0","logs","installation-cycle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
        var receipt=new List<object>();
        try
        {
            var host=new ToolDefinition("dashboard","KotorinToys","KotorinToys.exe","README.md",AppId:"",InstalledExecutable:"KotorinToys.exe");
            var hostInstallation=new InstallationDetector().Detect(host with{Id="host",AppId="KotorinToys"});
            if(hostInstallation.State!="導入済み")throw new IOException("正式本体が必要です。");
            if((await new ToolManager(hostInstallation.Directory!).Stop(host)).State!="停止中")throw new IOException("本体を正常終了できません。");
            foreach(var tool in manager.Load())
            {
                var installed=manager.Installation(tool);if(installed.State!="導入済み")throw new IOException(tool.Name+"は正式導入済みである必要があります。");
                var preserved=new Dictionary<string,string>();
                var candidates=new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),tool.Name,"settings.json"),Path.Combine(installed.Directory!,"data","settings.json"),Path.Combine(installed.Directory!,"data","last-region.json")};
                foreach(var file in candidates.Where(File.Exists))preserved[file]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
                var marker=Path.Combine(installed.Directory!,"data","retention-fixture-"+Guid.NewGuid().ToString("N")+".json");Directory.CreateDirectory(Path.GetDirectoryName(marker)!);File.WriteAllText(marker,"{\"fixture\":true}");preserved[marker]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(marker)));
                var removed=await adapter.Uninstall(tool);
                if(removed.State!="成功" || manager.Installation(tool).State!="未導入")throw new IOException(tool.Name+"の削除未完了："+removed.Detail);
                foreach(var file in preserved)if(!File.Exists(file.Key) || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file.Key)))!=file.Value)throw new IOException(tool.Name+"の保存データ保持が不一致です。");
                var restored=await adapter.Install(tool,catalog,installed.Directory);
                if(restored.State!="成功")throw new IOException(tool.Name+"の再導入未完了："+restored.Detail);
                foreach(var file in preserved)if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file.Key)))!=file.Value)throw new IOException(tool.Name+"の再導入後ハッシュが不一致です。");
                if((await manager.Start(tool,false,"standalone")).State!="実行中" || (await manager.Stop(tool)).State!="停止中")throw new IOException(tool.Name+"の単独再導入後起動・正常停止が不一致です。");
                receipt.Add(new {ToolId=tool.Id,Uninstall=removed,Reinstall=restored,PreservedFiles=preserved.Count,Standalone="準備完了・正常停止"});
                // 自作の空テスト標識だけ片付ける。実データ・フォルダーは削除しない。
                File.Delete(marker);
                File.WriteAllText(Path.Combine(run,"results.json"),JsonSerializer.Serialize(receipt,ToolCatalog.Json));
            }
            var coordinator=new StartupCoordinator(store,new TaskSchedulerGateway(),manager,hostInstallation.Executable!);
            foreach(var tool in manager.Load())
            {
                var result=await coordinator.SetMode(tool,before.StartupModes.GetValueOrDefault(tool.Id,"none"));
                if(!result.Success)throw new IOException("元の予約復元未完了："+result.Detail);
            }
            var hostResult=await coordinator.SetHost(before.HostStartup,before.ShowDashboardAtLogon);
            // 経由対象があるとfalse設定は解除操作として拒否される。共有予約と元の希望は保持される。
            if(!hostResult.Success && before.HostStartup)throw new IOException(hostResult.Detail);
            File.WriteAllText(Path.Combine(run,"result.txt"),"3製品：正式削除・再導入・保持ハッシュ・単独起動・予約復元 成功");return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(run,"failure.txt"),ex.ToString());throw;}
    }
    public static async Task<int> NativeInstall(string catalog)
    {
        var root=KotorinToys.Program.FindRoot();
        var full=Path.GetFullPath(catalog);
        if(!full.StartsWith(Path.Combine(root,"dev","history","v0.1.0","logs")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("検証ログ領域の配布台帳だけ実行できます。");
        var packages=InstallerAdapter.Load(full);
        if(packages.Packages.Length is not (1 or 4))throw new IOException("実利用者の導入試験は単体または4製品の台帳で行います。");
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"PowerShell","7","pwsh.exe")){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",Path.Combine(root,"dev","scripts","install-local.ps1"),"-CatalogPath",full,"-CustomValidationDirectory"})start.ArgumentList.Add(arg);
        if(packages.Packages.Length==1){start.ArgumentList.Add("-Products");start.ArgumentList.Add(packages.Packages[0].Name);}
        using var process=Process.Start(start)??throw new IOException("導入検証プロセスを開始できません。");
        await process.WaitForExitAsync();return process.ExitCode;
    }
    public static async Task<int> NativeTask(string arguments)
    {
        var gateway=new TaskSchedulerGateway();var sid=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var identity=Guid.NewGuid().ToString("N");
        var plan=new TaskPlan("KotorinToys.Test."+identity+"."+sid+".fixture",Environment.ProcessPath!,arguments,AppContext.BaseDirectory,sid,"KotorinToys:Test:"+identity);
        var own=gateway.Create(plan);var finished=false;
        try
        {
            gateway.RunOwned(own.Name,own.Fingerprint);
            for(int i=0;i<600;i++)
            {
                await Task.Delay(1000);var state=gateway.Outcome(own.Name);
                if(state.State!=4 && state.Result!=0x00041301){finished=true;Console.WriteLine("実利用者検証タスク：0x"+state.Result.ToString("X8"));return state.Result==0?0:1;}
            }
            throw new IOException("検証時間を超えました。強制終了しません。予約："+own.Name);
        }
        finally{if(finished)gateway.Delete(own.Name,own.Fingerprint);}
    }
    public static async Task Probe()
    {
        var gateway=new TaskSchedulerGateway();var identity=Guid.NewGuid().ToString("N");
        var sid=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var installed=new InstallationDetector().Detect(new("live-text-marker","LiveTextMarker","LiveTextMarker.exe","README.md",AppId:"KotorinToys.LiveTextMarker",InstalledExecutable:"LiveTextMarker.exe")).Directory
            ??throw new IOException("診断対象の正式導入先がありません。");
        var outputDirectory=Path.Combine(KotorinToys.Program.FindRoot(),"dev","history","v0.1.0","logs","scheduler-probe-"+identity);Directory.CreateDirectory(outputDirectory);
        var output=Path.Combine(outputDirectory,"filesystem.json");
        var cases=new[]{(Name:"テストEXE",Exe:Environment.ProcessPath!,Args:"--foreground-pid",Dir:AppContext.BaseDirectory),
            (Name:"経由方式のScheduler実態照合",Exe:Environment.ProcessPath!,Args:"--via-probe "+StartupCoordinator.Quote(new InstallationDetector().Detect(new("host","KotorinToys","KotorinToys.exe","README.md",AppId:"KotorinToys",InstalledExecutable:"KotorinToys.exe")).Directory!)+" "+StartupCoordinator.Quote(Path.Combine(outputDirectory,"via-state.json")),Dir:AppContext.BaseDirectory),
            (Name:"本体設定のScheduler読取",Exe:Environment.ProcessPath!,Args:"--filesystem-probe "+StartupCoordinator.Quote(Path.Combine(HostSettingsStore.DefaultDirectory,"settings.json"))+" "+StartupCoordinator.Quote(Path.Combine(outputDirectory,"host-settings-visibility.json")),Dir:AppContext.BaseDirectory),
            (Name:"Scheduler利用者からのファイル読取",Exe:Environment.ProcessPath!,Args:"--filesystem-probe "+StartupCoordinator.Quote(Path.Combine(installed,"LiveTextMarker.exe"))+" "+StartupCoordinator.Quote(output),Dir:AppContext.BaseDirectory),
            (Name:"同じ導入先のメンテナンス",Exe:Path.Combine(installed,"maintenance","KotorinToys.Maintenance.exe"),Args:"--product Unknown",Dir:installed),
            (Name:"メンテナンス・既知の作業先",Exe:Path.Combine(installed,"maintenance","KotorinToys.Maintenance.exe"),Args:"--product Unknown",Dir:AppContext.BaseDirectory),
            (Name:"メンテナンス・末尾区切り",Exe:Path.Combine(installed,"maintenance","KotorinToys.Maintenance.exe"),Args:"--product Unknown",Dir:installed+Path.DirectorySeparatorChar),
            (Name:"正式LiveTextMarker",Exe:Path.Combine(installed,"LiveTextMarker.exe"),Args:"--startup --launch-origin direct_task",Dir:installed)};
        foreach(var probe in cases)
        {
            var plan=new TaskPlan("KotorinToys.Test."+identity+"."+sid+".fixture",probe.Exe,probe.Args,probe.Dir,sid,"KotorinToys:Test:"+identity);
            ScheduledTaskState? own=null;
            try
            {
                own=gateway.Create(plan);gateway.RunOwned(own.Name,own.Fingerprint);await Task.Delay(3000);
                var outcome=gateway.Outcome(own.Name);Console.WriteLine(probe.Name+"：State="+outcome.State+", Result=0x"+outcome.Result.ToString("X8"));
            }
            finally{if(own!=null)gateway.Delete(own.Name,own.Fingerprint);}
            if(probe.Name=="Scheduler利用者からのファイル読取" && File.Exists(output))Console.WriteLine(File.ReadAllText(output));
            if(probe.Name=="正式LiveTextMarker")
            {
                var stopped=await new ToolManager(installed).Stop(new("live-text-marker","LiveTextMarker","LiveTextMarker.exe","README.md",AppId:"KotorinToys.LiveTextMarker",InstalledExecutable:"LiveTextMarker.exe"));
                if(stopped.State!="停止中")throw new IOException("診断で起動したアプリを正常終了できません。");
            }
        }
    }
    public static async Task Run(string fixtures,Action<bool,string> check)
    {
        var host=new ToolDefinition("host","KotorinToys","KotorinToys.exe","README.md",AppId:"KotorinToys",InstalledExecutable:"KotorinToys.exe");
        var installation=new InstallationDetector().Detect(host);
        if(installation.State!="導入済み" || installation.Directory==null || installation.Executable==null)throw new IOException("本体の正式導入が必要です。");
        var manager=new ToolManager(installation.Directory);var tools=manager.Load();
        foreach(var tool in tools)
        {
            check(manager.Installation(tool).State=="導入済み",tool.Name+"の独立した正式導入検出");
            if((await manager.Status(tool)).State!="停止中")throw new IOException("実予約試験には全ツールの停止状態が必要です。既存プロセスは変更しません。");
        }
        var dashboard=host with{Id="dashboard",AppId=""};
        if((await manager.Status(dashboard)).State!="停止中")throw new IOException("本体を正常終了してから試験してください。");
        var store=new HostSettingsStore(HostSettingsStore.DefaultDirectory);var gateway=new TaskSchedulerGateway();
        var coordinator=new StartupCoordinator(store,gateway,manager,installation.Executable);
        async Task Ready(ToolDefinition tool,string origin)
        {
            var timeout=DateTime.UtcNow.AddSeconds(30);
            while(DateTime.UtcNow<timeout)
            {
                var state=await manager.Status(tool);
                if(state.State=="実行中")
                {
                    check(state.LaunchOrigin==origin,tool.Name+"の実タスク起動経路 "+origin);
                    File.WriteAllText(Path.Combine(fixtures,tool.Id+"-"+origin+".json"),JsonSerializer.Serialize(state,ToolCatalog.Json));return;
                }
                await Task.Delay(250);
            }
            throw new IOException(tool.Name+"の準備完了を確認できません。");
        }
        // 明示的な--installed-startupだけが通常の管理予約を変更する。
        foreach(var tool in tools)
        {
            var result=await coordinator.SetMode(tool,"direct_task");check(result.Success,tool.Name+"の正式EXE直接予約登録："+result.Detail);
            var own=store.Load().OwnedTasks[tool.Id];gateway.RunOwned(own.Name,own.Fingerprint);
            await Ready(tool,"direct_task");
            check((await manager.Stop(tool)).State=="停止中",tool.Name+"の直接タスク実行後の正常終了");
            check((await coordinator.State(tool)).ActualState=="登録済み",tool.Name+"の停止では次回予約を解除しない");
        }
        foreach(var tool in tools)
        {
            var result=await coordinator.SetMode(tool,"via_kotorintoys");check(result.Success,tool.Name+"の経由方式切替："+result.Detail);
            check(!store.Load().OwnedTasks.ContainsKey(tool.Id),tool.Name+"の古い直接予約を残さない");
        }
        var hostTask=store.Load().OwnedTasks["host"];gateway.RunOwned(hostTask.Name,hostTask.Fingerprint);
        await Ready(dashboard,"via_kotorintoys");
        foreach(var tool in tools)await Ready(tool,"via_kotorintoys");
        check(store.Load().OwnedTasks.Count==1,"経由3対象が共有する本体予約は1件");
        Console.WriteLine("実タスク手動開始の検証成功。通常設定は3ツールの経由起動を維持し、実サインインの別検証に備えます。OS再起動は行いません。");
    }
}
