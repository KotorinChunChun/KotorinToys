using KotorinToys;

internal static class BundledReleaseFixture
{
    internal static ReleaseCatalog Load(string root)
    {
        var config = File.ReadAllBytes(Path.Combine(root, OnlineToolCatalog.RelativePath));
        return ReleaseDownloader.Load(root, OnlineToolCatalog.Parse(config, root));
    }
}
