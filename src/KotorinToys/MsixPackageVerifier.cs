using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace KotorinToys;

internal static class MsixPackageVerifier
{
    // ハンドルを展開終了まで保持する。検証後のファイル差替え・上書きを許さない。
    public static async Task<FileStream> Open(ToolDefinition tool, LocalPackage package, string path,
        Func<string, MsixSigner>? signature = null)
    {
        if (!MsixPolicy.Supports(tool) || !MsixPolicy.Valid(package) || package.Id != tool.Id || package.Name != tool.Name || package.AppId != tool.AppId) throw new IOException("MSIX製品識別・配布定義が一致しません。");
        PackageCatalogReader.Validate(new(1, "published", [package]));
        if (!File.Exists(path)) throw new IOException("MSIXファイルが見つかりません。");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length is <= 0 or > 512L * 1024 * 1024) throw new IOException("MSIXの容量が不正です。");
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            if (hash != package.Sha256) throw new IOException("MSIXのSHA-256が一致しません。実行しません。");
            var signer = await Task.Run(() => (signature ?? MsixSignature.Verify)(path));
            if (signer.Subject != MsixPolicy.Publisher || !string.Equals(signer.Thumbprint, MsixPolicy.Thumbprint, StringComparison.OrdinalIgnoreCase))
                throw new IOException("MSIXの署名者・証明書が固定配布元と一致しません。");
            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, true);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                // MSIXのZIP名はOPCのURI形式（@は%40）で、BlockMapは元のファイル名を使う。
                string name = Uri.UnescapeDataString(entry.FullName).Replace('\\', '/');
                if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p is "" or "." or "..") ||
                    !entries.TryAdd(name, entry) || (total += entry.Length) > 512L * 1024 * 1024 || entries.Count > 4096)
                    throw new IOException("MSIXの内部パス・重複・展開容量が不正です。");
            }
            ZipArchiveEntry Entry(string name) => entries.GetValueOrDefault(name) ?? throw new IOException("MSIXの必須ファイルがありません：" + name);
            XDocument Xml(string name)
            {
                if (Entry(name).Length > 2 * 1024 * 1024) throw new IOException("MSIX内部XMLが大きすぎます。");
                using var source = Entry(name).Open();
                using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 2 * 1024 * 1024 });
                return XDocument.Load(reader);
            }
            Entry("AppxSignature.p7x");
            var manifest = Xml("AppxManifest.xml"); XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            var root = manifest.Root;
            var identity = root?.Element(ns + "Identity");
            if (root?.Name != ns + "Package" || (string?)identity?.Attribute("Name") != MsixPolicy.NameFor(tool) ||
                (string?)identity?.Attribute("Publisher") != MsixPolicy.Publisher || (string?)identity?.Attribute("ProcessorArchitecture") != "x64" ||
                (string?)identity?.Attribute("Version") != package.Version)
                throw new IOException("MSIXのName・Publisher・アーキテクチャ・版が一致しません。");
            var apps = root.Element(ns + "Applications")?.Elements(ns + "Application").ToArray();
            if (apps is not { Length: 1 } || (string?)apps[0].Attribute("Id") != tool.Msix!.ApplicationId ||
                (string?)apps[0].Attribute("Executable") != tool.InstalledExecutable || (string?)apps[0].Attribute("EntryPoint") != "Windows.FullTrustApplication")
                throw new IOException("MSIXのApplication・起動設定が一致しません。");
            Entry(tool.InstalledExecutable);
            // SIPが検証する署名済みBlockMapと、実際の非圧縮payloadを全ブロック照合する。
            var map = Xml("AppxBlockMap.xml"); XNamespace bm = "http://schemas.microsoft.com/appx/2010/blockmap";
            if (map.Root?.Name != bm + "BlockMap" || (string?)map.Root.Attribute("HashMethod") != "http://www.w3.org/2001/04/xmlenc#sha256")
                throw new IOException("MSIX BlockMapの方式が不正です。");
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in map.Root.Elements(bm + "File"))
            {
                string name = ((string?)file.Attribute("Name") ?? "").Replace('\\', '/');
                var entry = Entry(name);
                if (!covered.Add(name) || !long.TryParse((string?)file.Attribute("Size"), out var size) || size != entry.Length)
                    throw new IOException("MSIX BlockMapのファイル・容量が不正です。");
                using var payload = entry.Open(); byte[] block = new byte[65536]; long read = 0;
                foreach (var expected in file.Elements(bm + "Block"))
                {
                    int length = payload.ReadAtLeast(block, (int)Math.Min(block.Length, size - read), false);
                    if (read >= size || length == 0 || Convert.ToBase64String(SHA256.HashData(block.AsSpan(0, length))) != (string?)expected.Attribute("Hash"))
                        throw new IOException("MSIX payloadのブロックハッシュが一致しません。");
                    read += length;
                }
                if (read != size || payload.ReadByte() != -1) throw new IOException("MSIX payloadがBlockMapと一致しません。");
            }
            if (entries.Keys.Except(covered, StringComparer.OrdinalIgnoreCase).Any(name => name is not ("AppxBlockMap.xml" or "AppxSignature.p7x" or "[Content_Types].xml" or "AppxMetadata/CodeIntegrity.cat")))
                throw new IOException("MSIXにBlockMapで照合できないpayloadがあります。");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
