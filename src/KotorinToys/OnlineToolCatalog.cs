using System.Text.Json;
using System.Text.RegularExpressions;

namespace KotorinToys;

// 公開configは他ファイルを参照しない。一度の取得を検証してから一覧全体を採用する。
internal static class OnlineToolCatalog
{
    static readonly JsonSerializerOptions jsonOptions = new(ToolCatalog.Json)
    { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public const int MaximumBytes = 512 * 1024;
    public const int MaximumTools = 128;
    public const string RelativePath = "config/tools.json";
    public static readonly Uri Source = new("https://raw.githubusercontent.com/KotorinChunChun/KotorinToys/main/config/tools.json");

    public static ToolDefinition[] Parse(byte[] bytes, string root)
    {
        if (bytes.Length > MaximumBytes) throw new IOException("一覧configの容量上限を超えています。");
        try
        {
            var json = bytes.AsMemory();
            if (json.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) json = json[3..];
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicateProperties(document.RootElement);
            foreach (var entry in document.RootElement.GetProperty("tools").EnumerateArray())
                foreach (var key in new[] { "id", "name", "version", "summary", "category", "executable", "installedExecutable", "appId", "manual", "readme", "toolRoot", "distributionStatus", "supportedEnvironment", "investigationStatus", "capabilities", "runtimes" })
                    if (!entry.TryGetProperty(key, out _)) throw new IOException("一覧configの必須項目がありません：" + key);
            var catalog = document.RootElement.Deserialize<Catalog>(jsonOptions) ?? throw new IOException("一覧configが空です。");
            if (catalog.SchemaVersion != 1) throw new IOException("未対応の一覧config形式です。");
            Validate(catalog.Tools, root, MaximumTools);
            // 公開configでの未設定は無効化を意味する。旧ローカル定義用の固定URLへ戻さない。
            return catalog.Tools.Select(t => t with { RepositoryUrl = t.RepositoryUrl ?? "" }).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or NullReferenceException)
        { throw new IOException("一覧configの形式が不正です。", ex); }
    }

    internal static void Validate(ToolDefinition[]? tools, string root, int maximum)
    {
        // 空一覧は全件を非表示にする明示的な設定として許可する。
        if (tools == null || tools.Length > maximum) throw new IOException("一覧configの件数が不正です。");
        foreach (var tool in tools)
        {
            if (tool == null || tool.Id is "host" or "dashboard" || string.IsNullOrWhiteSpace(tool.AppId) ||
                !Regex.IsMatch(tool.Name ?? "", "^[A-Za-z][A-Za-z0-9.-]{0,79}$") || tool.Summary == null || tool.Summary.Length > 500 ||
                tool.ToolRoot == null || !Regex.IsMatch(tool.ToolRoot, "^tools/[A-Za-z0-9._-]+$") ||
                tool.ToolRoot is "tools/." or "tools/.." || tool.Runtimes == null ||
                new[] { tool.Executable, tool.InstalledExecutable, tool.Manual, tool.Readme, tool.Shortcuts }.Any(p => p == null || p.Contains(':') || p.Any(char.IsControl)) ||
                !string.Equals(Path.GetExtension(tool.Executable), ".exe", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(tool.InstalledExecutable), ".exe", StringComparison.OrdinalIgnoreCase) ||
                !DocumentPath(tool.Manual) || !DocumentPath(tool.Readme))
                throw new IOException("一覧configの製品・パス・説明が不正です。");
            ToolCatalog.Validate(tool, root);
            if (!string.IsNullOrEmpty(tool.RepositoryUrl) && !IsRepositoryUrl(tool.RepositoryUrl)) throw new IOException("GitHubトップページのURLが不正です。");
        }
        if (tools.Select(t => t.Id).Distinct().Count() != tools.Length ||
            tools.Select(t => t.AppId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tools.Length)
            throw new IOException("一覧configのIDまたは製品識別が重複しています。");
    }

    static bool DocumentPath(string? value) => value != null && Path.GetExtension(value).ToLowerInvariant() is ".md" or ".txt" or ".pdf";
    internal static bool IsRepositoryUrl(string value) => Regex.IsMatch(value,
        "^https://github\\.com/KotorinChunChun/[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant);

    static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new IOException("一覧configのプロパティが重複しています。");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
