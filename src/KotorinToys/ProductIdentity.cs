namespace KotorinToys;

// 配布とメンテナンスが共有する固定の製品識別。表示名で曖昧検索しない。
internal sealed record ProductIdentity(string Id, string Name, string AppId, string? RepositoryName = null)
{
    public string Repository => RepositoryName ?? Name;
    static readonly ProductIdentity[] products =
    [
        new("voice-mixer-spk-router", "VoiceMixerSpkRouter", "KotorinToys.VoiceMixerSpkRouter"),
        new("host", "KotorinToys", "KotorinToys"),
        new("live-text-marker", "LiveTextMarker", "KotorinToys.LiveTextMarker"),
        new("smart-paste", "SmartPaste", "KotorinToys.SmartPaste"),
        new("ss-capture", "SSCapture", "KotorinToys.SSCapture"),
        new("shortcut-key-counter", "ShortcutKeyCounter", "KotorinToys.ShortcutKeyCounter"),
        new("capchun-screen", "CapchunWindow", "KotorinToys.CapchunScreen"),
        new("chulip-video", "ChulipVideo", "KotorinToys.ChulipVideo", "ChulipVideo-CSharp"),
        new("chunap-tool", "ChunapTool", "KotorinToys.ChunapTool"),
        new("http-selector", "HttpSelector", "KotorinToys.HttpSelector"),
        new("every-db", "EveryDB", "KotorinToys.EveryDB"),
        new("voice-mixer-mic-router", "VoiceMixerMicRouter", "KotorinToys.VoiceMixerMicRouter")
    ];
    static readonly Dictionary<string, ProductIdentity> byId = products.ToDictionary(p => p.Id, StringComparer.Ordinal);
    static readonly Dictionary<string, ProductIdentity> byName = products.ToDictionary(p => p.Name, StringComparer.Ordinal);

    public static ProductIdentity? FromId(string? id, IReadOnlyCollection<ToolDefinition>? definitions = null)
    {
        if (id == null) return null;
        if (byId.TryGetValue(id, out var known)) return known;
        var matches = definitions?.Where(t => t.Id == id).ToArray();
        return matches?.Length == 1 ? FromTool(matches[0]) : null;
    }
    public static ProductIdentity? FromName(string? name) => name is null ? null : byName.GetValueOrDefault(name);

    // 検証済みの定義を呼出し単位で渡す。グローバルな製品許可表へ登録しない。
    public static ProductIdentity? FromTool(ToolDefinition tool)
    {
        if (tool == null || string.IsNullOrWhiteSpace(tool.Id)) return null;
        if (byId.TryGetValue(tool.Id, out var known))
            return known.Name == tool.Name && known.AppId == tool.AppId &&
                (string.IsNullOrEmpty(tool.RepositoryUrl) || tool.RepositoryUrl == "https://github.com/KotorinChunChun/" + known.Repository) ? known : null;
        if (tool.Msix != null || tool.Id is "host" or "dashboard" || tool.Name == "KotorinToys" ||
            tool.AppId != "KotorinToys." + tool.Name || tool.InstalledExecutable != tool.Name + ".exe" ||
            string.IsNullOrEmpty(tool.RepositoryUrl) || !OnlineToolCatalog.IsRepositoryUrl(tool.RepositoryUrl) ||
            products.Any(p => p.Name == tool.Name || p.AppId.Equals(tool.AppId, StringComparison.OrdinalIgnoreCase)) ||
            new[] { "status", "show", "stop", "silent-start" }.Any(c => tool.Capabilities?.Contains(c) != true)) return null;
        try
        {
            OnlineToolCatalog.Validate([tool with { ToolRoot = "tools/" + tool.Name }], Path.GetTempPath(), 1);
            return new(tool.Id, tool.Name, tool.AppId, new Uri(tool.RepositoryUrl).Segments.Last());
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { return null; }
    }
}
