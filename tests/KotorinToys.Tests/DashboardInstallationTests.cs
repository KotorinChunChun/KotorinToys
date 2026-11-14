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
        var batches = new List<string[]>();
        TaskCompletionSource? gate = null;
        async Task<PublicRelease> Fetch(ToolDefinition tool, ReleasePackage? fallback, CancellationToken token)
        {
            requests++; if (gate != null) await gate.Task.WaitAsync(token);
            return new(fallback, fallback != null, false, "fixture");
        }
        using var form = new Dashboard(root, releaseCheck: Fetch, detectInstallation: t => actual[t.Id],
            showToolInstaller: (id, operation, releases) => calls.Add((id, operation, releases)),
            runUpdates: (targets, releases, _, _) =>
            {
                calls.Add((targets.Single().Id, "update", releases));
                return Task.FromResult<InstallResult[]>([new(targets.Single().Id, "update", "成功", "検証済み")]);
            },
            showBatchInstaller: (ids, _) => batches.Add(ids),
            showInstaller: (_, _) => advanced++, notify: notices.Add, fileExists: _ => true,
            executionCheck: (_, _) => Task.FromResult(new ToolState("停止中")));
        form.CreateControl(); form.PerformLayout();
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        Pump(form.RefreshAll()); grid.CurrentCell = grid.Rows[0].Cells[0];
        check(form.Controls.Find("installSelected", true).Single().Enabled && !form.Controls.Find("updateSelected", true).Single().Enabled,
            "メインで未導入の公開製品はインストールだけ可能");
        grid.Rows[1].Selected = true;
        Pump(form.RefreshAll());
        check(grid.SelectedRows.Count == 2 && form.Controls.Find("installSelected", true).Single().Enabled &&
            !form.Controls.Find("start", true).Single().Enabled && !form.Controls.Find("uninstallSelected", true).Single().Enabled,
            "状態更新後も複数選択を保持し、単体操作を無効にする");
        Pump(form.OpenToolInstallation("install"));
        check(batches.Count == 1 && batches[0].SequenceEqual(tools.Take(2).Select(t => t.Id)) && calls.Count == 0 && advanced == 0,
            "一覧のインストール操作は選択中の2件だけをまとめて渡す");
        grid.Rows[1].Selected = false;
        gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = form.OpenToolInstallation("install");
        Pump(form.OpenToolInstallation("install")); grid.CurrentCell = grid.Rows[1].Cells[0];
        gate.SetResult(); Pump(pending); gate = null;
        check(calls.Count == 1 && calls[0].Id == tools[0].Id && calls[0].Operation == "install" &&
            calls[0].Catalog?.Packages.Any(p => p.Package.Id == tools[0].Id) == true && advanced == 0,
            "インストールは通信中の選択変更・二重操作に影響されず元の1件へ進む");
        grid.CurrentCell = grid.Rows[0].Cells[0];
        actual[tools[0].Id] = new("導入済み", "0.0.1", null, "fixture.exe", "test-uninstaller.exe");
        Pump(form.RefreshAll());
        grid.Rows[1].Selected = true;
        check(!form.Controls.Find("installSelected", true).Single().Enabled, "導入済みと未導入の混在時は一括インストールを無効にする");
        Pump(form.OpenToolInstallation("install"));
        check(batches.Count == 1, "混在した選択を暗黙に更新または導入しない");
        grid.Rows[1].Selected = false;
        check(form.Controls.Find("updateSelected", true).Single().Enabled && form.Controls.Find("uninstallSelected", true).Single().Enabled,
            "導入済み・新公開版ありでアップデートと削除が有効");
        Pump(form.OpenToolInstallation("update"));
        check(calls.Count == 2 && calls[1].Operation == "update", "メインのアップデートは選択アプリを更新する");
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
        check(calls.Count == 3 && notices.Last().Contains("未公開") && grid.Rows[3].Cells["detail"].ToolTipText.Contains("未公開") == true, "未公開の導入拒否理由は通知と対象行の詳細で確認できる");
        Pump(form.OpenInstallation(false));
        check(advanced == 1 && calls.Count == 3, "詳細設定は単体操作と別の入口へ接続");
        var selected = form.Controls.Find("selectedActions", true).Single();
        var host = form.Controls.Find("hostActions", true).Single();
        check(new[] { "installSelected", "updateSelected", "uninstallSelected", "manual" }.All(name => form.Controls.Find(name, true).Single().Parent == selected) &&
            form.Controls.Find("advancedSettings", true).Single().Parent == host, "単体操作と本体の詳細設定は別行に配置");
        CheckBatchUpdates(root, tools, catalog, check);

        var candidate = tools[0]; var fallback = catalog.Packages.Single(p => p.Package.Id == candidate.Id);
        var previous = new InstallationState("未導入", null, null, null); var operations = new List<string>(); int autoChecks = 0;
        var runningElsewhere = new ToolState("確認できない", 123, "別の配置", RunningExecutable: @"C:\開発\LiveTextMarker.exe");
        using var replace = new Dashboard(root, detectInstallation: _ => previous, executionCheck: (_, _) => Task.FromResult(runningElsewhere),
            releaseCheck: (_, package, _) => { autoChecks++; return Task.FromResult(new PublicRelease(package, package != null, false, "fixture")); },
            showToolInstaller: (_, operation, _) => operations.Add(operation), notify: notices.Add, fileExists: _ => true);
        replace.CreateControl(); var replaceGrid = (DataGridView)replace.Controls.Find("toolTable", true).Single();
        replaceGrid.CurrentCell = replaceGrid.Rows[0].Cells[0];
        check(replace.Controls.Find("installSelected", true).Single().Enabled, "未確認の実行状態と公開版でも導入の自動確認を開始できる");
        Pump(replace.OpenToolInstallation("install"));
        check(autoChecks == tools.Length && operations.SequenceEqual(["install"]), "状態更新ボタンなしで別配置・未導入の公開版を自動確認する");
        previous = new("導入済み", "0.0.1", root, "fixture.exe");
        replace.ApplySnapshot(tools.Select(_ => previous).ToArray(), tools.Select(_ => runningElsewhere).ToArray(),
            tools.Select(_ => new PublicRelease(fallback, false, false, "未確認")).ToArray());
        Pump(replace.OpenToolInstallation("install"));
        check(operations.SequenceEqual(["install", "update"]), "外部から正式導入済みの場合は自動確認後に更新へ切り替える");
        previous = previous with { Version = fallback.Package.Version };
        Pump(replace.OpenToolInstallation("install"));
        check(operations.Count == 2 && notices.Last().Contains("新しい公開版"), "同じ版なら自動確認後も上書き更新しない");
    }

    static void CheckBatchUpdates(string root, ToolDefinition[] tools, ReleaseCatalog catalog, Action<bool, string> check)
    {
        var actual = tools.Select(_ => new InstallationState("未導入", null, null, null)).ToArray();
        actual[0] = new("導入済み", "0.0.1", null, "fixture.exe");
        actual[1] = actual[0] with { Version = catalog.Packages.Single(p => p.Package.Id == tools[1].Id).Package.Version };
        actual[2] = actual[0];
        var batches = new List<string[]>();
        var notices = new List<string>();
        var installerDialogs = 0;
        using var form = new Dashboard(root,
            detectInstallation: tool => actual[Array.FindIndex(tools, t => t.Id == tool.Id)],
            releaseCheck: (_, package, _) => Task.FromResult(new PublicRelease(package, package != null, false, "fixture")),
            executionCheck: (_, _) => Task.FromResult(new ToolState("停止中")), fileExists: _ => true,
            showToolInstaller: (_, _, _) => installerDialogs++, showBatchInstaller: (_, _) => installerDialogs++, notify: notices.Add,
            runUpdates: (targets, _, _, _) =>
            {
                batches.Add(targets.Select(t => t.Id).ToArray());
                return Task.FromResult(targets.Select(t => t.Id == tools[0].Id
                    ? new InstallResult(t.Id, "update", "失敗", "検証用終了コード：7")
                    : new InstallResult(t.Id, "update", "成功", "検証用更新完了")).ToArray());
            });
        form.CreateControl();
        Pump(form.RefreshAll());
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        var update = form.Controls.Find("updateSelected", true).Single();
        var all = form.Controls.Find("updateAll", true).Single();
        var filter = (ComboBox)form.Controls.Find("viewFilter", true).Single();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.Rows[1].Selected = grid.Rows[3].Selected = true;
        check(update.Enabled && all.Enabled, "最新版・未導入が混在しても更新対象があれば操作可能");
        Pump(form.OpenToolInstallation("update"));
        check(batches.Single().SequenceEqual([tools[0].Id]), "選択更新は最新版と未導入を飛ばし、選択外も更新しない");
        filter.SelectedIndex = 3;
        check(!grid.Rows[0].Visible && !grid.Rows[2].Visible, "すべて更新の試験で導入済み行を絞り込みから除外");
        Pump(form.UpdateTools(all: true));
        check(batches.Count == 2 && batches[1].SequenceEqual([tools[0].Id, tools[2].Id]) && installerDialogs == 0 && notices.Count == 0,
            "すべて更新は表示条件に関わらず新しい公開版がある導入済みアプリだけを画面なしで順に処理");
        check(form.Controls.Find("toolUpdateStatus", true).Single().Text.Contains("失敗 1件 / 成功 1件") &&
            grid.Rows[0].Cells["detail"].ToolTipText.Contains("終了コード：7"), "結果件数と個別の失敗理由を一覧に表示");
    }
}
