using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using KotorinToys;

internal static class ReleaseIndexTests
{
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(send(request)); }
    }

    public static async Task Run(string root, string fixtures, Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var empty = Array.Empty<object>();
        bool Rejected(string json, string repo = "SmartPaste")
        {
            try { PublicReleaseIndex.Releases(json, repo, now); return false; }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException) { return true; }
        }
        check(!Rejected(ReleaseIndexFixture.Json("SmartPaste", empty, now)), "新しい公開一覧の対象リポジトリを選択");
        check(Rejected(ReleaseIndexFixture.Json("SmartPaste", empty, now.AddHours(-25))), "24時間を超えた公開一覧を最新と扱わない");
        check(Rejected(ReleaseIndexFixture.Json("SmartPaste", empty, now.AddMinutes(6))), "未来の公開一覧を拒否");
        check(Rejected(ReleaseIndexFixture.Json("SmartPaste", empty, now, false)), "取得失敗した製品を公開済みにしない");
        check(Rejected(ReleaseIndexFixture.Json("Other", empty, now)), "一覧にない製品を公開済みにしない");
        check(Rejected(ReleaseIndexFixture.Json("SmartPaste", Enumerable.Repeat(new { }, 100).ToArray(), now)), "100件以上の不完全な公開一覧を拒否");
        check(Rejected("{\"schemaVersion\":1,\"schemaVersion\":1,\"generatedAt\":\"" + now.ToString("o") + "\",\"repositories\":[]}"), "重複するJSON項目を拒否");
        check(Rejected(JsonSerializer.Serialize(new { schemaVersion = 1, generatedAt = now, repositories = new[] {
            new { name = "SmartPaste", available = true, releases = empty }, new { name = "SmartPaste", available = true, releases = empty } } })), "同名リポジトリの重複を拒否");
        var maximum = Enumerable.Range(0, OnlineToolCatalog.MaximumTools).Select(i => new { name = "Repo" + i, available = true, releases = empty })
            .Append(new { name = "SmartPaste", available = true, releases = empty }).ToArray();
        check(!Rejected(JsonSerializer.Serialize(new { schemaVersion = 1, generatedAt = now, repositories = maximum })), "128製品と本体の公開先を受け付ける");
        check(Rejected(JsonSerializer.Serialize(new { schemaVersion = 1, generatedAt = now, repositories = maximum.Append(new { name = "Extra", available = true, releases = empty }) })), "対象上限を超える公開先を拒否");

        var tools = ToolCatalog.Load(root);
        var selected = new[] { tools.Single(t => t.Id == "smart-paste"), tools.Single(t => t.Id == "voice-mixer-spk-router") };
        byte[] bytes = "release-index-fixture"u8.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var packages = selected.ToDictionary(t => t.Name,
            t => new LocalPackage(t.Id, t.Name, "1.2.3", t.AppId, t.Name + "-1.2.3-setup.exe", hash, "unsigned-public-build"));
        string Url(ToolDefinition tool, string file) => $"https://github.com/KotorinChunChun/{ProductIdentity.FromTool(tool)!.Repository}/releases/download/v1.2.3/{file}";
        var index = JsonSerializer.Serialize(new { schemaVersion = 1, generatedAt = now, repositories = selected.Select(tool => new {
            name = ProductIdentity.FromTool(tool)!.Repository, available = true,
            releases = new[] { new { tag_name = "v1.2.3", draft = false, prerelease = true, published_at = now, assets = new[] {
                new { name = tool.Name + "-packages.json", state = "uploaded", size = 512, browser_download_url = Url(tool, tool.Name + "-packages.json"), digest = (string?)null },
                new { name = packages[tool.Name].File, state = "uploaded", size = bytes.Length, browser_download_url = Url(tool, packages[tool.Name].File), digest = (string?)("sha256:" + hash) }
            } } } }) }, ToolCatalog.Json);
        int indexCalls = 0, manifestCalls = 0;
        var cache = new PublicReleaseCache(utcNow: () => now);
        using var client = new PublicReleaseClient(new Handler(request =>
        {
            check(request.RequestUri!.Host != "api.github.com" && request.Headers.Authorization == null, "公開確認はGitHub APIと認証情報を使わない");
            if (ReleaseIndexFixture.IsIndex(request)) { indexCalls++; return new(HttpStatusCode.OK) { Content = new StringContent(index) }; }
            var tool = selected.Single(t => request.RequestUri.AbsoluteUri == Url(t, t.Name + "-packages.json"));
            manifestCalls++;
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new PackageCatalog(1, "published", [packages[tool.Name]]), ToolCatalog.Json)) };
        }), cache);
        var first = await client.Check(selected[0], null);
        var second = await client.Check(selected[1], null);
        check(first.Confirmed && second.Confirmed && indexCalls == 1 && manifestCalls == 2,
            $"複数製品で一覧を1回共有し、各製品の公開台帳を照合（一覧{indexCalls}、台帳{manifestCalls}、1件目={first.Detail}、2件目={second.Detail}）");
        using var downloader = new ReleaseDownloader(new Handler(request =>
        {
            check(request.RequestUri!.AbsoluteUri == Url(selected[1], packages[selected[1].Name].File), "SpkRouterの固定リリースから取得");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var path = await downloader.Prepare(new(1, [second.Package!]), [selected[1] with { Version = "1.2.3" }], Path.Combine(fixtures, "release-index-spk"));
        check(PackageCatalogReader.Load(path, selected).Packages.Single() == packages[selected[1].Name], "SpkRouterを容量・ハッシュ照合して導入用台帳へ渡す");
    }
}
