using System.Net;
using System.Text.Json;
using KotorinToys;

internal static class DistributionMappingTests
{
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request)); }
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, ToolCatalog.Json)) };
    public static async Task Run(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root);
        var seven = new List<ReleasePackage>(ReleaseDownloader.Load(root).Packages.Where(p => p.Package.Id is "live-text-marker" or "smart-paste" or "ss-capture"));
        foreach (var id in new[] { "capchun-screen", "chulip-video", "chunap-tool", "every-db", "voice-mixer-mic-router", "voice-mixer-spk-router" })
        {
            var tool = tools.Single(t => t.Id == id) with { DistributionStatus = "published" };
            var identity = ProductIdentity.FromId(id)!;
            var package = new LocalPackage(id, tool.Name, tool.Version, tool.AppId, tool.Name + "-" + tool.Version + "-setup.exe", new string('a', 64), "unsigned-public-build");
            string Url(string file) => "https://github.com/KotorinChunChun/" + identity.Repository + "/releases/download/v" + tool.Version + "/" + file;
            object Asset(string file, int size) => new { name = file, size, state = "uploaded", browser_download_url = Url(file) };
            using var client = new PublicReleaseClient(new Handler(request =>
            {
                if (ReleaseIndexFixture.IsIndex(request))
                {
                    check(request.RequestUri == PublicReleaseIndex.Source, tool.Name + "の固定公開情報一覧を使用");
                    return Json(ReleaseIndexFixture.Index(identity.Repository, new[] { new { tag_name = "v" + tool.Version, draft = false, prerelease = true, published_at = "2026-10-17T00:00:00Z",
                        assets = new[] { Asset(tool.Name + "-packages.json", 512), Asset(package.File, 4) } } }));
                }
                check(request.RequestUri!.AbsoluteUri == Url(tool.Name + "-packages.json"), tool.Name + "の製品別台帳も同じ固定配布先");
                return Json(new PackageCatalog(1, "published", [package]));
            }));
            var result = await client.Check(tool, null);
            check(result.Confirmed && result.Package?.Package == package && result.Package.PublishedAt != null, tool.Name + "の本体対応配布と日付を確認");
            check(ReleaseDownloader.Asset(result.Package!).AbsoluteUri == Url(package.File), tool.Name + "のダウンロード先も照合先と一致");
            seven.Add(result.Package!);
        }
        ReleaseDownloader.Validate(new(1, seven.ToArray()));
        check(seven.Count == 9, "対応9製品の台帳を旧4件制限で拒否しない");
        bool rejected = false;
        try { ReleaseDownloader.Asset(seven.Last() with { Package = seven.Last().Package with { Name = "AnotherTool" } }); }
        catch (IOException) { rejected = true; }
        check(rejected, "配布先を分離しても製品名の差し替えを許可しない");
        var chulip = tools.Single(t => t.Id == "chulip-video") with { DistributionStatus = "published" };
        using var wrong = new PublicReleaseClient(new Handler(_ => Json(ReleaseIndexFixture.Index("ChulipVideo-CSharp", new[] { new { tag_name = "v" + chulip.Version, draft = false, prerelease = true,
            assets = new[] { new { name = "ChulipVideo-packages.json", size = 512, state = "uploaded",
                browser_download_url = "https://github.com/KotorinChunChun/ChulipVideo/releases/download/v" + chulip.Version + "/ChulipVideo-packages.json" } } } }))));
        check(!(await wrong.Check(chulip, null)).Confirmed, "同名Python版リポジトリへ誘導するasset URLを拒否");
    }
}
