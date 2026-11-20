namespace KotorinToys;

// PPPaint は自己署名の Inno Setup 配布物だけを、固定した発行者で受け入れる。
internal static class PppaintDistribution
{
    internal const string Id = "pppaint";
    internal const string Name = "PPPaint";
    internal const string AppId = "KotorinToys.PPPaint";
    internal const string Repository = "https://github.com/KotorinChunChun/PPPaint";
    internal const string Kind = "pppaint-signed-inno";
    internal const string Signature = "authenticode-signed-inno";
    internal const string Publisher = "CN=KotorinChunChun";
    internal const string Thumbprint = "475BABC3C3D1E871D90503D45CC5236216D85EE8";

    internal static string FileName(string version) => $"PPPaint-{version}-setup.exe";
    internal static bool Supports(ToolDefinition tool) =>
        tool.Id == Id && tool.Name == Name && tool.AppId == AppId && tool.InstalledExecutable == "PPPaint.exe" &&
        tool.RepositoryUrl == Repository && tool.Msix == null && tool.Capabilities is { } capabilities &&
        capabilities.Length == 4 && capabilities.ToHashSet(StringComparer.Ordinal).SetEquals(["status", "show", "stop", "start"]);

    internal static bool Valid(LocalPackage package) =>
        package.Id == Id && package.Name == Name && package.AppId == AppId && package.Kind == Kind &&
        package.Signature == Signature &&
        System.Text.RegularExpressions.Regex.IsMatch(package.Version, @"^\d+\.\d+\.\d+$") &&
        System.Text.RegularExpressions.Regex.IsMatch(package.Sha256, "^[a-f0-9]{64}$") &&
        package.File == FileName(package.Version);
}
