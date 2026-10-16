using System.Text.Json;

namespace KotorinToys;

internal sealed record PublicRelease(ReleasePackage? Package, bool Confirmed, bool Prerelease, string Detail);

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
            if (tool.DistributionStatus != "published")
                return new(null, false, false, "本体対応インストーラーは未公開です。ポータブル版・開発版の利用方法は「使い方」を確認してください。");
            if (ProductIdentity.FromId(tool.Id) is not { } product || product.Name != tool.Name || product.AppId != tool.AppId || tool.Id == "host")
                return new(null, false, false, "この製品のインストーラー管理には本体側の対応が必要です。「GitHubを開く」から配布方法を確認してください。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var token = deadline.Token;
            using var list = JsonDocument.Parse(await Read(new($"https://api.github.com/repos/KotorinChunChun/{tool.Name}/releases?per_page=100"), 2 * 1024 * 1024, token));
            var latest = PublicReleaseMetadata.Select(list.RootElement);
            var manifest = latest.Asset(tool, tool.Name + "-packages.json");
            var installer = latest.Asset(tool, $"{tool.Name}-{latest.Version}-setup.exe");
            if (manifest.GetProperty("size").GetInt64() is <= 0 or > 32768) throw new IOException("公開台帳の容量が不正です。");
            var json = await Read(new(manifest.GetProperty("browser_download_url").GetString()!), 32768, token);
            var item = latest.Package(tool, json, installer, fallback);
            bool prerelease = latest.Prerelease;
            return new(item, true, prerelease, $"GitHub公開版・配布台帳照合済み（{DateTime.Now:MM/dd HH:mm}）。未署名です。");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OperationCanceledException or ArgumentException)
        {
            cancellation.ThrowIfCancellationRequested();
            return new(fallback, false, false, "公開確認失敗。表示版は同梱台帳です：" + ex.Message);
        }
    }

    public void Dispose() => client.Dispose();
}
