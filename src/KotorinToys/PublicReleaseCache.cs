using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KotorinToys;

internal sealed record PublicReleaseText(string Value, DateTimeOffset FetchedAt, bool Cached);

// HTTP成功本文だけを保存する。製品・版・配布先・ハッシュの照合は毎回呼出側で行う。
internal sealed class PublicReleaseCache
{
    internal sealed record Entry(int SchemaVersion, string Url, string Value, DateTimeOffset FetchedAt, string? ETag);
    sealed record Backoff(string Host, DateTimeOffset Until, bool HasServerTime);
    sealed record Backoffs(int SchemaVersion, Backoff[] Items);
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    const int MaximumEntries = 64, MaximumFileBytes = 8 * 1024 * 1024;
    static readonly Lazy<PublicReleaseCache> shared = new(() => new(
        Path.Combine(HostSettingsStore.DefaultDirectory, "public-release-cache")));
    internal static PublicReleaseCache Shared => shared.Value;
    readonly string? directory;
    readonly Func<DateTimeOffset> utcNow;
    readonly object sync = new();
    readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    readonly Dictionary<string, Backoff> backoffs = new(StringComparer.OrdinalIgnoreCase);
    // 版別URLが増えても待機用オブジェクトを増やし続けない。
    readonly SemaphoreSlim[] requests = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    internal PublicReleaseCache(string? directory = null, Func<DateTimeOffset>? utcNow = null)
    {
        this.directory = directory == null ? null : Path.GetFullPath(directory);
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        LoadBackoffs();
    }

    internal DateTimeOffset UtcNow => utcNow().ToUniversalTime();

    internal async Task<PublicReleaseText> Read(Uri uri, int limit,
        Func<Entry?, CancellationToken, Task<Entry>> fetch, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var request = requests[(uint)StringComparer.Ordinal.GetHashCode(uri.AbsoluteUri) % (uint)requests.Length];
        await request.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var previous = Load(uri, limit);
            if (previous != null && UtcNow - previous.FetchedAt < Lifetime)
                return new(previous.Value, previous.FetchedAt, true);
            ThrowIfBlocked(uri.Host);
            var value = await fetch(previous, cancellation).ConfigureAwait(false);
            if (!Valid(value, uri, limit)) throw new IOException("公開情報キャッシュの内容・容量・取得時刻が不正です。");
            lock (sync)
            {
                entries[uri.AbsoluteUri] = value;
                foreach (var old in entries.OrderBy(p => p.Value.FetchedAt).Take(Math.Max(0, entries.Count - MaximumEntries)).ToArray()) entries.Remove(old.Key);
            }
            Save(FileName(uri), value);
            return new(value.Value, value.FetchedAt, false);
        }
        finally { request.Release(); }
    }

    internal void ThrowIfBlocked(string host)
    {
        lock (sync)
            if (backoffs.TryGetValue(host, out var value) && value.Until > UtcNow)
                throw new ReleaseReadException(ReleaseProblem.RateLimit,
                    ReleaseProblems.RateLimitMessage(value.HasServerTime ? value.Until : null), value.HasServerTime ? value.Until : null);
    }

    internal void Block(string host, DateTimeOffset until, bool hasServerTime)
    {
        lock (sync)
        {
            if (backoffs.TryGetValue(host, out var old) && old.Until > until) return;
            backoffs[host] = new(host, until, hasServerTime);
            foreach (var expired in backoffs.Where(p => p.Value.Until <= UtcNow).ToArray()) backoffs.Remove(expired.Key);
            // 古いスナップショットの後勝ちで、別ホストの解除待ちを消さない。
            Save("retry-state.json", new Backoffs(1, backoffs.Values.ToArray()));
        }
    }

    Entry? Load(Uri uri, int limit)
    {
        lock (sync)
            if (entries.TryGetValue(uri.AbsoluteUri, out var value)) return Valid(value, uri, limit) ? value : null;
        var loaded = LoadFile<Entry>(FileName(uri));
        if (loaded == null || !Valid(loaded, uri, limit)) return null;
        lock (sync) entries[uri.AbsoluteUri] = loaded;
        return loaded;
    }

    bool Valid(Entry value, Uri uri, int limit) => value.SchemaVersion == 1 && value.Url == uri.AbsoluteUri &&
        value.Value != null && Encoding.UTF8.GetByteCount(value.Value) <= limit && value.FetchedAt <= UtcNow && value.ETag?.Length is not > 512;

    void LoadBackoffs()
    {
        var saved = LoadFile<Backoffs>("retry-state.json");
        if (saved is not { SchemaVersion: 1, Items: not null } || saved.Items.Length > 4) return;
        foreach (var item in saved.Items)
            if (item != null && item.Host is "github.com" or "raw.githubusercontent.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" &&
                item.Until > UtcNow) backoffs[item.Host] = item;
    }

    static string FileName(Uri uri) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant() + ".json";

    string PathFor(string name)
    {
        if (directory == null) throw new IOException("永続キャッシュは無効です。");
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("キャッシュ保存先のリンクは使用しません。");
        string path = PathSafety.Resolve(directory, name);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("キャッシュファイルのリンクは使用しません。");
        return path;
    }

    T? LoadFile<T>(string name) where T : class
    {
        if (directory == null) return null;
        try
        {
            string path = PathFor(name);
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumFileBytes) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), ToolCatalog.Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException) { return null; }
    }

    void Save<T>(string name, T value)
    {
        if (directory == null) return;
        string? temporary = null;
        try
        {
            string path = PathFor(name);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, ToolCatalog.Json);
            if (bytes.Length > MaximumFileBytes) return;
            Directory.CreateDirectory(directory);
            temporary = PathFor(name + "." + Guid.NewGuid().ToString("N") + ".tmp");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            File.Move(temporary, path, true);
            // 自分のURLハッシュ形式のキャッシュだけを最大64件に保つ。
            foreach (var old in new DirectoryInfo(directory).GetFiles("*.json")
                .Where(f => f.Name.Length == 69 && f.Name[..64].All(c => char.IsAsciiHexDigit(c)) &&
                    (f.Attributes & FileAttributes.ReparsePoint) == 0).OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaximumEntries)) old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        finally
        {
            try { if (temporary != null) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
    }
}
