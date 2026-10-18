using KotorinToys;

// 明示指定時だけ通信する。実アプリ・信頼ストア・設定・履歴は変更しない。
internal static class MsixPublicCheck
{
    public static async Task<int> Run(string root, string fixtures)
    {
        var source = new ToolCatalogSource(root, Path.Combine(fixtures, "catalog-cache"));
        var refresh = await source.Refresh();
        if (!refresh.Accepted) throw new IOException(refresh.Detail);
        var tool = source.Load().Single(t => t.Id == "http-selector");
        using var client = new PublicReleaseClient();
        var fallback = ReleaseDownloader.Load(root).Packages.Single(p => p.Package.Id == tool.Id);
        var release = await client.Check(tool, fallback);
        if (!release.Confirmed || release.Package == null) throw new IOException(release.Detail);
        using var downloader = new ReleaseDownloader();
        string catalog = await downloader.Prepare(new(1, [release.Package]), [tool with { Version = release.Package.Package.Version }], fixtures);
        string file = PathSafety.Resolve(Path.GetDirectoryName(catalog)!, release.Package.Package.File);
        using var verified = await MsixPackageVerifier.Open(tool, release.Package.Package, file);
        var actual = MsixInstallation.Detect(tool);
        Console.WriteLine($"公開MSIX: {ReleaseDisplay.Version(release.Package)} / {verified.Length} bytes / SHA-256 {release.Package.Package.Sha256}");
        Console.WriteLine($"Windows署名・固定署名者・Manifest・全payloadブロック照合: 成功");
        Console.WriteLine($"現在ユーザー登録: {actual.State} {actual.Version} / {actual.Detail}");
        if (MsixPolicy.Installed(actual))
        {
            var registered = new Windows.Management.Deployment.PackageManager().FindPackageForUser("", $"{MsixPolicy.Name}_{actual.Version}_x64__xtxrnffqrdk4w");
            if (registered?.Id.FamilyName != MsixPolicy.Family) throw new IOException("PackageManagerの現在ユーザー登録が一致しません。");
            Console.WriteLine("Windows PackageManagerの現在ユーザー登録も一致");
        }
        string tampered = Path.Combine(fixtures, "tampered.msix"); File.Copy(file, tampered, false);
        using (var zip = System.IO.Compression.ZipFile.Open(tampered, System.IO.Compression.ZipArchiveMode.Update))
        using (var payload = zip.GetEntry("HttpSelector.exe")!.Open())
        { int value = payload.ReadByte(); payload.Position = 0; payload.WriteByte((byte)(value ^ 1)); }
        string alteredHash; using (var input = File.OpenRead(tampered)) alteredHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).ToLowerInvariant();
        bool rejected = false;
        try { using var invalid = await MsixPackageVerifier.Open(tool, release.Package.Package with { Sha256 = alteredHash }, tampered); }
        catch (IOException) { rejected = true; }
        if (!rejected) throw new IOException("改変した実MSIXを拒否できません。");
        Console.WriteLine("実MSIXのpayload改変は台帳ハッシュを付け直しても拒否");
        Console.WriteLine($"GitHub config採用: {source.Load().Length}件 / MSIXの公開取得・署名検証経路へ接続済み");
        Console.WriteLine("インストール・削除・起動・証明書登録・既定アプリ変更: 実行していません");
        return 0;
    }
}
