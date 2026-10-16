namespace KotorinToys;

internal sealed class ToolCatalogClient : IDisposable
{
    readonly HttpClient client;
    public ToolCatalogClient(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KotorinToys/0.1.0");
        client.DefaultRequestHeaders.CacheControl = new() { NoCache = true };
    }

    public async Task<byte[]> Fetch(CancellationToken cancellation = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(OnlineToolCatalog.Source, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        // リダイレクト先・認証情報・任意URLをconfigから取り込まない。
        if (response.StatusCode != System.Net.HttpStatusCode.OK) throw new IOException("一覧configの取得に失敗しました：HTTP " + (int)response.StatusCode);
        if (response.Content.Headers.ContentLength is > OnlineToolCatalog.MaximumBytes) throw new IOException("一覧configの容量上限を超えています。");
        using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > OnlineToolCatalog.MaximumBytes) throw new IOException("一覧configの容量上限を超えています。");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    public void Dispose() => client.Dispose();
}
