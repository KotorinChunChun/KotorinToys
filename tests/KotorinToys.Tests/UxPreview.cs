using KotorinToys;

internal static class UxPreview
{
    // 可視UI確認専用。起動停止・配布実行は架空応答、保存先は版別fixtureだけ。
    public static int Run(string root,string fixtures)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var tools=ToolCatalog.Load(root);var catalog=ReleaseDownloader.Load(root);
        var actual=tools.ToDictionary(t=>t.Id,t=>new InstallationState("未導入",null,null,null));
        actual[tools[0].Id]=new("導入済み","0.0.1",fixtures,Path.Combine(fixtures,"available.exe"),Path.Combine(fixtures,"unins000.exe"));
        actual["smart-paste"]=new("修復が必要","0.1.5",fixtures,Path.Combine(fixtures,"missing.exe"),Repairable:true);
        var states=tools.ToDictionary(t=>t.Id,_=>new ToolState("停止中"));
        Dashboard? panel=null;
        bool Exists(string path)=>path.EndsWith("available.exe") || path.EndsWith("unins000.exe");
        PublicRelease Release(ToolDefinition t)=>t.Id=="shortcut-key-counter"?
            new(null,true,true,"ZIP版の架空情報です。",new("0.1.5",DateTimeOffset.Parse("2026-10-17T00:00:00Z"),"https://github.com/KotorinChunChun/ShortcutKeyCounter/releases",1,new string('a',64))):
            t.Id=="chulip-video"?new(null,false,false,"架空の通信失敗。時間をおいて再確認してください。",Problem:ReleaseProblem.Connection):
            new(catalog.Packages.SingleOrDefault(p=>p.Package.Id==t.Id),true,true,"架空の配布確認です。実setupを実行しません。");
        void ShowInstaller(string? id,string? operation,ReleaseCatalog? releases)
        {
            using var form=new InstallationForm(new(root),id,releases,operation,confirm:_=>false,
                execute:(_,_,_,_,_,_,_)=>throw new InvalidOperationException("fixtureから実配布は実行しません。"),
                detectInstallation:t=>actual[t.Id],executionState:t=>Task.FromResult(states[t.Id]),fileExists:Exists);
            form.ShowDialog(panel);
        }
        panel=new Dashboard(root,detectInstallation:t=>actual[t.Id],executionCheck:(t,_)=>Task.FromResult(states[t.Id]),
            controlExecution:(t,starting)=>{states[t.Id]=new(starting?"実行中":"停止中",Detail:"架空応答です。");return Task.FromResult(states[t.Id]);},
            releaseCheck:(t,_,_)=>Task.FromResult(Release(t)),showInstaller:(id,releases)=>ShowInstaller(id,null,releases),
            showToolInstaller:(id,op,releases)=>ShowInstaller(id,op,releases),fileExists:Exists,
            openRepository:_=>{},showSelfUpdater:()=>{},viewStore:new(Path.Combine(fixtures,"view")));
        using(panel)
        {
            var area=Screen.PrimaryScreen!.WorkingArea;
            panel.StartPosition=FormStartPosition.Manual;panel.Bounds=new(area.Left+area.Width/2,area.Top,area.Width-area.Width/2,area.Height);
            panel.Text=WindowTitle.Create("操作改善の確認（架空データ）");
            Console.WriteLine("架空データのUI確認。設定保存先："+fixtures);
            Application.Run(panel);
        }
        return 0;
    }
}
