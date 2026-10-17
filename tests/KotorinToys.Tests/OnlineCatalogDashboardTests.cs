using KotorinToys;

internal static class OnlineCatalogDashboardTests
{
    static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("動的一覧試験が完了しません。");
        task.GetAwaiter().GetResult();
    }

    public static void Run(string root, string fixtures, Action<bool, string> check)
    {
        var bundled = OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root, OnlineToolCatalog.RelativePath)), root);
        var added = bundled[0] with { Id = "dynamic-test-tool", Name = "DynamicTestTool", AppId = "KotorinToys.DynamicTestTool",
            ToolRoot = "tools/DynamicTestTool", Executable = "DynamicTestTool.exe", InstalledExecutable = "DynamicTestTool.exe", DistributionStatus = "unpublished",
            Summary = "本体を変更せず追加", RepositoryUrl = "https://github.com/KotorinChunChun/DynamicTestTool", Msix = null };
        byte[] response = OnlineCatalogTests.Encode(added, bundled[1] with { Summary = "GitHubで説明更新" });
        int requests = 0, dialogs = 0, opened = 0;
        var requestedReleases = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<byte[]> Fetch(CancellationToken token) { requests++; await gate.Task.WaitAsync(token); return response; }
        var settings = new HostSettingsStore(Path.Combine(fixtures, "dynamic-settings")); settings.Save(settings.Load());
        var settingsBefore = File.ReadAllBytes(Path.Combine(settings.DirectoryPath, "settings.json"));
        var source = new ToolCatalogSource(root, settings.DirectoryPath, Fetch);
        int starts = 0;
        var manager = new ToolManager(root, (_, _) => starts++, source);
        Task<PublicRelease> Release(ToolDefinition tool, ReleasePackage? fallback, CancellationToken token)
        { requestedReleases.Add(tool.Id); return Task.FromResult(new PublicRelease(fallback, false, false, "検証用・未照合")); }
        using var form = new Dashboard(root, toolManager: manager, releaseCheck: Release,
            detectInstallation: _ => new("未導入", null, null, null), showInstaller: (_, _) => dialogs++, openRepository: _ => opened++);
        form.CreateControl(); form.PerformLayout();
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        var selectedActions = form.Controls.Find("selectedActions", true).Single();
        var hostActions = form.Controls.Find("hostActions", true).Single();
        grid.CurrentCell = grid.Rows[1].Cells[0];
        var updating = form.RefreshAll(); var duplicate = form.RefreshAll();
        check(ReferenceEquals(updating, duplicate) && requests == 1, "全体更新を集約してconfigを一度だけ取得");
        check(!selectedActions.Enabled && !hostActions.Enabled, "一覧更新中は旧行を操作させない");
        Pump(form.OpenInstallation(true)); form.OpenRepository();
        check(dialogs == 0 && opened == 0 && !updating.IsCompleted, "config通信中の導入・リンク再操作を抑止");
        gate.SetResult(); Pump(updating);
        check(grid.Rows.Count == 2 && grid.Rows[0].Cells["tool"].Value?.ToString() == added.Name &&
            grid.Rows[1].Cells["summary"].Value?.ToString() == "GitHubで説明更新", "実フォームへ追加・削除・説明変更を再起動なしで反映");
        check(grid.CurrentRow?.Index == 1 && requestedReleases.SequenceEqual(new[] { added.Id, bundled[1].Id }), "選択IDを保持し変更後のツールだけ公開版照合");
        check(selectedActions.Enabled && hostActions.Enabled && !form.Controls.Find("catalogStatus", true).Any(), "取得成功の常設説明なしで操作行を復旧");
        check(manager.Load().Length == 2 && manager.KnownTools().Length == bundled.Length + 1 && starts == 0 && dialogs == 0, "管理部と一覧を共有し設定更新だけでは起動・導入しない");
        grid.CurrentCell = grid.Rows[0].Cells[0]; form.OpenRepository();
        check(opened == 1 && ToolRepository.Find(added)?.AbsoluteUri == added.RepositoryUrl, "追加ツールのGitHubトップページもconfigから反映");
        response = OnlineCatalogTests.Encode(bundled[1] with { Summary = "もう一度更新" }, added);
        requestedReleases.Clear(); Pump(form.RefreshAll());
        check(grid.Rows.Count == 2 && grid.CurrentRow?.Index == 1 && grid.Rows[0].Cells["summary"].Value?.ToString() == "もう一度更新", "連続したconfig変更でも選択を行番号でなくIDで追従");
        response = System.Text.Encoding.UTF8.GetBytes("bad JSON"); Pump(form.RefreshAll());
        check(grid.Rows.Count == 2 && grid.CurrentRow?.Index == 1 && grid.Rows[0].Cells["detail"].Value?.ToString()?.Contains("維持") == true, "取得したconfigが壊れても表示・選択を維持し理由を表へ残す");
        response = OnlineCatalogTests.Encode(); Pump(form.RefreshAll());
        check(grid.Rows.Count == 0 && grid.AccessibleDescription?.Contains("登録ツールなし") == true &&
            !form.Controls.Find("start", true).Single().Enabled && !form.Controls.Find("github", true).Single().Enabled, "空一覧は理由を表示し選択操作を無効にする");
        Pump(form.OpenInstallation(true)); check(dialogs == 0, "空一覧の更新操作で先頭行を参照しない");
        using (var installation = new InstallationForm(manager))
            check(!installation.Controls.Find("installFfmpeg", true).Single().Enabled, "SSCaptureを非表示にした一覧ではFFmpeg操作を無効化");
        check(File.ReadAllBytes(Path.Combine(settings.DirectoryPath, "settings.json")).SequenceEqual(settingsBefore), "一覧更新で利用者設定を変更しない");
        HiddenStartup(root, fixtures, bundled, check);
        Closing(root, fixtures, check);
        InFlight(root, fixtures, bundled, check);
    }

    sealed class Gateway : IStartupGateway
    {
        internal readonly Dictionary<string, ScheduledTaskState> Tasks = new();
        public ScheduledTaskState? Read(string name) => Tasks.GetValueOrDefault(name);
        public ScheduledTaskState Create(TaskPlan plan) => Tasks[plan.Name] = new(plan.Name, StartupTests.Xml(plan));
        public ScheduledTaskState Restore(string name, string xml, string securityDescriptor = "") => Tasks[name] = new(name, xml, securityDescriptor);
        public void Delete(string name, string expectedFingerprint)
        {
            if (Read(name)?.Fingerprint != expectedFingerprint) throw new IOException("外部変更");
            Tasks.Remove(name);
        }
    }

    static void HiddenStartup(string root, string fixtures, ToolDefinition[] bundled, Action<bool, string> check)
    {
        var directory = Path.Combine(fixtures, "hidden-startup");
        var store = new HostSettingsStore(directory);
        var source = new ToolCatalogSource(root, directory, _ => Task.FromResult(OnlineCatalogTests.Encode()));
        var manager = new ToolManager(root, catalogSource: source);
        var gateway = new Gateway();
        var coordinator = new StartupCoordinator(store, gateway, manager, Environment.ProcessPath!, allowDevelopment: true);
        Pump(coordinator.SetMode(bundled[0], "via_kotorintoys"));
        var before = File.ReadAllBytes(Path.Combine(directory, "settings.json"));
        var fingerprint = gateway.Tasks.Values.Single().Fingerprint;
        Pump(source.Refresh());
        check(manager.Load().Length == 0 && gateway.Tasks.Values.Single().Fingerprint == fingerprint &&
            File.ReadAllBytes(Path.Combine(directory, "settings.json")).SequenceEqual(before), "非表示だけで既存の予約・希望方式を削除しない");
        var state = coordinator.State(bundled[0]); Pump(state);
        check(state.Result.ActualState == "登録済み", "非表示後も既存の自動起動予約を照合できる");
        var disabled = coordinator.SetMode(bundled[0], "none"); Pump(disabled);
        check(disabled.Result.Success && gateway.Tasks.Count == 0, "非表示の登録も明示操作で方式解除できる");
    }

    static void Closing(string root, string fixtures, Action<bool, string> check)
    {
        bool canceled = false;
        async Task<byte[]> Fetch(CancellationToken token)
        {
            try { await Task.Delay(Timeout.Infinite, token); return []; }
            finally { canceled = token.IsCancellationRequested; }
        }
        var source = new ToolCatalogSource(root, Path.Combine(fixtures, "closing-catalog"), Fetch);
        using var form = new Dashboard(root, toolManager: new(root, catalogSource: source));
        var updating = form.RefreshAll(); form.Dispose(); Pump(updating);
        check(canceled && form.IsDisposed && !File.Exists(Path.Combine(fixtures, "closing-catalog", "tool-catalog-cache.json")), "画面破棄はconfig取得を取消し未検証キャッシュを作らない");
    }

    static void InFlight(string root, string fixtures, ToolDefinition[] bundled, Action<bool, string> check)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0, requests = 0;
        async Task<PublicRelease> Release(ToolDefinition tool, ReleasePackage? fallback, CancellationToken token)
        { calls++; if (calls <= bundled.Length) await gate.Task.WaitAsync(token); return new(fallback, false, false, "試験"); }
        var source = new ToolCatalogSource(root, Path.Combine(fixtures, "inflight-catalog"), _ =>
        { requests++; return Task.FromResult(OnlineCatalogTests.Encode(bundled[0])); });
        using var form = new Dashboard(root, toolManager: new(root, catalogSource: source), releaseCheck: Release);
        var oldRelease = form.RefreshReleases(); var all = form.RefreshAll();
        check(!all.IsCompleted && requests == 0, "旧一覧の公開照合中はconfig採用を待つ");
        gate.SetResult(); Pump(all); Pump(oldRelease);
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        check(grid.Rows.Count == 1 && calls == bundled.Length + 1 && requests == 1, "件数が減る更新でも旧照合を別製品へ適用しない");
    }
}
