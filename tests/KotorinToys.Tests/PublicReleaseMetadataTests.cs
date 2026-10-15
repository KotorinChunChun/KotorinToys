using System.Text.Json;
using KotorinToys;

internal static class PublicReleaseMetadataTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var tool = ToolCatalog.Load(root).Single(t => t.Id == "smart-paste");
        var package = new LocalPackage(tool.Id, tool.Name, "0.1.10", tool.AppId, "SmartPaste-0.1.10-setup.exe",
            new string('a', 64), "unsigned-public-build");
        string Url(string file) => "https://github.com/KotorinChunChun/SmartPaste/releases/download/v0.1.10/" + file;
        object Asset(string name, long size, string? digest = null) =>
            new { name, state = "uploaded", size, browser_download_url = Url(name), digest };
        object Release(string tag = "v0.1.10", bool draft = false, bool prerelease = true, object[]? assets = null) =>
            new { tag_name = tag, draft, prerelease, assets = assets ?? [Asset("SmartPaste-packages.json", 512), Asset(package.File, 4, "sha256:" + package.Sha256)] };
        PublicReleaseMetadata Select(params object[] values)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(values));
            return PublicReleaseMetadata.Select(document.RootElement);
        }
        string Manifest(LocalPackage value) => JsonSerializer.Serialize(new PackageCatalog(1, "published", [value]), ToolCatalog.Json);
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (IOException) { rejected = true; }
            check(rejected, message);
        }

        var metadata = Select(Release("v0.1.9"), Release("v9.0.0", draft: true), Release());
        check(metadata.Version == "0.1.10" && metadata.Prerelease, "通信なしで数値順・下書き除外・開発版表示を判定");
        var manifest = metadata.Asset(tool, "SmartPaste-packages.json");
        var installer = metadata.Asset(tool, package.File);
        check(manifest.GetProperty("size").GetInt64() == 512 && installer.GetProperty("size").GetInt64() == 4,
            "元JsonDocumentの破棄後も選択候補とassetを保持");
        check(metadata.Package(tool, Manifest(package), installer, null) == new ReleasePackage(package, 4),
            "通信なしで公開製品別台帳から導入候補を生成");
        var matching = new ReleasePackage(package, 4);
        check(metadata.Package(tool, "\uFEFF" + Manifest(package), installer, matching) == matching,
            "純粋な検証でもBOMと同梱固定版の完全一致を許可");
        check(!Select(Release(prerelease: false)).Prerelease, "正式版の公開フラグを変更しない");
        Reject(() => Select(), "純粋な候補選択で空一覧を拒否");
        Reject(() => Select(Enumerable.Repeat(Release(), 100).ToArray()), "純粋な候補選択で100件の未完了一覧を拒否");
        Reject(() => metadata.Asset(tool, "missing.exe"), "純粋な検証で欠落assetを拒否");
        Reject(() => Select(Release(assets: [Asset(package.File, 4), Asset(package.File, 4)])).Asset(tool, package.File),
            "純粋な検証で重複assetを拒否");
        Reject(() => metadata.Asset(tool with { Name = "SSCapture" }, package.File), "純粋な検証でも別製品URLを拒否");
        Reject(() => metadata.Package(tool, "null", installer, null), "空の公開台帳を成功にしない");
        Reject(() => metadata.Package(tool, Manifest(package with { Version = "0.1.9", File = "SmartPaste-0.1.9-setup.exe" }), installer, null),
            "純粋な台帳検証でリリースtagとの版不一致を拒否");
        Reject(() => metadata.Package(tool, Manifest(package), installer, matching with { Length = 5 }),
            "同じ版のassetサイズ差し替えを純粋な台帳検証でも拒否");
        var changed = Select(Release(assets: [Asset(package.File, 4, "sha256:" + new string('b', 64))]));
        Reject(() => changed.Package(tool, Manifest(package), changed.Asset(tool, package.File), null),
            "純粋な台帳検証でもGitHub digest不一致を拒否");
        var withoutDigest = Select(Release(assets: [Asset(package.File, 4)]));
        check(withoutDigest.Package(tool, Manifest(package), withoutDigest.Asset(tool, package.File), null) == matching,
            "digest未提供の従来互換を維持し台帳の固定ハッシュを使用");
    }
}
