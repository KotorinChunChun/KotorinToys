namespace KotorinToys;

// 配布configから任意のパッケージ・署名者の実行許可を追加することはできない。
internal static class MsixPolicy
{
    public const string Name = "HttpSelector.Development";
    public const string Family = "HttpSelector.Development_xtxrnffqrdk4w";
    public const string Publisher = "CN=KotorinChunChun";
    public const string Thumbprint = "475BABC3C3D1E871D90503D45CC5236216D85EE8";
    public static bool Supports(ToolDefinition tool) => tool.Id == "http-selector" && tool.Name == "HttpSelector" &&
        tool.AppId == "KotorinToys.HttpSelector" && tool.InstalledExecutable == "HttpSelector.exe" &&
        tool.Msix is { FamilyName: Family, Publisher: Publisher, ApplicationId: "App" };
    public static bool Installed(InstallationState state) => state.State is "導入済み" or "更新が必要";
    public static string FileName(string version) => $"HttpSelector_{version}_x64_dev.msix";
    public static bool Valid(LocalPackage package) => package.Id == "http-selector" && package.Name == "HttpSelector" &&
        package.AppId == "KotorinToys.HttpSelector" && package.Kind == "msix" && package.Signature == "signed-msix" &&
        System.Text.RegularExpressions.Regex.IsMatch(package.Version, @"^\d+\.\d+\.\d+\.\d+$") &&
        Version.TryParse(package.Version, out var version) && new[] { version.Major, version.Minor, version.Build, version.Revision }.All(n => n <= 65535) &&
        package.File == FileName(package.Version);
}
