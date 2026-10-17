using System.Text.Json;
using KotorinToys;

internal static class ReleaseDisplayTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var catalog = ReleaseDownloader.Load(root);
        var item = catalog.Packages[0] with { PublishedAt = DateTimeOffset.Parse("2026-10-16T18:00:00Z") };
        var tool = ToolCatalog.Load(root).Single(t => t.Id == item.Package.Id);
        check(ReleaseDisplay.Version(item) == item.Package.Version + "（2026/10/17 公開）", "UTC日付境界を日本時間へ変換し版の右へ表示");
        check(ReleaseDisplay.Version(item with { PublishedAt = null }).EndsWith("（公開日不明）"), "日付を取得できない場合は現在日付を捏造しない");
        var view = ToolVersionView.Create(new("導入済み", "0.0.1", null, null), new(item, true, true, "fixture"));
        check(view.CanUpdate && view.Published.Contains("2026/10/17 公開"), "日付追加後も数値版の更新比較を維持");
        string text = ReleaseDisplay.Confirmation([tool], "install", _ => new("未導入", null, null, null), new(1, [item]), null);
        check(text == tool.Name + "：未導入 → " + item.Package.Version + "（2026/10/17 公開）\n\nインストール / アップデートしてもよろしいですか？", "導入確認は対象・版・公開日と空行・指定の短い確認文のみ");
        text = ReleaseDisplay.Confirmation([tool], "install", _ => new("未導入", null, null, null), new(1, []), null);
        check(text.Contains("取得可能な公開版なし") && !text.Contains("→ " + tool.Version), "配布がない製品で開発版番号を導入できる版として表示しない");
        check(text.Contains("実行不可："), "短い確認文でも実行不可の原因を隠さない");
        text = ReleaseDisplay.Confirmation([tool], "uninstall", _ => new("導入済み", "1.0.0", null, null), null, null);
        check(text == tool.Name + "：1.0.0 → アンインストール\n\nアンインストールしてもよろしいですか？", "削除の確認も対象と短い質問だけにする");
        foreach (var raw in new[] { "\"2026-10-16T18:00:00Z\"", "null", "\"invalid\"" })
        {
            using var json = JsonDocument.Parse("[{\"tag_name\":\"v1.0.0\",\"draft\":false,\"prerelease\":false,\"published_at\":" + raw + "}]");
            var metadata = PublicReleaseMetadata.Select(json.RootElement);
            check(raw.Contains("2026") ? metadata.PublishedAt == item.PublishedAt : metadata.PublishedAt == null, "GitHub published_atを読取り不正・未設定は不明：" + raw);
        }
    }
}
