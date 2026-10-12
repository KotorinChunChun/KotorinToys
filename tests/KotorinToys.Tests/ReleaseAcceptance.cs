using System.Security.Cryptography;
using System.Text.Json;
using KotorinToys;

internal static class ReleaseAcceptance
{
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public static async Task<int> Host(string catalog)
    {
        var root=KotorinToys.Program.FindRoot();var manager=new ToolManager(root);var tools=manager.Load();
        var host=new ToolDefinition("host","KotorinToys","KotorinToys.exe","README.md",AppId:"KotorinToys",InstalledExecutable:"KotorinToys.exe");
        var installation=manager.Installation(host);if(installation.State!="導入済み")throw new IOException("正式本体が必要です。");
        var dashboard=host with{Id="dashboard",AppId=""};var hostManager=new ToolManager(installation.Directory!);
        var before=await hostManager.Status(dashboard);var children=await Task.WhenAll(tools.Select(t=>manager.Status(t)));
        var directory=Path.Combine(root,"dev","history","v0.1.0","logs","release-host-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            if(before.State is not ("実行中" or "停止中"))throw new IOException("本体の安全な状態を確認できません。");
            var code=await InstalledStartupTests.NativeInstall(catalog);if(code!=0)throw new IOException("正式本体更新の終了コード："+code);
            var store=new HostSettingsStore(HostSettingsStore.DefaultDirectory);var own=store.Load().OwnedTasks["host"];
            if(Hash(Path.Combine(installation.Directory!,"catalog","releases.json"))!=Hash(Path.Combine(root,"catalog","releases.json")))throw new IOException("正式本体の固定公開台帳が不一致です。");
            new TaskSchedulerGateway().RunOwned(own.Name,own.Fingerprint);
            var deadline=DateTime.UtcNow.AddSeconds(45);ToolState state=new("停止中");
            while(DateTime.UtcNow<deadline){state=await hostManager.Status(dashboard);if(state.State=="実行中")break;await Task.Delay(500);}
            if(state.State!="実行中")throw new IOException("更新した正式本体の準備完了を確認できません。");
            for(int i=0;i<tools.Length;i++)if((await manager.Status(tools[i])).Pid!=children[i].Pid)throw new IOException("本体更新が子PIDを変更しました。");
            if(before.State=="停止中" && (await hostManager.Stop(dashboard)).State!="停止中")throw new IOException("元の本体停止状態へ戻せません。");
            File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{Outcome="pass",Host=state,ChildPids=children.Select(s=>s.Pid),PinnedCatalog="同梱hash一致",Launch="所有照合付き実タスク手動開始。サインイン試験ではない。"},ToolCatalog.Json));return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(directory,"failure.txt"),ex.ToString());return 1;}
    }
    public static async Task<int> Run()
    {
        var root=KotorinToys.Program.FindRoot();var manager=new ToolManager(root);var tools=manager.Load();
        var directory=Path.Combine(root,"dev","history","v0.1.0","logs","release-acceptance-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var states=await Task.WhenAll(tools.Select(t=>manager.Status(t)));var changed=new List<int>();
        var installed=tools.Select(manager.Installation).ToArray();
        var receipts=new List<object>();var outcome=0;
        try
        {
            if(states.Any(s=>s.State is not ("実行中" or "停止中")) || installed.Any(i=>i.State!="導入済み"))throw new IOException("正式導入と安全な現在状態が必要です。");
            using var downloader=new ReleaseDownloader();
            var catalog=await downloader.Prepare(ReleaseDownloader.Load(root),tools,Path.Combine(directory,"downloads"));
            for(int i=0;i<tools.Length;i++)
            {
                if((await manager.Stop(tools[i])).State!="停止中")throw new IOException("正常終了を拒否しました。導入しません："+tools[i].Name);
                changed.Add(i);
            }
            var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var known=new List<string>{Path.Combine(local,"KotorinToys","settings.json")};
            for(int i=0;i<tools.Length;i++)known.AddRange(new[]{Path.Combine(local,tools[i].Name,"settings.json"),Path.Combine(installed[i].Directory!,"data","settings.json"),Path.Combine(installed[i].Directory!,"data","last-region.json")});
            known.Add(Path.Combine(root,"tools","SSCapture","src","SSCapture","bin","Release","net10.0-windows","data","last-region.json"));
            var preserved=known.Distinct().Where(File.Exists).ToDictionary(p=>p,Hash);
            var adapter=new InstallerAdapter(manager);
            for(int i=0;i<tools.Length;i++)
            {
                var result=await adapter.Install(tools[i],catalog,installed[i].Directory);
                if(result.State!="成功")throw new IOException(tools[i].Name+"の公開取得後導入："+result.State+" / "+result.Detail);
                foreach(var file in preserved)if(!File.Exists(file.Key) || Hash(file.Key)!=file.Value)throw new IOException("既知設定・履歴の保持ハッシュが不一致です。");
                if((await manager.Start(tools[i],false,"standalone")).State!="実行中" || (await manager.Stop(tools[i])).State!="停止中")throw new IOException("公開版の単独準備完了・正常終了が不一致です。");
                receipts.Add(new {ToolId=tools[i].Id,Installation=result,Verification="公開HTTP取得・固定ハッシュ・正式導入・単独準備完了・正常終了"});
            }
            File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{Outcome="pass",Results=receipts,PreservedHashes=preserved},ToolCatalog.Json));
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(directory,"failure.txt"),ex.ToString());outcome=1;}
        finally
        {
            var restoration=new List<object>();
            foreach(var i in changed)
            {
                try
                {
                    var result=states[i].State=="実行中"?await manager.Start(tools[i],false,states[i].LaunchOrigin):await manager.Stop(tools[i]);
                    restoration.Add(new {ToolId=tools[i].Id,Expected=states[i].State,Actual=result});
                    if(result.State!=states[i].State)outcome=1;
                }
                catch(Exception ex){restoration.Add(new {ToolId=tools[i].Id,Error=ex.Message});outcome=1;}
            }
            File.WriteAllText(Path.Combine(directory,"restoration.json"),JsonSerializer.Serialize(restoration,ToolCatalog.Json));
            if(outcome!=0 && File.Exists(Path.Combine(directory,"result.json")))File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{Outcome="fail",Detail="導入または元の起動状態復帰が未完了。failure.txt／restoration.json参照。"},ToolCatalog.Json));
        }
        return outcome;
    }
}
