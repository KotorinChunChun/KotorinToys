using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace KotorinToys;

internal sealed partial class PublicReleaseClient
{
    Task<PublicReleaseText> Read(Uri uri, int limit, CancellationToken token)
    {
        RequireAllowed(uri);
        return cache.Read(uri, limit, (previous, cancellation) => ReadNetwork(uri, limit, previous, cancellation), token);
    }

    static void RequireAllowed(Uri uri)
    {
        if (!(ReleaseDownloader.Allowed(uri) || uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo == "" && uri.Host == "api.github.com"))
            throw new IOException("公開情報の許可外URLへ移動しません。");
    }

    async Task<PublicReleaseCache.Entry> ReadNetwork(Uri original, int limit, PublicReleaseCache.Entry? previous, CancellationToken token)
    {
        var uri = original;
        for (int hop = 0; hop <= 4; hop++)
        {
            RequireAllowed(uri);
            cache.ThrowIfBlocked(uri.Host);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (previous?.ETag is { } tag && EntityTagHeaderValue.TryParse(tag, out var parsedTag)) request.Headers.IfNoneMatch.Add(parsedTag);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            bool emptyQuota = response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Any(v => v.Trim() == "0");
            bool secondaryLimit = response.StatusCode == HttpStatusCode.Forbidden && !emptyQuota &&
                (response.Headers.RetryAfter != null || await IsSecondaryLimit(response, token).ConfigureAwait(false));
            if (response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.Forbidden && (emptyQuota || secondaryLimit))
            {
                var retry = RetryAt(response, emptyQuota);
                cache.Block(uri.Host, retry.Until, retry.Known);
                if (uri.Host != original.Host) cache.Block(original.Host, retry.Until, retry.Known);
                throw new ReleaseReadException(ReleaseProblem.RateLimit,
                    ReleaseProblems.RateLimitMessage(retry.Known ? retry.Until : null), retry.Known ? retry.Until : null);
            }
            // 最後のAPI枠で成功した本文は利用し、別製品の追加APIだけを抑止する。
            if (uri.Host == "api.github.com" && emptyQuota && (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified))
            {
                var retry = RetryAt(response, true);
                cache.Block(uri.Host, retry.Until, retry.Known);
            }
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (previous == null || request.Headers.IfNoneMatch.Count == 0)
                    throw new IOException("条件付き取得をしていない公開情報を304で再検証できません。");
                return previous with { FetchedAt = cache.UtcNow, ETag = response.Headers.ETag?.ToString() ?? previous.ETag };
            }
            if ((int)response.StatusCode is >= 300 and < 400)
            { uri = new(uri, response.Headers.Location ?? throw new IOException("公開情報の移動先がありません。")); continue; }
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new ReleaseReadException(ReleaseProblem.Connection, "GitHubへのアクセスが拒否されました。公開先の状態を確認し、時間をおいて再確認してください。");
            if (response.StatusCode == HttpStatusCode.NotFound) throw new ReleaseReadException(ReleaseProblem.Unavailable, "公開ファイルが見つかりません。配布元のRelease・台帳・ファイル名を確認してください。");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new IOException("公開情報の容量上限を超えました。");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var content = new MemoryStream();
            var buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (content.Length + read > limit) throw new IOException("公開情報の容量上限を超えました。");
                content.Write(buffer, 0, read);
            }
            return new(1, original.AbsoluteUri, System.Text.Encoding.UTF8.GetString(content.ToArray()), cache.UtcNow, response.Headers.ETag?.ToString());
        }
        throw new IOException("公開情報のリダイレクト回数が上限を超えました。");
    }

    static async Task<bool> IsSecondaryLimit(HttpResponseMessage response, CancellationToken token)
    {
        // 二次制限にはRemaining:0もRetry-Afterも付かない場合がある。本文は小さく制限して判別にだけ使う。
        if (response.Content.Headers.ContentLength > 16384) return false;
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var content = new MemoryStream();
        var buffer = new byte[4096]; int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (content.Length + read > 16384) return false;
            content.Write(buffer, 0, read);
        }
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(content.ToArray());
            return json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                json.RootElement.TryGetProperty("message", out var message) && message.ValueKind == System.Text.Json.JsonValueKind.String &&
                message.GetString()!.Contains("secondary rate limit", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    (DateTimeOffset Until, bool Known) RetryAt(HttpResponseMessage response, bool emptyQuota)
    {
        var now = cache.UtcNow;
        DateTimeOffset? until = null;
        if (emptyQuota && response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            try { var reset = DateTimeOffset.FromUnixTimeSeconds(seconds); if (reset > now) until = reset; }
            catch (ArgumentOutOfRangeException) { }
        }
        var retry = response.Headers.RetryAfter;
        try
        {
            var candidate = retry?.Date ?? (retry?.Delta is { } delta ? now.Add(delta) : (DateTimeOffset?)null);
            if (candidate > now && (until == null || candidate > until)) until = candidate;
        }
        catch (ArgumentOutOfRangeException) { }
        return until is { } known ? (known, true) : (now.AddMinutes(1), false);
    }
}
