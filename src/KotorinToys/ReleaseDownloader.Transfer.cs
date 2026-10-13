using System.Security.Cryptography;

namespace KotorinToys;

internal sealed partial class ReleaseDownloader
{
    async Task Download(ReleasePackage item, string directory, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        HttpResponseMessage? response = null;
        var partial = PathSafety.Resolve(directory, item.Package.File + ".partial");
        try
        {
            response = await OpenResponse(Asset(item), token);
            if (response.Content.Headers.ContentLength is long length && length != item.Length)
                throw new IOException("配布物のサイズが公開台帳と一致しません。");
            await WriteVerified(response.Content, item, partial, token);
            token.ThrowIfCancellationRequested();
            File.Move(partial, PathSafety.Resolve(directory, item.Package.File));
        }
        finally
        {
            response?.Dispose();
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    // 検証の途中では公開ファイル名に確定しない。サイズ・ハッシュを同じ読み取りで照合する。
    static async Task WriteVerified(HttpContent content, ReleasePackage item, string partial, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = await content.ReadAsStreamAsync(token);
        await using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total > item.Length || total > MaxBytes) throw new IOException("配布物の容量上限を超えました。");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        if (total != item.Length || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != item.Package.Sha256)
            throw new IOException("配布物のSHA-256またはサイズが一致しません。実行しません。");
        await output.FlushAsync(token);
        output.Flush(true);
    }
}
