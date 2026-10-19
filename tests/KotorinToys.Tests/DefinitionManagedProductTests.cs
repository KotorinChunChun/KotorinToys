using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KotorinToys;

internal static class DefinitionManagedProductTests
{
    static void Pump(Task task)
    {
        var until=DateTime.UtcNow.AddSeconds(15);
        while(!task.IsCompleted && DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(1);}
        if(!task.IsCompleted)throw new TimeoutException("定義追加の画面確認が完了しません。");
        task.GetAwaiter().GetResult();
    }
    public static void Form(string root,string fixtures,Action<bool,string> check)
    {
        var previous=SynchronizationContext.Current;
        try
        {
            var added=ToolCatalog.Load(root)[0] with{Id="defined-ui-tool",Name="DefinedUiTool",AppId="KotorinToys.DefinedUiTool",
                InstalledExecutable="DefinedUiTool.exe",Executable="DefinedUiTool.exe",ToolRoot="tools/DefinedUiTool",Msix=null,
                RepositoryUrl="https://github.com/KotorinChunChun/DefinedUiTool",Version="1.0.0",DistributionStatus="published"};
            var source=new ToolCatalogSource(root,Path.Combine(fixtures,"definition-ui-cache"),_=>Task.FromResult(OnlineCatalogTests.Encode(added)));
            var item=new ReleasePackage(new(added.Id,added.Name,"1.0.0",added.AppId,"DefinedUiTool-1.0.0-setup.exe",new string('a',64),"unsigned-public-build"),1);
            int installs=0;
            using var form=new Dashboard(root,toolManager:new(root,catalogSource:source),
                detectInstallation:_=>new("未導入",null,null,null),executionCheck:(_,_)=>Task.FromResult(new ToolState("停止中")),
                releaseCheck:(_,_,_)=>Task.FromResult(new PublicRelease(item,true,true,"fixture")),
                showToolInstaller:(id,operation,_)=>{check(id==added.Id && operation=="install","新規定義の選択1件を導入画面へ渡す");installs++;},notify:text=>throw new IOException(text));
            form.CreateControl();Pump(form.RefreshAll());
            var grid=(DataGridView)form.Controls.Find("toolTable",true).Single();
            check(grid.Rows.Count==1 && grid.Rows[0].Cells["tool"].Value?.ToString()==added.Name,"新規定義を本体再起動なしで一覧へ反映");
            check(form.Controls.Find("installSelected",true).Single().Enabled && !form.Controls.Find("start",true).Single().Enabled,"本体未収録でも照合済み導入を許可しEXEなしの起動は無効");
            Pump(form.OpenToolInstallation("install"));check(installs==1,"本体内製品表の更新なしで新規アプリの導入操作へ接続");
        }
        finally{SynchronizationContext.SetSynchronizationContext(previous);}
    }
    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(response(request)); }
    static HttpResponseMessage Json(object data)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data,ToolCatalog.Json))};
    public static async Task Run(string root,string fixtures,Action<bool,string> check)
    {
        var added=ToolCatalog.Load(root)[0] with {Id="catalog-only-tool",Name="CatalogOnlyTool",AppId="KotorinToys.CatalogOnlyTool",
            Version="1.0.0",ToolRoot="tools/CatalogOnlyTool",Executable="release/CatalogOnlyTool.exe",InstalledExecutable="CatalogOnlyTool.exe",
            RepositoryUrl="https://github.com/KotorinChunChun/CatalogOnlyToolRepo",DistributionStatus="published",Msix=null};
        var directory=Path.Combine(fixtures,"definition-managed");Directory.CreateDirectory(directory);
        byte[] config=OnlineCatalogTests.Encode(added);
        var source=new ToolCatalogSource(root,Path.Combine(directory,"cache"),_=>Task.FromResult(config));
        var refreshed=await source.Refresh();var manager=new ToolManager(root,catalogSource:source);
        check(refreshed.Accepted && manager.Load().Single().Id==added.Id,"定義の更新だけで本体未収録の候補を追加");
        check(ProductIdentity.FromId(added.Id)==null && ProductIdentity.FromTool(added)?.Repository=="CatalogOnlyToolRepo","配布先を定義から取得しグローバルな固定表を書き換えない");
        byte[] bytes=Encoding.UTF8.GetBytes("fixture-not-an-executable");
        string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var package=new LocalPackage(added.Id,added.Name,"1.0.1",added.AppId,"CatalogOnlyTool-1.0.1-setup.exe",hash,"unsigned-public-build");
        string Url(string file)=>added.RepositoryUrl+"/releases/download/v1.0.1/"+file;
        object Asset(string file,long length)=>new{name=file,size=length,state="uploaded",browser_download_url=Url(file),digest="sha256:"+hash};
        int requests=0;
        using var client=new PublicReleaseClient(new Handler(request=>
        {
            requests++;
            if(request.RequestUri!.Host=="api.github.com")
            {
                check(request.RequestUri.AbsolutePath=="/repos/KotorinChunChun/CatalogOnlyToolRepo/releases","新規製品も定義の固定所有者・リポジトリだけを照会");
                return Json(new[]{new{tag_name="v1.0.1",draft=false,prerelease=true,published_at="2026-10-19T00:00:00Z",assets=new[]{Asset(added.Name+"-packages.json",512),Asset(package.File,bytes.Length)}}});
            }
            check(request.RequestUri.AbsoluteUri==Url(added.Name+"-packages.json"),"公開台帳は同じ固定リリースからだけ取得");
            return Json(new PackageCatalog(1,"published",[package]));
        }));
        var release=await client.Check(added,null);
        check(requests==2 && release.Confirmed && release.Package?.Package==package,"定義を変えなくても新しい公開版と台帳を照合");
        var catalog=new ReleaseCatalog(1,[release.Package!]);
        check(ToolInstallationRunner.PublicReason(added,catalog)==null,"標準Inno製品は本体の固定製品表へのコード追加なしで導入候補になる");
        using var downloader=new ReleaseDownloader(new Handler(request=>
        {
            check(request.RequestUri!.AbsoluteUri==Url(package.File),"新規製品の取得先も同じリポジトリ・tag・ファイル名");
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        }));
        var path=await downloader.Prepare(catalog,[added with{Version="1.0.1"}],Path.Combine(directory,"downloads"));
        check(PackageCatalogReader.Load(path,manager.KnownTools()).Packages.Single()==package,"新規製品の容量・SHA256照合済み配布物をローカル導入台帳へ渡せる");
        bool rejected=false;try{PackageCatalogReader.Load(path);}catch(IOException){rejected=true;}
        check(rejected && ProductIdentity.FromId(added.Id)==null,"検証済み定義を渡さない別の処理へ許可を漏らさない");
        int executions=0;
        var adapter=new InstallerAdapter(manager,(file,args)=>{executions++;check(file==Path.Combine(Path.GetDirectoryName(path)!,package.File) && args.Contains("/NOFORCECLOSEAPPLICATIONS"),"新規製品も照合済みファイルと正常終了条件だけを実行部へ渡す");return Task.FromResult(0);});
        var result=await adapter.Install(added,path);
        check(executions==1 && result.State=="要確認","新規製品も実行後の正式登録を省略せず未登録は要確認");
        var installer=Path.Combine(Path.GetDirectoryName(path)!,package.File);File.WriteAllBytes(installer,[0]);
        result=await adapter.Install(added,path);
        check(executions==1 && result.State=="失敗","新規製品でも改変された配布物を実行しない");File.WriteAllBytes(installer,bytes);
        int stops=0,restarts=0,updates=0;
        var update=new ToolUpdateCoordinator(_=>new("導入済み","1.0.0",directory,Path.Combine(directory,added.InstalledExecutable)),
            _=>Task.FromResult(new ToolState("実行中",42,LaunchOrigin:"host_manual")),_=>{stops++;return Task.FromResult(new ToolState("停止中"));},
            (_,origin)=>{restarts++;check(origin=="host_manual","新規製品更新も元の起動経路を保持");return Task.FromResult(new ToolState("実行中",43));},
            (_,_,target)=>{updates++;check(target==directory,"新規製品更新も導入先を保持");return Task.FromResult(new InstallResult(added.Id,"install","成功","fixture"));},definitions:manager.KnownTools);
        result=await update.Run(added,path);
        check(result.State=="成功" && stops==1 && restarts==1 && updates==1,"本体未収録の製品も正常停止・更新・元の起動状態復帰に対応");
        var declaration=JsonSerializer.SerializeToNode(added,ToolCatalog.Json)!;declaration["schemaVersion"]=1;
        File.WriteAllText(Path.Combine(directory,"tool.json"),declaration.ToJsonString());
        var managed=ManagedProductDeclaration.Read(added.Name,directory);
        check(managed.Id==added.Id && managed.Executable==added.InstalledExecutable && managed.ToolRoot=="","単体更新削除補助も標準の同梱定義から製品を照合");
        var store=new HostSettingsStore(Path.Combine(directory,"settings"));store.Save(store.Load() with{StartupModes=new(){[added.Id]="via_kotorintoys"}});
        var removal=new RemovalReservations(store,new StartupTests.FakeGateway(),definitions:manager.KnownTools());
        check(removal.Remove(added.Id).Success && store.Load().StartupModes[added.Id]=="none","新規製品の削除時もその予約だけを整理できる");
        foreach(var invalid in new[]{added with{RepositoryUrl="https://github.com/AnotherOwner/CatalogOnlyToolRepo"},added with{AppId="KotorinToys.SmartPaste"},added with{Name="SmartPaste",AppId="KotorinToys.SmartPaste"},added with{RepositoryUrl=null},added with{Capabilities=[]},added with{Executable="../outside.exe"}})
            check(ProductIdentity.FromTool(invalid)==null,"所有者・製品識別・管理能力・パス不正の新規定義を拒否");
        config=OnlineCatalogTests.Encode(added with{RepositoryUrl="https://github.com/KotorinChunChun/DifferentRepo"});
        check(!(await source.Refresh()).Accepted && source.Load().Single().RepositoryUrl==added.RepositoryUrl,"追加後に同じIDの公開先を別製品へ差し替えない");
        config=OnlineCatalogTests.Encode(added with{RepositoryUrl=""});
        check(!(await source.Refresh()).Accepted,"新規管理製品の公開先を一旦空にして固定先照合を迂回しない");
        config=OnlineCatalogTests.Encode(added with{Name="DifferentName"});
        check(!(await source.Refresh()).Accepted,"同じIDの製品名を差し替えない");
        foreach(var invalid in new[]{package with{Kind="msi"},package with{File="../escape.exe"},package with{Sha256=new string('z',64)}})
        {
            rejected=false;try{PackageCatalogReader.Validate(new(1,"published",[invalid]),[added]);}catch(IOException){rejected=true;}
            check(rejected,"新規製品にも配布方式・ファイル名・SHA256の拒否条件を適用");
        }
    }
}
