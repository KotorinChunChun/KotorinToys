using KotorinToys;

internal static class InstallationFormWorkflowTests
{
    static void Pump(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("単体導入画面の試験が完了しません。");
        task.GetAwaiter().GetResult(); Application.DoEvents();
    }
    public static void Run(string root, Action<bool, string> check)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        try { RunUi(root, check); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    static void RunUi(string root, Action<bool, string> check)
    {
        var catalog = ReleaseDownloader.Load(root);
        var item = catalog.Packages[1] with { PublishedAt = DateTimeOffset.Parse("2026-10-16T18:00:00Z") };
        catalog = new(1, [item]); var manager = new ToolManager(root);
        int executions = 0, confirmations = 0; bool approved = true;
        var gate = new TaskCompletionSource<InstallResult[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        InstallationExecution execute = (selected, operation, releases, local, progress, download, token) =>
        {
            executions++;
            check(selected.Length == 1 && selected[0].Id == item.Package.Id && local == null && releases == catalog && operation == "install", "メインの単体操作は選択した1製品とGitHub台帳だけを渡す");
            return gate.Task;
        };
        bool Confirm(string text)
        { confirmations++; check(text.Contains("2026/10/17 公開") && text.EndsWith("\n\nインストール / アップデートしてもよろしいですか？") && !text.Contains("設定・履歴を保持"), "単体操作の確認は公開日と短い質問にする"); return approved; }
        using var form = new InstallationForm(manager, item.Package.Id, catalog, "update", Confirm, execute,
            detectInstallation: _ => new("導入済み", "0.0.1", root, "test.exe", "unins000.exe"),
            executionState: _ => Task.FromResult(new ToolState("停止中")), fileExists: _ => true);
        form.CreateControl(); form.PerformLayout();
        check(form.Controls.Find("installationSource", true).Single().Text == "GitHub Releasesの検証版をインストールします。", "取得元の案内を指定の短い文にする");
        check(form.Text == WindowTitle.Create("アップデート") && form.Icon != null, "単体アップデートも共通タイトル・04アイコン");
        check(!form.Controls.Find("chooseLocalCatalog", true).Any() && !form.Controls.Find("useGithubReleases", true).Any() &&
            !form.Controls.Find("installFfmpeg", true).Any() && !form.Controls.Find("batchInstall", true).Any(), "単体画面に台帳・取得元切替・FFmpeg・一括操作を出さない");
        var pending = form.RunSelected(); Pump(form.RunSelected());
        check(confirmations == 1 && executions == 1 && !form.Controls.Find("closeInstallation", true).Single().Enabled, "確認後は進捗へ直結し二重実行を拒否");
        gate.SetResult([new(item.Package.Id, "install", "成功", "テスト完了")]); Pump(pending);
        check(form.Controls.Find("installationSummary", true).Single().Text == "すべてのインストールが完了しました。" && form.Controls.Find("closeInstallation", true).Single().Enabled,
            "単体操作の完了を表示し閉じる操作を復旧");
        Pump(form.RunSelected()); check(executions == 1, "完了画面から同じ操作を再実行しない");
        approved = false;
        using var canceled = new InstallationForm(manager, item.Package.Id, catalog, "install", Confirm, execute,
            detectInstallation: _ => new("未導入", null, null, null), executionState: _ => Task.FromResult(new ToolState("停止中")));
        Pump(canceled.RunSelected());
        check(executions == 1 && canceled.Controls.Find("installationSummary", true).Single().Text.Contains("変更していません"), "確認をキャンセルした場合は導入を実行しない");
        using var advanced = new InstallationForm(manager, releases: catalog); advanced.CreateControl(); advanced.PerformLayout();
        check(advanced.Text == WindowTitle.Create("詳細設定（開発者向け）") &&
            new[] { "chooseLocalCatalog", "useGithubReleases", "installFfmpeg", "batchInstall", "batchUninstall", "selectInstallable" }
                .All(name => advanced.Controls.Find(name, true).Length == 1), "一括導入削除・FFmpeg・台帳は開発者向け詳細設定へ集約");
        var target = (CheckedListBox)advanced.Controls.Find("installationTargets", true).Single();
        check(target.Items.Cast<string>().Any(text => text.Contains("未公開")) && target.Items.Cast<string>().Any(text => text.Contains("2026/10/17 公開")), "詳細設定の一覧に未対応理由と公開日を表示");
        advanced.Size = advanced.MinimumSize; advanced.PerformLayout();
        check(advanced.Controls.Find("installationResults", true).Single().Height >= 100, "詳細設定は最小サイズでも進捗結果を表示");
    }
}
