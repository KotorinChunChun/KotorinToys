using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using KotorinToys;

internal static class HostReleaseTests
{
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(send(request)); }
    }
    static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data, ToolCatalog.Json)) };

    public static async Task Run(string fixtures, Action<bool, string> check)
    {
        await HostGuiUpdateCheckTests.Run(check);
        var bytes = "自己更新の非実行テスト"u8.ToArray();
        var package = new LocalPackage("host", "KotorinToys", "0.1.1", "KotorinToys", "KotorinToys-0.1.1-setup.exe",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "unsigned-public-build");
        var item = new ReleasePackage(package, bytes.Length);
        string Url(string version, string name) => $"https://github.com/KotorinChunChun/KotorinToys/releases/download/v{version}/{name}";
        object Release(string version, bool preview, string? installerUrl = null) => new
        {
            draft = false, prerelease = preview, tag_name = "v" + version,
            assets = new[]
            {
                new { name = "KotorinToys-packages.json", state = "uploaded", size = 1024L, browser_download_url = Url(version, "KotorinToys-packages.json"), digest = (string?)null },
                new { name = $"KotorinToys-{version}-setup.exe", state = "uploaded", size = (long)bytes.Length, browser_download_url = installerUrl ?? Url(version, $"KotorinToys-{version}-setup.exe"), digest = (string?)("sha256:" + package.Sha256) }
            }
        };
        var releases = new[] { Release("0.2.0", true), Release("0.1.1", false) };
        int calls = 0;
        using (var client = new PublicReleaseClient(new Handler(request =>
        {
            calls++;
            if (ReleaseIndexFixture.IsIndex(request))
            {
                check(request.RequestUri == PublicReleaseIndex.Source, "本体も固定の公開情報一覧を使用");
                return Json(ReleaseIndexFixture.Index("KotorinToys", releases));
            }
            var version = request.RequestUri!.AbsolutePath.Contains("v0.2.0/") ? "0.2.0" : "0.1.1";
            return Json(new PackageCatalog(1, "published", [package with { Version = version, File = $"KotorinToys-{version}-setup.exe" }]));
        })))
        {
            var actual = await client.CheckHost();
            check(actual.Confirmed && actual.Package == item && !actual.Prerelease, "本体は標準で正式版だけを選択");
            actual = await client.CheckHost(true);
            check(actual.Confirmed && actual.Package?.Package.Version == "0.2.0" && actual.Prerelease, "明示選択時だけ開発版も対象にする");
            check(await client.DetectHostChannel("0.2.0") == HostUpdateChannel.Preview &&
                await client.DetectHostChannel("0.1.1") == HostUpdateChannel.Stable &&
                await client.DetectHostChannel("0.0.9") == HostUpdateChannel.Unknown,
                "現在版に一致する公開情報から開発版・通常版を判定し、未公開版は推測しない");
            actual = await client.CheckHostChannel(HostUpdateChannel.Stable);
            check(actual.Confirmed && actual.Package?.Package.Version == "0.1.1" && !actual.Prerelease, "通常版の照合は開発版を含めない");
            actual = await client.CheckHostChannel(HostUpdateChannel.Preview);
            check(actual.Confirmed && actual.Package?.Package.Version == "0.2.0" && actual.Prerelease, "開発版の照合は通常版を混ぜない");
            var before = calls;
            check(!(await client.Check(HostProduct.Definition, null)).Confirmed && calls == before, "子ツールの公開照合経路は本体を拒否し続ける");
        }
        foreach (var invalid in new[] { item with { Package = package with { Id = "smart-paste" } }, item with { Package = package with { AppId = "Other" } },
            item with { Package = package with { Name = "Other" } }, item with { Package = package with { File = "../evil.exe" } }, item with { Length = 0 },
            item with { Length = 513L * 1024 * 1024 }, item with { Package = package with { Sha256 = "bad" } } })
        {
            bool denied = false; try { HostProduct.Validate(invalid); } catch (IOException) { denied = true; }
            check(denied, "本体の製品識別・hash・サイズ・ファイル名を検証");
        }
        check(HostProduct.IsNewer(item, "0.1.0") && !HostProduct.IsNewer(item, "0.1.1") && !HostProduct.IsNewer(item, "1.0.0"), "自己更新は新しい版だけ・同版と旧版を拒否");
        var switchToStable = new HostChannelTransition(HostUpdateChannel.Preview, HostUpdateChannel.Stable, "backup/manifest.json");
        check(HostProduct.CanInstall(item, "0.2.0", switchToStable) && !HostProduct.CanInstall(item, "0.2.0"),
            "確認済みの別チャネル切替だけ古い版を許可する");
        foreach (var transition in new[] { switchToStable with { To = HostUpdateChannel.Preview },
            switchToStable with { To = HostUpdateChannel.Unknown }, switchToStable with { BackupManifest = "../other.json" } })
        {
            bool denied = false; try { HostProduct.CanInstall(item, "0.2.0", transition); } catch (IOException) { denied = true; }
            check(denied, "切替先・異なるチャネル・固定バックアップ先を検証する");
        }
        var cache = Path.Combine(fixtures, "host-download");
        using (var downloader = new ReleaseDownloader(new Handler(request =>
        {
            check(request.RequestUri!.AbsoluteUri == Url("0.1.1", package.File), "本体ダウンロード先の所有者・tag・ファイルを固定");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        })))
        {
            var path = await downloader.PrepareHost(item, "0.1.0", cache);
            check(InstallerAdapter.Load(path).Packages.Single() == package && File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path)!, package.File)).SequenceEqual(bytes),
                "本体専用取得で検証済み台帳とインストーラーを確定");
            bool denied = false;
            try { await downloader.Prepare(new(1, [item]), [HostProduct.Definition with { Version = "0.1.1" }], cache); }
            catch (IOException) { denied = true; }
            check(denied, "子ツールの一括取得へ本体を混入させない");
        }
        foreach (var current in new[] { "0.1.1", "0.2.0" })
        {
            using var downloader = new ReleaseDownloader(new Handler(_ => throw new Exception("通信してはいけない")));
            bool denied = false; try { await downloader.PrepareHost(item, current, cache); } catch (IOException) { denied = true; }
            check(denied, "同版・旧版は通信前に自己更新を拒否：" + current);
        }
        using (var downloader = new ReleaseDownloader(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) })))
        {
            bool denied = false; try { await downloader.PrepareHost(item, "0.1.0", cache); } catch (IOException) { denied = true; }
            check(denied && !Directory.EnumerateFiles(cache, "*.partial", SearchOption.AllDirectories).Any(), "本体取得のサイズ・hash不一致では実行用ファイルを残さない");
        }
        foreach (var payload in new[] { new[] { Release("0.1.1", false, "https://example.com/evil.exe") }, Array.Empty<object>() })
        {
            using var client = new PublicReleaseClient(new Handler(_ => Json(ReleaseIndexFixture.Index("KotorinToys", payload))));
            check(!(await client.CheckHost()).Confirmed, "外部assetと未公開の本体を確認済みにしない");
        }
        using (var client = new PublicReleaseClient(new Handler(_ => throw new HttpRequestException("オフライン"))))
            check(!(await client.CheckHost()).Confirmed, "通信失敗で本体の最新判定をしない");
        using (var client = new PublicReleaseClient(new Handler(_ => throw new Exception("通信してはいけない"))))
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); bool denied = false;
            try { await client.CheckHost(cancellation: canceled.Token); } catch (OperationCanceledException) { denied = true; }
            check(denied, "本体の公開確認にも取消を伝播");
        }
    }
}
