using System.Text.Json;

namespace KotorinToys;

internal sealed partial class ReleaseDownloader
{
    public async Task<string> PrepareHost(ReleasePackage item, string currentVersion, string cache,
        IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        if (!HostProduct.IsNewer(item, currentVersion)) throw new IOException("同じ版または古い版へは自己更新しません。");
        cancellation.ThrowIfCancellationRequested();
        var directory = PathSafety.Resolve(cache, "download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        progress?.Report("本体インストーラーをダウンロードしています…");
        await Download(item, directory, cancellation);
        progress?.Report("製品・容量・SHA-256の照合完了。未署名の配布物です。");
        var path = PathSafety.Resolve(directory, "packages.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new PackageCatalog(1, "published", [item.Package]), ToolCatalog.Json), cancellation);
        return path;
    }
}
