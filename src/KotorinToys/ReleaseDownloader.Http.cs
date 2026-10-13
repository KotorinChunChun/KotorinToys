namespace KotorinToys;

internal sealed partial class ReleaseDownloader
{
    // 取得成功時だけ応答の所有権を呼出側へ渡す。失敗した応答はここで破棄する。
    async Task<HttpResponseMessage> OpenResponse(Uri uri, CancellationToken token)
    {
        HttpResponseMessage? response = null;
        try
        {
            for (int hops = 0; hops <= 4; hops++)
            {
                if (!Allowed(uri)) throw new IOException("許可外の配布先へ移動しません。");
                for (int attempt = 0; ; attempt++)
                {
                    response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                    if ((int)response.StatusCode is >= 500 and <= 599 && attempt < 2)
                    {
                        response.Dispose();
                        response = null;
                        await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token);
                        continue;
                    }
                    break;
                }
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location = response.Headers.Location ?? throw new IOException("リダイレクト先がありません。");
                    uri = new Uri(uri, location);
                    response.Dispose();
                    response = null;
                    continue;
                }
                response.EnsureSuccessStatusCode();
                break;
            }
            return response ?? throw new IOException("リダイレクト回数が上限を超えました。");
        }
        catch
        {
            response?.Dispose();
            throw;
        }
    }
}
