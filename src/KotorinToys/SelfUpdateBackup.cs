using System.Security.Cryptography;
using System.Text.Json;

namespace KotorinToys;

internal sealed record SelfUpdateBackupEntry(string Path, long Length, string Sha256);
internal sealed record SelfUpdateBackupManifest(int SchemaVersion, SelfUpdateBackupEntry[] Files);

// 版の種類を切り替える前に、設定と履歴を別の更新領域へ退避する。
internal static class SelfUpdateBackup
{
    const int MaximumFiles = 10000;
    const long MaximumBytes = 1024L * 1024 * 1024;
    const int MaximumManifestBytes = 4 * 1024 * 1024;

    public static async Task Create(string settings, string job, CancellationToken cancellation)
    {
        var entries = new List<SelfUpdateBackupEntry>();
        var backup = PathSafety.Resolve(job, "backup");
        Directory.CreateDirectory(backup);
        long total = 0;
        if (Directory.Exists(settings)) await CopyDirectory(settings, "");
        var manifest = PathSafety.Resolve(backup, "manifest.json");
        var json = JsonSerializer.SerializeToUtf8Bytes(new SelfUpdateBackupManifest(1, entries.ToArray()), SelfUpdateFiles.Json);
        if (json.Length > MaximumManifestBytes) throw new IOException("設定の退避一覧が大きすぎます。");
        await File.WriteAllBytesAsync(manifest, json, cancellation);
        using var verified = await LockAndVerify(job, cancellation);

        async Task CopyDirectory(string directory, string relative)
        {
            foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                cancellation.ThrowIfCancellationRequested();
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("設定の退避先にリンクがあるため中止しました。");
                var name = Path.Combine(relative, item.Name);
                // 更新ジョブの実行ファイル・過去バックアップは再帰的に含めない。
                if (relative.Length == 0 && item is DirectoryInfo && item.Name.Equals("updates", StringComparison.OrdinalIgnoreCase))
                {
                    var latest = Path.Combine(item.FullName, "latest.json");
                    if (File.Exists(latest)) await CopyFile(Path.Combine(name, "latest.json"));
                    continue;
                }
                if (item is DirectoryInfo) await CopyDirectory(PathSafety.Resolve(settings, name), name);
                else await CopyFile(name);
            }
        }

        async Task CopyFile(string relative)
        {
            if (entries.Count >= MaximumFiles) throw new IOException("設定の退避ファイルが多すぎます。");
            var inputPath = PathSafety.Resolve(settings, relative);
            var outputPath = PathSafety.Resolve(backup, Path.Combine("files", relative));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > MaximumBytes - total) throw new IOException("設定の退避容量が大きすぎます。");
            total += input.Length;
            using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await input.CopyToAsync(output, cancellation); output.Flush(true); }
            input.Position = 0;
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation));
            entries.Add(new(relative.Replace('\\', '/'), input.Length, hash));
        }
    }

    // インストール完了まで検証済みの退避ファイルをロックする。
    public static async Task<IDisposable> LockAndVerify(string job, CancellationToken cancellation)
    {
        var locks = new List<FileStream>();
        try
        {
            var backup = PathSafety.Resolve(job, "backup");
            var manifest = new FileStream(PathSafety.Resolve(backup, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            locks.Add(manifest);
            if (manifest.Length > MaximumManifestBytes) throw new IOException("設定の退避一覧が大きすぎます。");
            var data = await JsonSerializer.DeserializeAsync<SelfUpdateBackupManifest>(manifest, SelfUpdateFiles.Json, cancellation)
                ?? throw new IOException("設定の退避一覧が空です。");
            if (data.SchemaVersion != 1 || data.Files == null || data.Files.Length > MaximumFiles) throw new IOException("設定の退避一覧が不正です。");
            long total = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in data.Files)
            {
                cancellation.ThrowIfCancellationRequested();
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path) || !seen.Add(entry.Path) || entry.Length < 0 ||
                    entry.Length > MaximumBytes - total || entry.Sha256 == null || entry.Sha256.Length != 64)
                    throw new IOException("設定の退避一覧が不正です。");
                total += entry.Length;
                var path = PathSafety.Resolve(PathSafety.Resolve(backup, "files"), entry.Path);
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); locks.Add(stream);
                if (stream.Length != entry.Length || Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation)) != entry.Sha256)
                    throw new IOException("設定・履歴の退避を照合できません。");
            }
            return new BackupLocks(locks);
        }
        catch { foreach (var stream in locks) stream.Dispose(); throw; }
    }

    sealed class BackupLocks(List<FileStream> streams) : IDisposable
    {
        public void Dispose() { foreach (var stream in streams) stream.Dispose(); }
    }
}
