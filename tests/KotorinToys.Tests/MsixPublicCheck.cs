using KotorinToys;

// 明示指定時だけ通信する。実アプリ・信頼ストア・設定・履歴は変更しない。
internal static class MsixPublicCheck
{
    public static async Task<int> Run(string root, string fixtures)
    {
        var tool = ToolCatalog.Load(root).Single(t => t.Id == "http-selector");
        using var client = new PublicReleaseClient();
        var release = await client.Check(tool, null);
        if (!release.Confirmed || release.Package == null) throw new IOException(release.Detail);
        using var downloader = new ReleaseDownloader();
        string catalog = await downloader.Prepare(new(1, [release.Package]), [tool with { Version = release.Package.Package.Version }], fixtures);
        string file = PathSafety.Resolve(Path.GetDirectoryName(catalog)!, release.Package.Package.File);
        using var verified = await MsixPackageVerifier.Open(tool, release.Package.Package, file);
        var actual = MsixInstallation.Detect(tool);
        Console.WriteLine($"公開MSIX: {ReleaseDisplay.Version(release.Package)} / {verified.Length} bytes / SHA-256 {release.Package.Package.Sha256}");
        Console.WriteLine($"Windows署名・固定署名者・Manifest・全payloadブロック照合: 成功");
        Console.WriteLine($"現在ユーザー登録: {actual.State} {actual.Version} / {actual.Detail}");
        Console.WriteLine("インストール・削除・起動・証明書登録・既定アプリ変更: 実行していません");
        return 0;
    }
}
