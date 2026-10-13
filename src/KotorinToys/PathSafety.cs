using System.Text.Json;
using System.Text.RegularExpressions;

namespace KotorinToys;

internal static class PathSafety
{
    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new IOException("相対パスが必要です。");
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(canonicalRoot, relative));
        var prefix = canonicalRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("許可範囲外のパスです。");
        // 存在する途中ディレクトリのリンクで許可範囲を迂回しない。
        var cursor = canonicalRoot;
        foreach (var part in Path.GetRelativePath(canonicalRoot, full).Split(Path.DirectorySeparatorChar))
        {
            cursor = Path.Combine(cursor, part);
            FileSystemInfo item = Directory.Exists(cursor) ? new DirectoryInfo(cursor) : new FileInfo(cursor);
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = item.ResolveLinkTarget(true)?.FullName;
                if (target == null || !target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("許可範囲外のリンクです。");
            }
        }

        return full;
    }
}
