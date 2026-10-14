namespace KotorinToys;

// 配布とメンテナンスが共有する固定の製品識別。表示名で曖昧検索しない。
internal sealed record ProductIdentity(string Id, string Name, string AppId)
{
    static readonly ProductIdentity[] products =
    [
        new("host", "KotorinToys", "KotorinToys"),
        new("live-text-marker", "LiveTextMarker", "KotorinToys.LiveTextMarker"),
        new("smart-paste", "SmartPaste", "KotorinToys.SmartPaste"),
        new("ss-capture", "SSCapture", "KotorinToys.SSCapture"),
        new("shortcut-key-counter", "ShortcutKeyCounter", "KotorinToys.ShortcutKeyCounter")
    ];
    static readonly Dictionary<string, ProductIdentity> byId = products.ToDictionary(p => p.Id, StringComparer.Ordinal);
    static readonly Dictionary<string, ProductIdentity> byName = products.ToDictionary(p => p.Name, StringComparer.Ordinal);

    public static ProductIdentity? FromId(string? id) => id is null ? null : byId.GetValueOrDefault(id);
    public static ProductIdentity? FromName(string? name) => name is null ? null : byName.GetValueOrDefault(name);
}
