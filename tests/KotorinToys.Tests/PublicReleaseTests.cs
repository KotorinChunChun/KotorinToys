using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using KotorinToys;

internal static class PublicReleaseTests
{
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(response(request, token)); } }
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, ToolCatalog.Json)) };
    public static async Task Run(string root, string fixtures, Action<bool, string> check)
    {
        int unsupportedRequests = 0;
        using (var client = new PublicReleaseClient(new Handler((_, _) => { unsupportedRequests++; return new(HttpStatusCode.OK); })))
        {
            foreach (var unpublished in ToolCatalog.Load(root).Where(t => t.DistributionStatus == "unpublished"))
            {
                var actual = await client.Check(unpublished, null);
                check(unsupportedRequests == 0 && !actual.Confirmed && actual.Package == null && actual.Detail.Contains("インストーラーは未公開"), unpublished.Name + " 未公開インストーラーを通信・取得しない");
            }
        }
        var tool = ToolCatalog.Load(root).Single(t => t.Id == "smart-paste");
        byte[] bytes = [1, 2, 3, 4];
        var package = new LocalPackage(tool.Id, tool.Name, "0.1.10", tool.AppId, "SmartPaste-0.1.10-setup.exe", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "unsigned-public-build");
        var fallback = ReleaseDownloader.Load(root).Packages.Single(p => p.Package.Id == tool.Id);
        string Url(string file) => "https://github.com/KotorinChunChun/SmartPaste/releases/download/v0.1.10/" + file;
        object[] Assets(string? url = null, string? digest = null) =>
        [
            new { name = "SmartPaste-packages.json", state = "uploaded", size = 512, browser_download_url = url ?? Url("SmartPaste-packages.json") },
            new { name = package.File, state = "uploaded", size = bytes.Length, browser_download_url = Url(package.File), digest = digest ?? "sha256:" + package.Sha256 }
        ];
        object Release(bool draft = false, string tag = "v0.1.10", object[]? assets = null) => new { tag_name = tag, draft, prerelease = true, assets = assets ?? Assets() };
        PublicReleaseClient Make(object[] list, LocalPackage? manifest = null) => new(new Handler((request, _) =>
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                check(request.RequestUri.AbsolutePath == "/repos/KotorinChunChun/SmartPaste/releases", "公開版照合は既知所有者の製品別API");
                return Json(list);
            }
            check(request.RequestUri == new Uri(Url("SmartPaste-packages.json")), "公開台帳は固定所有者・製品・tag・ファイルから取得");
            return Json(new PackageCatalog(1, "published", [manifest ?? package]));
        }));
        using (var client = Make([Release(), Release(tag: "v0.1.9")]))
        {
            var actual = await client.Check(tool, fallback);
            check(actual.Confirmed && actual.Package?.Package.Version == "0.1.10" && actual.Prerelease, "数値版比較で最新開発版と台帳を照合");
            using var downloader = new ReleaseDownloader(new Handler((request, _) =>
            {
                check(request.RequestUri == new Uri(Url(package.File)), "確認した新公開版の固定assetだけを取得");
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }));
            var catalog = await downloader.Prepare(new(1, [actual.Package!]), [tool with { Version = actual.Package!.Package.Version }], Path.Combine(fixtures, "public-update"));
            check(InstallerAdapter.Load(catalog).Packages.Single().Version == "0.1.10", "新公開版を製品識別を保って導入台帳へ渡す");
        }
        async Task Reject(object[] list, string name, LocalPackage? manifest = null)
        {
            using var client = Make(list, manifest); var actual = await client.Check(tool, fallback);
            check(!actual.Confirmed && actual.Package == fallback && actual.Detail.Contains("公開確認失敗"), name);
        }
        await Reject([Release(draft: true)], "下書きは公開版にしない");
        await Reject([], "公開一覧が空なら同梱版を未確認として維持");
        await Reject(Enumerable.Repeat(Release(), 100).ToArray(), "100件の一覧を全件確認済みと誤認しない");
        await Reject([Release(tag: "main")], "数値版でないtagを更新にしない");
        await Reject([Release(tag: "v0.1.10-beta")], "非数値の接尾辞付きtagを更新にしない");
        await Reject([Release()], "台帳の版とリリースtag不一致を拒否", package with { Version = "0.1.9", File = "SmartPaste-0.1.9-setup.exe" });
        await Reject([Release(assets: Assets("https://example.com/SmartPaste-packages.json"))], "別所有者・別配布先の公開台帳を拒否");
        await Reject([Release(assets: Assets(digest: "sha256:" + new string('0', 64)))], "GitHubassetと公開台帳のハッシュ不一致を拒否");
        await Reject([Release(assets: [Assets()[0]])], "インストーラー欠落を成功にしない");
        await Reject([Release(assets: [Assets()[0], Assets()[0], Assets()[1]])], "同名の公開台帳assetを重複許可しない");
        await Reject([Release(assets: [new { name = "SmartPaste-packages.json", state = "new", size = 512, browser_download_url = Url("SmartPaste-packages.json") }, Assets()[1]])], "準備未完了のassetを拒否");
        await Reject([Release(assets: [new { name = "SmartPaste-packages.json", state = "uploaded", size = 32769, browser_download_url = Url("SmartPaste-packages.json") }, Assets()[1]])], "台帳assetの容量上限を取得前に検査");
        using (var client = Make([Release(draft: true, tag: "v9.0.0"), Release()]))
            check((await client.Check(tool, fallback)).Package?.Package.Version == "0.1.10", "新しい下書きを除外して公開中の最新候補を選択");
        using (var client = new PublicReleaseClient(new Handler((request, _) => request.RequestUri!.Host == "api.github.com"
            ? Json(new[] { Release() })
            : new(HttpStatusCode.OK) { Content = new StringContent("\uFEFF" + JsonSerializer.Serialize(new PackageCatalog(1, "published", [package]), ToolCatalog.Json)) })))
            check((await client.Check(tool, fallback)).Confirmed, "UTF-8 BOM付きの公開台帳を受け付ける");
        int redirects = 0;
        using (var client = new PublicReleaseClient(new Handler((request, _) =>
        {
            redirects++; return new(HttpStatusCode.Redirect) { Headers = { Location = request.RequestUri } };
        })))
            check(!(await client.Check(tool, fallback)).Confirmed && redirects == 5, "公開情報の循環リダイレクトは5要求で打ち切る");
        using (var client = Make([Release()]))
            check(!(await client.Check(tool, new(package with { Sha256 = new string('0', 64) }, bytes.Length))).Confirmed, "同梱固定版の同版asset差替をオンライン台帳で許容しない");
        using (var client = new PublicReleaseClient(new Handler((_, _) => new(HttpStatusCode.Forbidden))))
            check(!(await client.Check(tool, fallback)).Confirmed, "API制限では最新確認済みにしない");
        using (var client = new PublicReleaseClient(new Handler((_, _) => new(HttpStatusCode.Redirect) { Headers = { Location = new("https://example.com/evil") } })))
            check(!(await client.Check(tool, fallback)).Confirmed, "公開APIの許可外リダイレクトを拒否");
        using (var client = new PublicReleaseClient(new Handler((_, _) => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 2 * 1024 * 1024 + 1)) })))
            check(!(await client.Check(tool, fallback)).Confirmed, "公開応答の容量を制限");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var cancelledClient = Make([Release()]); bool rejected = false;
        try { await cancelledClient.Check(tool, fallback, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
        check(rejected, "利用者の公開照合取消を成功へ変換しない");
    }

    public static async Task<int> Live(string root)
    {
        var bundled = ReleaseDownloader.Load(root); using var client = new PublicReleaseClient();
        bool success = true;
        foreach (var tool in ToolCatalog.Load(root).Where(t => t.DistributionStatus == "published"))
        {
            var result = await client.Check(tool, bundled.Packages.Single(p => p.Package.Id == tool.Id));
            Console.WriteLine($"{tool.Name}: {result.Package?.Package.Version} / {result.Confirmed} / {result.Detail}");
            success &= result.Confirmed;
        }
        return success ? 0 : 1;
    }
}
