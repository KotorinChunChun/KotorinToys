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
        foreach (var state in new[] { "確認中", "起動処理中", "停止処理中", "処理中" })
            foreach (var operation in new[] { "start", "stop", "install", "update", "uninstall", "ffmpeg" })
                check(!Rule(operation, state).Enabled && Rule(operation, state).Reason.Length > 0, state + "の" + operation + "を理由付きで無効化");
        foreach (var operation in new[] { "start", "stop", "uninstall", "ffmpeg" })
            check(!Rule(operation, "確認できない").Enabled, "状態不明の" + operation + "を無効化");
        check(Rule("update", "確認できない").Enabled && ToolActionPolicy.Evaluate("install", tool, new("未導入", null, null, null),
            new("確認できない"), release).Enabled, "状態不明でも導入・更新の事前確認へ進める");
        check(ToolActionPolicy.InstallEntry(tool).Enabled && !ToolActionPolicy.InstallEntry(tool with { DistributionStatus = "unpublished" }).Enabled &&
            !ToolActionPolicy.InstallEntry(tool, true).Enabled, "公開未確認でも入口を使え、未公開・処理中は無効");
        foreach (var operation in new[] { "start", "stop", "manual", "install", "update", "uninstall", "ffmpeg" })
            check(!Rule(operation, "実行中", busy: true).Enabled, "画面処理中の" + operation + "を無効化");
        check(!Rule("start", exists: false).Enabled && Rule("start", exists: false).Reason.Contains("実行ファイル"), "実行ファイル欠落を起動前に説明");
        var uninstalledUpdate = ToolActionPolicy.Evaluate("update", tool, new("未導入", null, null, null), new("停止中"), release);
        check(!uninstalledUpdate.Enabled && uninstalledUpdate.Reason.Contains("インストール") && !uninstalledUpdate.Reason.Contains("修復"),
            "未導入の更新ボタンは修復ではなくインストールを案内");
        check(!Rule("update", exists: false).Enabled, "実行ファイルが消えた導入済みスナップショットでも更新を進めない");
        check(Rule("start").Enabled && !Rule("stop").Enabled && Rule("stop", "実行中").Enabled, "停止・実行状態に応じて起動と停止を切替");
        check(!Rule("manual", exists: false).Enabled && Rule("manual").Enabled, "使い方もファイルの存在に連動");
        check(!Rule("uninstall", exists: false).Enabled && Rule("uninstall").Enabled, "削除プログラム欠落を操作前に拒否");
        check(!Rule("ffmpeg", "実行中").Enabled && Rule("ffmpeg").Enabled, "FFmpegは正式導入・停止中だけ可能");
        check(!ToolActionPolicy.Evaluate("start", tool with { Capabilities = [] }, installed, new("停止中"), executableExists: true).Enabled, "未対応の起動能力を有効にしない");

        check(Rule("startadmin").Enabled && !Rule("startadmin", exists: false).Enabled && !Rule("startadmin", busy: true).Enabled,
            "正式導入済み・停止中・EXEありの場合に管理者起動を許可");
        foreach (var state in new[] { "実行中", "確認中", "確認できない", "起動処理中", "停止処理中" })
            check(!Rule("startadmin", state).Enabled, state + "は管理者として二重起動しない");
        check(!ToolActionPolicy.Evaluate("startadmin", tool, new("未導入", null, null, null), new("停止中"), executableExists: true).Enabled,
            "開発配置のEXEを管理者起動に使わない");
        var msix = tools.First(t => t.Msix != null);
        check(!ToolActionPolicy.Evaluate("startadmin", msix, installed, new("停止中"), executableExists: true).Enabled,
            "MSIXのパッケージ起動をrunasへ置き換えない");
        foreach (var operation in new[] { "startadmin", "start", "stop", "update", "uninstall" })
            check(!ToolActionPolicy.Evaluate(operation, tool, installed, new("実行中", 123, IsAdministrator: true), release,
                executableExists: true, uninstallerExists: true).Enabled, "管理者実行中は別権限から" + operation + "を行わない");

        var launchPath = Path.Combine(root, "Test App", "test.exe");
        var request = ToolManager.RequestAdministratorLaunch(launchPath, start =>
        {
            check(start.UseShellExecute && start.Verb == "runas" && start.FileName == launchPath &&
                start.WorkingDirectory == Path.GetDirectoryName(launchPath) && start.ArgumentList.Count == 0,
                "選んだEXEだけを作業フォルダー指定・引数なしのrunasでWindowsに渡す");
            return 123;
        });
        check(request.State == "起動処理中" && request.Pid == 123, "Shellの起動依頼をアプリの準備完了と区別する");
        var canceled = ToolManager.RequestAdministratorLaunch(launchPath, _ => throw new System.ComponentModel.Win32Exception(1223));
        var failed = ToolManager.RequestAdministratorLaunch(launchPath, _ => throw new System.ComponentModel.Win32Exception(5));
        check(canceled.State == "取消" && failed.State == "確認できない" && failed.Detail.Contains("Windowsエラー 5"),
            "UACの取り消しと起動エラーを区別する");
        var inspected = WindowsToolProcess.Read(Environment.ProcessId);
        check(string.Equals(inspected.Path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase),
            "実プロセスのパスとトークンを限定された権限で取得する");

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
            actual[0] = new("未導入", null, null, null); dashboard.ApplySnapshot(actual, states, releases);
            var update = (Button)dashboard.Controls.Find("updateSelected", true).Single();
            var install = (Button)dashboard.Controls.Find("installSelected", true).Single();
            check(install.Enabled && !update.Enabled && update.AccessibleDescription!.Contains("インストール") && !update.AccessibleDescription.Contains("修復"),
                "実フォームも未導入はインストール可能で更新の無効理由を正しく表示");
            actual[0] = installed;
            exists = true; dashboard.ApplySnapshot(actual, states, releases);
            check(start.Enabled && !dashboard.Controls.Find("stop", true).Single().Enabled, "実フォームは復旧したEXEと停止状態を反映");
            var startAdmin = (Button)dashboard.Controls.Find("startadmin", true).Single();
            check(startAdmin.Enabled, "選択した停止中の正式導入EXEに管理者起動ボタンが使える");
            states[0] = new("実行中", 123, ToolManager.AdministratorRunningNotice, IsAdministrator: true);
            dashboard.ApplySnapshot(actual, states, releases);
            check(!startAdmin.Enabled && !start.Enabled && !dashboard.Controls.Find("stop", true).Single().Enabled &&
                startAdmin.AccessibleDescription!.Contains("アプリ側") &&
                (string?)grid.Rows[0].Cells["state"].Value == "管理者として実行中", "実フォームは管理者実行中と操作できない理由を表示");
            states[0] = new("停止処理中"); dashboard.ApplySnapshot(actual, states, releases);
            check(!start.Enabled && !dashboard.Controls.Find("uninstallSelected", true).Single().Enabled, "終了処理中は起動・削除を競合させない");

            var paint = Array.FindIndex(tools, t => t.Id == PppaintDistribution.Id);
            actual[paint] = new("未導入", null, null, null);
            exists = false;
            grid.CurrentCell = grid.Rows[paint].Cells[0];
            grid.ClearSelection();
            grid.Rows[paint].Selected = true;
            dashboard.ApplySnapshot(actual, states, releases);
            check(install.Enabled && !start.Enabled, "PPPaintの未導入行でインストールを選べる");
            actual[paint] = installed with { Version = "0.2.36" };
            exists = true;
            dashboard.ApplySnapshot(actual, states, releases);
            check(start.Enabled && !update.Enabled, "PPPaintの導入済み行で手動起動を選べる");

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
