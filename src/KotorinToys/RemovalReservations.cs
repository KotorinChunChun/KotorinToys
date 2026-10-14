using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;

namespace KotorinToys;

internal sealed record RemovalJournal(string TargetId, HostSettings Before, HostSettings After, Dictionary<string,TaskOwnership> Removed, string Phase);
internal sealed class RemovalReservations(HostSettingsStore store, IStartupGateway gateway, string prefix="KotorinToys")
{
    readonly string sid=WindowsIdentity.GetCurrent().User!.Value;
    string JournalFile=>Path.Combine(store.DirectoryPath,"removal-operation.json");
    string CompletedFile=>Path.Combine(store.DirectoryPath,"last-removal-operation.json");
    void Write(RemovalJournal value)
    {
        var temporary=JournalFile+".tmp";
        using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None))
        {JsonSerializer.Serialize(stream,value,ToolCatalog.Json);stream.Flush(true);}
        File.Move(temporary,JournalFile,true);
    }
    public StartupResult Remove(string id)
    {
        try
        {
            if(ProductIdentity.FromId(id) is null)throw new IOException("未登録の削除対象です。");
            using var lease=store.AcquireLease();
            if(store.Journal!=null || File.Exists(JournalFile))throw new IOException("未完了の操作があります。削除せず復旧してください。");
            var before=store.Load();var modes=new Dictionary<string,string>(before.StartupModes);
            if(id=="host") {foreach(var key in modes.Keys.ToArray())if(modes[key]=="via_kotorintoys")modes[key]="none";}
            else if(modes.ContainsKey(id))modes[id]="none";
            var after=before with{StartupModes=modes,HostStartup=id=="host"?false:before.HostStartup};
            var ids=new HashSet<string>{id};
            if(!after.HostStartup && !modes.Values.Contains("via_kotorintoys"))ids.Add("host");
            var removed=new Dictionary<string,TaskOwnership>();
            var remaining=new Dictionary<string,TaskOwnership>(before.OwnedTasks);
            foreach(var key in ids)
            {
                if(!before.OwnedTasks.TryGetValue(key,out var own))continue;
                Validate(before,key,own);
                var actual=gateway.Read(own.Name);
                if(actual!=null && actual.Fingerprint!=own.Fingerprint)throw new IOException("削除対象の予約が外部変更されています。");
                if(actual!=null)removed[key]=own;
                remaining.Remove(key);
            }
            after=after with{OwnedTasks=remaining};
            var journal=new RemovalJournal(id,before,after,removed,"解除開始");Write(journal);
            try
            {
                foreach(var own in removed.Values)gateway.Delete(own.Name,own.Fingerprint);
                store.Save(after);File.Move(JournalFile,CompletedFile,true);
                return new(true,"対象の管理予約を整理しました。設定・履歴・外部予約は削除しません。");
            }
            catch(Exception ex){var result=RestoreCore(journal);return new(false,ex.Message+" / "+result.Detail);}
        }
        catch(Exception ex){return new(false,ex.Message);}
    }
    void Validate(HostSettings settings,string id,TaskOwnership own)
    {
        if(own.Name!=prefix+"."+sid+"."+id || new ScheduledTaskState(own.Name,own.Xml,own.SecurityDescriptor).Fingerprint!=own.Fingerprint)
            throw new IOException("予約の所有記録が一致しません。");
        XNamespace ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
        if(XDocument.Parse(own.Xml).Root?.Element(ns+"RegistrationInfo")?.Element(ns+"Description")?.Value!="KotorinToys:"+settings.OwnerToken+":"+id)
            throw new IOException("予約の所有マーカーが一致しません。");
    }
    public StartupResult Recover(bool completed=false,string? expectedId=null)
    {
        try
        {
            using var lease=store.AcquireLease();
            var path=completed?CompletedFile:JournalFile;
            if(!File.Exists(path))return new(true,"削除予約の復旧対象はありません。");
            var journal=JsonSerializer.Deserialize<RemovalJournal>(File.ReadAllText(path),ToolCatalog.Json)??throw new IOException("削除操作記録が空です。");
            if(expectedId!=null && journal.TargetId!=expectedId)throw new IOException("別製品の削除記録です。復元しません。");
            var current=JsonSerializer.Serialize(store.Load(),ToolCatalog.Json);
            if(current!=JsonSerializer.Serialize(journal.Before,ToolCatalog.Json) && current!=JsonSerializer.Serialize(journal.After,ToolCatalog.Json))
                throw new IOException("削除操作後に設定が変更されています。自動復元しません。");
            return RestoreCore(journal);
        }
        catch(Exception ex){return new(false,ex.Message);}
    }
    StartupResult RestoreCore(RemovalJournal journal)
    {
        try
        {
            foreach(var pair in journal.Removed)
            {
                Validate(journal.Before,pair.Key,pair.Value);
                var actual=gateway.Read(pair.Value.Name);
                if(actual!=null && actual.Fingerprint!=pair.Value.Fingerprint)throw new IOException("復元先に外部変更があります。");
            }
            foreach(var own in journal.Removed.Values)
            {
                var actual=gateway.Read(own.Name)??gateway.Restore(own.Name,own.Xml,own.SecurityDescriptor);
                if(actual.Fingerprint!=own.Fingerprint)throw new IOException("復元後の照合に失敗しました。");
            }
            store.Save(journal.Before);
            Write(journal with{Phase="復元済み"});File.Move(JournalFile,CompletedFile,true);
            return new(true,"削除前の管理設定・予約を復元しました。");
        }
        catch(Exception ex){Write(journal with{Phase="要対応"});return new(false,"復元未確認："+ex.Message);}
    }
}
