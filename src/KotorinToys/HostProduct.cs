namespace KotorinToys;

internal static class HostProduct
{
    public static ToolDefinition Definition { get; } = new("host", "KotorinToys", "KotorinToys.exe", "README.md",
        AppId: "KotorinToys", InstalledExecutable: "KotorinToys.exe", DistributionStatus: "published");
    public static string CurrentVersion => typeof(HostProduct).Assembly.GetName().Version!.ToString(3);

    public static void Validate(ReleasePackage item)
    {
        PackageCatalogReader.Validate(new(1, "published", [item.Package]));
        if (item.Package.Id != Definition.Id || item.Package.Name != Definition.Name || item.Package.AppId != Definition.AppId ||
            item.Length is <= 0 or > 512L * 1024 * 1024 || !Version.TryParse(item.Package.Version, out _))
            throw new IOException("本体の更新配布物ではありません。");
    }

    public static bool IsNewer(ReleasePackage item, string currentVersion)
    {
        Validate(item);
        if (!Version.TryParse(currentVersion, out var current)) throw new IOException("現在の本体版を確認できません。");
        return Version.Parse(item.Package.Version) > current;
    }
}
