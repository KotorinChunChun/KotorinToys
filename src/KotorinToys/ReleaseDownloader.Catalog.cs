using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace KotorinToys;

internal sealed partial class ReleaseDownloader
{
    public static ReleaseCatalog Load(string root, IReadOnlyCollection<ToolDefinition>? definitions = null)
    {
        var path = PathSafety.Resolve(root, "catalog/releases.json");
        if (new FileInfo(path).Length > 2 * 1024 * 1024)
            throw new IOException("公開台帳の容量が不正です。");
        var catalog = JsonSerializer.Deserialize<ReleaseCatalog>(File.ReadAllText(path), ToolCatalog.Json) ?? throw new IOException("公開台帳が空です。");
        Validate(catalog, definitions);
        return catalog;
    }

    internal static void Validate(ReleaseCatalog catalog, IReadOnlyCollection<ToolDefinition>? definitions = null)
    {
        if (catalog.SchemaVersion != 1 || catalog.Packages == null)
            throw new IOException("公開台帳の形式が不正です。");
        PackageCatalogReader.Validate(new(1, "published", catalog.Packages.Select(p => p.Package).ToArray()), definitions);
        if (catalog.Packages.Any(p => p.Package.Id == "host" || p.Length is <= 0 or > MaxBytes))
            throw new IOException("公開ツールのサイズまたは対象が不正です。");
    }

    internal static Uri Asset(ReleasePackage item, ToolDefinition? definition = null)
    {
        var product = definition == null ? ProductIdentity.FromId(item.Package.Id) : ProductIdentity.FromTool(definition);
        if (product == null || product.Id != item.Package.Id) throw new IOException("公開製品の検証済み配布先がありません。");
        if (product.Name != item.Package.Name || product.AppId != item.Package.AppId) throw new IOException("公開製品の識別が一致しません。");
        return new($"https://github.com/KotorinChunChun/{product.Repository}/releases/download/v{item.Package.Version}/{item.Package.File}");
    }
    internal static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
}
