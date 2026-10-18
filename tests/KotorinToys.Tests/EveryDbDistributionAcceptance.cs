using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using KotorinToys;

// 明示引数でのみ実行する。通常のDBでは製品を起動しない。
internal static class EveryDbDistributionAcceptance
{
    static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    static Dictionary<string,string> Hashes(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories).ToDictionary(p=>p,Hash)
        : new();
    static void Require(bool value,string message) { if(!value) throw new IOException(message); Console.WriteLine("PASS: "+message); }
    public static async Task<int> Lifecycle(string root,string oldCatalog,string newCatalog,string output)
    {
        var manager = new ToolManager(root);
        var tool = manager.Load().Single(t=>t.Id=="every-db");
        Require(manager.Installation(tool).State=="未導入","既存の共通版EveryDB登録なし");
        Require(Process.GetProcessesByName("everydb").Length==0,"通常のEveryDBプロセスなし");
        var store = new HostSettingsStore(HostSettingsStore.DefaultDirectory);
        var before = store.Load();
        Require(!store.RemovalPending && store.Journal==null,"未完了の本体操作なし");
        Require(before.StartupModes.GetValueOrDefault(tool.Id,"none")=="none" && !before.OwnedTasks.ContainsKey(tool.Id),"EveryDBの既存予約なし");
        Require(before.HostStartup || before.StartupModes.Values.Contains("via_kotorintoys") || !before.OwnedTasks.ContainsKey("host"),"他製品の共有予約を解除しない条件");
        var settingsJson = JsonSerializer.Serialize(before,ToolCatalog.Json);
        var stateFiles = new[]{"settings.json","settings.json.bak","last-removal-operation.json"}.Select(p=>Path.Combine(store.DirectoryPath,p)).ToArray();
        var shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"EveryDB.lnk");
        var originals=stateFiles.Append(shortcut).ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);
        var vault=Path.Combine(@"C:\AI\env\KotorinToys", "everydb-acceptance-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(vault);
        int fileNumber=0;
        foreach(var bytes in originals.Values) if(bytes!=null) File.WriteAllBytes(Path.Combine(vault,(fileNumber++)+".backup"),bytes);
        var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"com.everydb.app");
        var dataBefore=Hashes(data);
        Directory.CreateDirectory(output);
        var target=Path.Combine(Path.GetFullPath(output),"installed");
        Require(!Directory.Exists(target),"新規の専用導入先");
        var adapter = new InstallerAdapter(manager);
        try
        {
            var installed=await adapter.Install(tool,oldCatalog,target);
            Require(installed.State=="成功" && manager.Installation(tool).Version=="0.1.0","試験用旧共通版0.1.0を正式登録");
            var oldHash=Hash(manager.Installation(tool).Executable!);
            var updated=await adapter.Install(tool,newCatalog,target);
            Require(updated.State=="成功" && manager.Installation(tool).Version=="0.1.1","共通maintenance経由で0.1.1へ更新");
            Require(Hash(manager.Installation(tool).Executable!)!=oldHash,"実EXEの更新を照合");
            var repaired=await adapter.Install(tool,newCatalog,target);
            Require(repaired.State=="成功","同版の再導入を照合");
            var removed=await adapter.Uninstall(tool);
            Require(removed.State=="成功" && manager.Installation(tool).State=="未導入","共通maintenance経由で削除・正式登録の消去を照合");
            var after=Hashes(data);
            Require(dataBefore.Count==after.Count && dataBefore.All(p=>after.GetValueOrDefault(p.Key)==p.Value),"通常EveryDBデータの全ファイルを保持");
            Require(JsonSerializer.Serialize(store.Load(),ToolCatalog.Json)==settingsJson,"本体の設定・他製品予約を保持");
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new { Status="pass",Installed="0.1.0 synthetic Inno fixture",Updated="0.1.1",Repaired=true,Uninstalled=true,PreservedDataFiles=dataBefore.Count,ProductionLaunch=false },ToolCatalog.Json));
            return 0;
        }
        finally
        {
            if(manager.Installation(tool).State=="導入済み")
            {
                var cleanup=await adapter.Uninstall(tool);
                if(cleanup.State!="成功") throw new IOException("試験導入の後片付けを確認できません："+cleanup.Detail);
            }
            using var lease=store.AcquireLease();
            Require(JsonSerializer.Serialize(store.Load(),ToolCatalog.Json)==settingsJson,"外部の設定変更なしを確認して試験前の記録を復元");
            var journal=Path.Combine(store.DirectoryPath,"last-removal-operation.json");
            if(File.Exists(journal))
            {
                using var json=JsonDocument.Parse(File.ReadAllText(journal));
                Require(json.RootElement.GetProperty("targetId").GetString()==tool.Id,"EveryDBの削除記録だけを復元対象にする");
            }
            foreach(var pair in originals)
            {
                if(pair.Value==null) { if(File.Exists(pair.Key)) File.Delete(pair.Key); }
                else File.WriteAllBytes(pair.Key,pair.Value);
            }
            Require(originals.All(p=>p.Value==null?!File.Exists(p.Key):File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"本体の原本・削除記録・既存ショートカットを復元");
        }
    }
    public static async Task<int> Online(string root,string output)
    {
        var tool=ToolCatalog.Load(root).Single(t=>t.Id=="every-db");
        using var client=new PublicReleaseClient();
        var release=await client.Check(tool,null);
        Require(release.Confirmed && release.Package?.Package.Version=="0.1.1" && release.Package.PublishedAt!=null,"EveryDBの公開版・公開日・固定配布先を本体で照合");
        using var downloader=new ReleaseDownloader();
        var catalog=await downloader.Prepare(new(1,[release.Package!]),[tool],output,new Progress<string>(Console.WriteLine));
        await File.WriteAllTextAsync(Path.Combine(output,"receipt.json"),JsonSerializer.Serialize(new{Release=release,Catalog=catalog},ToolCatalog.Json));
        Require(File.Exists(catalog),"公開Innoを匿名取得し容量とSHA-256を照合");
        return 0;
    }
}