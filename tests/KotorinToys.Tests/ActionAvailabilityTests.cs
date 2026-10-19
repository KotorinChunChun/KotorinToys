using KotorinToys;

internal static class ActionAvailabilityTests
{
    static void Pump(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("操作可否の確認が完了しません。");
        task.GetAwaiter().GetResult();
    }
    public static void Run(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root); var tool = tools[0];
        var installed = new InstallationState("導入済み", "0.0.1", root, "test.exe", "unins000.exe");
        var release = new PublicRelease(ReleaseDownloader.Load(root).Packages.Single(p => p.Package.Id == tool.Id), true, true, "fixture");
        ActionAvailability Rule(string operation, string state = "停止中", bool exists = true, bool busy = false) =>
            ToolActionPolicy.Evaluate(operation, tool, installed, new(state), release, exists, exists, exists, busy);
        foreach (var state in new[] { "確認中", "起動処理中", "停止処理中", "確認できない", "処理中" })
            foreach (var operation in new[] { "start", "stop", "install", "update", "uninstall", "ffmpeg" })
                check(!Rule(operation, state).Enabled && Rule(operation, state).Reason.Length > 0, state + "の" + operation + "を理由付きで無効化");
        foreach (var operation in new[] { "start", "stop", "manual", "install", "update", "uninstall", "ffmpeg" })
            check(!Rule(operation, "実行中", busy: true).Enabled, "画面処理中の" + operation + "を無効化");
        check(!Rule("start", exists: false).Enabled && Rule("start", exists: false).Reason.Contains("実行ファイル"), "実行ファイル欠落を起動前に説明");
        check(Rule("start").Enabled && !Rule("stop").Enabled && Rule("stop", "実行中").Enabled, "停止・実行状態に応じて起動と停止を切替");
        check(!Rule("manual", exists: false).Enabled && Rule("manual").Enabled, "使い方もファイルの存在に連動");
        check(!Rule("uninstall", exists: false).Enabled && Rule("uninstall").Enabled, "削除プログラム欠落を操作前に拒否");
        check(!Rule("ffmpeg", "実行中").Enabled && Rule("ffmpeg").Enabled, "FFmpegは正式導入・停止中だけ可能");
        check(!ToolActionPolicy.Evaluate("start", tool with { Capabilities = [] }, installed, new("停止中"), executableExists: true).Enabled, "未対応の起動能力を有効にしない");

        var previous = SynchronizationContext.Current;
        try
        {
            bool exists = false;
            var actual = tools.Select(_ => installed).ToArray();
            var states = tools.Select(_ => new ToolState("停止中")).ToArray();
            var releases = tools.Select(t => new PublicRelease(ReleaseDownloader.Load(root).Packages.SingleOrDefault(p => p.Package.Id == t.Id), true, true, "fixture")).ToArray();
            using var dashboard = new Dashboard(root, fileExists: _ => exists);
            dashboard.CreateControl();
            var grid = (DataGridView)dashboard.Controls.Find("toolTable", true).Single();
            grid.CurrentCell = grid.Rows[0].Cells[0]; dashboard.ApplySnapshot(actual, states, releases);
            var start = (Button)dashboard.Controls.Find("start", true).Single();
            check(!start.Enabled && start.AccessibleDescription!.Contains("実行ファイル"), "実フォームも起動欠落を理由付きでグレーアウト");
            exists = true; dashboard.ApplySnapshot(actual, states, releases);
            check(start.Enabled && !dashboard.Controls.Find("stop", true).Single().Enabled, "実フォームは復旧したEXEと停止状態を反映");
            states[0] = new("停止処理中"); dashboard.ApplySnapshot(actual, states, releases);
            check(!start.Enabled && !dashboard.Controls.Find("uninstallSelected", true).Single().Enabled, "終了処理中は起動・削除を競合させない");

            var ss = tools.Single(t => t.Id == "ss-capture"); string execution = "実行中";
            using var advanced = new InstallationForm(new(root), releases: ReleaseDownloader.Load(root),
                detectInstallation: _ => installed, executionState: _ => Task.FromResult(new ToolState(execution)), fileExists: _ => exists);
            advanced.CreateControl(); Pump(advanced.RefreshAvailability());
            var ffmpeg = (Button)advanced.Controls.Find("installFfmpeg", true).Single();
            check(!ffmpeg.Enabled && ffmpeg.AccessibleDescription!.Contains("停止"), "実フォームのFFmpegは実行中に無効化して停止を案内");
            execution = "停止中"; Pump(advanced.RefreshAvailability());
            check(ffmpeg.Enabled, "停止後のFFmpeg導入を再有効化");
            var targets = (CheckedListBox)advanced.Controls.Find("installationTargets", true).Single();
            check(!advanced.Controls.Find("batchInstall", true).Single().Enabled && !advanced.Controls.Find("batchUninstall", true).Single().Enabled, "一括処理は対象チェックなしで無効");
            targets.SetItemChecked(Array.FindIndex(tools, t => t.Id == ss.Id), true);
            check(advanced.Controls.Find("batchInstall", true).Single().Enabled, "選択行が導入・更新可能な場合だけ一括処理を有効化");
            exists = false; Pump(advanced.RefreshAvailability());
            check(!ffmpeg.Enabled && !advanced.Controls.Find("batchUninstall", true).Single().Enabled, "依存EXEや削除EXEの消失を再検出");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
