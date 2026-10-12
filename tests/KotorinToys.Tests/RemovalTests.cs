using System.Security.Principal;
using KotorinToys;

internal static class RemovalTests
{
    sealed class Gateway:IStartupGateway
    {
        public Dictionary<string,ScheduledTaskState> Tasks=new();
        public int FailDeleteAt;
        int deletes;
        public ScheduledTaskState? Read(string name)=>Tasks.GetValueOrDefault(name);
        public ScheduledTaskState Create(TaskPlan plan)=>Tasks[plan.Name]=new(plan.Name,StartupTests.Xml(plan));
        public ScheduledTaskState Restore(string name,string xml,string securityDescriptor="")=>Tasks[name]=new(name,xml,securityDescriptor);
        public void Delete(string name,string fingerprint)
        {
            if(++deletes==FailDeleteAt)throw new IOException("削除失敗注入");
            if(Read(name)?.Fingerprint!=fingerprint)throw new IOException("指紋不一致");
            Tasks.Remove(name);
        }
    }
    public static void Run(string fixtures,Action<bool,string> check)
    {
        var sid=WindowsIdentity.GetCurrent().User!.Value;
        var prefix="KotorinToys.Test."+Guid.NewGuid().ToString("N");
        var gateway=new Gateway();var store=new HostSettingsStore(Path.Combine(fixtures,"removal"));
        var settings=new HostSettings{StartupModes=new(){["smart-paste"]="direct_task",["ss-capture"]="direct_task"}};
        TaskOwnership Own(string id)
        {
            var plan=new TaskPlan(prefix+"."+sid+"."+id,"C:\\fixture\\"+id+".exe","--startup","C:\\fixture",sid,"KotorinToys:"+settings.OwnerToken+":"+id);
            var actual=gateway.Create(plan);return new(actual.Name,actual.Fingerprint,actual.Xml);
        }
        settings.OwnedTasks["smart-paste"]=Own("smart-paste");settings.OwnedTasks["ss-capture"]=Own("ss-capture");store.Save(settings);
        var removal=new RemovalReservations(store,gateway,prefix);
        check(removal.Remove("smart-paste").Success && gateway.Tasks.Count==1 && store.Load().StartupModes["ss-capture"]=="direct_task","削除対象の直接予約だけ整理し他ツールは保持");
        check(removal.Recover(true).Success && gateway.Tasks.Count==2 && store.Load().StartupModes["smart-paste"]=="direct_task","削除失敗時の完了記録から旧管理予約を復元");
        var owned=settings.OwnedTasks["smart-paste"];
        gateway.Tasks[owned.Name]=new(owned.Name,owned.Xml.Replace("--startup","--external"));
        check(!removal.Remove("smart-paste").Success && gateway.Tasks.Count==2 && store.Load().StartupModes["smart-paste"]=="direct_task","外部変更された対象予約を削除しない");
        gateway.Tasks[owned.Name]=new(owned.Name,owned.Xml);
        settings=settings with{StartupModes=new(){["smart-paste"]="via_kotorintoys",["ss-capture"]="direct_task"},OwnedTasks=new(){["host"]=Own("host"),["ss-capture"]=settings.OwnedTasks["ss-capture"]}};
        gateway.Tasks.Remove(owned.Name);store.Save(settings);
        check(removal.Remove("host").Success && gateway.Tasks.Count==1 && store.Load().StartupModes["smart-paste"]=="none" && store.Load().StartupModes["ss-capture"]=="direct_task","本体のみ削除で経由を解除し子と直接予約を保持");
        check(removal.Recover(true).Success && gateway.Tasks.Count==2,"共有本体予約を失敗復元できる");
        settings=settings with{StartupModes=new(){["smart-paste"]="via_kotorintoys",["live-text-marker"]="via_kotorintoys",["ss-capture"]="direct_task"}};store.Save(settings);
        check(removal.Remove("smart-paste").Success && gateway.Tasks.Count==2 && store.Load().OwnedTasks.ContainsKey("host"),"他の経由対象があれば共有本体予約を保持");
        check(removal.Remove("live-text-marker").Success && gateway.Tasks.Count==1 && !store.Load().OwnedTasks.ContainsKey("host"),"最後の経由削除で不要な共有予約を整理");
        check(!removal.Remove("unknown").Success,"未登録製品の予約整理を拒否");
        gateway=new Gateway{FailDeleteAt=2};store=new HostSettingsStore(Path.Combine(fixtures,"removal-failure"));
        settings=new HostSettings{StartupModes=new(){["smart-paste"]="direct_task"},HostStartup=false};
        settings.OwnedTasks["smart-paste"]=Own("smart-paste");settings.OwnedTasks["host"]=Own("host");store.Save(settings);
        removal=new RemovalReservations(store,gateway,prefix);
        check(!removal.Remove("smart-paste").Success && gateway.Tasks.Count==2 && store.Load().StartupModes["smart-paste"]=="direct_task" && !store.RemovalPending,"予約解除の部分失敗を復元して要対応ジャーナルを残さない");
        check(removal.Remove("smart-paste").Success,"失敗復元後に再度予約整理できる");
        store.Save(store.Load() with{ShowDashboardAtLogon=true});
        check(!removal.Recover(true).Success && gateway.Tasks.Count==0,"操作後に変更された設定へ古い削除復元を適用しない");
        check(removal.Remove("smart-paste").Success && removal.Recover(true,"smart-paste").Success && gateway.Tasks.Count==0 && store.Load().ShowDashboardAtLogon,
            "整理不要の再削除を取り消しても古い予約や設定を復元しない");
    }
}
