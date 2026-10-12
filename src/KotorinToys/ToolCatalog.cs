using System.Text.Json;
using System.Text.RegularExpressions;

namespace KotorinToys;
internal sealed record StartupResult(bool Success,string Detail);

internal sealed record RuntimeRequirement(string Id, string Name, string Kind, string? Path = null,
    int? Major = null, bool Optional = false, string Feature = "基本機能", string? Guide = null);
internal sealed record RuntimeState(string Id, string Name, string State, string Feature, bool Optional, string Detail);
internal sealed record ToolDefinition(string Id, string Name, string Executable, string Manual,
    string Summary = "", string Category = "未分類", string Version = "", string ToolRoot = "",
    string AppId = "", string InstalledExecutable = "", string Readme = "README.md",
    string Shortcuts = "config/shortcuts.json", RuntimeRequirement[]? Runtimes = null,
    string DistributionStatus = "unpublished", string? SupportedEnvironment = null,
    string? Icon = null, string[]? Capabilities = null, string InvestigationStatus = "uninvestigated");
internal sealed record Catalog(int SchemaVersion, ToolDefinition[] Tools);
internal sealed record InstallationState(string State, string? Version, string? Directory,
    string? Executable, string? Uninstaller = null, string Detail = "");
internal sealed record StartupState(string? DesiredMode = null, string ActualState = "未確認", string Detail = "");
internal sealed record ToolSnapshot(ToolDefinition Definition, InstallationState Installation, RuntimeState[] Runtimes,
    ToolState Execution, StartupState Startup, DateTimeOffset RetrievedAt, string Evidence);

internal static class ToolCatalog
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static ToolDefinition[] Load(string root)
    {
        try { return LoadCore(root); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        { throw new IOException("カタログまたはツール定義の形式が不正です。", ex); }
    }
    static ToolDefinition[] LoadCore(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PathSafety.Resolve(root, "catalog/tools.json")));
        var schema = document.RootElement.GetProperty("schemaVersion").GetInt32();
        if (schema is not (1 or 2)) throw new IOException("未対応のカタログ形式です。");
        var tools = new List<ToolDefinition>();
        foreach (var entry in document.RootElement.GetProperty("tools").EnumerateArray())
        {
            ToolDefinition tool;
            if (schema == 2)
            {
                if (!entry.TryGetProperty("definition", out var definition)) throw new IOException("ツール定義への参照がありません。");
                var path = PathSafety.Resolve(root, definition.GetString() ?? "");
                using var data = JsonDocument.Parse(File.ReadAllText(path));
                if (data.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new IOException("未対応のツール定義です。");
                foreach(var required in new[] { "id", "name", "version", "summary", "category", "executable", "installedExecutable", "appId", "manual", "readme", "shortcuts", "distributionStatus", "supportedEnvironment", "investigationStatus", "capabilities", "runtimes" })
                    if(!data.RootElement.TryGetProperty(required,out _)) throw new IOException("ツール定義の必須項目がありません："+required);
                tool = data.RootElement.Deserialize<ToolDefinition>(Json) ?? throw new IOException("ツール定義が空です。");
                if (entry.GetProperty("id").GetString() != tool.Id) throw new IOException("登録IDとツール定義が一致しません。");
                tool = tool with { ToolRoot = Path.GetRelativePath(root, Path.GetDirectoryName(path)!) };
            }
            else tool = entry.Deserialize<ToolDefinition>(Json) ?? throw new IOException("登録情報が空です。");
            if (!Regex.IsMatch(tool.Id ?? "", "^[a-z][a-z0-9-]{0,63}$") || string.IsNullOrWhiteSpace(tool.Name))
                throw new IOException("ツールIDまたは名前が不正です。");
            var logicalRoot = string.IsNullOrEmpty(tool.ToolRoot) ? root : PathSafety.Resolve(root, tool.ToolRoot);
            PathSafety.Resolve(logicalRoot, tool.Executable);
            PathSafety.Resolve(logicalRoot, tool.Manual);
            PathSafety.Resolve(logicalRoot, tool.Readme);
            PathSafety.Resolve(logicalRoot, tool.Shortcuts);
            if (!string.IsNullOrEmpty(tool.InstalledExecutable)) PathSafety.Resolve(logicalRoot, tool.InstalledExecutable);
            if (!string.IsNullOrEmpty(tool.Icon)) PathSafety.Resolve(logicalRoot, tool.Icon);
            if (schema == 2 && (tool.SupportedEnvironment != "windows11-x64" || tool.InvestigationStatus != "confirmed"
                || tool.Capabilities == null || tool.Capabilities.Distinct().Count() != tool.Capabilities.Length
                || tool.Capabilities.Any(c => c is not ("status" or "show" or "stop" or "silent-start" or "shortcuts"))
                || tool.DistributionStatus is not ("unpublished" or "published")
                || !Regex.IsMatch(tool.Version, "^[0-9]+\\.[0-9]+\\.[0-9]+$")))
                throw new IOException("環境・調査状態・能力・版の定義が不正です。");
            if (!string.IsNullOrEmpty(tool.AppId) && !Regex.IsMatch(tool.AppId, "^KotorinToys\\.[A-Za-z0-9]+$"))
                throw new IOException("製品識別が不正です。");
            foreach (var runtime in tool.Runtimes ?? [])
            {
                if (string.IsNullOrWhiteSpace(runtime.Id) || string.IsNullOrWhiteSpace(runtime.Name)
                    || runtime.Kind is not ("file" or "dotnet-desktop" or "vc-x64" or "external")
                    || runtime.Kind == "file" && string.IsNullOrWhiteSpace(runtime.Path)
                    || runtime.Kind == "dotnet-desktop" && runtime.Major is not (> 0))
                    throw new IOException("依存情報が不正です。");
                if (!string.IsNullOrEmpty(runtime.Path)) PathSafety.Resolve(logicalRoot, runtime.Path);
            }
            if (tool.Runtimes?.Select(r => r.Id).Distinct().Count() != tool.Runtimes?.Length)
                throw new IOException("依存IDが重複しています。");
            tools.Add(tool);
        }
        if (tools.Count == 0 || tools.Select(t => t.Id).Distinct().Count() != tools.Count)
            throw new IOException("登録が空またはツールIDが重複しています。");
        return tools.ToArray();
    }
}

internal static class PathSafety
{
    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new IOException("相対パスが必要です。");
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(canonicalRoot, relative));
        var prefix = canonicalRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("許可範囲外のパスです。");
        // 存在する途中ディレクトリのリンクで許可範囲を迂回しない。
        var cursor = canonicalRoot;
        foreach (var part in Path.GetRelativePath(canonicalRoot, full).Split(Path.DirectorySeparatorChar))
        {
            cursor = Path.Combine(cursor, part);
            FileSystemInfo item = Directory.Exists(cursor) ? new DirectoryInfo(cursor) : new FileInfo(cursor);
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = item.ResolveLinkTarget(true)?.FullName;
                if (target == null || !target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("許可範囲外のリンクです。");
            }
        }
        return full;
    }
}
