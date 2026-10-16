using KotorinToys;

internal static class DashboardTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root);
        var catalog = ReleaseDownloader.Load(root);
        var releases = tools.Select(t => new PublicRelease(catalog.Packages.SingleOrDefault(p => p.Package.Id == t.Id), t.DistributionStatus == "published", true, "テスト")).ToArray();
        var installed = tools.Select(t => new InstallationState("導入済み", t.Version, null, null)).ToArray();
        var view = ToolVersionView.Create(installed[1] with { Version = "0.1.3" }, releases[1]);
        check(view.CanUpdate && view.Update == "更新あり", "公開版が新しいときだけ更新可能");
        check(ToolVersionView.Create(installed[1], releases[1]).Update == "公開版と同じ", "同一版は更新不要");
        check(!ToolVersionView.Create(installed[1] with { Version = "9.0.0" }, releases[1]).CanUpdate, "公開版へのダウングレードを提案しない");
        check(!ToolVersionView.Create(installed[1], releases[1] with { Confirmed = false }).CanUpdate, "同梱台帳だけで最新や更新可能にしない");
        check(ToolVersionView.Create(installed[1] with { State = "未導入", Version = null }, releases[1]).Installed == "未導入", "開発配置を正式導入版にしない");
        check(!ToolVersionView.Create(installed[1] with { State = "確認できない" }, releases[1]).CanUpdate, "未確認の導入状態を更新対象にしない");
        var states = tools.Select(_ => new ToolState("実行中")).ToArray();
        check(DashboardStatus.Summary(states) == "全体：実行中 9件", "下部は最後の1件ではなく全体9件を集計");
        check(DashboardStatus.Summary([new("実行中"), new("停止中"), new("確認できない")]).Contains("確認できない 1件"), "全体集計に停止・未確認も含める");
        using var form = new Dashboard(root); form.CreateControl();
        installed[1] = installed[1] with { Version = "0.1.3" };
        form.ApplySnapshot(installed, states, releases); form.PerformLayout();
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        check(grid.Rows.Count == 9 && grid.Rows.Cast<DataGridViewRow>().Select(row => row.Cells["summary"].Value?.ToString()).SequenceEqual(tools.Select(t => t.Summary)), "実フォームの表に9件の一言解説");
        var expectedPublished = releases[1].Package!.Package.Version + "（開発版）";
        check(grid.Rows[1].Cells["installed"].Value?.ToString() == "0.1.3" && grid.Rows[1].Cells["published"].Value?.ToString() == expectedPublished, "実フォームに導入済み版と現在の公開台帳の版を別表示");
        grid.CurrentCell = grid.Rows[1].Cells[0];
        check(((Button)form.Controls.Find("updateSelected", true).Single()).Enabled, "更新あり行を選択すると更新導線が有効");
        grid.CurrentCell = grid.Rows[2].Cells[0];
        check(((Label)form.Controls.Find("selectedStatus", true).Single()).Text.Contains("SSCapture") &&
            ((Label)form.Controls.Find("overallStatus", true).Single()).Text.Contains("実行中 9件"), "選択変更で選択情報だけを切替・全体9件を維持");
        check(((Button)form.Controls.Find("updateSelected", true).Single()).Enabled, "同一版でも更新ボタンから最新情報を再確認できる");
        grid.CurrentCell = grid.Rows[3].Cells[0];
        check(((Label)form.Controls.Find("selectedStatus", true).Single()).Text.Contains("ShortcutKeyCounter") &&
            !((Button)form.Controls.Find("updateSelected", true).Single()).Enabled, "追加ツールを選択可能で未公開インストーラーの更新は無効");
        var httpSelectorIndex = Array.FindIndex(tools, tool => tool.Id == "http-selector");
        grid.CurrentCell = grid.Rows[httpSelectorIndex].Cells[0];
        check(grid.Rows[httpSelectorIndex].Cells["installed"].Value?.ToString() == tools[httpSelectorIndex].Version &&
            ((Label)form.Controls.Find("selectedStatus", true).Single()).Text.Contains("HttpSelector") &&
            !((Button)form.Controls.Find("updateSelected", true).Single()).Enabled, "HttpSelectorの4成分版を表示し未公開Innoへの更新を無効化");
        check(grid.Columns.Cast<DataGridViewColumn>().All(column => column.SortMode == DataGridViewColumnSortMode.NotSortable), "行順で別製品を操作しないよう手動並替を禁止");
        form.Size = form.MinimumSize; form.PerformLayout();
        check(grid.Width >= 800 && grid.Height >= 180, "最小サイズで表と下部表示の領域を確保");
        form.Dispose(); // 二度目のDisposeでもタイマー・取消資源が例外にならない。
        check(form.IsDisposed, "フォーム終了のタイマー・取消資源を安全に解放");
    }
}
