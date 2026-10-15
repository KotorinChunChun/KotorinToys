namespace KotorinToys;

internal sealed partial class PublicReleaseClient
{
    async Task<string> Read(Uri uri, int limit, CancellationToken token)
    {
        for (int hop = 0; hop <= 4; hop++)
        {
            if (!(ReleaseDownloader.Allowed(uri) || uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo == "" && uri.Host == "api.github.com"))
                throw new IOException("公開情報の許可外URLへ移動しません。");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            { uri = new(uri, response.Headers.Location ?? throw new IOException("公開情報の移動先がありません。")); continue; }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new IOException("公開情報の容量上限を超えました。");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var content = new MemoryStream();
            var buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                if (content.Length + read > limit) throw new IOException("公開情報の容量上限を超えました。");
                content.Write(buffer, 0, read);
            }
            return System.Text.Encoding.UTF8.GetString(content.ToArray());
        }
        throw new IOException("公開情報のリダイレクト回数が上限を超えました。");
    }
}
