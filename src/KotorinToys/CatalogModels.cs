using System.Text.Json;
using System.Text.RegularExpressions;

namespace KotorinToys;

internal sealed record StartupResult(bool Success, string Detail);
internal sealed record RuntimeRequirement(string Id, string Name, string Kind, string? Path = null, int? Major = null, bool Optional = false, string Feature = "基本機能", string? Guide = null);
internal sealed record RuntimeState(string Id, string Name, string State, string Feature, bool Optional, string Detail);
internal sealed record MsixDefinition(string FamilyName, string Publisher, string ApplicationId = "App");
internal sealed record ToolDefinition(
    string Id, string Name, string Executable, string Manual,
    string Summary = "", string Category = "未分類", string Version = "", string ToolRoot = "",
    string AppId = "", string InstalledExecutable = "", string Readme = "README.md",
    string Shortcuts = "config/shortcuts.json", RuntimeRequirement[]? Runtimes = null,
    string DistributionStatus = "unpublished", string? SupportedEnvironment = null,
    string? Icon = null, string[]? Capabilities = null, string InvestigationStatus = "uninvestigated", MsixDefinition? Msix = null,
    string? RepositoryUrl = null);
internal sealed record Catalog(int SchemaVersion, ToolDefinition[] Tools);
internal sealed record InstallationState(string State, string? Version, string? Directory, string? Executable, string? Uninstaller = null, string Detail = "");
internal sealed record StartupState(string? DesiredMode = null, string ActualState = "未確認", string Detail = "");
internal sealed record ToolSnapshot(ToolDefinition Definition, InstallationState Installation, RuntimeState[] Runtimes, ToolState Execution, StartupState Startup, DateTimeOffset RetrievedAt, string Evidence);
