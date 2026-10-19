using System.Text.Json;

namespace KotorinToys;

internal sealed record PortablePublication(string Version, DateTimeOffset? PublishedAt, string DownloadUrl, long Length, string Sha256);
internal sealed record PublicRelease(ReleasePackage? Package, bool Confirmed, bool Prerelease, string Detail, PortablePublication? Portable = null, ReleaseProblem Problem = ReleaseProblem.None);

internal sealed partial class PublicReleaseClient : IDisposable
{
    readonly HttpClient client;
    public PublicReleaseClient(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KotorinToys/0.1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<PublicRelease> Check(ToolDefinition tool, ReleasePackage? fallback, CancellationToken cancellation = default)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (tool.Msix != null) return await CheckMsix(tool, fallback, cancellation);
            if (tool.DistributionStatus != "published")
            {
                if (tool.Id == "shortcut-key-counter" && tool.Name == "ShortcutKeyCounter" &&
                    tool.AppId == "KotorinToys.ShortcutKeyCounter" && tool.Msix == null)
                    return await CheckPortable(tool, cancellation);
                return new(null, false, false, "本体対応インストーラーは未公開です。ポータブル版・開発版の利用方法は「使い方」を確認してください。", Problem: ReleaseProblem.Unpublished);
            }
            if (ProductIdentity.FromTool(tool) is not { } product || product.Name != tool.Name || product.AppId != tool.AppId || tool.Id == "host")
                return new(null, false, false, "この製品のインストーラー管理には本体側の対応が必要です。「GitHubを開く」から配布方法を確認してください。", Problem: ReleaseProblem.Unsupported);
            return await CheckCore(tool, fallback, true, false, cancellation);
        }
        catch (OperationCanceledException) { throw; }
    }

    public Task<PublicRelease> CheckHost(bool includePrerelease = false, CancellationToken cancellation = default) =>
        CheckCore(HostProduct.Definition, null, includePrerelease, true, cancellation);

    async Task<PublicRelease> CheckCore(ToolDefinition tool, ReleasePackage? fallback, bool includePrerelease, bool hostUpdate, CancellationToken cancellation)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var token = deadline.Token;
            var repository = ProductIdentity.FromTool(tool)?.Repository ?? throw new IOException("公開製品の検証済み配布先がありません。");
            using var list = JsonDocument.Parse(await Read(new($"https://api.github.com/repos/KotorinChunChun/{repository}/releases?per_page=100"), 2 * 1024 * 1024, token));
            var latest = PublicReleaseMetadata.Select(list.RootElement, includePrerelease);
            var manifest = latest.Asset(tool, tool.Name + "-packages.json");
            var installer = latest.Asset(tool, $"{tool.Name}-{latest.Version}-setup.exe");
            if (manifest.GetProperty("size").GetInt64() is <= 0 or > 32768) throw new IOException("公開台帳の容量が不正です。");
            var json = await Read(new(manifest.GetProperty("browser_download_url").GetString()!), 32768, token);
            var item = latest.Package(tool, json, installer, fallback, hostUpdate);
            bool prerelease = latest.Prerelease;
            return new(item, true, prerelease, $"GitHub公開版・配布台帳照合済み（{DateTime.Now:MM/dd HH:mm}）。未署名です。");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OperationCanceledException or ArgumentException)
        {
            cancellation.ThrowIfCancellationRequested();
            return new(fallback, false, false, (hostUpdate ? "本体公開版の確認失敗：" : "公開確認失敗。表示版は同梱台帳です：") + ex.Message, Problem: ReleaseProblems.From(ex));
        }
    }

    public void Dispose() => client.Dispose();
}
