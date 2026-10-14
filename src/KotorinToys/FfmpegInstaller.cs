using System.IO.Compression;
using System.Security.Cryptography;

namespace KotorinToys;

internal sealed record FfmpegArchive(long Length, string Sha256)
{
    public static readonly FfmpegArchive Pinned = new(114768076, "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba");
}

// 製品配布台帳とは分離する。外部依存のバイナリを製品のReleasesへ再配布しない。
internal sealed class FfmpegInstaller : IDisposable
{
    public const string Version = "9.0.2";
    public static readonly Uri Source = new("https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip");
    const string Prefix = "ffmpeg-9.0.2-essentials_build/";
    const long ExpandedLimit = 512L * 1024 * 1024;
    static readonly (string Entry, string File, string Target)[] Files =
    [
        ("bin/ffmpeg.exe", "ffmpeg.exe", "tools/ffmpeg.exe"),
        ("bin/ffprobe.exe", "ffprobe.exe", "tools/ffprobe.exe"),
        ("LICENSE", "LICENSE", "tools/ffmpeg-distribution/LICENSE"),
        ("README.txt", "README.txt", "tools/ffmpeg-distribution/README.txt")
    ];
    readonly HttpClient client;
    readonly FfmpegArchive expected;
    public FfmpegInstaller(HttpMessageHandler? handler = null, FfmpegArchive? archive = null)
    {
        expected = archive ?? FfmpegArchive.Pinned;
        if (expected.Length is <= 0 or > ExpandedLimit || expected.Sha256.Length != 64 ||
            expected.Sha256.Any(c => !Uri.IsHexDigit(c))) throw new IOException("FFmpegの固定配布情報が不正です。");
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KotorinToys/0.1.0");
    }

    public static string State(InstallationState installed)
    {
        if (installed.State != "導入済み" || installed.Directory == null) return "SSCaptureを先に導入してください";
        try
        {
            var present = Files.Take(2).Count(file => File.Exists(SafeTarget(installed.Directory, file.Target)));
            return present == 2 ? "配置済み（録画動作は未確認）" : present == 0 ? "未導入" : "一部不足（再導入が必要）";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return "配置を確認できません"; }
    }

