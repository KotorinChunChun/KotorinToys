using System.Diagnostics;
using System.Reflection;
using KotorinToys;

internal static class ToolLocationTests
{
    public static void Run(string root, Action<bool,string> check)
    {
        const string running = @"C:\開発 用\SSCapture.exe", expected = @"C:\正式 導入\SSCapture.exe";
        var state = new ToolState("確認できない", 123, "別の配置", RunningExecutable: running, ExpectedExecutable: expected);
        var opens = new List<ProcessStartInfo>(); string? current = running;
        var locations = new ToolLocations(_ => current, _ => true, opens.Add);
        check(locations.Running(state).Enabled, "構造化した別配置だけを開ける");
        locations.OpenRunning(state);
        check(opens.Single().FileName == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") &&
            !opens[0].UseShellExecute && opens[0].ArgumentList.SequenceEqual(["/select," + running]), "日本語・空白入りEXEは実行せずExplorerの選択表示へ渡す");
        current = expected; bool rejected = false;
        try { locations.OpenRunning(state); } catch(IOException) { rejected = true; }
        check(rejected && opens.Count == 1, "PIDの実配置が変わったら開かない");
        current = null; rejected = false;
        try { locations.OpenRunning(state); } catch(IOException) { rejected = true; }
        check(rejected && opens.Count == 1, "終了・別セッション・照合不能では開かない");
        foreach (var value in new string?[] { null, "", "relative.exe", @"\\server\share\app.exe", "https://example.com/app.exe", "C:\\a.exe\r\n", "C:\\a.exe:stream.exe", "C:\\a\"b.exe", "C:\\a.bat" })
            check(!locations.Running(state with { RunningExecutable = value }).Enabled, "不正・外部・非EXEパスを拒否：" + (value ?? "null"));
        check(!locations.Running(new("確認できない",123,"別配置："+running)).Enabled, "説明文だけのパスを抽出しない");
        check(!locations.Running(state with { RunningExecutable = expected }).Enabled && !locations.Running(state with { State="停止中" }).Enabled,
            "同じ配置と停止中を別配置として扱わない");
        var missing = new ToolLocations(_=>running,_=>false,opens.Add);
        check(!missing.Running(state).Enabled, "消えた実行ファイルは開かない");
        var installed = new InstallationState("導入済み","1.0.0",Path.GetDirectoryName(expected),expected);
        locations.OpenInstalled(installed);
        check(opens.Last().ArgumentList.Single()=="/select,"+expected && !locations.Installed(installed with {State="未導入"}).Enabled && !missing.Installed(installed).Enabled,
            "正式導入先だけを選択し未導入・欠落時は拒否する");

        var previous=SynchronizationContext.Current;
        try
        {
            var tools=ToolCatalog.Load(root);var catalog=ReleaseDownloader.Load(root);current=running;
            var actual=tools.Select(_=>installed).ToArray();var states=tools.Select(_=>new ToolState("停止中")).ToArray();states[0]=state;
            var releases=tools.Select(t=>new PublicRelease(catalog.Packages.FirstOrDefault(p=>p.Package.Id==t.Id),false,false,"fixture")).ToArray();
            var notices=new List<string>();int details=0;
            using var form=new Dashboard(root,locations:locations,detectInstallation:t=>actual[Array.FindIndex(tools,t0=>t0.Id==t.Id)],
                notify:notices.Add,showDetails:(_,_)=>details++,fileExists:_=>true);
            form.CreateControl();form.ApplySnapshot(actual,states,releases);
            var grid=(DataGridView)form.Controls.Find("toolTable",true).Single();grid.CurrentCell=grid.Rows[0].Cells["detail"];
            check(grid.CurrentCell is DataGridViewButtonCell && grid.CurrentCell.ToolTipText.Contains(running), "別配置の詳細セルを配置先付きボタンにする");
            int before=opens.Count;form.OpenSelectedDetailAction();
            check(opens.Count==before+1 && details==0 && !form.Controls.Find("start",true).Single().Enabled && !form.Controls.Find("stop",true).Single().Enabled,
                "配置の表示だけを許可し起動・停止の安全照合を維持する");
            form.OpenSelectedDetails();check(details==1,"配置ボタンがあっても詳細を読む経路を残す");
            typeof(Dashboard).GetMethod("TryBeginOperation",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null);
            form.OpenSelectedDetailAction();form.OpenInstalledLocation();
            check(!grid.Enabled && opens.Count==before+1,"処理中は一覧と配置の直接呼出しを無効にする");
            typeof(Dashboard).GetMethod("EndOperation",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null);
            var click=new DataGridViewCellMouseEventArgs(grid.Columns["detail"]!.Index,0,0,0,new MouseEventArgs(MouseButtons.Right,1,0,0,0));
            typeof(DataGridView).GetMethod("OnCellMouseClick",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(grid,[click]);
            check(opens.Count==before+1 && grid.ContextMenuStrip!.Items[0].Text=="詳細を表示","右クリックでは場所を勝手に開かず詳細メニューを出す");
            actual[0]=installed with {State="未導入"};form.OpenInstalledLocation();
            check(opens.Count==before+1 && notices.Last().Contains("正式導入先"),"表示後の正式登録消失も再検出して拒否する");
            form.ApplySnapshot(actual,states,releases);
            check(grid.Rows[0].Cells["detail"] is DataGridViewTextBoxCell && grid.Rows[0].Cells["detail"].Value?.ToString()=="-","未導入へ変わった行に配置ボタンを残さない");
            using var dialog=new OperationDetailsForm("Sample","説明",()=>{},ActionAvailability.Deny("未導入です"));
            var button=(Button)dialog.Controls.Find("openInstalledLocation",true).Single();
            check(!button.Enabled && button.AccessibleDescription=="未導入です","正式導入先を開けない理由と無効ボタンを表示する");
        }
        finally{SynchronizationContext.SetSynchronizationContext(previous);}
    }
}
