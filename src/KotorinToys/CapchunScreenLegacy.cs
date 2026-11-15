namespace KotorinToys;

// 旧名で公開済みのv0.1.2だけを識別する。新規配布の製品識別には使わない。
internal static class CapchunScreenLegacy
{
    public const string Repository = "CapchunWindow";
    public const string Executable = "CapchunWindow.exe";
    public const string Version = "0.1.2";
    const string Id = "capchun-screen";
    const string AppId = "KotorinToys.CapchunScreen";
    const string DebugDirectory = "artifacts/management/bin/CapchunScreen/debug/";
    const string LegacyDebugPath = DebugDirectory + Executable;
    const string LegacyRepositoryUrl = "https://github.com/KotorinChunChun/" + Repository;

    public static ToolDefinition NormalizeCatalog(ToolDefinition tool) =>
        tool is { Id: Id, AppId: AppId, Name: Repository, Version: Version, Msix: null, InstalledExecutable: Executable,
            Executable: LegacyDebugPath, ToolRoot: "tools/CapchunScreen",
            RepositoryUrl: null or "" or LegacyRepositoryUrl }
            ? tool with { Name = "CapchunScreen", InstalledExecutable = "CapchunScreen.exe",
                Executable = DebugDirectory + "CapchunScreen.exe", DistributionStatus = "unpublished",
                Runtimes = tool.Runtimes?.Select(r => r with
                { Guide = r.Guide?.Replace(Repository, "CapchunScreen", StringComparison.Ordinal) }).ToArray() }
            : tool;

    public static bool MatchesInstallation(ToolDefinition tool, string? name, string? executable, string? version) =>
        tool is { Id: Id, AppId: AppId, Name: "CapchunScreen", InstalledExecutable: "CapchunScreen.exe", Msix: null }
        && name == Repository && executable == Executable && version == Version;
}
