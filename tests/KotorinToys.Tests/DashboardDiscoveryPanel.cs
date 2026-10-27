using KotorinToys;

// 専用fixtureの実画面確認。実製品・OS予約・通信・インストーラーは操作しない。
internal static class DashboardDiscoveryPanel
{
    public static int Run(string root,string output)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var actual=Environment.ProcessPath!;
        var installed=Path.Combine(output,"正式導入","KotorinToys.Tests.exe");Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.Copy(actual,installed,false);
        var mismatch=new ToolState("確認できない",Environment.ProcessId,"別の配置の検証です。実製品の状態ではありません。",RunningExecutable:actual,ExpectedExecutable:installed);
        ToolState State(ToolDefinition tool)=>tool.Id=="ss-capture"?mismatch:new("停止中");
        void Notify(string message)=>MessageBox.Show(message,WindowTitle.Create("検証用"));
        using var form=new Dashboard(root,viewStore:new(Path.Combine(output,"view")),
            detectInstallation:t=>t.Id is "ss-capture" or "live-text-marker" or "smart-paste"?new("導入済み",t.Version,Path.GetDirectoryName(installed),installed):new("未導入",null,null,null),
            executionCheck:(t,_)=>Task.FromResult(State(t)),controlExecution:(t,_)=>Task.FromResult(State(t)),
            releaseCheck:(_,fallback,_)=>Task.FromResult(new PublicRelease(fallback,fallback!=null,true,"専用fixture・通信なし")),
            showInstaller:(_,_)=>Notify("検証では導入しません。"),showToolInstaller:(_,_,_)=>Notify("検証では導入しません。"),
            showSelfUpdater:()=>Notify("検証では本体更新しません。"),openRepository:_=>Notify("検証ではブラウザーを開きません。"),notify:Notify);
        form.Text=WindowTitle.Create("検証用一覧");
        var area=Screen.PrimaryScreen!.WorkingArea;
        form.StartPosition=FormStartPosition.Manual;
        form.Bounds=new(area.Left+area.Width/2,area.Top,Math.Max(form.MinimumSize.Width,area.Width-area.Width/2),area.Height);
        using var timer=new System.Windows.Forms.Timer{Interval=1800000};timer.Tick+=(_,_)=>form.Close();timer.Start();
        form.Shown+=(_,_)=>Console.WriteLine("検証画面表示済み：PID="+Environment.ProcessId);
        Console.WriteLine("実アプリの状態を使わないfixture画面です。Explorerは検証EXE／そのコピーだけを選択表示します。");
        Application.Run(form);return 0;
    }
}
