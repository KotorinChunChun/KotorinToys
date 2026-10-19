using System.Security.Principal;
using System.Xml.Linq;
using KotorinToys;

internal static class StartupTests
{
    internal sealed class FakeGateway : IStartupGateway
    {
        public Dictionary<string,ScheduledTaskState> Tasks=new();
        public bool FailNextCreate;
        public ScheduledTaskState? Read(string name)=>Tasks.GetValueOrDefault(name);
        public ScheduledTaskState Create(TaskPlan plan)
        {
            if(FailNextCreate){FailNextCreate=false;throw new IOException("登録失敗注入");}
            if(Tasks.ContainsKey(plan.Name))throw new IOException("同名予約");
            return Tasks[plan.Name]=new(plan.Name,Xml(plan));
        }
        public ScheduledTaskState Restore(string name,string xml,string securityDescriptor="")
        {if(Tasks.ContainsKey(name))throw new IOException("復元衝突");return Tasks[name]=new(name,xml,securityDescriptor);}
        public void Delete(string name,string expectedFingerprint)
        {if(Read(name)?.Fingerprint!=expectedFingerprint)throw new IOException("外部変更");Tasks.Remove(name);}
    }
    internal static string Xml(TaskPlan plan)
    {
        XNamespace ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement Element(string name,object? value)=>new(ns+name,value);
        return new XDocument(new XElement(ns+"Task",
            Element("RegistrationInfo",Element("Description",plan.Marker)),
            Element("Principals",Element("Principal",new[]{Element("UserId",plan.Sid),Element("LogonType","InteractiveToken"),Element("RunLevel","LeastPrivilege")})),
            Element("Triggers",Element("LogonTrigger",new[]{Element("UserId",plan.Sid),Element("Delay","PT5S")})),
            Element("Actions",Element("Exec",new[]{Element("Command",plan.Executable),Element("Arguments",plan.Arguments),Element("WorkingDirectory",plan.WorkingDirectory)})),
            Element("Settings",new[]{Element("ExecutionTimeLimit","PT0S"),Element("DisallowStartIfOnBatteries","false"),Element("StopIfGoingOnBatteries","false"),Element("MultipleInstancesPolicy","IgnoreNew"),Element("AllowHardTerminate","false"),Element("StartWhenAvailable","true"),Element("Enabled","true")}))).ToString();
    }
    public static void Cleanup(string name,string marker)
    {
        var gateway=new TaskSchedulerGateway();var actual=gateway.Read(name);if(actual==null)return;
        var plan=new TaskPlan(name,Environment.ProcessPath!,"--foreground-pid",Path.GetDirectoryName(Environment.ProcessPath!)!,WindowsIdentity.GetCurrent().User!.Value,marker);
        if(!TaskSchedulerGateway.Matches(actual,plan))throw new IOException("テスト予約の条件が一致しません。削除しません。");
        gateway.Delete(name,actual.Fingerprint);Console.WriteLine("専用テスト予約を所有条件照合後に解除しました。");
    }
    public static void Run(string root,string fixtures,Action<bool,string> check)
    {
        var directory=Path.Combine(fixtures,"startup");var store=new HostSettingsStore(directory);
        var gateway=new FakeGateway();var manager=new ToolManager(root);var tools=manager.Load();
        var coordinator=new StartupCoordinator(store,gateway,manager,Environment.ProcessPath!,allowDevelopment:true);
        var initial=store.Load();check(initial.SchemaVersion==1 && initial.StartupModes.Count==0,"本体設定の既定値と形式版");
        check(ExternalStartupDetector.MatchesCommand("\"C:/example path/app.exe\" --startup","C:/example path/app.exe"),"外部Run登録の引用付き実パス照合");
        check(!ExternalStartupDetector.MatchesCommand("C:/another/app.exe --startup","C:/example/app.exe"),"外部Run登録を名前の部分一致で識別しない");
        store.Save(initial);store.Save(initial with{ShowDashboardAtLogon=true});
        check(File.Exists(Path.Combine(directory,"settings.json.bak")),"本体設定の更新前バックアップ");
        check(store.Load().ShowDashboardAtLogon,"本体設定の原子的保存・往復");
        using(var lease=store.AcquireLease())
        {
            bool denied=false;try{using var second=store.AcquireLease();}catch(IOException){denied=true;}
            check(denied,"別プロセス相当の予約操作競合を拒否");
        }
        var result=coordinator.SetMode(tools[0],"direct_task").GetAwaiter().GetResult();
        check(result.Success && gateway.Tasks.Count==1,"直接予約だけを登録");
        check(coordinator.State(tools[0]).GetAwaiter().GetResult().ActualState=="登録済み","希望方式と実予約の照合");
        result=coordinator.SetMode(tools[0],"via_kotorintoys").GetAwaiter().GetResult();
        check(result.Success && gateway.Tasks.Count==1 && store.Load().OwnedTasks.ContainsKey("host"),"直接から経由へ切替・直接予約は残さない");
        result=coordinator.SetMode(tools[1],"via_kotorintoys").GetAwaiter().GetResult();
        check(result.Success && gateway.Tasks.Count==1,"経由対象が共有する本体予約");
        var shared=store.Load().OwnedTasks["host"];
        gateway.Tasks[shared.Name]=gateway.Tasks[shared.Name] with{Xml=shared.Xml.Replace("PT0S","PT72H")};
        coordinator.StartViaTargets().GetAwaiter().GetResult();
        var attempt=store.LastAttempt!;
        check(attempt.Items.Length==2 && attempt.Items.All(i=>i.Result=="見送り" && i.Detail.Contains("要対応") && i.Pid==null),"経由起動の予約不一致は無言で捨てず対象別の理由を記録");
        gateway.Tasks[shared.Name]=new(shared.Name,shared.Xml,shared.SecurityDescriptor);
        check(!coordinator.SetHost(false,false).GetAwaiter().GetResult().Success,"経由対象がある本体予約解除を拒否");
        coordinator.SetMode(tools[0],"none").GetAwaiter().GetResult();
        check(gateway.Tasks.Count==1,"他の経由対象があれば本体予約を保持");
        coordinator.SetMode(tools[1],"none").GetAwaiter().GetResult();
        check(gateway.Tasks.Count==0,"最後の経由解除で不要な本体予約だけ解除");
        check(coordinator.SetHost(true,false).GetAwaiter().GetResult().Success,"本体自身のサインイン希望を登録");
        coordinator.SetMode(tools[0],"via_kotorintoys").GetAwaiter().GetResult();
        coordinator.SetMode(tools[0],"none").GetAwaiter().GetResult();
        check(gateway.Tasks.Count==1,"本体自身の希望があれば共有予約を残す");
        coordinator.SetHost(false,false).GetAwaiter().GetResult();
        VerifyRootMigration(root, fixtures, tools[0], check);
        coordinator.SetMode(tools[0],"direct_task").GetAwaiter().GetResult();
        gateway.FailNextCreate=true;
        result=coordinator.SetMode(tools[0],"via_kotorintoys").GetAwaiter().GetResult();
        check(!result.Success && store.Load().StartupModes[tools[0].Id]=="direct_task" && gateway.Tasks.Count==1,"登録失敗時に希望と旧予約を復元");
        check(store.Journal==null,"復元の照合完了後に操作記録を完了化");
        var owner=store.Load().OwnedTasks[tools[0].Id];
        gateway.Tasks[owner.Name]=gateway.Tasks[owner.Name] with{Xml=gateway.Tasks[owner.Name].Xml.Replace("PT0S","PT72H")};
        check(!coordinator.SetMode(tools[0],"none").GetAwaiter().GetResult().Success && gateway.Tasks.Count==1,"外部変更された予約を削除しない");
        check(coordinator.State(tools[0]).GetAwaiter().GetResult().ActualState=="要対応","外部変更は登録済みにしない");
        var clean=new HostSettingsStore(Path.Combine(fixtures,"startup-production"));
        var developmentRoot=Path.Combine(fixtures,"unregistered-development");Directory.CreateDirectory(Path.Combine(developmentRoot,"catalog"));
        var unregistered=tools[0] with{AppId="",ToolRoot="",Executable="development.exe"};
        File.WriteAllText(Path.Combine(developmentRoot,"catalog","tools.json"),System.Text.Json.JsonSerializer.Serialize(new Catalog(1,[unregistered]),ToolCatalog.Json));
        var rejected=new StartupCoordinator(clean,new FakeGateway(),new ToolManager(developmentRoot),Environment.ProcessPath!).SetMode(unregistered,"direct_task").GetAwaiter().GetResult();
        check(!rejected.Success && rejected.Detail.Contains("正式") && rejected.Detail.Contains(unregistered.Name),
            "開発EXEを正式な安定起動先として予約しない");
        var recoveryStore=new HostSettingsStore(Path.Combine(fixtures,"startup-recovery"));var recoveryGateway=new FakeGateway();
        var recovery=new StartupCoordinator(recoveryStore,recoveryGateway,manager,Environment.ProcessPath!,allowDevelopment:true);
        var beforeRecovery=recoveryStore.Load();recoveryStore.Save(beforeRecovery);
        var taskName="KotorinToys."+WindowsIdentity.GetCurrent().User!.Value+"."+tools[0].Id;
        var recoveryPlan=new TaskPlan(taskName,manager.Executable(tools[0]),"--startup --launch-origin direct_task",Path.GetDirectoryName(manager.Executable(tools[0]))!,WindowsIdentity.GetCurrent().User!.Value,"KotorinToys:"+beforeRecovery.OwnerToken+":"+tools[0].Id);
        var created=recoveryGateway.Create(recoveryPlan);
        var journal=new StartupJournal(Guid.NewGuid().ToString("N"),tools[0].Id,beforeRecovery,"登録後中断",new(),new(){[tools[0].Id]=recoveryPlan});
        recoveryStore.SaveJournal(journal);
        check(recovery.State(tools[0]).GetAwaiter().GetResult().ActualState=="要対応","途中中断の記録があれば登録済みとしない");
        check(recovery.Recover().GetAwaiter().GetResult().Success && recoveryGateway.Tasks.Count==0,"登録直後・所有記録保存前の中断を条件照合して復旧");
        created=recoveryGateway.Create(recoveryPlan);recoveryStore.SaveJournal(journal);
        recoveryGateway.Tasks[taskName]=created with{Xml=created.Xml.Replace("PT5S","PT9S")};
        check(!recovery.Recover().GetAwaiter().GetResult().Success && recoveryStore.Journal?.Phase=="要対応" && recoveryGateway.Tasks.Count==1,"中断後の外部変更を残し要対応として報告");
        recoveryGateway.Tasks[taskName]=created;
        check(recovery.Recover().GetAwaiter().GetResult().Success,"条件が戻った未完了操作を再照合して復旧");
        var modes=new[]{recovery.SetMode(tools[0],"direct_task"),recovery.SetMode(tools[0],"via_kotorintoys")};Task.WaitAll(modes);
        check(modes.Any(task=>task.Result.Success) && recoveryGateway.Tasks.Count==1,"同時方式切替を直列化または拒否し管理方式は最大1");
        var xmlPlan=new TaskPlan(taskName,Environment.ProcessPath!,"--startup",Path.GetDirectoryName(Environment.ProcessPath!)!,WindowsIdentity.GetCurrent().User!.Value,"fixture");
        var valid=new ScheduledTaskState(taskName,Xml(xmlPlan));
        check(!TaskSchedulerGateway.Matches(valid with{Xml=valid.Xml.Replace("PT0S","PT72H")},xmlPlan),"予約照合は常駐の時間上限変更を拒否");
        check(!TaskSchedulerGateway.Matches(valid with{Xml=valid.Xml.Replace("<StopIfGoingOnBatteries>false","<StopIfGoingOnBatteries>true")},xmlPlan),"予約照合はバッテリー移行の停止を拒否");
        check(!TaskSchedulerGateway.Matches(valid with{Xml=valid.Xml.Replace("LeastPrivilege","HighestAvailable")},xmlPlan),"予約照合は権限昇格を拒否");
        var raw=Path.Combine(directory,"settings.json");File.WriteAllText(raw,"invalid");
        bool corrupted=false;try{store.Load();}catch(IOException){corrupted=true;}
        check(corrupted && File.ReadAllText(raw)=="invalid","破損した本体設定を既定値で上書きしない");
    }
    static void VerifyRootMigration(string root, string fixtures, ToolDefinition tool, Action<bool,string> check)
    {
        var newRoot=Path.Combine(fixtures,"new installed root");
        Directory.CreateDirectory(Path.Combine(newRoot,"catalog"));
        File.WriteAllText(Path.Combine(newRoot,"catalog","tools.json"),System.Text.Json.JsonSerializer.Serialize(new Catalog(1,[tool with{ToolRoot=""}]),ToolCatalog.Json));
        var store=new HostSettingsStore(Path.Combine(fixtures,"root-migration"));var gateway=new FakeGateway();
        var previous=new StartupCoordinator(store,gateway,new ToolManager(root),Environment.ProcessPath!,allowDevelopment:true);
        var current=new StartupCoordinator(store,gateway,new ToolManager(newRoot),Environment.ProcessPath!,allowDevelopment:true);
        check(previous.SetMode(tool,"via_kotorintoys").GetAwaiter().GetResult().Success,"旧ルートの共有予約を準備");
        var before=store.Load();var old=before.OwnedTasks["host"];
        gateway.FailNextCreate=true;
        var failed=StartupRootMaintenance.ChangeWithRecovery(current,previous,tool).GetAwaiter().GetResult();
        check(!failed.Success && store.Journal==null && gateway.Read(old.Name)?.Fingerprint==old.Fingerprint,"ルート変更の登録失敗時は旧ルートの所有条件で正確に復元");
        check(store.Load().OwnerToken==before.OwnerToken && store.Load().StartupModes.SequenceEqual(before.StartupModes),"復元時に所有トークンと希望方式を保持");
        var success=StartupRootMaintenance.ChangeWithRecovery(current,previous,tool).GetAwaiter().GetResult();
        check(success.Success && current.State(tool).GetAwaiter().GetResult().ActualState=="登録済み","空白付き正式ルートへ共有予約と所有記録を同時切替");
        var own=store.Load().OwnedTasks["host"];
        check(own.Xml.Contains(newRoot) && store.Load().OwnerToken==before.OwnerToken && store.Load().StartupModes.SequenceEqual(before.StartupModes),"切替後も利用者設定は保持し参照先のみ更新");
        gateway.Tasks[own.Name]=gateway.Tasks[own.Name] with{Xml=own.Xml.Replace("PT5S","PT9S")};
        var external=gateway.Tasks[own.Name].Fingerprint;
        check(!StartupRootMaintenance.ChangeWithRecovery(previous,current,tool).GetAwaiter().GetResult().Success && gateway.Read(own.Name)?.Fingerprint==external,"切替時の外部変更を復旧処理でも上書きしない");
    }
    public static void LiveScheduler(string root,string fixtures,Action<bool,string> check)
    {
        var gateway=new TaskSchedulerGateway();var name="KotorinToys.Test."+Guid.NewGuid().ToString("N")+"."+WindowsIdentity.GetCurrent().User!.Value+".fixture";
        var plan=new TaskPlan(name,Environment.ProcessPath!,"--foreground-pid",Path.GetDirectoryName(Environment.ProcessPath!)!,WindowsIdentity.GetCurrent().User!.Value,"KotorinToys:Test:"+Guid.NewGuid().ToString("N"));
        ScheduledTaskState? created=null;
        try
        {
            check(gateway.Read(name)==null,"専用の実予約テスト名は未使用");
            created=gateway.Create(plan);check(TaskSchedulerGateway.Matches(created,plan),"実Task SchedulerのSID・無期限・電源・権限・実行先照合");
            check(gateway.Read(name)?.Fingerprint==created.Fingerprint,"実予約の再読み取りで定義一致");
            bool collision=false;try{gateway.Create(plan);}catch(IOException){collision=true;}
            check(collision,"実予約の同名上書きを拒否");
            bool changed=false;try{gateway.Delete(name,"not-the-fingerprint");}catch(IOException){changed=true;}
            check(changed && gateway.Read(name)!=null,"実予約は所有照合なしに削除しない");
            gateway.Delete(name,created.Fingerprint);check(gateway.Read(name)==null,"専用実予約を所有照合付きで解除");
            var restored=gateway.Restore(name,created.Xml,created.SecurityDescriptor);
            File.WriteAllText(Path.Combine(fixtures,"scheduler-before.xml"),created.Xml);
            File.WriteAllText(Path.Combine(fixtures,"scheduler-restored.xml"),restored.Xml);
            File.WriteAllText(Path.Combine(fixtures,"scheduler-security.txt"),created.SecurityDescriptor+Environment.NewLine+restored.SecurityDescriptor);
            check(restored.Fingerprint==created.Fingerprint,"実予約のXML・権限復元を照合");
        }
        finally
        {
            var remaining=gateway.Read(name);
            if(remaining!=null && (remaining.Fingerprint==created?.Fingerprint || TaskSchedulerGateway.Matches(remaining,plan)))
                gateway.Delete(name,remaining.Fingerprint);
        }
    }
}
