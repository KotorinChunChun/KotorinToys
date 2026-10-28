namespace KotorinToys;

// 配布configで任意の署名者や製品を追加できない固定方針。
internal static class MsixPolicy
{
    public const string Name = "HttpSelector.Development";
    public const string Family = "HttpSelector.Development_xtxrnffqrdk4w";
    public const string MdName = "com.mdvisualizer.viewer";
    public const string MdFamily = "com.mdvisualizer.viewer_xtxrnffqrdk4w";
    public const string Publisher = "CN=KotorinChunChun";
    public const string Thumbprint = "475BABC3C3D1E871D90503D45CC5236216D85EE8";
    public static bool Supports(ToolDefinition tool) =>
        tool.Id == "http-selector" && tool.Name == "HttpSelector" && tool.AppId == "KotorinToys.HttpSelector" &&
        tool.InstalledExecutable == "HttpSelector.exe" && tool.Msix is { FamilyName: Family, Publisher: Publisher, ApplicationId: "App" } ||
        tool.Id == "md-visualizer" && tool.Name == "MdVisualizer" && tool.AppId == "KotorinToys.MdVisualizer" &&
        tool.RepositoryUrl == "https://github.com/KotorinChunChun/MdVisualizer" && tool.InstalledExecutable == "mdvisualizer.exe" &&
        tool.Msix is { FamilyName: MdFamily, Publisher: Publisher, ApplicationId: "MdVisualizer" };
    public static string NameFor(ToolDefinition tool) => Supports(tool)
        ? tool.Id == "md-visualizer" ? MdName : Name : throw new IOException("未対応のMSIX製品です。");
    public static bool Installed(InstallationState state) => state.State is "導入済み" or "更新が必要";
    public static string FileName(string version, string id = "http-selector") => id switch {
        "http-selector" => $"HttpSelector_{version}_x64_dev.msix",
        "md-visualizer" => $"MdVisualizer_{version}_x64.msix",
        _ => throw new IOException("未対応のMSIX製品です。")
    };
    public static bool Valid(LocalPackage package) =>
        (package.Id == "http-selector" && package.Name == "HttpSelector" && package.AppId == "KotorinToys.HttpSelector" ||
         package.Id == "md-visualizer" && package.Name == "MdVisualizer" && package.AppId == "KotorinToys.MdVisualizer") &&
        package.Kind == "msix" && package.Signature == "signed-msix" &&
        System.Text.RegularExpressions.Regex.IsMatch(package.Version, @"^\d+\.\d+\.\d+\.\d+$") &&
        Version.TryParse(package.Version, out var version) && new[] { version.Major, version.Minor, version.Build, version.Revision }.All(n => n <= 65535) &&
        package.File == FileName(package.Version, package.Id);
}
