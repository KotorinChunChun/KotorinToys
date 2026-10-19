using System.Text.Json;
using KotorinToys;

internal static class RepairTests
{
    public static async Task Run(string root, string fixtures, Action<bool,string> check)
    {
        var tool = ToolCatalog.Load(root).Single(t=>t.Id=="smart-paste");
        var directory = Path.Combine(fixtures,"repair"); Directory.CreateDirectory(directory);
        var definition = Path.Combine(directory,"tool.json");
        var detector = new InstallationDetector(_ => new(directory));
        check(!detector.Detect(tool).Repairable, "製品定義なしでは登録だけで自動修復を許可しない");
        void Save(string name, string version = "0.1.5") => File.WriteAllText(definition, JsonSerializer.Serialize(new
        { id=tool.Id, name, appId=tool.AppId, version, installedExecutable=tool.InstalledExecutable }));
        Save("Other"); check(!detector.Detect(tool).Repairable, "別製品の定義では修復を許可しない");
        Save(tool.Name);
        var actual = detector.Detect(tool);
        check(actual is { State:"修復が必要", Repairable:true, Version:"0.1.5" } && actual.Directory == directory,
            "正式登録先と同梱定義を照合してEXE欠落の修復候補を作る");
        var item = ReleaseDownloader.Load(root).Packages.Single(p=>p.Package.Id==tool.Id);
        var release = new PublicRelease(item,true,true,"fixture");
        ActionAvailability Rule(InstallationState installed, ToolState state, PublicRelease value) =>
            ToolActionPolicy.Evaluate("repair",tool,installed,state,value);
        check(Rule(actual,new("停止中"),release).Enabled, "同じ公開版の停止中の修復を許可");
        check(DashboardText.Detail(tool,actual,new("停止中"),release,new("なし",""),null,null,null)=="修復できます" &&
            DashboardText.Advice(tool,actual,new("停止中"),release).Contains("「修復」を押して"),"修復可能な場合は一覧と詳細から本体の修復操作を案内する");
        check(!Rule(actual,new("実行中"),release).Enabled && !Rule(actual,new("確認できない"),release).Enabled, "実行中・状態不明で修復しない");
        check(!Rule(actual with {Repairable=false},new("停止中"),release).Enabled && !Rule(actual,new("停止中"),release with{Confirmed=false}).Enabled,
            "未照合の導入先・公開版では修復しない");
        var old = release with { Package=item with { Package=item.Package with{Version="0.1.4"} } };
        check(!Rule(actual,new("停止中"),old).Enabled, "古い公開版への修復はUIでも拒否");
        var catalogPath = Path.Combine(directory,"packages.json");
        void Catalog(string version) => File.WriteAllText(catalogPath,JsonSerializer.Serialize(new PackageCatalog(1,"published",[item.Package with{Version=version,File=$"SmartPaste-{version}-setup.exe"}]),ToolCatalog.Json));
        Catalog("0.1.5");
        var settings = Path.Combine(directory,"settings.json"); File.WriteAllText(settings,"保持する架空設定");
        int executions = 0, stops = 0;
        ToolState state = new("停止中");
        var coordinator = new ToolUpdateCoordinator(_=>actual, _=>Task.FromResult(state), _=>{stops++;return Task.FromResult(new ToolState("停止中"));},
            (_,_)=>throw new Exception("停止中の修復で起動しません。"), (_,_,path)=>
            { executions++;check(path==directory,"修復は登録済みカスタム導入先へ固定");return Task.FromResult(new InstallResult(tool.Id,"install","成功","fixture")); });
        var results = await new InstallationQueue(new InstallerAdapter(new(root)),coordinator).Run([tool],"repair",catalogPath);
        check(results.Single() is { State:"成功",Operation:"repair" } && executions==1 && stops==0 && File.ReadAllText(settings)=="保持する架空設定",
            "修復のキューは停止状態と設定を保ち結果を修復として集計");
        state = new("確認できない"); await coordinator.Run(tool,catalogPath,true);
        state = new("実行中"); await coordinator.Run(tool,catalogPath,true);
        check(executions==1 && stops==0,"直接実行でも状態不明・実行中の修復を停止前に拒否");
        state = new("停止中"); Catalog("0.1.4");
        check((await coordinator.Run(tool,catalogPath,true)).State=="失敗" && executions==1,"直接実行でもダウングレード修復を拒否");
        check(InstallationSummary.Create([tool],"repair",results).Title=="すべての修復が完了しました。", "修復の結果をインストールや未実行と混同しない");
        Save(tool.Name,"not-version"); check(!detector.Detect(tool).Repairable,"欠落EXEの版が不明なら修復不可");
    }
    public static void Form(string root,Action<bool,string> check)
    {
        var previous=SynchronizationContext.Current;
        try
        {
            var tools=ToolCatalog.Load(root);var catalog=ReleaseDownloader.Load(root);var tool=tools.Single(t=>t.Id=="smart-paste");
            var item=catalog.Packages.Single(p=>p.Package.Id==tool.Id);int opened=0;
            var actual=new InstallationState("修復が必要",item.Package.Version,root,Path.Combine(root,tool.InstalledExecutable),Repairable:true);
            using var form=new Dashboard(root,detectInstallation:t=>t.Id==tool.Id?actual:new("未導入",null,null,null),fileExists:_=>false,
                executionCheck:(_,_)=>Task.FromResult(new ToolState("停止中")),releaseCheck:(t,_,_)=>Task.FromResult(new PublicRelease(catalog.Packages.SingleOrDefault(p=>p.Package.Id==t.Id),true,true,"fixture")),
                showToolInstaller:(id,operation,_)=>{check(id==tool.Id && operation=="repair","修復ボタンは対象1件の修復を渡す");opened++;});
            form.ApplySnapshot(tools.Select(t=>t.Id==tool.Id?actual:new InstallationState("未導入",null,null,null)).ToArray(),tools.Select(_=>new ToolState("停止中")).ToArray(),
                tools.Select(t=>new PublicRelease(catalog.Packages.SingleOrDefault(p=>p.Package.Id==t.Id),true,true,"fixture")).ToArray());
            var grid=(DataGridView)form.Controls.Find("toolTable",true).Single();grid.CurrentCell=grid.Rows[Array.FindIndex(tools,t=>t.Id==tool.Id)].Cells["tool"];
            check(form.Controls.Find("installSelected",true).Single() is Button {Text:"修復",Enabled:true},"既存導入ボタンを修復へ切り替えボタン数を増やさない");
            var pending=form.OpenToolInstallation("repair");while(!pending.IsCompleted){Application.DoEvents();Thread.Sleep(1);}pending.GetAwaiter().GetResult();
            check(opened==1,"メインから修復の単体操作へ接続");
        }
        finally {SynchronizationContext.SetSynchronizationContext(previous);}
    }
}
