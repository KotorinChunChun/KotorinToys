using System.Text.Json;

namespace KotorinToys;

internal sealed record CatalogRefresh(ToolDefinition[] Tools, string Detail, bool Accepted);
internal sealed record CatalogCache(int SchemaVersion, string Root, ToolDefinition[] Tools, ToolDefinition[] RetiredTools);

// 利用者の設定・履歴とは独立した、最後に検証できた一覧。取得は呼出側で集約する。
internal sealed class ToolCatalogSource
{
    readonly string root, cachePath;
    readonly Func<CancellationToken, Task<byte[]>> fetch;
    readonly object sync = new();
    ToolDefinition[] tools, retired = [];
    Task<CatalogRefresh>? pending;
    public string Detail { get; private set; } = "ツール一覧：同梱config";

    public ToolCatalogSource(string root, string cacheDirectory, Func<CancellationToken, Task<byte[]>>? fetch = null)
    {
        this.root = Path.GetFullPath(root);
        cachePath = PathSafety.Resolve(cacheDirectory, "tool-catalog-cache.json");
        this.fetch = fetch ?? Fetch;
        var bundled = PathSafety.Resolve(root, OnlineToolCatalog.RelativePath);
        tools = File.Exists(bundled) ? OnlineToolCatalog.Parse(File.ReadAllBytes(bundled), root) :
            ToolCatalog.Load(root).Select(t => t with { ToolRoot = t.ToolRoot.Replace('\\', '/') }).ToArray();
        try
        {
            if (!File.Exists(cachePath)) return;
            if (new FileInfo(cachePath).Length > 4 * OnlineToolCatalog.MaximumBytes) throw new IOException("キャッシュが大きすぎます。");
            var cache = JsonSerializer.Deserialize<CatalogCache>(File.ReadAllBytes(cachePath), ToolCatalog.Json) ?? throw new IOException("キャッシュが空です。");
            if (cache.SchemaVersion != 1 || !string.Equals(this.root, cache.Root, StringComparison.OrdinalIgnoreCase)) throw new IOException("別配置のキャッシュです。");
            OnlineToolCatalog.Validate(cache.Tools, root, OnlineToolCatalog.MaximumTools);
            OnlineToolCatalog.Validate(cache.RetiredTools, root, 512);
            ValidateIdentities(cache.Tools.Concat(cache.RetiredTools).ToArray(), tools);
            var current = cache.Tools.Select(t => t.Id).ToHashSet();
            // 同梱定義は一度非表示になっても予約の照合・解除用に残す。
            var history = cache.RetiredTools.Concat(tools).Where(t => !current.Contains(t.Id)).DistinctBy(t => t.Id).ToArray();
            ValidateIdentities(cache.Tools.Concat(history).ToArray(), []);
            if (history.Length > 512) throw new IOException("旧登録の保持件数の上限です。");
            retired = history;
            tools = cache.Tools;
            Detail = "ツール一覧：前回取得したconfig（未再確認）";
        }
        catch (Exception ex) when (IsRecoverable(ex)) { Detail = "ツール一覧：キャッシュを使えないため同梱一覧。" + ex.Message; }
    }

    public ToolDefinition[] Load() { lock (sync) return tools.ToArray(); }
    public ToolDefinition[] KnownTools() { lock (sync) return tools.Concat(retired).ToArray(); }

    public Task<CatalogRefresh> Refresh(CancellationToken cancellation = default)
    {
        lock (sync)
        {
            if (pending is { IsCompleted: false }) return pending;
            return pending = RefreshCore(cancellation);
        }
    }

    async Task<CatalogRefresh> RefreshCore(CancellationToken cancellation)
    {
        try
        {
            var bytes = await fetch(cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var next = OnlineToolCatalog.Parse(bytes, root);
            lock (sync)
            {
                ValidateIdentities(next, tools.Concat(retired).ToArray());
                var ids = next.Select(t => t.Id).ToHashSet();
                var history = tools.Concat(retired).Where(t => !ids.Contains(t.Id)).DistinctBy(t => t.Id).ToArray();
                if (history.Length > 512) throw new IOException("旧登録の保持件数の上限です。キャッシュを確認してください。");
                if (next.Concat(history).Select(t => t.AppId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != next.Length + history.Length)
                    throw new IOException("旧登録と製品識別が重複しています。");
                var cache = JsonSerializer.SerializeToUtf8Bytes(new CatalogCache(1, root, next, history), ToolCatalog.Json);
                if (cache.Length > 4 * OnlineToolCatalog.MaximumBytes) throw new IOException("キャッシュの容量上限です。");
                // 保存できない一覧は採用せず、次回起動時との食い違いを避ける。
                Save(cache);
                tools = next; retired = history;
                Detail = $"ツール一覧：GitHub config取得済み {tools.Length}件（{DateTime.Now:MM/dd HH:mm}）";
                return new(tools.ToArray(), Detail, true);
            }
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            cancellation.ThrowIfCancellationRequested();
            lock (sync)
            {
                Detail = "ツール一覧：取得失敗・現在の一覧を維持。" + ex.Message;
                return new(tools.ToArray(), Detail, false);
            }
        }
    }

    static void ValidateIdentities(ToolDefinition[] next, ToolDefinition[] previous)
    {
        var known = previous.ToDictionary(t => t.Id);
        if (next.Select(t => t.Id).Distinct().Count() != next.Length ||
            next.Select(t => t.AppId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != next.Length)
            throw new IOException("キャッシュまたは一覧の製品識別が重複しています。");
        foreach (var tool in next)
            if (known.TryGetValue(tool.Id, out var old) && (old.AppId != tool.AppId || old.Msix != tool.Msix))
                throw new IOException("既存IDの製品識別・MSIX識別は変更できません：" + tool.Id);
    }

    void Save(byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var temporary = PathSafety.Resolve(Path.GetDirectoryName(cachePath)!, "tool-catalog-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, cachePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static bool IsRecoverable(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or
        HttpRequestException or OperationCanceledException or ArgumentException or InvalidOperationException or NullReferenceException;
    static async Task<byte[]> Fetch(CancellationToken cancellation)
    {
        using var client = new ToolCatalogClient();
        return await client.Fetch(cancellation).ConfigureAwait(false);
    }
}
