using System.Text.Json;
using System.Text.RegularExpressions;

namespace KotorinToys;

internal sealed record PublicRelease(ReleasePackage? Package, bool Confirmed, bool Prerelease, string Detail);

internal sealed class PublicReleaseClient : IDisposable
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
            if (ProductIdentity.FromId(tool.Id) is not { } product || product.Name != tool.Name || product.AppId != tool.AppId || tool.Id == "host")
                throw new IOException("公開版の照合対象が不正です。");
            cancellation.ThrowIfCancellationRequested();
            if (tool.DistributionStatus != "published")
                return new(null, false, false, "本体対応インストーラーは未公開です。ポータブル版・開発版の利用方法は「使い方」を確認してください。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var token = deadline.Token;
            using var list = JsonDocument.Parse(await Read(new($"https://api.github.com/repos/KotorinChunChun/{tool.Name}/releases?per_page=100"), 2 * 1024 * 1024, token));
            var releases = list.RootElement.EnumerateArray().ToArray();
            if (releases.Length >= 100) throw new IOException("公開版の全件照合が必要です。100件以上のリリースは自動判定しません。");
            var candidates = releases.Where(r => !r.GetProperty("draft").GetBoolean())
                .Select(r => (Release: r, Tag: r.GetProperty("tag_name").GetString()))
                .Where(r => r.Tag != null && Regex.IsMatch(r.Tag, @"^v[0-9]+\.[0-9]+\.[0-9]+$") && System.Version.TryParse(r.Tag[1..], out _))
                .OrderByDescending(r => System.Version.Parse(r.Tag![1..])).ToArray();
            if (candidates.Length == 0) throw new IOException("対応する公開リリースがありません。");
            var latest = candidates[0];
            var version = latest.Tag![1..];
            var assets = latest.Release.GetProperty("assets").EnumerateArray().ToArray();
            JsonElement Asset(string name)
            {
                var matches = assets.Where(a => a.GetProperty("name").GetString() == name).ToArray();
                if (matches.Length != 1 || matches[0].GetProperty("state").GetString() != "uploaded") throw new IOException("公開assetが欠落・重複・未準備です：" + name);
                var url = $"https://github.com/KotorinChunChun/{tool.Name}/releases/download/{latest.Tag}/{name}";
                if (matches[0].GetProperty("browser_download_url").GetString() != url) throw new IOException("公開assetの所有者・製品・tagが一致しません。");
                return matches[0];
            }
            var manifest = Asset(tool.Name + "-packages.json");
            var installer = Asset($"{tool.Name}-{version}-setup.exe");
            if (manifest.GetProperty("size").GetInt64() is <= 0 or > 32768) throw new IOException("公開台帳の容量が不正です。");
            var json = await Read(new(manifest.GetProperty("browser_download_url").GetString()!), 32768, token);
            // PowerShellで作成された台帳のUTF-8 BOMも受け付ける。
            var catalog = JsonSerializer.Deserialize<PackageCatalog>(json.TrimStart('\uFEFF'), ToolCatalog.Json) ?? throw new IOException("公開台帳が空です。");
            if (catalog.Packages == null || catalog.Packages.Any(p => p == null)) throw new IOException("公開台帳の製品定義が欠落しています。");
            PackageCatalogReader.Validate(catalog);
            if (catalog.DistributionStatus != "published" || catalog.Packages.Length != 1) throw new IOException("公開製品別台帳が不正です。");
            var package = catalog.Packages[0];
            if (package.Id != tool.Id || package.Name != tool.Name || package.AppId != tool.AppId || package.Version != version)
                throw new IOException("公開台帳と製品・リリースtagが一致しません。");
            var item = new ReleasePackage(package, installer.GetProperty("size").GetInt64());
            ReleaseDownloader.Validate(new(1, [item]));
            // 同梱済み固定版のasset差し替えを、オンライン台帳で許容しない。
            if (fallback != null && Version.Parse(fallback.Package.Version) == Version.Parse(version) &&
                (fallback.Package != package || fallback.Length != item.Length))
                throw new IOException("同梱固定版の配布情報が変更されています。同じ版の差し替えは実行しません。");
            if (installer.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String &&
                digest.GetString() != "sha256:" + package.Sha256) throw new IOException("GitHubのassetハッシュと配布台帳が一致しません。");
            bool prerelease = latest.Release.GetProperty("prerelease").GetBoolean();
            return new(item, true, prerelease, $"GitHub公開版・配布台帳照合済み（{DateTime.Now:MM/dd HH:mm}）。未署名です。");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OperationCanceledException or ArgumentException)
        {
            cancellation.ThrowIfCancellationRequested();
            return new(fallback, false, false, "公開確認失敗。表示版は同梱台帳です：" + ex.Message);
        }
    }

    async Task<string> Read(Uri uri, int limit, CancellationToken token)
    {
        for (int hop = 0; hop <= 4; hop++)
        {
            if (!(ReleaseDownloader.Allowed(uri) || uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo == "" && uri.Host == "api.github.com"))
                throw new IOException("公開情報の許可外URLへ移動しません。");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            { uri = new(uri, response.Headers.Location ?? throw new IOException("公開情報の移動先がありません。")); continue; }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new IOException("公開情報の容量上限を超えました。");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var content = new MemoryStream();
            var buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                if (content.Length + read > limit) throw new IOException("公開情報の容量上限を超えました。");
                content.Write(buffer, 0, read);
            }
            return System.Text.Encoding.UTF8.GetString(content.ToArray());
        }
        throw new IOException("公開情報のリダイレクト回数が上限を超えました。");
    }
    public void Dispose() => client.Dispose();
}
