using KotorinToys;

internal static class HostUpdateFormTests
{
    static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("本体アップデート画面の試験が完了しません。");
        task.GetAwaiter().GetResult(); Application.DoEvents();
    }
    sealed class Fake : IHostSelfUpdateService
    {
        public string CurrentVersion => "0.1.0";
        public string? Blocked;
        public string? BlockReason() => Blocked;
        public TaskCompletionSource? CheckGate, HandoffGate;
        public bool FailCheck, FailHandoff;
        public List<bool> Queries = new();
        public int Handoffs;
        public ReleasePackage Item = new(new("host", "KotorinToys", "0.1.1", "KotorinToys", "KotorinToys-0.1.1-setup.exe", new string('a', 64), "unsigned-public-build"), 4);
        public async Task<PublicRelease> Check(bool includePrerelease, CancellationToken cancellation)
        {
            Queries.Add(includePrerelease);
            if (CheckGate != null) await CheckGate.Task.WaitAsync(cancellation);
            if (FailCheck) throw new IOException("オフライン試験");
            return new(Item, true, includePrerelease, "fixture");
        }
        public async Task Handoff(ReleasePackage release, bool includePrerelease, IProgress<string> progress, CancellationToken cancellation)
        {
            Handoffs++; progress.Report("fixtureの準備中");
            if (HandoffGate != null) await HandoffGate.Task.WaitAsync(cancellation);
            if (FailHandoff) throw new IOException("引継ぎ失敗試験");
        }
        public SelfUpdateResult? Result;
        public SelfUpdateResult? LastResult() => Result;
    }
    static T Control<T>(System.Windows.Forms.Control form, string name) where T : System.Windows.Forms.Control => (T)form.Controls.Find(name, true).Single();

    public static void Run(string root, Action<bool, string> check)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        try { RunOnUi(root, check); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static void RunOnUi(string root, Action<bool, string> check)
    {
        var service = new Fake();
        using var form = new HostUpdateForm(service); form.CreateControl(); form.PerformLayout();
        var install = Control<Button>(form, "installHostUpdate");
        var consent = Control<CheckBox>(form, "hostUpdateConsent");
        var preview = Control<CheckBox>(form, "hostIncludePrerelease");
        check(form.Text == WindowTitle.Create("本体アップデート") && form.Icon != null, "本体更新画面は共通タイトルと04アイコンを使用");
        check(!preview.Checked && !install.Enabled, "開発版と未確認インストールは既定で無効");
        Pump(form.RefreshRelease());
        check(Control<Label>(form, "hostCurrentVersion").Text.Contains("0.1.0") && Control<Label>(form, "hostAvailableVersion").Text.Contains("0.1.1（公開日不明）（正式版）"), "現在の本体版と検証済み公開版を分離表示");
        service.Item = service.Item with { PublishedAt = DateTimeOffset.Parse("2026-10-16T18:00:00Z") };
        Pump(form.RefreshRelease());
        check(Control<Label>(form, "hostAvailableVersion").Text.Contains("0.1.1（2026/10/17 公開）"), "本体更新でも版の右に日本時間の公開日");
        Pump(form.InstallUpdate()); check(service.Handoffs == 0 && !install.Enabled, "新しい版でも未署名の明示同意前には開始しない");
        consent.Checked = true; check(install.Enabled, "同意・正式導入・新しい公開版が揃うと更新可能");
        service.Blocked = "開発実行です。"; consent.Checked = false; consent.Checked = true;
        check(!install.Enabled && Control<Label>(form, "hostUpdateBlockedReason").Text == service.Blocked, "開発実行・別配置の原因を更新ボタンと同時に表示");
        Pump(form.RefreshRelease());
        check(Control<Label>(form, "hostUpdateBlockedReason").Text == service.Blocked, "公開確認後も更新不可の具体的な原因を表示");
        service.Blocked = null;
        preview.Checked = true;
        check(service.Queries.Last() && Control<Label>(form, "hostAvailableVersion").Text.Contains("開発版") && !consent.Checked, "開発版への切替では再照合と再同意を必要とする");
        foreach (var version in new[] { "0.1.0", "0.0.9" })
        {
            service.Item = service.Item with { Package = service.Item.Package with { Version = version, File = $"KotorinToys-{version}-setup.exe" } };
            Pump(form.RefreshRelease()); consent.Checked = true;
            check(!install.Enabled, "同版・旧版の実フォームは更新不可：" + version);
        }
        service.Item = service.Item with { Package = service.Item.Package with { Version = "0.1.1", File = "KotorinToys-0.1.1-setup.exe" } };
        service.FailCheck = true; Pump(form.RefreshRelease()); consent.Checked = true;
        check(!install.Enabled && Control<Label>(form, "hostAvailableVersion").Text == "公開版：未確認", "照合失敗で古い確認済みデータを更新に使わない");
        service.FailCheck = false; service.CheckGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = form.RefreshRelease(); var requests = service.Queries.Count;
        Pump(form.RefreshRelease()); Pump(form.InstallUpdate());
        check(service.Queries.Count == requests && service.Handoffs == 0 && !preview.Enabled && !install.Enabled, "公開確認中の重複操作と対象変更を抑止");
        form.CancelOperation(); Pump(pending);
        check(Control<Label>(form, "hostUpdateStatus").Text.Contains("取り消") && preview.Enabled && !install.Enabled, "公開確認の取消で操作を復旧し更新しない");
        service.CheckGate = null; Pump(form.RefreshRelease()); consent.Checked = true;
        service.FailHandoff = true; Pump(form.InstallUpdate());
        check(Control<Label>(form, "hostUpdateStatus").Text.Contains("開始できません") && !install.Enabled && preview.Enabled,
            $"引継ぎ失敗で本体を閉じず再確認操作を残す（{Control<Label>(form, "hostUpdateStatus").Text} / 更新={install.Enabled} / 対象変更={preview.Enabled} / 引継ぎ={service.Handoffs}）");
        service.FailHandoff = false; service.HandoffGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Pump(form.RefreshRelease()); consent.Checked = true;
        pending = form.InstallUpdate(); var count = service.Handoffs;
        Pump(form.InstallUpdate());
        check(service.Handoffs == count && !install.Enabled && Control<Button>(form, "cancelHostUpdate").Enabled, "取得・準備中は重複更新を抑止し取消できる");
        service.HandoffGate.SetResult(); Pump(pending);
        check(Control<Label>(form, "hostUpdateStatus").Text.Contains("引き継ぎ") && !Control<Button>(form, "cancelHostUpdate").Enabled && !preview.Enabled,
            "引継ぎ後は強制取消・二重更新をできなくする");
        using var closing = new HostUpdateForm(new Fake { CheckGate = new(TaskCreationOptions.RunContinuationsAsynchronously) });
        pending = closing.RefreshRelease(); closing.Dispose(); Pump(pending);
        check(closing.IsDisposed, "画面破棄で確認を取消し破棄済み画面へ反映しない");
        int opened = 0;
        using var dashboard = new Dashboard(root, selfUpdates: new Fake(), showSelfUpdater: () => opened++);
        dashboard.CreateControl(); dashboard.PerformLayout();
        var button = Control<Button>(dashboard, "hostUpdate");
        check(button.Parent?.Name == "hostActions" && !dashboard.Controls.Find("selfUpdateStatus", true).Any(), "本体アップデートは別の操作行へ配置し下部へ常設情報を出さない");
        dashboard.OpenSelfUpdate(); check(opened == 1, "本体ボタンから専用更新画面へ進む");
        var resultService = new Fake { Result = new(1, Guid.NewGuid().ToString("N"), root, "0.1.1", "0.1.2", "失敗", "アクセス拒否の試験", false, 7, DateTimeOffset.UtcNow) };
        using var history = new HostUpdateForm(resultService); history.CreateControl(); history.PerformLayout();
        check(Control<Label>(history, "hostLastUpdate").Text.Contains("0.1.1 → 0.1.2 / 失敗") &&
            Control<Label>(history, "hostLastUpdate").Text.Contains("アクセス拒否の試験"), "前回更新の対象版・結果・理由はメインでなく専用画面へ保持");
        history.Size = history.MinimumSize; history.PerformLayout();
        var reasonLabel = Control<Label>(history, "hostUpdateBlockedReason");
        resultService.Blocked = "更新できません：正式導入先とは別のEXEです。\n実行中：C:\\develop\\source\\bin\\debug\\KotorinToys.exe\n正式な実行ファイル：C:\\Programs\\KotorinToys\\KotorinToys.exe\n対処：正式導入版を起動してください。";
        Pump(history.RefreshRelease()); history.PerformLayout();
        check(reasonLabel.Text == resultService.Blocked && reasonLabel.MaximumSize.Width <= reasonLabel.Parent!.ClientSize.Width - reasonLabel.Parent.Padding.Horizontal &&
            Control<Button>(history, "installHostUpdate").Bottom <= history.ClientSize.Height, "最小サイズでも実パス付きの原因を折返し・スクロール対象にしてボタンを隠さない");
    }
}
