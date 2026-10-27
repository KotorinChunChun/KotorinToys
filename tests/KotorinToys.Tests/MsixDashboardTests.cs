using KotorinToys;

internal static class MsixDashboardTests
{
    static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("MSIX一覧の試験が完了しません。");
        task.GetAwaiter().GetResult();
    }
    public static void Run(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root); int i = Array.FindIndex(tools, t => t.Id == "http-selector");
        var tool = tools[i];
        var package = new LocalPackage(tool.Id, tool.Name, "0.0.3.3", tool.AppId, MsixPolicy.FileName("0.0.3.3"), new string('a', 64), "signed-msix", "msix");
        var published = new ReleasePackage(package, 100, DateTimeOffset.Parse("2026-10-16T18:10:50Z"));
        var releases = tools.Select(_ => new PublicRelease(null, false, false, "fixture")).ToArray(); releases[i] = new(published, true, false, "MSIX確認済み");
        var actual = tools.Select(_ => new InstallationState("未導入", null, null, null)).ToArray();
        var states = tools.Select(_ => new ToolState("停止中")).ToArray();
        var calls = new List<string>(); var notices = new List<string>();
        using var form = new Dashboard(root, releaseCheck: (t, _, _) => Task.FromResult(releases[Array.FindIndex(tools, tool => tool.Id == t.Id)]),
            detectInstallation: t => actual[Array.FindIndex(tools, tool => tool.Id == t.Id)],
            showToolInstaller: (id, operation, catalog) => calls.Add(id + ":" + operation + ":" + catalog?.Packages.Single().Package.Kind), notify: notices.Add, fileExists: _ => true);
        form.CreateControl(); form.ApplySnapshot(actual, states, releases); form.PerformLayout();
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single(); grid.CurrentCell = grid.Rows[i].Cells[0];
        bool Enabled(string name) => form.Controls.Find(name, true).Single().Enabled;
        check(Enabled("installSelected") && !Enabled("updateSelected") && !Enabled("uninstallSelected"), "未導入MSIXではインストールだけ有効");
        check(!grid.Columns.Contains("published") && grid.Rows[i].Cells["update"].Value?.ToString() == "-" && grid.Rows[i].Cells["update"].ToolTipText.Contains("2026/10/17 公開"), "未導入MSIXはハイフン、4成分公開版の日付はツールチップへ分離");
        Pump(form.OpenToolInstallation("install"));
        check(calls.SequenceEqual(["http-selector:install:msix"]), "MSIX導入ボタンは選択対象1件の専用配布種別を渡す");
        actual[i] = new("更新が必要", "0.0.3.1", "fixture", "fixture.exe"); form.ApplySnapshot(actual, states, releases);
        check(Enabled("installSelected") && Enabled("updateSelected") && Enabled("uninstallSelected"), "旧MSIXは自動確認の入口と更新・削除が有効");
        check(grid.Rows[i].Cells["installed"].Value?.ToString() == "0.0.3.1", "更新が必要でもMSIXの導入済み4成分版を隠さない");
        Pump(form.OpenToolInstallation("update")); Pump(form.OpenToolInstallation("uninstall"));
        check(calls.Skip(1).SequenceEqual(["http-selector:update:msix", "http-selector:uninstall:msix"]), "MSIXの更新・削除をメイン一覧から呼び出せる");
        actual[i] = actual[i] with { State = "導入済み", Version = "0.0.3.3" }; form.ApplySnapshot(actual, states, releases);
        check(!Enabled("updateSelected") && Enabled("uninstallSelected"), "最新版MSIXでは更新を無効、削除を有効にする");
        actual[i] = actual[i] with { State = "確認できない" }; form.ApplySnapshot(actual, states, releases);
        check(Enabled("installSelected") && !Enabled("updateSelected") && !Enabled("uninstallSelected"), "登録確認不能のMSIXも事前確認へ進め、直接更新・削除は無効にする");
        actual[i] = actual[i] with { State = "導入済み", Version = "0.0.3.1" }; releases[i] = releases[i] with { Confirmed = false, Detail = "MSIX公開確認失敗：証拠fixture" };
        form.ApplySnapshot(actual, states, releases);
        check(!Enabled("updateSelected") && Enabled("uninstallSelected") && grid.Rows[i].Cells["detail"].ToolTipText.Contains("MSIX公開確認失敗") == true,
            "公開確認失敗でも正式MSIX削除は可能、更新不可の原因は詳細へ表示");
        check(notices.Count == 0, "正常なMSIX一覧・画面接続の操作はエラー通知なし");
    }
}
