using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace KotorinToys;

internal sealed record ToolState(string State, int? Pid = null, string Detail = "", string LaunchOrigin = "unknown", string? Version = null, int ProtocolVersion = 0,
    string? RunningExecutable = null, string? ExpectedExecutable = null, DateTime? ProcessStartedAtUtc = null);
internal sealed record Reply(int Pid, bool Ready, int ProtocolVersion = 1, string Result = "", string Detail = "", string LaunchOrigin = "unknown", string? Version = null, string[]? Capabilities = null, JsonElement? Shortcuts = null);
