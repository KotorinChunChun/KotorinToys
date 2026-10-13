using System.Text.Json;

namespace KotorinToys;

internal sealed record TaskOwnership(string Name, string Fingerprint, string Xml, string SecurityDescriptor = "");
internal sealed record HostSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string OwnerToken { get; init; } = Guid.NewGuid().ToString("N");
    public bool HostStartup { get; init; }
    public bool ShowDashboardAtLogon { get; init; }
    public Dictionary<string, string> StartupModes { get; init; } = new();
    public Dictionary<string, TaskOwnership> OwnedTasks { get; init; } = new();
}

internal sealed record StartupJournal(string OperationId, string ToolId, HostSettings Before, string Phase, Dictionary<string, TaskOwnership> CreatedTasks, Dictionary<string, TaskPlan> PlannedTasks);
internal sealed record StartupAttemptItem(string ToolId, string Result, string Detail, int? Pid = null);
internal sealed record StartupAttempt(DateTimeOffset AttemptedAt, StartupAttemptItem[] Items);
