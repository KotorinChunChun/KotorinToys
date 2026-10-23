using KotorinToys;

internal static class PublicReleaseCacheUiTests
{
    static readonly DateTimeOffset Reset = DateTimeOffset.Parse("2026-10-24T06:40:00Z");
    static PublicRelease Limited(ReleasePackage? fallback, bool known = true) =>
        new(fallback, false, false, "模擬403（表示は構造化された原因から生成）", Problem: ReleaseProblem.RateLimit, RetryAt: known ? Reset : null);
    static InstallationState Missing(ToolDefinition _) => new("未導入", null, null, null);
    static Task<ToolState> Stopped(ToolDefinition _) => Task.FromResult(new ToolState("停止中"));
    static T Find<T>(Control form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();
    static void Pump(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("公開キャッシュの画面試験が完了しません。");
        task.GetAwaiter().GetResult(); Application.DoEvents();
    }
    sealed class Host(PublicRelease release) : IHostSelfUpdateService
    {
        public string CurrentVersion => HostProduct.CurrentVersion;
        public string? BlockReason() => null;
        public SelfUpdateResult? LastResult() => null;
        public Task<PublicRelease> Check(bool includePrerelease, CancellationToken cancellation) => Task.FromResult(release);
        public Task Handoff(ReleasePackage package, bool preview, IProgress<string> progress, CancellationToken cancellation) =>
            throw new Exception("検証から自己更新を実行しない");
    }

    public static void Run(string root, Action<bool, string> check)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            var tools = ToolCatalog.Load(root); var catalog = ReleaseDownloader.Load(root);
            var tool = tools.Single(t => t.Id == "ss-capture");
            var package = catalog.Packages.Single(p => p.Package.Id == tool.Id);
            var release = Limited(package);
            var rule = ToolActionPolicy.Evaluate("install", tool, Missing(tool), new("停止中"), release);
            check(!rule.Enabled && rule.Reason.Contains("2026/10/24 15:40:00") && rule.Reason.Contains("日本時間") && rule.Reason.Contains("取得回数制限"),
                "導入ボタンの無効理由へ取得制限と日本時間の解除予定を伝える");
            check(ToolInstallationRunner.PublicReason(tool, catalog, release)!.Contains("解除予定"),
                "同梱台帳があっても未照合の取得制限を一般的な配布物なしへ置き換えない");
            int invoked = 0; string details = ""; var notices = new List<string>();
            using var form = new Dashboard(root, releaseCheck: (_, fallback, _) => Task.FromResult(Limited(fallback)),
                detectInstallation: Missing, executionCheck: (t, _) => Stopped(t), fileExists: _ => false,
                showToolInstaller: (_, _, _) => invoked++, notify: notices.Add, showDetails: (_, text) => details = text);
            form.CreateControl(); form.PerformLayout(); Pump(form.RefreshAll());
            var grid = Find<DataGridView>(form, "toolTable");
            grid.CurrentCell = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Cells["tool"].Value?.ToString() == tool.Name).Cells["detail"];
            var install = Find<Button>(form, "installSelected");
            check(!install.Enabled && install.AccessibleDescription!.Contains("15:40:00"), "実一覧の無効導入ボタンにも解除時刻を保持");
            check(grid.CurrentRow!.Cells["detail"].Value?.ToString() == "-" && grid.CurrentRow.Cells["detail"].ToolTipText.Contains("15:40:00"),
                "未導入の一覧はハイフン、詳細は取得制限の解除時刻を表示");
            form.OpenSelectedDetails();
            check(details.Contains("解除予定") && details.Contains("15:40:00") && details.Contains("導入不可"), "クリックして読む説明にも原因と解除予定と無効理由を表示");
            Pump(form.OpenToolInstallation("install"));
            check(invoked == 0 && notices.Last().Contains("15:40:00") && !notices.Last().Contains("照合済みの公開インストーラーがありません"),
                "直接導入要求でもinstallerは起動せず取得制限の具体的な警告を表示");

