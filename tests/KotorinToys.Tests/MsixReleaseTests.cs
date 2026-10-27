using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using KotorinToys;

internal static class MsixReleaseTests
{
    internal static ToolDefinition Tool => new("http-selector", "HttpSelector", "debug.exe", "README.md", Version: "0.0.3.2",
        InstalledExecutable: "HttpSelector.exe", AppId: "KotorinToys.HttpSelector", Msix: new(MsixPolicy.Family, MsixPolicy.Publisher));
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request)); }
    public static async Task Run(string fixtures, Action<bool, string> check)
    {
        byte[] data = [1, 3, 5]; string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        string version = "0.0.3.3", file = MsixPolicy.FileName(version);
        string Url(string name) => "https://github.com/KotorinChunChun/HttpSelector/releases/download/v" + version + "/" + name;
        object Asset(string name, string? url = null, string? digest = null) => new { name, state = "uploaded", size = name == file ? data.Length : 200, browser_download_url = url ?? Url(name), digest };
        object Release(string tag, bool draft = false, object[]? assets = null) => new { tag_name = tag, draft, prerelease = false, published_at = "2026-10-16T18:10:50Z", assets = assets ?? [Asset(file, digest: "sha256:" + hash), Asset("SHA256SUMS.txt")] };
        PublicReleaseClient Client(object[] releases, string? sums = null) => new(new Handler(r => new(HttpStatusCode.OK) {
            Content = new StringContent(ReleaseIndexFixture.IsIndex(r) ? ReleaseIndexFixture.Json("HttpSelector", releases) : sums ?? hash + " *" + file + "\n") }));
        using (var client = Client([Release("v0.0.3.2"), Release("v0.0.3.3"), Release("v9.0.0.0", true)]))
        {
            var actual = await client.Check(Tool, null);
            check(actual.Confirmed && actual.Package?.Package is { Kind: "msix", Version: "0.0.3.3", Signature: "signed-msix" }, "未公開Inno定義でも固定MSIX公開版を4成分で確認");
            check(actual.Package?.PublishedAt == DateTimeOffset.Parse("2026-10-16T18:10:50Z"), "MSIX公開日を保持");
            using var downloader = new ReleaseDownloader(new Handler(r => {
                check(r.RequestUri!.AbsoluteUri == Url(file), "MSIXを固定所有者・製品・tagから取得");
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }; }));
            string path = await downloader.Prepare(new(1, [actual.Package!]), [Tool with { Version = version }], Path.Combine(fixtures, "msix-release"));
            check(PackageCatalogReader.Load(path).Packages.Single().Kind == "msix", "MSIX種別を取得後の台帳へ保持");
            var changed = actual.Package! with { Length = 10 };
            check(!(await client.Check(Tool, changed)).Confirmed, "同じ版のMSIX差し替えを拒否");
        }
        async Task Reject(object[] list, string label, string? sums = null)
        { using var client = Client(list, sums); var actual = await client.Check(Tool, null); check(!actual.Confirmed && actual.Detail.StartsWith("MSIX公開確認失敗"), label); }
        await Reject([Release("v0.0.3.3")], "MSIXチェックサム欠落を拒否", "");
        await Reject([Release("v0.0.3.3")], "MSIXチェックサム重複を拒否", hash + " *" + file + "\n" + hash + " *" + file);
        await Reject([Release("v0.0.3.3", assets: [Asset(file, digest: "sha256:" + new string('0', 64)), Asset("SHA256SUMS.txt")])], "MSIXのGitHub digest不一致を拒否");
        await Reject([Release("v0.0.3.3", assets: [Asset(file, "https://example.com/file.msix"), Asset("SHA256SUMS.txt")])], "MSIXの別配布先を拒否");
        await Reject([Release("v0.0.3")], "MSIXで3成分版を採用しない");
        using var unsupported = Client([]);
        check(!(await unsupported.Check(Tool with { Msix = new("Other_abcdef", MsixPolicy.Publisher) }, null)).Confirmed, "別FamilyのMSIXを拒否");
        var valid = new LocalPackage(Tool.Id, Tool.Name, version, Tool.AppId, file, hash, "signed-msix", "msix");
        foreach (var bad in new[] { valid with { Kind = "inno" }, valid with { Signature = "unsigned-public-build" }, valid with { Version = "0.0.3" }, valid with { Id = "smart-paste" }, valid with { File = "../" + file } })
        {
            bool rejected = false; try { PackageCatalogReader.Validate(new(1, "published", [bad])); } catch (IOException) { rejected = true; }
            check(rejected, "MSIX不正台帳拒否：" + bad.Id + "/" + bad.Kind + "/" + bad.Version + "/" + bad.File + "/" + bad.Signature);
        }
    }
}
