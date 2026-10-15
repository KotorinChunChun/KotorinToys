namespace KotorinToys;

// 子Gitのremoteと.gitmodulesで確認したトップページだけを使用する。
// 配布物の公開状態とは別。未設定製品のURLを名前から推測しない。
internal static class ToolRepository
{
    static readonly Dictionary<string, string> repositories = new(StringComparer.Ordinal)
    {
        ["live-text-marker"] = "https://github.com/KotorinChunChun/LiveTextMarker",
        ["smart-paste"] = "https://github.com/KotorinChunChun/SmartPaste",
        ["ss-capture"] = "https://github.com/KotorinChunChun/SSCapture",
        ["shortcut-key-counter"] = "https://github.com/KotorinChunChun/ShortcutKeyCounter"
    };

    public static Uri? Find(ToolDefinition tool)
    {
        var product = ProductIdentity.FromId(tool.Id);
        return product != null && product.Name == tool.Name && product.AppId == tool.AppId &&
            repositories.TryGetValue(tool.Id, out var url) ? new Uri(url) : null;
    }
}
