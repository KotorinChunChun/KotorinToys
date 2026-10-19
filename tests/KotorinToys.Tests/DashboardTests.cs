using KotorinToys;

internal static class DashboardTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root);
        var catalog = ReleaseDownloader.Load(root);
        var releases = tools.Select(t => new PublicRelease(catalog.Packages.SingleOrDefault(p => p.Package.Id == t.Id), t.DistributionStatus == "published", true, "テスト")).ToArray();
        var installed = tools.Select(t => new InstallationState("導入済み", t.Version, null, "test.exe", "test-uninstaller.exe")).ToArray();
        var view = ToolVersionView.Create(installed[1] with { Version = "0.1.3" }, releases[1]);
        check(view.CanUpdate && view.Update == "更新あり", "公開版が新しいときだけ更新可能");
        check(ToolVersionView.Create(installed[1], releases[1]).Update == "公開版と同じ", "同一版は更新不要");
        check(!ToolVersionView.Create(installed[1] with { Version = "9.0.0" }, releases[1]).CanUpdate, "公開版へのダウングレードを提案しない");
        check(!ToolVersionView.Create(installed[1], releases[1] with { Confirmed = false }).CanUpdate, "同梱台帳だけで最新や更新可能にしない");
        check(ToolVersionView.Create(installed[1] with { State = "未導入", Version = null }, releases[1]).Installed == "未導入", "開発配置を正式導入版にしない");
        check(!ToolVersionView.Create(installed[1] with { State = "確認できない" }, releases[1]).CanUpdate, "未確認の導入状態を更新対象にしない");
        var states = tools.Select(_ => new ToolState("実行中")).ToArray();
        var notices = new List<string>();
        using var form = new Dashboard(root, notify: notices.Add, fileExists: _ => true); form.CreateControl();
        installed[1] = installed[1] with { Version = "0.1.3" };
        form.ApplySnapshot(installed, states, releases); form.PerformLayout();
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        check(grid.Rows.Count == tools.Length && grid.Rows.Cast<DataGridViewRow>().Select(row => row.Cells["summary"].Value?.ToString()).SequenceEqual(tools.Select(t => t.Summary)), "実フォームの表に9件の一言解説");
        var expectedPublished = releases[1].Package!.Package.Version;
        check(grid.Rows[1].Cells["installed"].Value?.ToString() == "0.1.3" && grid.Rows[1].Cells["published"].Value?.ToString() == expectedPublished, "実フォームに導入済み版と現在の公開台帳の版を別表示");
        grid.CurrentCell = grid.Rows[1].Cells[0];
        check(((Button)form.Controls.Find("updateSelected", true).Single()).Enabled, "更新あり行を選択すると更新導線が有効");
        grid.CurrentCell = grid.Rows[2].Cells[0];
        check(grid.CurrentRow?.Cells["tool"].Value?.ToString() == "SSCapture" && grid.Rows.Cast<DataGridViewRow>().All(row => row.Cells["state"].Value?.ToString() == "実行中"), "選択変更後も状態は表の各行で確認できる");
        check(new[] { "overallStatus", "selectedStatus", "lastOperation", "catalogStatus", "selfUpdateStatus" }
            .All(name => form.Controls.Find(name, true).Length == 0), "メイン下部の状態・説明ラベルを生成しない");
        check(grid.Parent is TableLayoutPanel layout && layout.RowCount == 3 && layout.Controls.Count == 3, "メインは一覧と2つの操作行だけでフッター領域を残さない");
        check(!((Button)form.Controls.Find("updateSelected", true).Single()).Enabled, "同一版はアップデートを無効にする");
        check(((Button)form.Controls.Find("uninstallSelected", true).Single()).Enabled &&
            !((Button)form.Controls.Find("installSelected", true).Single()).Enabled, "導入済みはインストールを無効・アンインストールを有効にする");
        states[2] = new("停止中"); form.ApplySnapshot(installed, states, releases);
        check(!form.Controls.Find("stop", true).Single().Enabled, "停止中のツールは停止ボタンを無効にする");
        states[2] = new("実行中"); form.ApplySnapshot(installed, states, releases);
        check(form.Controls.Find("stop", true).Single().Enabled, "実行中へ変化すると停止ボタンを復旧する");
        form.ReportExecution(tools[2], false, new("実行中", Detail: "録画中のため停止できません。"));
        form.ApplySnapshot(installed, states, releases);
        check(notices.Single().Contains("録画中のため停止できません") && grid.Rows[2].Cells["detail"].ToolTipText.Contains("録画中のため停止できません") == true,
            "実行中のまま返る正常終了拒否も通知し状態再取得後に理由を消さない");
        form.ReportExecution(tools[2], true, new("実行中"));
        check(notices.Count == 1, "正常な起動成功は通知しない");
        installed[2] = new("未導入", null, null, null); states[2] = new("停止中"); form.ApplySnapshot(installed, states, releases);
        check(form.Controls.Find("installSelected", true).Single().Enabled && !form.Controls.Find("uninstallSelected", true).Single().Enabled &&
            !form.Controls.Find("updateSelected", true).Single().Enabled, "未導入で公開配布照合済みの場合だけインストールを有効にする");
        form.ApplySnapshot(installed, states, releases.Select(r => r with { Confirmed = false }).ToArray());
        check(!form.Controls.Find("installSelected", true).Single().Enabled, "未確認の同梱版でインストールを許可しない");
        form.ApplySnapshot(installed, states, releases);
        grid.CurrentCell = grid.Rows[3].Cells[0];
        check(grid.CurrentRow?.Cells["tool"].Value?.ToString() == "ShortcutKeyCounter" &&
            !((Button)form.Controls.Find("updateSelected", true).Single()).Enabled, "追加ツールを選択可能で未公開インストーラーの更新は無効");
        var httpSelectorIndex = Array.FindIndex(tools, tool => tool.Id == "http-selector");
        grid.CurrentCell = grid.Rows[httpSelectorIndex].Cells[0];
        check(grid.Rows[httpSelectorIndex].Cells["installed"].Value?.ToString() == tools[httpSelectorIndex].Version &&
            grid.CurrentRow?.Cells["tool"].Value?.ToString() == "HttpSelector" &&
            ((Button)form.Controls.Find("updateSelected", true).Single()).Enabled == ToolVersionView.Create(installed[httpSelectorIndex], releases[httpSelectorIndex]).CanUpdate,
            "HttpSelectorの4成分版を表示し照合済みMSIXの新旧で更新を制御");
        check(grid.Columns.Cast<DataGridViewColumn>().All(column => column.SortMode == DataGridViewColumnSortMode.NotSortable), "行順で別製品を操作しないよう手動並替を禁止");
        form.Size = form.MinimumSize; form.PerformLayout();
        check(grid.Width >= 800 && grid.Height >= 320, "最小サイズでも下部の空白を作らず一覧を広げる");
        form.Dispose(); // 二度目のDisposeでもタイマー・取消資源が例外にならない。
        check(form.IsDisposed, "フォーム終了のタイマー・取消資源を安全に解放");
    }
}
