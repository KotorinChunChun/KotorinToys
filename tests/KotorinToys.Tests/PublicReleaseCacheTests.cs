using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KotorinToys;

internal static class PublicReleaseCacheTests
{
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => send(request, cancellation);
    }

    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value, ToolCatalog.Json)) };

    static HttpResponseMessage Publication(ToolDefinition tool, HttpRequestMessage request, bool badHash = false)
    {
        string repository = ProductIdentity.FromTool(tool)!.Repository;
        string url = $"https://github.com/KotorinChunChun/{repository}/releases/download/v0.1.99/";
        string hash = new('a', 64);
        var package = new LocalPackage(tool.Id, tool.Name, "0.1.99", tool.AppId,
            tool.Name + "-0.1.99-setup.exe", hash, "unsigned-public-build");
        if (request.RequestUri!.Host != "api.github.com") return Json(new PackageCatalog(1, "published", [package]));
        return Json(new[] { new { tag_name = "v0.1.99", draft = false, prerelease = true, published_at = "2026-10-23T15:00:00Z", assets = new object[]
        {
            new { name = tool.Name + "-packages.json", state = "uploaded", size = 512, browser_download_url = url + tool.Name + "-packages.json" },
            new { name = package.File, state = "uploaded", size = 4, browser_download_url = url + package.File, digest = "sha256:" + (badHash ? new string('b', 64) : hash) }
        } } });
    }

    static PublicReleaseClient Client(PublicReleaseCache cache, Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new Handler((request, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(send(request)); }), cache);

    static HttpResponseMessage Limited(DateTimeOffset? reset, string? retryAfter = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("X-RateLimit-Remaining", "0");
        if (reset is { } time) response.Headers.Add("X-RateLimit-Reset", time.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (retryAfter != null) response.Headers.Add("Retry-After", retryAfter);
        return response;
    }

    public static async Task Run(string root, string fixtures, Action<bool, string> check)
    {
        var tool = ToolCatalog.Load(root).Single(t => t.Id == "smart-paste");
        var other = ToolCatalog.Load(root).First(t => t.Id == "ss-capture");
        var fallback = ReleaseDownloader.Load(root).Packages.Single(p => p.Package.Id == tool.Id);
        var start = DateTimeOffset.Parse("2026-10-23T15:20:00Z");
        string DirectoryFor(string name) => Path.Combine(fixtures, "public-release-cache", name);

        {
            int calls = 0; var now = start;
            var cache = new PublicReleaseCache(DirectoryFor("reuse"), () => now);
            using var first = Client(cache, request => { calls++; return Publication(tool, request); });
            var initial = await first.Check(tool, fallback);
            now += TimeSpan.FromMinutes(4);
            using var second = Client(cache, request => { calls++; return Publication(tool, request); });
            var cached = await second.Check(tool, fallback);
            check(initial.Confirmed && !initial.Cached && cached.Confirmed && cached.Cached && calls == 2,
                "公開一覧と台帳を5分共有し新しいクライアントでも追加通信0");
            check(cached.FetchedAt == start && cached.Detail.Contains("2026/10/24 00:20:00") && cached.Detail.Contains("キャッシュ"),
                "キャッシュは取得日時を再確認時刻へ書き換えず日本時間で表示");
            var restarted = new PublicReleaseCache(DirectoryFor("reuse"), () => now);
            using var third = Client(restarted, _ => throw new Exception("永続キャッシュの再利用で通信しない"));
            var restored = await third.Check(tool, fallback);
            check(restored.Confirmed && restored.Cached && restored.Package == initial.Package && restored.FetchedAt == start,
                "再起動相当の新キャッシュでも保存済みの公開情報を再利用");
        }

        {
            int calls = 0, conditional = 0; var now = start;
            var cache = new PublicReleaseCache(utcNow: () => now);
            using var client = Client(cache, request =>
            {
                calls++;
                if (request.Headers.IfNoneMatch.Any(t => t.Tag == "\"cache-test\""))
                { conditional++; return new(HttpStatusCode.NotModified); }
                var response = Publication(tool, request); response.Headers.ETag = new("\"cache-test\""); return response;
            });
            await client.Check(tool, fallback);
            now += PublicReleaseCache.Lifetime;
            var revalidated = await client.Check(tool, fallback);
            var repeated = await client.Check(tool, fallback);
            check(revalidated.Confirmed && !revalidated.Cached && revalidated.FetchedAt == now && calls == 4 && conditional == 2,
                "5分境界はETag条件付き取得し304をリダイレクトと誤認しない");
            check(repeated.Confirmed && repeated.Cached && calls == 4, "304再検証後の5分間も追加通信しない");
        }

        {
            var now = start; bool notModified = false;
            var cache = new PublicReleaseCache(utcNow: () => now);
            using var client = Client(cache, request => notModified ? new(HttpStatusCode.NotModified) : Publication(tool, request));
            await client.Check(tool, fallback); now += PublicReleaseCache.Lifetime; notModified = true;
            check(!(await client.Check(tool, fallback)).Confirmed, "ETagなしの要求へ返った304で期限切れ本文を再確認済みにしない");
        }

        {
            int calls = 0; var cache = new PublicReleaseCache(utcNow: () => start);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var clients = Enumerable.Range(0, 12).Select(_ => new PublicReleaseClient(new Handler(async (request, token) =>
            {
                Interlocked.Increment(ref calls);
                if (request.RequestUri!.Host == "api.github.com") { entered.TrySetResult(); await proceed.Task.WaitAsync(token); }
                return Publication(tool, request);
            }), cache)).ToArray();
            try
            {
                var pending = clients.Select(client => client.Check(tool, fallback)).ToArray();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                check(pending.All(t => !t.IsCompleted) && calls == 1, "先行HTTPを停止中は12件全てが完了せず追加通信も待機");
                proceed.TrySetResult();
                var results = await Task.WhenAll(pending);
                check(results.All(r => r.Confirmed) && calls == 2, "12件同時照合でも同じ一覧と台帳の通信を各1回へ集約");
            }
            finally { proceed.TrySetResult(); foreach (var client in clients) client.Dispose(); }
        }

        {
            int calls = 0; var now = start; var reset = start.AddHours(1);
            var directory = DirectoryFor("primary-limit"); var cache = new PublicReleaseCache(directory, () => now);
            using var client = Client(cache, _ => { calls++; return Limited(reset); });
            var limited = await client.Check(tool, fallback);
            check(!limited.Confirmed && limited.Package == fallback && limited.Problem == ReleaseProblem.RateLimit && limited.RetryAt == reset &&
                limited.Detail.Contains("2026/10/24 01:20:00") && limited.Detail.Contains("日本時間"),
                "403残回数0の原因とepoch解除時刻を型・日本時間で保持");
            var another = await client.Check(other, null);
            check(!another.Confirmed && another.Problem == ReleaseProblem.RateLimit && calls == 1,
                "取得制限は他製品のAPI再取得も解除まで抑止");
            using var restarted = Client(new PublicReleaseCache(directory, () => now), _ => throw new Exception("解除待ちの再起動で通信しない"));
            check((await restarted.Check(tool, fallback)).RetryAt == reset, "再起動後も解除待ちを引き継ぐ");
            now = reset;
            using var recovered = Client(cache, request => { calls++; return Publication(tool, request); });
            check((await recovered.Check(tool, fallback)).Confirmed && calls == 3, "解除時刻後の次回照合でAPIと台帳を再取得できる");
        }

        {
            int calls = 0; var cache = new PublicReleaseCache(utcNow: () => start);
            using var client = Client(cache, request =>
            {
                calls++; var response = Publication(tool, request);
                if (request.RequestUri!.Host == "api.github.com")
                { response.Headers.Add("X-RateLimit-Remaining", "0"); response.Headers.Add("X-RateLimit-Reset", start.AddHours(1).ToUnixTimeSeconds().ToString()); }
                return response;
            });
            check((await client.Check(tool, fallback)).Confirmed && calls == 2,
                "最後のAPI枠で一覧取得成功ならAPI以外の台帳取得を妨げない");
            check((await client.Check(tool, fallback)).Cached && calls == 2, "解除待ち中でも有効な照合用キャッシュは利用できる");
            check((await client.Check(other, null)).Problem == ReleaseProblem.RateLimit && calls == 2,
                "最後のAPI枠を使った後は別製品への追加通信を抑止");
        }

        {
            var now = start; int calls = 0; bool limited = false;
            var cache = new PublicReleaseCache(utcNow: () => now);
            using var client = Client(cache, request => { calls++; return limited ? Limited(now.AddHours(1)) : Publication(tool, request); });
            await client.Check(tool, fallback); now += PublicReleaseCache.Lifetime; limited = true;
            var failed = await client.Check(tool, fallback);
            check(!failed.Confirmed && failed.Package == fallback && !failed.Cached && calls == 3,
                "期限切れキャッシュは取得制限時の照合済み候補へ昇格させない");
        }

        {
            var cache = new PublicReleaseCache(utcNow: () => start);
            using var client = Client(cache, _ => { var value = Limited(start.AddMinutes(1), "120"); value.StatusCode = HttpStatusCode.TooManyRequests; return value; });
            var value = await client.Check(tool, fallback);
            check(value.RetryAt == start.AddMinutes(2), "resetより長いRetry-Afterを優先し早すぎる再試行をしない");
            using var dateClient = Client(new(utcNow: () => start), _ => Limited(null, start.AddMinutes(3).ToString("R")));
            check((await dateClient.Check(tool, fallback)).RetryAt == start.AddMinutes(3), "HTTP日時形式のRetry-Afterも解釈する");
        }

        {
            int calls = 0; var now = start; var cache = new PublicReleaseCache(utcNow: () => now);
            using var client = Client(cache, _ => { calls++; return Limited(null); });
            var value = await client.Check(tool, fallback); await client.Check(tool, fallback);
            check(value.Problem == ReleaseProblem.RateLimit && value.RetryAt == null && value.Detail.Contains("解除予定時刻はGitHubから取得できません") && calls == 1,
                "解除時刻がない制限は不明と明示し少なくとも1分は連打通信しない");
            now = start.AddMinutes(1); await client.Check(tool, fallback);
            check(calls == 2, "解除時刻不明の抑止も期限後に再確認できる");
            using var overflow = Client(new(utcNow: () => start), _ =>
            { var response = Limited(null); response.Headers.Add("X-RateLimit-Reset", long.MaxValue.ToString()); return response; });
            check((await overflow.Check(tool, fallback)).RetryAt == null, "不正なresetの桁数で例外終了せず時刻不明として扱う");
        }

        foreach (var code in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
        {
            int calls = 0; var cache = new PublicReleaseCache(utcNow: () => start);
            using var client = Client(cache, _ =>
            {
                calls++; var response = Limited(start.AddHours(1), "120"); response.StatusCode = code;
                response.Headers.Remove("X-RateLimit-Remaining"); response.Headers.Add("X-RateLimit-Remaining", "10");
                return response;
            });
            var value = await client.Check(tool, fallback); await client.Check(other, null);
            check(value.Problem == ReleaseProblem.RateLimit && value.RetryAt == start.AddMinutes(2) && calls == 1,
                "残枠ありの二次制限はRetry-Afterまで抑止し無関係な一次resetまで延長しない：" + code);
        }

        {
            int calls = 0; var cache = new PublicReleaseCache(utcNow: () => start);
            using var client = Client(cache, _ =>
            {
                calls++; return new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"message\":\"You have exceeded a secondary rate limit.\"}") };
            });
            var value = await client.Check(tool, fallback); await client.Check(other, null);
            check(value.Problem == ReleaseProblem.RateLimit && value.RetryAt == null && calls == 1,
                "ヘッダーのない二次制限もGitHubの理由から判別して少なくとも1分抑止");
        }

        {
            var directory = DirectoryFor("long-limit"); var cache = new PublicReleaseCache(directory, () => start);
            using var client = Client(cache, _ => { var response = Limited(null, "172800"); response.StatusCode = HttpStatusCode.TooManyRequests; return response; });
            var value = await client.Check(tool, fallback);
            using var restarted = Client(new(directory, () => start), _ => throw new Exception("長い解除待ちでも再起動直後に通信しない"));
            check(value.RetryAt == start.AddDays(2) && (await restarted.Check(tool, fallback)).RetryAt == value.RetryAt,
                "24時間を超えるRetry-Afterも再起動前後で同じ期限を保持");
        }

        {
            var directory = DirectoryFor("parallel-limit"); var cache = new PublicReleaseCache(directory, () => start);
            await Task.WhenAll(Task.Run(() => cache.Block("api.github.com", start.AddHours(1), true)),
                Task.Run(() => cache.Block("github.com", start.AddHours(2), true)));
            var restarted = new PublicReleaseCache(directory, () => start); int blocked = 0;
            foreach (var host in new[] { "api.github.com", "github.com" })
                try { restarted.ThrowIfBlocked(host); } catch (ReleaseReadException) { blocked++; }
            check(blocked == 2, "複数ホストの並行制限を保存順で消さず再起動後も保持");
        }

        {
            int calls = 0; var cache = new PublicReleaseCache(utcNow: () => start);
            using var client = Client(cache, _ => { calls++; return new(HttpStatusCode.Forbidden); });
            var first = await client.Check(tool, fallback); await client.Check(tool, fallback);
            check(first.Problem == ReleaseProblem.Connection && first.RetryAt == null && calls == 2,
                "残回数0の根拠がない403を取得制限・解除待ちと誤認しない");
        }

        {
            int calls = 0; var cache = new PublicReleaseCache(utcNow: () => start);
            using var client = Client(cache, request => { calls++; return Publication(tool, request, badHash: true); });
            var first = await client.Check(tool, fallback); var second = await client.Check(tool, fallback);
            check(!first.Confirmed && !second.Confirmed && second.Package == fallback && calls == 2 && second.Problem == ReleaseProblem.Validation,
                "キャッシュの本文でもGitHub digestと台帳hashの不一致を毎回拒否");
        }

        {
            var now = start; int calls = 0; var directory = DirectoryFor("corruption");
            using (var client = Client(new(directory, () => now), request => { calls++; return Publication(tool, request); })) await client.Check(tool, fallback);
            string apiFile = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"https://api.github.com/repos/KotorinChunChun/{ProductIdentity.FromTool(tool)!.Repository}/releases?per_page=100"))).ToLowerInvariant() + ".json");
            var saved = JsonNode.Parse(File.ReadAllText(apiFile))!.AsObject(); saved["url"] = "https://example.com/evil";
            File.WriteAllText(apiFile, saved.ToJsonString());
            using (var client = Client(new(directory, () => now), request => { calls++; return Publication(tool, request); }))
                check((await client.Check(tool, fallback)).Confirmed && calls == 3, "別URLを名乗るディスクキャッシュは捨てて正規配布先から取得");
            File.WriteAllText(apiFile, "not-json");
            using (var client = Client(new(directory, () => now), request => { calls++; return Publication(tool, request); }))
                check((await client.Check(tool, fallback)).Confirmed && calls == 4, "壊れたキャッシュでも通常の公開照合を継続");
            now -= TimeSpan.FromMinutes(1);
            using var rollback = Client(new(directory, () => now), request => { calls++; return Publication(tool, request); });
            check((await rollback.Check(tool, fallback)).Confirmed && calls == 6, "時計巻き戻りで未来の取得時刻となったキャッシュは使わない");
        }

        {
            var directory = DirectoryFor("blocked-directory"); Directory.CreateDirectory(Path.GetDirectoryName(directory)!); File.WriteAllText(directory, "original");
            int calls = 0; var cache = new PublicReleaseCache(directory, () => start);
            using var client = Client(cache, request => { calls++; return Publication(tool, request); });
            check((await client.Check(tool, fallback)).Confirmed && (await client.Check(tool, fallback)).Cached && calls == 2 && File.ReadAllText(directory) == "original",
                "ディスク保存不能でも公開照合とメモリーキャッシュを維持し既存ファイルを保持");
        }

        {
            var directory = DirectoryFor("bounded"); var cache = new PublicReleaseCache(directory, () => start);
            for (int i = 0; i < 128; i++)
            {
                var uri = new Uri($"https://github.com/KotorinChunChun/SmartPaste/releases/download/v0.1.{i}/SmartPaste-packages.json");
                await cache.Read(uri, 100, (_, _) => Task.FromResult(new PublicReleaseCache.Entry(1, uri.AbsoluteUri, "{}", start, null)), default);
            }
            check(Directory.GetFiles(directory, "*.json").Length == 64, "版別URLを反復しても永続本文は64件を超えない");
        }

        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0; var cache = new PublicReleaseCache(utcNow: () => start);
            using var client = new PublicReleaseClient(new Handler(async (_, token) =>
            { Interlocked.Increment(ref calls); entered.TrySetResult(); await proceed.Task.WaitAsync(token); return Limited(start.AddHours(1)); }), cache);
            var first = client.Check(tool, fallback); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var second = client.Check(other, null); proceed.TrySetResult();
            var values = await Task.WhenAll(first, second);
            check(values.All(v => v.Problem == ReleaseProblem.RateLimit) && calls == 1,
                "別製品がAPI待機中に制限を検出しても追撃せず同じ解除待ちを返す");
        }

        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource(); var cache = new PublicReleaseCache(utcNow: () => start);
            using var first = new PublicReleaseClient(new Handler(async (_, token) =>
            { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); throw new Exception("到達しない"); }), cache);
            var pending = first.Check(tool, fallback, cancellation.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
            using var recovered = Client(cache, request => Publication(tool, request));
            var result = await recovered.Check(tool, fallback).WaitAsync(TimeSpan.FromSeconds(3));
            check(result.Confirmed, "先行HTTP取消後もURL待機とAPI待機を解放して再取得できる");
        }

        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0; var cache = new PublicReleaseCache(utcNow: () => start);
            using var first = new PublicReleaseClient(new Handler(async (request, token) =>
            {
                Interlocked.Increment(ref calls);
                if (request.RequestUri!.Host == "api.github.com") { entered.TrySetResult(); await proceed.Task.WaitAsync(token); }
                return Publication(tool, request);
            }), cache);
            var firstTask = first.Check(tool, fallback);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            using var cancel = new CancellationTokenSource();
            using var second = Client(cache, _ => throw new Exception("キャンセルした待機から通信しない"));
            var secondTask = second.Check(tool, fallback, cancel.Token); cancel.Cancel();
            bool cancelled = false;
            try { await secondTask; } catch (OperationCanceledException) { cancelled = true; }
            finally { proceed.TrySetResult(); }
            check(cancelled && (await firstTask).Confirmed && calls == 2, "同時取得の待機側キャンセルは先行取得を中断しない");
        }
    }
}
