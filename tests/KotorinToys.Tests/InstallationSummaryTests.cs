using KotorinToys;

internal static class InstallationSummaryTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        // 公開インストーラー3件を選ぶケース。管理対象全件とは別に扱う。
        var tools = ToolCatalog.Load(root).Where(t => t.DistributionStatus == "published").Take(3).ToArray();
        InstallResult[] Results(params string[] states) => states.Select((state, i) => new InstallResult(tools[i].Id, "install", state, "テスト結果")).ToArray();
        var summary = InstallationSummary.Create(tools, "install", Results("成功", "成功", "成功"));
        check(summary.AllSucceeded && summary.Title == "すべてのインストールが完了しました。" && summary.Detail == "", "全件成功は指定の短い完了文だけにする");
        check(summary.Rows.Select(r => r.Name).SequenceEqual(tools.Select(t => t.Name)), "内部IDではなく正式製品名を表示");
        summary = InstallationSummary.Create(tools, "install", Results("成功", "失敗", "未実行"));
        check(!summary.AllSucceeded && summary.Detail.Contains("成功 1件 / 失敗 1件") && summary.Detail.Contains("未実行 1件"), "部分失敗とキャンセルを全件成功にしない");
        summary = InstallationSummary.Create(tools, "install", Results("再起動待ち", "要確認", "成功"));
        check(!summary.AllSucceeded && summary.Detail.Contains("PCの再起動が必要") && summary.Detail.Contains("要確認 1件"), "再起動と要確認を区別");
        summary = InstallationSummary.Create(tools, "install", [], "ダウンロード失敗");
        check(summary.Rows.All(r => r.State == "未実行" && r.Detail == "ダウンロード失敗"), "取得失敗の理由を対象ごとに表示");
        check(!InstallationSummary.Create([tools[0]], "install", Results("処理中")).AllSucceeded, "途中状態は最終成功でない");
        check(!InstallationSummary.Create([], "install", []).AllSucceeded, "対象0件は全件成功でない");
        check(InstallationSummary.Create([tools[0]], "uninstall", [new(tools[0].Id, "uninstall", "成功", "削除済み")]).Title.Contains("アンインストールが完了"), "削除と導入の完了表示を区別");
        using var form = new InstallationForm(new(root));
        form.CreateControl(); form.ShowSummary(InstallationSummary.Create(tools, "install", Results("成功", "成功", "成功"))); form.PerformLayout();
        var grid = (DataGridView)form.Controls.Find("installationResults", true).Single();
        var label = (Label)form.Controls.Find("installationSummary", true).Single();
        check(grid.Rows.Count == 3 && label.Text == "すべてのインストールが完了しました。", "実フォームに完了一覧と余分な改行のない完了文を反映");
        check(((Button)form.Controls.Find("closeInstallation", true).Single()).Enabled &&
            !((Button)form.Controls.Find("cancelRemaining", true).Single()).Enabled, "完了時は閉じる有効・キャンセル無効");
        check(grid.ColumnHeadersDefaultCellStyle.Padding.Vertical >= 16, "結果ヘッダーの上下余白");
        form.Size = form.MinimumSize; form.PerformLayout();
        check(grid.Width >= 450 && grid.Height >= 100, "最小サイズでも完了一覧の表示領域を確保");
    }
}
