using KotorinToys;

internal static class DashboardInstallationTests
{
    static void Pump(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("メイン導入操作の試験が完了しません。");
        task.GetAwaiter().GetResult();
    }
    public static void Run(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root); var catalog = ReleaseDownloader.Load(root);
        var actual = tools.ToDictionary(t => t.Id, _ => new InstallationState("未導入", null, null, null));
        int requests = 0, advanced = 0; var notices = new List<string>();
        var calls = new List<(string Id, string Operation, ReleaseCatalog? Catalog)>();
        TaskCompletionSource? gate = null;
        async Task<PublicRelease> Fetch(ToolDefinition tool, ReleasePackage? fallback, CancellationToken token)
        {
            requests++; if (gate != null) await gate.Task.WaitAsync(token);
            return new(fallback, fallback != null, false, "fixture");
        }
        using var form = new Dashboard(root, releaseCheck: Fetch, detectInstallation: t => actual[t.Id],
            showToolInstaller: (id, operation, releases) => calls.Add((id, operation, releases)),
            showInstaller: (_, _) => advanced++, notify: notices.Add);
        form.CreateControl(); form.PerformLayout();
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        Pump(form.RefreshReleases()); grid.CurrentCell = grid.Rows[0].Cells[0];
        check(form.Controls.Find("installSelected", true).Single().Enabled && !form.Controls.Find("updateSelected", true).Single().Enabled,
            "メインで未導入の公開製品はインストールだけ可能");
        gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = form.OpenToolInstallation("install");
        Pump(form.OpenToolInstallation("install")); grid.CurrentCell = grid.Rows[1].Cells[0];
        gate.SetResult(); Pump(pending); gate = null;
        check(calls.Count == 1 && calls[0].Id == tools[0].Id && calls[0].Operation == "install" &&
            calls[0].Catalog?.Packages.Any(p => p.Package.Id == tools[0].Id) == true && advanced == 0,
            "インストールは通信中の選択変更・二重操作に影響されず元の1件へ進む");
        grid.CurrentCell = grid.Rows[0].Cells[0];
        actual[tools[0].Id] = new("導入済み", "0.0.1", null, null, "test-uninstaller.exe");
        Pump(form.RefreshAll());
        check(form.Controls.Find("updateSelected", true).Single().Enabled && form.Controls.Find("uninstallSelected", true).Single().Enabled,
            "導入済み・新公開版ありでアップデートと削除が有効");
        Pump(form.OpenToolInstallation("update"));
        check(calls.Count == 2 && calls[1].Operation == "update", "メインのアップデートは単体操作を指定する");
        int before = requests;
        Pump(form.OpenToolInstallation("uninstall"));
        check(calls.Count == 3 && calls[2].Operation == "uninstall" && requests == before, "削除は公開版取得不要で選択製品の削除へ進む");
        actual[tools[0].Id] = actual[tools[0].Id] with { Uninstaller = null };
        Pump(form.OpenToolInstallation("uninstall"));
        check(calls.Count == 3 && !form.Controls.Find("uninstallSelected", true).Single().Enabled, "削除プログラムが消えた場合は実行前の再検出で中止");
        actual[tools[0].Id] = actual[tools[0].Id] with { Version = catalog.Packages[0].Package.Version };
        Pump(form.RefreshAll()); before = requests;
        Pump(form.OpenToolInstallation("update"));
        check(calls.Count == 3 && requests == before && !form.Controls.Find("updateSelected", true).Single().Enabled,
            "同版の無効ボタン相当の直接呼出しでも更新を開始しない");
        grid.CurrentCell = grid.Rows[3].Cells[0];
        Pump(form.OpenToolInstallation("install"));
        check(calls.Count == 3 && notices.Last().Contains("未公開") && grid.Rows[3].Cells["detail"].Value?.ToString()?.Contains("未公開") == true, "未公開の導入拒否理由は通知と対象行の詳細で確認できる");
        Pump(form.OpenInstallation(false));
        check(advanced == 1 && calls.Count == 3, "詳細設定は単体操作と別の入口へ接続");
        var selected = form.Controls.Find("selectedActions", true).Single();
        var host = form.Controls.Find("hostActions", true).Single();
        check(new[] { "installSelected", "updateSelected", "uninstallSelected", "manual" }.All(name => form.Controls.Find(name, true).Single().Parent == selected) &&
            form.Controls.Find("advancedSettings", true).Single().Parent == host, "単体操作と本体の詳細設定は別行に配置");
    }
}
