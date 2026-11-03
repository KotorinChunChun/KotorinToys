using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KotorinToys;

internal static class Program
{
    static void Require(bool value,string reason){if(!value)throw new IOException(reason);Console.WriteLine("PASS: "+reason);}
    static string? Hash(string file)=>File.Exists(file)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))):null;
    static string UserDataHash(string folder)
    {
        if(!Directory.Exists(folder))return "missing";
        var values=Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(file=>Path.GetRelativePath(folder,file)+":"+Hash(file));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",values))));
    }
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding=Encoding.UTF8;
        if(args.Length!=3 || args[0] is not ("validate" or "online" or "install")){Console.Error.WriteLine("validate|online|install <root> <版別logsの新規出力>");return 2;}
        var root=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);
        Require(output.Contains(Path.DirectorySeparatorChar+"logs"+Path.DirectorySeparatorChar),"出力は版別logs内");
        Directory.CreateDirectory(output);
        try
        {
            var definitions=OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root,"config/tools.json")),root);
            var tool=definitions.Single(t=>t.Id=="reading-repair");
            Require(tool is {Name:"ReadingRepair",Version:"0.0.1",AppId:"KotorinToys.ReadingRepair",DistributionStatus:"published"},"ReadingRepairの公開定義と固定製品識別");
            Require(ProductIdentity.FromTool(tool)?.Repository=="ReadingRepair","標準の定義ベース管理で配布先を照合");
            if(args[0]=="validate")return 0;
            var source=new ToolCatalogSource(root,Path.Combine(output,"catalog-cache"));
            var refreshed=await source.Refresh();
            Require(refreshed.Accepted,"公開mainの一覧を本体の取得・検証経路で採用");
            tool=refreshed.Tools.Single(t=>t.Id=="reading-repair");
            using var client=new PublicReleaseClient(cache:new PublicReleaseCache());
            var release=await client.Check(tool,null);
            Require(release.Confirmed && release.Package?.Package.Version=="0.0.1","公開情報一覧・製品別台帳・GitHub digest照合: "+release.Detail);
            var catalog=new ReleaseCatalog(1,[release.Package!]);
            Require(ToolInstallationRunner.PublicReason(tool,catalog,release)==null,"KotorinToysのインストール操作が許可される");
            await File.WriteAllTextAsync(Path.Combine(output,"publication.json"),JsonSerializer.Serialize(release,ToolCatalog.Json));
            if(args[0]=="online")return 0;
            var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userFolder=Path.Combine(local,"ReadingRepair");var before=UserDataHash(userFolder);
            var hostSettings=Path.Combine(HostSettingsStore.DefaultDirectory,"settings.json");var hostBefore=Hash(hostSettings);
            var manager=new ToolManager(root,(_,start)=>{start.ArgumentList.Add("--settings");start.ArgumentList.Add(Path.Combine(output,"app-settings.json"));},source);
            Require((await manager.Status(tool)).State=="停止中","導入前に通常インスタンスが正常停止済み");
            var results=await new ToolInstallationRunner(manager).Run([tool],"install",catalog,null);
            Require(results.Single().State=="成功","本体と同じ匿名取得・容量/SHA検査・setup実行・正式登録: "+results.Single().Detail);
            var installed=manager.Installation(tool);
            Require(installed.State=="導入済み" && installed.Version=="0.0.1","HKCU登録・tool.json・EXE製品名と版を検出");
            var state=await manager.Start(tool,false,"host_manual");
            Require(state.State=="実行中" && state.Pid.HasValue,"正式導入先から無表示起動・ready・PID/実行パス照合");
            using var process=Process.GetProcessById(state.Pid!.Value);process.Refresh();
            Require(process.MainWindowHandle==0,"管理起動で主画面が表示されない");
            var shown=await manager.Start(tool,true,"host_manual");
            Require(shown.State=="実行中","本体のshow経路で画面表示");
            var duplicate=await manager.Start(tool,false,"host_manual");
            Require(duplicate.Pid==state.Pid,"再起動操作は既存PIDを保持");
            bool refused=false;try{await manager.Send(tool,"stop",0);}catch(IOException){refused=true;}
            Require(refused,"誤ったexpectedPidの停止を送信前に拒否");
            Require((await manager.Stop(tool)).State=="停止中","本体の正常停止・プロセス終了検出");
            Require(UserDataHash(userFolder)==before,"既存のReadingRepair設定・履歴の全件ハッシュを保持");
            Require(Hash(hostSettings)==hostBefore,"本体の既存設定・自動起動希望を保持");
            await File.WriteAllTextAsync(Path.Combine(output,"installation.json"),JsonSerializer.Serialize(new{installed,downloadAndInstall=results,managedPid=state.Pid,userDataPreserved=true,hostSettingsPreserved=true},ToolCatalog.Json));
            return 0;
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(output,"failure.txt"),error.ToString());Console.Error.WriteLine(error.Message);return 1;}
    }
}
