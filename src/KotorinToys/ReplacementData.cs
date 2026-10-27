using System.Security.Cryptography;

namespace KotorinToys;

// 実行ファイルの隣に設定を置く既知の製品だけを対象にする。
internal static class ReplacementData
{
    static readonly IReadOnlyDictionary<string, string[]> Sidecars = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["ss-capture"] = ["data/settings.json", "data/last-region.json"],
        ["voice-mixer-spk-router"] = ["spk-settings.json"],
        ["voice-mixer-mic-router"] = ["mic-settings.json"]
    };

    public static void Preserve(ToolDefinition tool, string previousExecutable, string installedExecutable)
    {
        if (!Sidecars.TryGetValue(tool.Id, out var files)) return;
        var sourceRoot = Path.GetDirectoryName(ToolLocations.LocalExecutable(previousExecutable))!;
        var targetRoot = Path.GetDirectoryName(ToolLocations.LocalExecutable(installedExecutable))!;
        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase)) return;
        RejectLinks(sourceRoot, sourceRoot);
        RejectLinks(targetRoot, targetRoot);
        var copies = new List<(string Source, string Target)>();
        foreach (var relative in files)
        {
            var source = PathSafety.Resolve(sourceRoot, relative);
            var target = PathSafety.Resolve(targetRoot, relative);
            RejectLinks(sourceRoot, source);
            RejectLinks(targetRoot, target);
            if (!File.Exists(source)) continue;
            if (new FileInfo(source).Length > 16 * 1024 * 1024) throw new IOException("設定ファイルが大きすぎるため、自動で引き継げません：" + relative);
            if (File.Exists(target))
            {
                using var original = File.OpenRead(source);
                using var current = File.OpenRead(target);
                if (!SHA256.HashData(original).SequenceEqual(SHA256.HashData(current)))
                    throw new IOException("移行元と導入先の設定が異なります。どちらも保持しています：" + relative);
            }
            else copies.Add((source, target));
        }
        foreach (var (source, target) in copies)
        {
            var directory = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(directory);
            RejectLinks(targetRoot, target);
            File.Copy(source, target, false);
        }
    }

    static void RejectLinks(string root, string path)
    {
        var current = Path.GetFullPath(root);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("リンク先の設定を自動で引き継げません。");
        foreach (var part in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part is "." or "") continue;
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("リンク先の設定を自動で引き継げません。");
        }
    }
}
