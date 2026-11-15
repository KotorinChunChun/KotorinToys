namespace KotorinToys;

// 旧リポジトリURLと、旧製品名で公開済みのv0.1.2を読み替える。
internal static class CapchunScreenLegacy
{
    public const string ProductName = "CapchunWindow";
    public const string Executable = "CapchunWindow.exe";
    public const string Version = "0.1.2";
    const string Id = "capchun-screen";
    const string AppId = "KotorinToys.CapchunScreen";
    const string DebugDirectory = "artifacts/management/bin/CapchunScreen/debug/";
    const string LegacyDebugPath = DebugDirectory + Executable;
    const string LegacyRepositoryUrl = "https://github.com/KotorinChunChun/" + ProductName;
    const string RepositoryUrl = "https://github.com/KotorinChunChun/CapchunScreen";

    public static ToolDefinition NormalizeCatalog(ToolDefinition tool)
    {
        if (tool is { Id: Id, AppId: AppId, Name: ProductName, Version: Version, Msix: null, InstalledExecutable: Executable,
            Executable: LegacyDebugPath, RepositoryUrl: null or "" or LegacyRepositoryUrl or RepositoryUrl } &&
            tool.ToolRoot is "tools/CapchunScreen" or @"tools\CapchunScreen")
        {
            tool = tool with { Name = "CapchunScreen", InstalledExecutable = "CapchunScreen.exe",
                Executable = DebugDirectory + "CapchunScreen.exe", DistributionStatus = "unpublished",
                Runtimes = tool.Runtimes?.Select(r => r with
                { Guide = r.Guide?.Replace(ProductName, "CapchunScreen", StringComparison.Ordinal) }).ToArray() };
        }
        return NormalizeRepository(tool);
    }

    internal static ToolDefinition NormalizeRepository(ToolDefinition tool) =>
        tool is { Id: Id, AppId: AppId, Name: "CapchunScreen", InstalledExecutable: "CapchunScreen.exe",
            Msix: null, RepositoryUrl: LegacyRepositoryUrl }
            ? tool with { RepositoryUrl = RepositoryUrl }
            : tool;

    public static bool MatchesInstallation(ToolDefinition tool, string? name, string? executable, string? version) =>
        tool is { Id: Id, AppId: AppId, Name: "CapchunScreen", InstalledExecutable: "CapchunScreen.exe", Msix: null }
        && name == ProductName && executable == Executable && version == Version;
}
