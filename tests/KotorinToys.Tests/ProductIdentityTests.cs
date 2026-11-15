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
        check(ProductIdentity.FromId("chulip-video")?.Repository == "ChulipVideo-CSharp", "C#版は同名Python版ではなく専用リポジトリへ固定");
        const string old = CapchunScreenLegacy.ProductName;
        const string newUrl = "https://github.com/KotorinChunChun/CapchunScreen";
        var legacy = new ToolDefinition("capchun-screen", old, $"artifacts/management/bin/CapchunScreen/debug/{old}.exe",
            "README.md", Version: "0.1.2", ToolRoot: "tools/CapchunScreen", AppId: "KotorinToys.CapchunScreen",
            InstalledExecutable: old + ".exe", DistributionStatus: "published",
            RepositoryUrl: "https://github.com/KotorinChunChun/" + old);
        var current = CapchunScreenLegacy.NormalizeCatalog(legacy);
        check(current.Name == "CapchunScreen" && current.InstalledExecutable == "CapchunScreen.exe" &&
              current.Executable.EndsWith("/CapchunScreen.exe", StringComparison.Ordinal) && current.DistributionStatus == "unpublished" &&
              current.RepositoryUrl == newUrl && ProductIdentity.FromTool(current)?.Repository == "CapchunScreen",
              "旧公開一覧の製品名とリポジトリURLを更新し、自動導入は未公開として扱う");
        var currentWithOldUrl = current with { RepositoryUrl = legacy.RepositoryUrl };
        var otherWithOldUrl = currentWithOldUrl with { Id = "other" };
        check(CapchunScreenLegacy.NormalizeCatalog(currentWithOldUrl) == current &&
              ProductIdentity.FromTool(currentWithOldUrl)?.Repository == "CapchunScreen" &&
              CapchunScreenLegacy.NormalizeCatalog(otherWithOldUrl) == otherWithOldUrl,
              "新製品名の旧URLは該当製品だけ読み替える");
        check(CapchunScreenLegacy.NormalizeCatalog(legacy with { RepositoryUrl = newUrl }) == current &&
              CapchunScreenLegacy.NormalizeCatalog(legacy with { RepositoryUrl = "https://github.com/Other/CapchunWindow" }).Name == old,
              "新URLで旧製品名が配信された場合も取り込み、別の所有者のURLへの置き換えは行わない");
        check(CapchunScreenLegacy.NormalizeCatalog(current) == current &&
              CapchunScreenLegacy.NormalizeCatalog(legacy with { AppId = "Other" }).Name == old &&
              CapchunScreenLegacy.NormalizeCatalog(legacy with { InstalledExecutable = "Other.exe" }).Name == old &&
              CapchunScreenLegacy.NormalizeCatalog(legacy with { Version = "0.1.3" }).Name == old,
              "旧一覧の読み替えは一致する製品・実行ファイル・版だけに限る");
        check(CapchunScreenLegacy.MatchesInstallation(current, old, old + ".exe", "0.1.2") &&
              !CapchunScreenLegacy.MatchesInstallation(current, old, old + ".exe", "0.1.3") &&
              !CapchunScreenLegacy.MatchesInstallation(current with { AppId = "Other" }, old, old + ".exe", "0.1.2"),
              "旧インストールの照合は旧定義のv0.1.2だけを許可する");
    }
}
