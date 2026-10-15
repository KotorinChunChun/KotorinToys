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
            ("capchun-screen", "CapchunScreen", "KotorinToys.CapchunScreen"),
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
    }
}
