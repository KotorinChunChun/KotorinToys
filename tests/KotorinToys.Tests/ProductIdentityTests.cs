using KotorinToys;

internal static class ProductIdentityTests
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var (id, name, appId) in new[]
        {
            ("host", "KotorinToys", "KotorinToys"),
            ("live-text-marker", "LiveTextMarker", "KotorinToys.LiveTextMarker"),
            ("smart-paste", "SmartPaste", "KotorinToys.SmartPaste"),
            ("ss-capture", "SSCapture", "KotorinToys.SSCapture"),
            ("shortcut-key-counter", "ShortcutKeyCounter", "KotorinToys.ShortcutKeyCounter"),
            ("capchun-screen", "CapchunWindow", "KotorinToys.CapchunScreen"),
            ("chulip-video", "ChulipVideo", "KotorinToys.ChulipVideo"),
            ("chunap-tool", "ChunapTool", "KotorinToys.ChunapTool"),
            ("http-selector", "HttpSelector", "KotorinToys.HttpSelector")
        })
        {
            var byId = ProductIdentity.FromId(id);
            var byName = ProductIdentity.FromName(name);
            check(byId is { } && byId.Name == name && byId.AppId == appId, name + " IDから固定製品識別を取得");
            check(byName == byId, name + " 配布とメンテナンスの製品識別が一致");
        }
        check(ProductIdentity.FromId("unknown") is null && ProductIdentity.FromName("Unknown") is null, "未知の製品を補完しない");
        check(ProductIdentity.FromId(null) is null && ProductIdentity.FromName(null) is null, "欠落した製品識別を補完しない");
        check(ProductIdentity.FromName("smartpaste") is null && ProductIdentity.FromId("SMART-PASTE") is null, "製品識別の大文字小文字を曖昧一致しない");
        check(ProductIdentity.FromId("chulip-video")?.Repository == "ChulipVideo-CSharp", "C#版は同名Python版ではなく専用リポジトリへ固定");
        var capchun = new ToolDefinition("capchun-screen", "CapchunWindow", "CapchunWindow.exe", "README.md",
            AppId: "KotorinToys.CapchunScreen", RepositoryUrl: "https://github.com/KotorinChunChun/CapchunWindow");
        check(ProductIdentity.FromTool(capchun)?.Repository == "CapchunScreen" &&
            ProductIdentity.FromTool(capchun with { RepositoryUrl = "https://github.com/KotorinChunChun/CapchunScreen" })?.Repository == "CapchunScreen" &&
            ProductIdentity.FromTool(capchun with { RepositoryUrl = "https://github.com/OtherOwner/CapchunScreen" }) is null,
            "CapchunWindowの旧定義と新定義を正規のCapchunScreenに解決し別所有者を拒否");
    }
}
