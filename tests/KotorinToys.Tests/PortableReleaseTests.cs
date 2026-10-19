using System.Net;
using System.Text.Json;
using KotorinToys;

internal static class PortableReleaseTests
{
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
    }

    public static async Task Run(string root, Action<bool, string> check)
    {
        var tool = ToolCatalog.Load(root).Single(t => t.Id == "shortcut-key-counter");
        const string file = "ShortcutKeyCounter-v0.1.5-win-x64.zip";
        string hash = new('a', 64);
        string Url(string name) => "https://github.com/KotorinChunChun/ShortcutKeyCounter/releases/download/v0.1.5/" + name;
        object Asset(string name, string? url = null, string? digest = null, long size = 128, string state = "uploaded") =>
            new { name, state, size, browser_download_url = url ?? Url(name), digest };
        object[] Assets() => [Asset(file, digest: "sha256:" + hash), Asset("SHA256SUMS.txt")];
        object Release(object[]? assets = null, bool draft = false, bool prerelease = false) =>
            new { tag_name = "v0.1.5", draft, prerelease, published_at = "2026-10-17T11:10:21Z", assets = assets ?? Assets() };
        string sums = hash + "  " + file + "\n";
        int requests = 0;
        PublicReleaseClient Make(object[] list, string? text = null) => new(new Handler(request =>
        {
            requests++;
            if (request.RequestUri!.Host == "api.github.com")
            {
                check(request.RequestUri.AbsolutePath == "/repos/KotorinChunChun/ShortcutKeyCounter/releases", "ポータブル公開確認も固定製品のAPIだけを使用");
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(list)) };
            }
            check(request.RequestUri.AbsoluteUri == Url("SHA256SUMS.txt"), "公開表示ではZIPを取得せず固定ハッシュ台帳だけを読む");
            return new(HttpStatusCode.OK) { Content = new StringContent(text ?? sums) };
        }));

        PublicRelease publication;
        using (var client = Make([Release()])) publication = await client.Check(tool, null);
        check(requests == 2 && publication.Confirmed && publication.Package == null && publication.Portable?.Version == "0.1.5",
            "未公開Innoのまま公開ZIPを検出しインストーラー候補へ混入させない");
        var view = ToolVersionView.Create(new("未導入", null, null, null), publication);
        check(view.Published.Contains("0.1.5（2026/10/17 公開）") && view.Published.Contains("ポータブル版") &&
            view.Update.Contains("未対応") && !view.CanUpdate, "ZIP公開版・日本時間の日付・導入未対応を別表示");
        check(!ToolVersionView.Create(new("導入済み", "0.1.4", null, null), publication).CanUpdate,
            "ポータブル公開版で既存導入版の自動更新を許可しない");
        check(ToolInstallationRunner.PublicReason(tool, null) != null, "ZIP公開確認後もインストーラー導入を許可しない");
        using (var client = Make([Release(prerelease: true)]))
            check(ToolVersionView.Create(new("未導入", null, null, null), await client.Check(tool, null)).Published.Contains("開発版"), "ZIPの開発版も区別して表示");
        using (var client = Make([Release([Asset(file), Asset("SHA256SUMS.txt")])], "\uFEFF" + sums))
            check((await client.Check(tool, null)).Confirmed, "GitHub digest未提供でもBOM付きSHA256SUMSの固定版を照合");

        async Task Reject(object[] list, string label, string? text = null)
        {
            using var client = Make(list, text);
            var result = await client.Check(tool, null);
            check(!result.Confirmed && result.Portable == null && result.Package == null, label);
        }
        await Reject([Release(draft: true)], "ZIPの下書きを公開済みにしない");
        await Reject([], "ZIPの空一覧を公開済みにしない");
        await Reject([Release([Asset("SHA256SUMS.txt")])], "ZIP欠落を公開済みにしない");
        await Reject([Release([Asset(file)])], "ハッシュ台帳欠落を公開済みにしない");
        await Reject([Release([Asset(file), Asset(file), Asset("SHA256SUMS.txt")])], "ZIPの重複assetを拒否");
        await Reject([Release([Asset(file, "https://example.com/" + file), Asset("SHA256SUMS.txt")])], "ZIPの別所有者URLを拒否");
        await Reject([Release([Asset(file), Asset("SHA256SUMS.txt", "https://example.com/SHA256SUMS.txt")])], "ハッシュ台帳の別所有者URLを取得前に拒否");
        await Reject([Release([Asset(file, size: 0), Asset("SHA256SUMS.txt")])], "空ZIPを拒否");
        await Reject([Release([Asset(file, size: 512L * 1024 * 1024 + 1), Asset("SHA256SUMS.txt")])], "ZIPの容量上限を検査");
        await Reject([Release([Asset(file, state: "new"), Asset("SHA256SUMS.txt")])], "準備未完了のZIPを拒否");
        await Reject([Release([Asset(file), Asset("SHA256SUMS.txt", size: 32769)])], "ハッシュ台帳の容量上限を取得前に検査");
        await Reject([Release()], "SHA256SUMSの対象ファイル不一致を拒否", hash + "  other.zip\n");
        await Reject([Release()], "SHA256SUMSの重複行を拒否", sums + sums);
        await Reject([Release()], "SHA256SUMSの不正ハッシュを拒否", "not-a-hash  " + file);
        await Reject([Release([Asset(file, digest: "sha256:" + new string('b', 64)), Asset("SHA256SUMS.txt")])], "GitHub digestとZIP台帳の不一致を拒否");
        using (var client = new PublicReleaseClient(new Handler(_ => new(HttpStatusCode.Forbidden))))
            check(!(await client.Check(tool, null)).Confirmed, "ZIP照合のAPI失敗を公開済みにしない");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using (var client = Make([Release()]))
        {
            bool rejected = false;
            try { await client.Check(tool, null, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
            check(rejected, "ZIP公開確認でも利用者の取消を維持");
        }
    }

    public static void Form(string root, Action<bool, string> check)
    {
        var previousContext = SynchronizationContext.Current;
        try { FormCore(root, check); }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    static void FormCore(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root);
        var releases = tools.Select(_ => new PublicRelease(null, false, false, "未確認")).ToArray();
        int index = Array.FindIndex(tools, t => t.Id == "shortcut-key-counter");
        releases[index] = new(null, true, false, "ZIP公開済み・本体導入未対応",
            new("0.1.5", DateTimeOffset.Parse("2026-10-17T11:10:21Z"), "https://github.com/KotorinChunChun/ShortcutKeyCounter", 128, new string('a', 64)));
        using var form = new Dashboard(root); form.CreateControl();
        form.ApplySnapshot(tools.Select(_ => new InstallationState("未導入", null, null, null)).ToArray(),
            tools.Select(_ => new ToolState("停止中")).ToArray(), releases);
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        grid.CurrentCell = grid.Rows[index].Cells[0];
        check(grid.Rows[index].Cells["published"].Value?.ToString() == "0.1.5" && grid.Rows[index].Cells["published"].ToolTipText.Contains("2026/10/17 公開"),
            "実フォームへポータブル公開版と公開日を掲載");
        check(!form.Controls.Find("installSelected", true).Single().Enabled && !form.Controls.Find("updateSelected", true).Single().Enabled,
            "実フォームでもZIP掲載と導入ボタンの有効条件を分離");
        check(grid.Rows[index].Cells["published"].ToolTipText.Contains("ZIP公開済み・本体導入未対応"),
            "公開確認済みZIPの利用経路を公開版セルの説明に保持");
    }

    // API回数制限中も、CLIで取得した公開API応答を実際の匿名asset取得と照合できる。
    sealed class RecordedListHandler(string json) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.AbsoluteUri == "https://api.github.com/repos/KotorinChunChun/ShortcutKeyCounter/releases?per_page=100")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
            return base.SendAsync(request, token);
        }
    }

    public static PublicReleaseClient LiveClient(string? releaseListPath = null) => releaseListPath == null
        ? new() : new(new RecordedListHandler(File.ReadAllText(releaseListPath)));

    public static async Task<int> Live(string root, string? releaseListPath = null)
    {
        var tool = ToolCatalog.Load(root).Single(t => t.Id == "shortcut-key-counter");
        using var client = LiveClient(releaseListPath);
        var result = await client.Check(tool, null);
        Console.WriteLine(JsonSerializer.Serialize(result, ToolCatalog.Json));
        return result.Confirmed && result.Package == null && result.Portable != null ? 0 : 1;
    }
}
