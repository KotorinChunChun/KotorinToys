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
        public string CurrentVersion { get; set; } = "0.1.0";
        public HostUpdateChannel CurrentChannel = HostUpdateChannel.Preview;
        public string? Blocked;
        public string? BlockReason() => Blocked;
        public TaskCompletionSource? CheckGate, HandoffGate;
        public bool FailCheck, FailHandoff;
        public List<HostUpdateChannel> Queries = new();
        public int Handoffs;
        public (HostUpdateChannel From, HostUpdateChannel To, bool Confirmed) LastHandoff;
        public ReleasePackage Item = new(new("host", "KotorinToys", "0.1.1", "KotorinToys", "KotorinToys-0.1.1-setup.exe", new string('a', 64), "unsigned-public-build"), 4);
        public Task<HostUpdateChannel> DetectChannel(CancellationToken cancellation) => Task.FromResult(CurrentChannel);
        public async Task<PublicRelease> Check(HostUpdateChannel channel, CancellationToken cancellation)
        {
            Queries.Add(channel);
            if (CheckGate != null) await CheckGate.Task.WaitAsync(cancellation);
            if (FailCheck) throw new IOException("オフライン試験");
            return new(Item, true, channel == HostUpdateChannel.Preview, "fixture");
        }
        public async Task Handoff(ReleasePackage release, HostUpdateChannel from, HostUpdateChannel to, bool confirmed, IProgress<string> progress, CancellationToken cancellation)
        {
            Handoffs++; LastHandoff = (from, to, confirmed); progress.Report("fixtureの準備中");
            if (HandoffGate != null) await HandoffGate.Task.WaitAsync(cancellation);
            if (FailHandoff) throw new IOException("引継ぎ失敗試験");
        }
        public SelfUpdateResult? Result;
        public SelfUpdateResult? LastResult() => Result;
    }
    static T Control<T>(Control form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();
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
        var switching = Control<CheckBox>(form, "switchHostChannel");
        var channel = Control<ComboBox>(form, "hostTargetChannel");
        check(form.Text == WindowTitle.Create("本体アップデート") && form.Icon != null, "本体更新画面は共通タイトルと04アイコンを使用");
        check(!switching.Checked && !install.Enabled && !channel.Enabled, "未確認時は更新できず種類切替は無効");
        Pump(form.RefreshRelease());
        check(service.Queries.Last() == HostUpdateChannel.Preview && Control<Label>(form, "hostCurrentVersion").Text.Contains("0.1.0（開発版）") &&
            Control<Label>(form, "hostAvailableVersion").Text.Contains("0.1.1（公開日不明）（開発版）") && install.Enabled,
            "開発版は開発版のみ自動確認しチェックなしで更新可能");
        service.Item = service.Item with { PublishedAt = DateTimeOffset.Parse("2026-10-16T18:00:00Z") };
        Pump(form.RefreshRelease());
        check(Control<Label>(form, "hostAvailableVersion").Text.Contains("0.1.1（2026/10/17 公開）"), "本体更新にも日本時間の公開日を表示");
        service.Blocked = "開発実行です。"; Pump(form.RefreshRelease());
        check(!install.Enabled && Control<Label>(form, "hostUpdateBlockedReason").Text == service.Blocked, "正式導入でない理由を表示して更新を抑止");
        service.Blocked = null;
        foreach (var version in new[] { "0.1.0", "0.0.9" })
        {
            service.Item = service.Item with { Package = service.Item.Package with { Version = version, File = $"KotorinToys-{version}-setup.exe" } };
            Pump(form.RefreshRelease()); check(!install.Enabled, "同じ種類の同版・旧版は更新不可：" + version);
        }
        service.Item = service.Item with { Package = service.Item.Package with { Version = "0.1.1", File = "KotorinToys-0.1.1-setup.exe" } };
        service.FailCheck = true; Pump(form.RefreshRelease());
        check(!install.Enabled && Control<Label>(form, "hostAvailableVersion").Text == "公開版：未確認", "確認失敗時は古い候補を使わない");
        service.FailCheck = false; service.CheckGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = form.RefreshRelease(); var requests = service.Queries.Count;
        Pump(form.RefreshRelease()); Pump(form.InstallUpdate());
        check(service.Queries.Count == requests && service.Handoffs == 0 && !switching.Enabled && !install.Enabled && Control<Button>(form, "cancelHostUpdate").Enabled,
            "確認中の二重操作を抑止し取消可能");
        form.CancelOperation(); Pump(pending);
        check(Control<Label>(form, "hostUpdateStatus").Text.Contains("取り消") && switching.Enabled && !install.Enabled, "確認取消後に操作を復旧");
        service.CheckGate = null; Pump(form.RefreshRelease());
        service.FailHandoff = true; Pump(form.InstallUpdate());
        check(Control<Label>(form, "hostUpdateStatus").Text.Contains("開始できません") && !install.Enabled && switching.Enabled, "引継ぎ失敗時は本体を閉じない");
        service.FailHandoff = false; service.HandoffGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Pump(form.RefreshRelease()); pending = form.InstallUpdate(); var count = service.Handoffs; Pump(form.InstallUpdate());
        check(service.Handoffs == count && service.LastHandoff == (HostUpdateChannel.Preview, HostUpdateChannel.Preview, false) && !install.Enabled &&
            Control<Button>(form, "cancelHostUpdate").Enabled, "開発版の更新はワンクリックで引継ぎ二重実行を抑止");
        service.HandoffGate.SetResult(); Pump(pending);
        check(Control<Label>(form, "hostUpdateStatus").Text.Contains("再起動") && !Control<Button>(form, "cancelHostUpdate").Enabled && !switching.Enabled, "引継ぎ後は強制取消できない");
        var stable = new Fake { CurrentChannel = HostUpdateChannel.Stable };
        using var stableForm = new HostUpdateForm(stable); stableForm.CreateControl(); Pump(stableForm.RefreshRelease());
        check(stable.Queries.Last() == HostUpdateChannel.Stable && Control<Button>(stableForm, "installHostUpdate").Enabled, "通常版は通常版のみ自動確認しワンクリック更新可能");
        var older = new Fake { CurrentVersion = "0.2.0", CurrentChannel = HostUpdateChannel.Preview };
        bool confirm = false; string prompt = "";
        using var switchForm = new HostUpdateForm(older, message => { prompt = message; return confirm; }); switchForm.CreateControl(); Pump(switchForm.RefreshRelease());
        Control<CheckBox>(switchForm, "switchHostChannel").Checked = true; Application.DoEvents();
        check(older.Queries.Last() == HostUpdateChannel.Stable && Control<Button>(switchForm, "installHostUpdate").Enabled, "種類切替を選ぶと反対側の公開版を確認");
        Pump(switchForm.InstallUpdate());
        check(older.Handoffs == 0 && prompt.Contains("古い版") && prompt.Contains("0.2.0") && prompt.Contains("0.1.1"), "古い通常版への切替は注意と確認が必須");
        confirm = true; Pump(switchForm.InstallUpdate());
        check(older.LastHandoff == (HostUpdateChannel.Preview, HostUpdateChannel.Stable, true), "古い通常版は明示確認後のみ引き継ぐ");
        var unknown = new Fake { CurrentChannel = HostUpdateChannel.Unknown };
        using var unknownForm = new HostUpdateForm(unknown, _ => false); unknownForm.CreateControl(); Pump(unknownForm.RefreshRelease());
        check(unknown.Queries.Count == 0 && !Control<Button>(unknownForm, "installHostUpdate").Enabled && Control<Label>(unknownForm, "hostUpdateStatus").Text.Contains("判定できません"),
            "種類が判定不能な版は推測せず明示選択を求める");
        Control<CheckBox>(unknownForm, "switchHostChannel").Checked = true; Application.DoEvents(); Pump(unknownForm.InstallUpdate());
        check(unknown.Queries.Last() == HostUpdateChannel.Stable && unknown.Handoffs == 0, "種類不明からの導入も確認を取り消せる");
        using var closing = new HostUpdateForm(new Fake { CheckGate = new(TaskCreationOptions.RunContinuationsAsynchronously) });
        pending = closing.RefreshRelease(); closing.Dispose(); Pump(pending); check(closing.IsDisposed, "画面破棄時は確認を取り消す");
        int opened = 0;
        using var dashboard = new Dashboard(root, selfUpdates: new Fake(), showSelfUpdater: () => opened++);
        dashboard.CreateControl(); dashboard.PerformLayout();
        check(Control<Button>(dashboard, "hostUpdate").Parent?.Name == "hostActions" && !dashboard.Controls.Find("selfUpdateStatus", true).Any(), "本体更新ボタンは全体操作行へ配置");
        dashboard.OpenSelfUpdate(); check(opened == 1, "本体ボタンから更新画面を開く");
        var resultService = new Fake { Result = new(1, Guid.NewGuid().ToString("N"), root, "0.1.1", "0.1.2", "失敗", "アクセス拒否の試験", false, 7, DateTimeOffset.UtcNow) };
        using var history = new HostUpdateForm(resultService); history.CreateControl(); history.PerformLayout();
        check(Control<Label>(history, "hostLastUpdate").Text.Contains("0.1.1 → 0.1.2 / 失敗") && Control<Label>(history, "hostLastUpdate").Text.Contains("アクセス拒否の試験"), "前回更新の対象版・結果・理由を保持");
        history.Size = history.MinimumSize; history.PerformLayout();
        var reasonLabel = Control<Label>(history, "hostUpdateBlockedReason");
        resultService.Blocked = "更新できません：正式導入先とは別のEXEです。\n実行中：C:\\develop\\source\\bin\\debug\\KotorinToys.exe\n正式な実行ファイル：C:\\Programs\\KotorinToys\\KotorinToys.exe\n対処：正式導入版を起動してください。";
        Pump(history.RefreshRelease()); history.PerformLayout();
        check(reasonLabel.Text == resultService.Blocked && reasonLabel.MaximumSize.Width <= reasonLabel.Parent!.ClientSize.Width - reasonLabel.Parent.Padding.Horizontal &&
            Control<Button>(history, "installHostUpdate").Bottom <= history.ClientSize.Height, "最小サイズでも更新不可の理由とボタンを確認可能");
    }
}