            var publications = tools.ToDictionary(t => t.Id, t => Limited(catalog.Packages.FirstOrDefault(p => p.Package.Id == t.Id)));
            using var advanced = new InstallationForm(new ToolManager(root), tool.Id, catalog,
                detectInstallation: Missing, executionState: Stopped, fileExists: _ => false, publications: publications);
            advanced.CreateControl(); Pump(advanced.RefreshAvailability());
            var targets = Find<CheckedListBox>(advanced, "installationTargets");
            check(targets.Items.Cast<string>().Any(text => text.StartsWith(tool.Name + "：") && text.Contains("15:40:00")),
                "一括導入の対象説明でも解除予定を失わない");
            targets.SetItemChecked(Array.FindIndex(tools, t => t.Id == tool.Id), true);
            check(!Find<Button>(advanced, "batchInstall").Enabled && Find<Button>(advanced, "batchInstall").AccessibleDescription!.Contains("15:40:00"),
                "一括導入の無効理由も取得制限を優先する");
            using var single = new InstallationForm(new ToolManager(root), tool.Id, catalog, "install",
                confirm: _ => throw new Exception("制限中に確認・実行へ進まない"), detectInstallation: Missing, executionState: Stopped,
                fileExists: _ => false, publications: publications);
            Pump(single.RunSelected());
            check(Find<Label>(single, "installationSummary").Text.Contains("15:40:00"), "単体導入画面の実行直前チェックでも解除予定を表示");

            foreach (bool known in new[] { true, false })
            {
                using var host = new HostUpdateForm(new Host(Limited(null, known))); host.CreateControl(); Pump(host.RefreshRelease());
                string status = Find<Label>(host, "hostUpdateStatus").Text;
                check(!Find<Button>(host, "installHostUpdate").Enabled && status.Contains(known ? "15:40:00" : "解除予定時刻はGitHubから取得できません"),
                    "本体自己更新でも取得制限と解除時刻の既知/不明を表示：" + known);
            }
            check(DashboardText.Advice(tool, Missing(tool), new("停止中"), Limited(package, false)).Contains("少なくとも1分"),
                "時刻不明の取得制限を一般的な通信エラー扱いにしない");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    // 実機の最終表示確認用。通信・導入・設定保存・アプリ制御は行わない。
    public static int Panel(string root)
    {
        var catalog = ReleaseDownloader.Load(root); var manager = new ToolManager(root);
        var publications = manager.Load().ToDictionary(t => t.Id, t => Limited(catalog.Packages.FirstOrDefault(p => p.Package.Id == t.Id)));
        using var form = new Dashboard(root, toolManager: manager, detectInstallation: Missing,
            releaseCheck: (tool, _, _) => Task.FromResult(publications[tool.Id]), executionCheck: (t, _) => Stopped(t),
            fileExists: _ => false, openRepository: _ => { }, selfUpdates: new Host(Limited(null)),
            startupCheck: (_, _) => Task.FromResult(new StartupOverview("検証対象外", "OS予約は参照・変更しません。")),
            showInstaller: (id, releases) =>
            {
                using var advanced = new InstallationForm(manager, id, releases, detectInstallation: Missing,
                    executionState: Stopped, fileExists: _ => false, publications: publications);
                advanced.ShowDialog();
            });
        // 通常画面の配置は変えず、検証用画面だけを現在の作業領域の右半分へ出す。
        var working = Screen.PrimaryScreen!.WorkingArea;
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = new(working.Left + working.Width / 2, working.Top, working.Width - working.Width / 2, working.Height);
        form.Shown += (_, _) =>
        {
            var grid = Find<DataGridView>(form, "toolTable");
            grid.CurrentCell = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Cells["tool"].Value?.ToString() == "SSCapture").Cells["detail"];
        };
        using var timeout = new System.Windows.Forms.Timer { Interval = 600000 };
        timeout.Tick += (_, _) => form.Close(); timeout.Start();
        Application.Run(form); return 0;
    }
}
