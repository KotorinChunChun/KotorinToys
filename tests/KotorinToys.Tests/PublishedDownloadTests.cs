using System.Text.Json;
using KotorinToys;

internal static class PublishedDownloadTests
{
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
