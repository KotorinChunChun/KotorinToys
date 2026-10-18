using System.Text.Json;
using System.Text.RegularExpressions;

namespace KotorinToys;

// 公開情報の選択と照合だけを担当する。HTTP・画面・設定保存には依存しない。
internal sealed class PublicReleaseMetadata
{
    readonly JsonElement release;
    readonly string tag;

    PublicReleaseMetadata(JsonElement release, string tag)
    {
        // 呼出元のJsonDocument破棄後も、選択した候補の検証を継続できるよう所有する。
        this.release = release.Clone();
        this.tag = tag;
    }

    public string Version => tag[1..];
    public bool Prerelease => release.GetProperty("prerelease").GetBoolean();
    public DateTimeOffset? PublishedAt => release.TryGetProperty("published_at", out var value) &&
        value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(),
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : null;

    public static PublicReleaseMetadata Select(JsonElement list, bool includePrerelease = true, bool msix = false)
    {
        var releases = list.EnumerateArray().ToArray();
        if (releases.Length >= 100) throw new IOException("公開版の全件照合が必要です。100件以上のリリースは自動判定しません。");
        var candidates = releases.Where(r => !r.GetProperty("draft").GetBoolean() && (includePrerelease || !r.GetProperty("prerelease").GetBoolean()))
            .Select(r => (Release: r, Tag: r.GetProperty("tag_name").GetString()))
            .Where(r => r.Tag != null && Regex.IsMatch(r.Tag, msix ? @"^v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$" : @"^v[0-9]+\.[0-9]+\.[0-9]+$") && System.Version.TryParse(r.Tag[1..], out _))
            .OrderByDescending(r => System.Version.Parse(r.Tag![1..])).ToArray();
        if (candidates.Length == 0) throw new IOException("対応する公開リリースがありません。");
        return new(candidates[0].Release, candidates[0].Tag!);
    }

    public JsonElement Asset(ToolDefinition tool, string name)
    {
        var assets = release.GetProperty("assets").EnumerateArray();
        var matches = assets.Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (matches.Length != 1 || matches[0].GetProperty("state").GetString() != "uploaded")
            throw new IOException("公開assetが欠落・重複・未準備です：" + name);
        var product = ProductIdentity.FromId(tool.Id) ?? throw new IOException("公開製品の固定配布先がありません。");
        if (product.Name != tool.Name || product.AppId != tool.AppId) throw new IOException("公開製品の識別が一致しません。");
        var url = $"https://github.com/KotorinChunChun/{product.Repository}/releases/download/{tag}/{name}";
        if (matches[0].GetProperty("browser_download_url").GetString() != url)
            throw new IOException("公開assetの所有者・製品・tagが一致しません。");
        return matches[0];
    }

    public ReleasePackage Package(ToolDefinition tool, string json, JsonElement installer, ReleasePackage? fallback, bool hostUpdate = false)
    {
        // PowerShellで作成された台帳のUTF-8 BOMも受け付ける。
        var catalog = JsonSerializer.Deserialize<PackageCatalog>(json.TrimStart('\uFEFF'), ToolCatalog.Json)
            ?? throw new IOException("公開台帳が空です。");
        if (catalog.Packages == null || catalog.Packages.Any(p => p == null))
            throw new IOException("公開台帳の製品定義が欠落しています。");
        PackageCatalogReader.Validate(catalog);
        if (catalog.DistributionStatus != "published" || catalog.Packages.Length != 1)
            throw new IOException("公開製品別台帳が不正です。");
        var package = catalog.Packages[0];
        if (package.Id != tool.Id || package.Name != tool.Name || package.AppId != tool.AppId || package.Version != Version)
            throw new IOException("公開台帳と製品・リリースtagが一致しません。");
        var item = new ReleasePackage(package, installer.GetProperty("size").GetInt64(), PublishedAt);
        if (hostUpdate) HostProduct.Validate(item);
        else ReleaseDownloader.Validate(new(1, [item]));
        // 同梱済み固定版のasset差し替えを、オンライン台帳で許容しない。
        if (fallback != null && System.Version.Parse(fallback.Package.Version) == System.Version.Parse(Version) &&
            (fallback.Package != package || fallback.Length != item.Length))
            throw new IOException("同梱固定版の配布情報が変更されています。同じ版の差し替えは実行しません。");
        if (installer.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String &&
            digest.GetString() != "sha256:" + package.Sha256)
            throw new IOException("GitHubのassetハッシュと配布台帳が一致しません。");
        return item;
    }

    public PortablePublication Portable(ToolDefinition tool, string checksums)
    {
        var name = $"{tool.Name}-v{Version}-win-x64.zip";
        var asset = Asset(tool, name);
        long length = asset.GetProperty("size").GetInt64();
        if (length is <= 0 or > 512 * 1024 * 1024)
            throw new IOException("公開ZIPの容量が不正です。");
        var entries = checksums.TrimStart('\uFEFF').Split('\n')
            .Select(line => Regex.Match(line.TrimEnd('\r'), @"^([a-fA-F0-9]{64}) [ *](.+)$"))
            .Where(match => match.Success && match.Groups[2].Value == name).ToArray();
        if (entries.Length != 1) throw new IOException("公開ZIPのSHA-256が欠落または重複しています。");
        string hash = entries[0].Groups[1].Value.ToLowerInvariant();
        if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String &&
            !string.Equals(digest.GetString(), "sha256:" + hash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("GitHubのassetハッシュとSHA256SUMSが一致しません。");
        return new(Version, PublishedAt, asset.GetProperty("browser_download_url").GetString()!, length, hash);
    }

    public ReleasePackage Msix(ToolDefinition tool, string checksums, ReleasePackage? fallback)
    {
        if (!MsixPolicy.Supports(tool)) throw new IOException("未対応のMSIX製品識別です。");
        string name = MsixPolicy.FileName(Version);
        var asset = Asset(tool, name);
        var matches = checksums.TrimStart('\uFEFF').Split('\n').Select(line => Regex.Match(line.TrimEnd('\r'), @"^([a-fA-F0-9]{64}) [ *](.+)$"))
            .Where(m => m.Success && m.Groups[2].Value == name).ToArray();
        if (matches.Length != 1) throw new IOException("MSIXのSHA-256が欠落または重複しています。");
        string hash = matches[0].Groups[1].Value.ToLowerInvariant();
        var package = new LocalPackage(tool.Id, tool.Name, Version, tool.AppId, name, hash, "signed-msix", "msix");
        var result = new ReleasePackage(package, asset.GetProperty("size").GetInt64(), PublishedAt);
        ReleaseDownloader.Validate(new(1, [result]));
        if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String &&
            !string.Equals(digest.GetString(), "sha256:" + hash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("GitHubのassetハッシュとSHA256SUMSが一致しません。");
        if (fallback != null && fallback.Package.Version == Version && (fallback.Package != package || fallback.Length != result.Length))
            throw new IOException("同じMSIX版の配布情報が変更されています。差し替えは実行しません。");
        return result;
    }
}
