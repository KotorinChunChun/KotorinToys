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
            if ((int)response.StatusCode == 429 || (int)response.StatusCode == 403 && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0"))
                throw new ReleaseReadException(ReleaseProblem.RateLimit, "GitHubの取得回数制限です。時間をおいて「状態を更新」で再確認してください。");
            if ((int)response.StatusCode == 403) throw new ReleaseReadException(ReleaseProblem.Connection, "GitHubへのアクセスが拒否されました。公開先の状態を確認し、時間をおいて再確認してください。");
            if ((int)response.StatusCode == 404) throw new ReleaseReadException(ReleaseProblem.Unavailable, "公開ファイルが見つかりません。配布元のRelease・台帳・ファイル名を確認してください。");
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
