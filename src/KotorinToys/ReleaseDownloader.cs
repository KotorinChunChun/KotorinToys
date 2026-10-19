using System.Text.Json;

namespace KotorinToys;

internal sealed partial class ReleaseDownloader : IDisposable
{
    const long MaxBytes = 512L * 1024 * 1024;
    readonly HttpClient client;
    public ReleaseDownloader(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KotorinToys/0.1.0");
    }

    public async Task<string> Prepare(ReleaseCatalog catalog, IReadOnlyList<ToolDefinition> tools, string cache, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        Validate(catalog, tools);
        var selected = tools.DistinctBy(t => t.Id).Select(tool =>
        {
            var item = catalog.Packages.SingleOrDefault(p => p.Package.Id == tool.Id) ?? throw new IOException("公開配布物がありません：" + tool.Name);
            if (item.Package.Name != tool.Name || item.Package.AppId != tool.AppId || item.Package.Version != tool.Version)
                throw new IOException("登録製品・版と公開台帳が一致しません。");
            return item;
        }).ToArray();
        if (selected.Length == 0)
            throw new IOException("取得対象がありません。");
        var directory = PathSafety.Resolve(cache, "download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var item in selected)
        {
            progress?.Report(item.Package.Name + "：ダウンロード中（固定版 " + ReleaseDisplay.Version(item) + "）");
            await Download(item, directory, cancellation, tools.Single(t => t.Id == item.Package.Id));
            progress?.Report(item.Package.Name + (item.Package.Kind == "msix" ? "：SHA-256・サイズ一致。MSIX署名・製品識別は導入前に照合します。" : "：SHA-256・サイズ一致。未署名です。"));
        }

        var path = PathSafety.Resolve(directory, "packages.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new PackageCatalog(1, "published", selected.Select(p => p.Package).ToArray()), ToolCatalog.Json), cancellation);
        return path;
    }
    public void Dispose() => client.Dispose();
}
