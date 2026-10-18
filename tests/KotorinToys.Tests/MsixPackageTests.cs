using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using KotorinToys;

internal static class MsixPackageTests
{
    public static MsixSigner Trusted(string _) => new(MsixPolicy.Publisher, MsixPolicy.Thumbprint);
    public static (string Catalog, LocalPackage Package, string File) Fixture(string root, string publisher = MsixPolicy.Publisher,
        string identity = MsixPolicy.Name, string version = "0.0.3.3", string architecture = "x64", bool tamper = false,
        bool signed = true, string? extra = null, string application = "App", bool corruptMap = false)
    {
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, MsixPolicy.FileName("0.0.3.3"));
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var manifest = new XDocument(new XElement(ns + "Package", new XElement(ns + "Identity",
            new XAttribute("Name", identity), new XAttribute("Publisher", publisher), new XAttribute("Version", version), new XAttribute("ProcessorArchitecture", architecture)),
            new XElement(ns + "Applications", new XElement(ns + "Application", new XAttribute("Id", application), new XAttribute("Executable", "HttpSelector.exe"), new XAttribute("EntryPoint", "Windows.FullTrustApplication")))));
        byte[] payload = new byte[140001]; Random.Shared.NextBytes(payload);
        var entries = new Dictionary<string, byte[]> { ["HttpSelector.exe"] = payload, ["AppxManifest.xml"] = Encoding.UTF8.GetBytes(manifest.ToString()) };
        XNamespace bm = "http://schemas.microsoft.com/appx/2010/blockmap";
        var map = new XElement(bm + "BlockMap", new XAttribute("HashMethod", "http://www.w3.org/2001/04/xmlenc#sha256"));
        foreach (var entry in entries)
        {
            var element = new XElement(bm + "File", new XAttribute("Name", entry.Key), new XAttribute("Size", entry.Value.Length));
            for (int offset = 0; offset < entry.Value.Length; offset += 65536)
                element.Add(new XElement(bm + "Block", new XAttribute("Hash", Convert.ToBase64String(SHA256.HashData(entry.Value.AsSpan(offset, Math.Min(65536, entry.Value.Length - offset)))))));
            map.Add(element);
        }
        if (corruptMap) map.Elements().First().Elements().Last().Remove();
        if (tamper) payload[70000] ^= 1;
        entries["AppxBlockMap.xml"] = Encoding.UTF8.GetBytes(map.ToString());
        if (signed) entries["AppxSignature.p7x"] = [1, 2, 3];
        if (extra != null) entries[extra] = [4, 5, 6];
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            foreach (var pair in entries) { using var output = zip.CreateEntry(pair.Key).Open(); output.Write(pair.Value); }
        string hash; using (var input = File.OpenRead(file)) hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        var tool = MsixReleaseTests.Tool;
        var package = new LocalPackage(tool.Id, tool.Name, "0.0.3.3", tool.AppId, Path.GetFileName(file), hash, "signed-msix", "msix");
        string catalog = Path.Combine(directory, "packages.json");
        File.WriteAllText(catalog, System.Text.Json.JsonSerializer.Serialize(new PackageCatalog(1, "published", [package]), ToolCatalog.Json));
        return (catalog, package, file);
    }
    public static async Task Run(string root, Action<bool, string> check)
    {
        var good = Fixture(root); var tool = MsixReleaseTests.Tool;
        using (var locked = await MsixPackageVerifier.Open(tool, good.Package, good.File, Trusted))
        {
            bool denied = false; try { using var writer = File.Open(good.File, FileMode.Open, FileAccess.Write); } catch (IOException) { denied = true; }
            check(denied, "署名・Manifest・複数ブロック照合後もMSIXを書換不能のまま保持");
        }
        async Task Reject((string Catalog, LocalPackage Package, string File) item, string label, Func<string, MsixSigner>? signature = null)
        {
            bool denied = false;
            try { using var handle = await MsixPackageVerifier.Open(tool, item.Package, item.File, signature ?? Trusted); }
            catch (IOException) { denied = true; }
            check(denied, label);
        }
        await Reject(good with { Package = good.Package with { Sha256 = new string('0', 64) } }, "MSIX取得後のSHA不一致を拒否");
        await Reject(good, "信頼署名でも別証明書を拒否", _ => new(MsixPolicy.Publisher, new string('0', 40)));
        await Reject(good, "信頼署名でも別Publisherを拒否", _ => new("CN=Other", MsixPolicy.Thumbprint));
        await Reject(good, "署名失敗を例外のまま導入禁止にする", _ => throw new IOException("未信頼の署名"));
        await Reject(Fixture(root, publisher: "CN=Other"), "Manifestの別Publisherを拒否");
        await Reject(Fixture(root, identity: "Other"), "Manifestの別Nameを拒否");
        await Reject(Fixture(root, version: "0.0.3.4"), "Manifestの版不一致を拒否");
        await Reject(Fixture(root, architecture: "arm64"), "MSIXの別アーキテクチャを拒否");
        await Reject(Fixture(root, application: "Other"), "MSIXの別Applicationを拒否");
        await Reject(Fixture(root, signed: false), "署名ファイルのないMSIXを拒否");
        await Reject(Fixture(root, tamper: true), "署名済みBlockMapと異なるpayloadを拒否");
        await Reject(Fixture(root, corruptMap: true), "末尾ブロック欠落を拒否");
        await Reject(Fixture(root, extra: "untracked.exe"), "BlockMap未収録の実行ファイルを拒否");
        await Reject(Fixture(root, extra: "../outside"), "MSIX内部の親ディレクトリ参照を拒否");
        await Reject(Fixture(root, extra: "HTTPSELECTOR.EXE"), "MSIX内部の大小文字違いの重複を拒否");
        await Reject(good with { File = Path.Combine(root, "missing.msix") }, "MSIXファイル欠落を署名エラーと区別して拒否");
    }
}