    public async Task<string> Install(InstallationState installed, Func<Task<ToolState>> status,
        IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        if (installed.State != "導入済み" || installed.Directory == null || installed.Executable == null ||
            !File.Exists(installed.Executable)) throw new IOException("正式導入済みのSSCaptureが必要です。");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installed.Directory));
        if (!string.Equals(SafeTarget(root, "SSCapture.exe"), Path.GetFullPath(installed.Executable), StringComparison.OrdinalIgnoreCase))
            throw new IOException("SSCaptureの正式配置を照合できません。");
        async Task RequireStopped()
        {
            cancellation.ThrowIfCancellationRequested();
            if ((await status()).State != "停止中")
                throw new IOException("本体の「正常停止」でSSCaptureを停止してから再試行してください。強制停止はしません。");
        }
        await RequireStopped();
        foreach (var file in Files) _ = SafeTarget(root, file.Target);
        var stage = SafeTarget(root, ".ffmpeg-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        bool retain = false;
        string warning = "";
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            progress?.Report("FFmpeg / FFprobe：固定9.0.2をダウンロード中（約110 MB）");
            var zip = Path.Combine(stage, "archive.zip");
            await Download(zip, deadline.Token).ConfigureAwait(false);
            progress?.Report("FFmpeg：容量・SHA-256一致。必要ファイルを検査・展開中。");
            await Extract(zip, stage, deadline.Token).ConfigureAwait(false);
            await RequireStopped().ConfigureAwait(false); // 取得中にSSCaptureが起動した場合は配置しない。
            cancellation.ThrowIfCancellationRequested();
            progress?.Report("FFmpeg / FFprobe：専用配置へ反映中。設定・履歴は変更しません。");
            try { Apply(root, stage); }
            catch (FfmpegRecoveryException) { retain = true; throw; }
        }
        finally
        {
            if (!retain)
            {
                try
                {
                    // この処理が作った既知のフラットファイルだけを削除する。再帰削除しない。
                    foreach (var file in new[] { "archive.zip" }.Concat(Files.Select(f => f.File)).Concat(Enumerable.Range(0, Files.Length).Select(i => "backup-" + i)))
                        File.Delete(SafeTarget(stage, file));
                    Directory.Delete(stage);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { warning = " 一時ファイルの整理は未完了です：" + stage; progress?.Report(warning); }
            }
        }
        return "FFmpeg / FFprobe 9.0.2の配置が完了しました。SSCaptureを起動して録画をご確認ください。" + warning;
    }

    async Task Download(string zip, CancellationToken token)
    {
        using var response = await client.GetAsync(Source, HttpCompletionOption.ResponseHeadersRead, token);
        // 配布先が変わった場合は自動追従せず、固定情報の見直しを要求する。
        if (response.StatusCode != System.Net.HttpStatusCode.OK) throw new IOException("固定FFmpeg配布先から取得できません。HTTP " + (int)response.StatusCode);
        if (response.Content.Headers.ContentLength is long length && length != expected.Length) throw new IOException("FFmpegの容量が固定情報と一致しません。");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total > expected.Length) throw new IOException("FFmpegの容量上限を超えました。");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        if (total != expected.Length || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("FFmpegのSHA-256または容量が不一致です。配置しません。");
        await output.FlushAsync(token); output.Flush(true);
    }

    internal static async Task Extract(string zip, string stage, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > 1024) throw new IOException("FFmpegアーカイブの項目数が不正です。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(part => part is "." or "..") ||
                !name.StartsWith(Prefix, StringComparison.Ordinal) || !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new IOException("FFmpegアーカイブに不正パス・リンク・重複があります。");
        }
        long total = 0;
        foreach (var file in Files)
        {
            var entry = archive.GetEntry(Prefix + file.Entry) ?? throw new IOException("FFmpegの必要ファイルがありません：" + file.File);
            var limit = file.File.EndsWith(".exe", StringComparison.Ordinal) ? 200L * 1024 * 1024 : 4L * 1024 * 1024;
            if (entry.Length is <= 0 || entry.Length > limit || (total += entry.Length) > ExpandedLimit) throw new IOException("FFmpegの展開容量が不正です。");
            await using var input = entry.Open();
            await using var output = new FileStream(SafeTarget(stage, file.File), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            long written = 0; var buffer = new byte[81920]; int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                if ((written += read) > entry.Length) throw new IOException("FFmpegの展開容量が宣言を超えました。");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            if (written != entry.Length) throw new IOException("FFmpegの展開が不完全です。");
            await output.FlushAsync(token); output.Flush(true);
        }
    }

    internal static void Apply(string root, string stage)
    {
        var changes = new List<(string Target, string Backup, bool HadOriginal, bool Placed)>();
        try
        {
            foreach (var (file, index) in Files.Select((file, index) => (file, index)))
            {
                var target = SafeTarget(root, file.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var backup = SafeTarget(stage, "backup-" + index);
                bool hadOriginal = File.Exists(target);
                if (hadOriginal) File.Move(target, backup);
                changes.Add((target, backup, hadOriginal, false));
                File.Move(SafeTarget(stage, file.File), target);
                changes[^1] = (target, backup, hadOriginal, true);
            }
        }
        catch (Exception original)
        {
            try
            {
                foreach (var change in changes.AsEnumerable().Reverse())
                {
                    if (change.Placed) File.Delete(change.Target);
                    if (change.HadOriginal) File.Move(change.Backup, change.Target);
                }
            }
            catch (Exception recovery) { throw new FfmpegRecoveryException("FFmpegの復旧が未完了です。バックアップを保持しました：" + stage, new AggregateException(original, recovery)); }
            throw;
        }
    }
    static string SafeTarget(string root, string relative)
    {
        var path = PathSafety.Resolve(root, relative);
        for (var current = path; current != null && current.Length >= Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).Length; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("FFmpeg導入先のリンクは変更しません。");
        return path;
    }
    public void Dispose() => client.Dispose();
    sealed class FfmpegRecoveryException(string message, Exception inner) : IOException(message, inner);
}
