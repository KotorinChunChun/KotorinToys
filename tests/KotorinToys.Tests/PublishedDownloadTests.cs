using System.Text.Json;
using KotorinToys;

internal static class PublishedDownloadTests
{
    public static async Task RunCatalog(string root, string output)
    {
        var source = new ToolCatalogSource(root, output);
        var result = await source.Refresh();
        var manager = new ToolManager(root, catalogSource: source);
        var tools = manager.Load();
        if (!result.Accepted || tools.Length != 9 || tools.Count(t => t.DistributionStatus == "published") != 6 ||
            tools.Single(t => t.Id == "shortcut-key-counter").DistributionStatus != "unpublished")
            throw new IOException("公開configを管理一覧へ採用できません：" + result.Detail);
        using var client = new PublicReleaseClient();
        var bundled = ReleaseDownloader.Load(root);
        var releases = new List<PublicRelease>();
        foreach (var tool in tools.Where(t => t.DistributionStatus == "published"))
        {
            var release = await client.Check(tool, bundled.Packages.Single(p => p.Package.Id == tool.Id));
            if (!release.Confirmed || release.Package?.PublishedAt == null) throw new IOException(tool.Name + "の公開版・日付を照合できません：" + release.Detail);
            releases.Add(release);
            Console.WriteLine(tool.Name + "：" + ReleaseDisplay.Version(release.Package));
        }
        await File.WriteAllTextAsync(Path.Combine(output, "published-catalog-receipt.json"), JsonSerializer.Serialize(new { result.Detail, Tools = tools, Releases = releases }, ToolCatalog.Json));
        Console.WriteLine("実GitHub configから管理一覧9件へ採用、本体対応6件の公開版と公開日照合：成功（利用者設定変更なし）");
    }

    // 明示引数でのみ実行。匿名HTTPで本体と同じ検証・取得経路を使い、EXEは実行しない。
    public static async Task Run(string root, string output, bool host)
    {
        using var client = new PublicReleaseClient();
        using var downloader = new ReleaseDownloader();
        var receipts = new List<object>();
        if (host)
        {
            var release = await client.CheckHost(true);
            if (!release.Confirmed || release.Package?.Package.Version != HostProduct.CurrentVersion || release.Package.PublishedAt == null)
                throw new IOException("今回公開した本体と公開日時を照合できません：" + release.Detail);
            var catalog = await downloader.PrepareHost(release.Package, "0.1.1", output, new Progress<string>(Console.WriteLine));
            receipts.Add(new { Product = "KotorinToys", Release = release, Catalog = catalog });
        }
        else
        {
            var bundled = ReleaseDownloader.Load(root);
            foreach (var tool in ToolCatalog.Load(root).Where(t => t.Id is "capchun-screen" or "chulip-video" or "chunap-tool"))
            {
                var release = await client.Check(tool, bundled.Packages.Single(p => p.Package.Id == tool.Id));
                if (!release.Confirmed || release.Package == null || release.Package.PublishedAt == null)
                    throw new IOException(tool.Name + "の公開を照合できません：" + release.Detail);
                var catalog = await downloader.Prepare(new(1, [release.Package]), [tool with { Version = release.Package.Package.Version }], output,
                    new Progress<string>(Console.WriteLine));
                receipts.Add(new { Product = tool.Name, Release = release, Catalog = catalog });
            }
        }
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "published-download-receipts.json"), JsonSerializer.Serialize(receipts, ToolCatalog.Json));
        Console.WriteLine($"公開配布物の匿名再取得・容量・SHA-256・公開日照合：{receipts.Count}件成功（EXE実行なし）");
    }
}
