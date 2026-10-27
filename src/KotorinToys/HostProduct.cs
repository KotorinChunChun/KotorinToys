namespace KotorinToys;

internal enum HostUpdateChannel { Unknown, Stable, Preview }

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

    public static bool CanInstall(ReleasePackage item, string currentVersion, HostChannelTransition? transition = null)
    {
        Validate(item);
        if (!Version.TryParse(currentVersion, out _)) throw new IOException("現在の本体版を確認できません。");
        if (transition == null) return IsNewer(item, currentVersion);
        if (transition.From is not (HostUpdateChannel.Unknown or HostUpdateChannel.Stable or HostUpdateChannel.Preview) ||
            transition.To is not (HostUpdateChannel.Stable or HostUpdateChannel.Preview) || transition.From == transition.To ||
            transition.BackupManifest != "backup/manifest.json")
            throw new IOException("更新チャネルの切替指定が不正です。");
        return true;
    }
}
