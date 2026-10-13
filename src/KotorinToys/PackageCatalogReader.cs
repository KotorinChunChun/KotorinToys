using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace KotorinToys;

internal static class PackageCatalogReader
{
    public static PackageCatalog Load(string path)
    {
        var value = JsonSerializer.Deserialize<PackageCatalog>(File.ReadAllText(path), ToolCatalog.Json) ?? throw new IOException("配布台帳が空です。");
        Validate(value);
        foreach (var package in value.Packages)
            PathSafety.Resolve(Path.GetDirectoryName(Path.GetFullPath(path))!, package.File);
        return value;
    }

    internal static void Validate(PackageCatalog value)
    {
        if (value.SchemaVersion != 1 || value.DistributionStatus is not ("unpublished" or "published") || value.Packages == null || value.Packages.Length is < 1 or > 4 || value.Packages.Select(p => p.Id).Distinct().Count() != value.Packages.Length)
            throw new IOException("未対応または重複したローカル配布台帳です。");
        foreach (var package in value.Packages)
        {
            var product = ProductIdentity.FromId(package.Id) ?? throw new IOException("未登録の配布製品です。");
            if (package.Name != product.Name || package.AppId != product.AppId
                || !System.Text.RegularExpressions.Regex.IsMatch(package.Version, "^[0-9]+\\.[0-9]+\\.[0-9]+$")
                || !System.Text.RegularExpressions.Regex.IsMatch(package.Sha256, "^[a-f0-9]{64}$")
                || package.Signature != (value.DistributionStatus == "published" ? "unsigned-public-build" : "unsigned-local-build")
                || package.File != $"{product.Name}-{package.Version}-setup.exe")
                throw new IOException("製品・版・ハッシュの配布定義が不正です。");
        }
    }
}
