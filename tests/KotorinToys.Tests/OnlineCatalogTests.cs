using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KotorinToys;

internal static class OnlineCatalogTests
{
    internal static byte[] Encode(params ToolDefinition[] tools) => JsonSerializer.SerializeToUtf8Bytes(new Catalog(1, tools), ToolCatalog.Json);
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    public static async Task Run(string root, string fixtures, Action<bool, string> check)
    {
        void Reject(Action action, string name)
        {
            try { action(); } catch (IOException) { check(true, name); return; }
            throw new Exception("拒否しませんでした：" + name);
        }
        var bytes = File.ReadAllBytes(Path.Combine(root, OnlineToolCatalog.RelativePath));
        var bundled = OnlineToolCatalog.Parse(bytes, root);
        check(bundled.Select(t => t.Id).SequenceEqual(ToolCatalog.Load(root).Select(t => t.Id)), "同梱configは現行8ツールと同じ順序");
        var tool = bundled[0];
        check(ToolRepository.Find(OnlineToolCatalog.Parse(Encode(tool with { RepositoryUrl = null }), root).Single()) == null,
            "configで公開先を未設定にすると旧固定URLへ補完しない");
        check(ToolRepository.Find(OnlineToolCatalog.Parse(Encode(tool with { RepositoryUrl = "" }), root).Single()) == null,
            "configで空の公開先を指定してリンクを無効化できる");
        var newTool = tool with { Id = "future-tool", Name = "FutureTool", ToolRoot = "tools/FutureTool", AppId = "KotorinToys.FutureTool", DistributionStatus = "unpublished", Summary = "後から追加するツール" };
        check(OnlineToolCatalog.Parse(Encode(newTool), fixtures).Single().Id == newTool.Id, "新ツールの定義を子フォルダーなしで読み込める");
        check(OnlineToolCatalog.Parse(Encode(), root).Length == 0, "空configで全件非表示にできる");
        check(OnlineToolCatalog.Parse(new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray(), root).Length == bundled.Length, "UTF-8 BOM付きのconfigも読み込める");
        Reject(() => OnlineToolCatalog.Parse(new byte[OnlineToolCatalog.MaximumBytes + 1], root), "過大なconfigを拒否");
        Reject(() => OnlineToolCatalog.Parse(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"SchemaVersion\":1,\"tools\":[]}"), root), "大文字小文字を変えた重複プロパティも拒否");
        Reject(() => OnlineToolCatalog.Parse(Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"tools\":[]}"), root), "未知のconfig形式を拒否");
        Reject(() => OnlineToolCatalog.Parse(Encoding.UTF8.GetBytes("{}"), root), "必須キー欠落を拒否");
        Reject(() => OnlineToolCatalog.Parse(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"tools\":[],\"commands\":[]}"), root), "未対応のキーを黙って無視しない");
        Reject(() => OnlineToolCatalog.Parse(Encode(tool, tool), root), "重複ツールIDを拒否");
        Reject(() => OnlineToolCatalog.Parse(Encode(tool, newTool with { AppId = tool.AppId }), root), "重複製品識別を拒否");
        Reject(() => OnlineToolCatalog.Parse(Encode(Enumerable.Repeat(newTool, 129).ToArray()), root), "件数上限を検査");
        int invalidIndex = 0;
        foreach (var invalid in new[] { tool with { Id = "host" }, tool with { Id = "dashboard" }, tool with { ToolRoot = "../outside" },
            tool with { ToolRoot = "tools/.." }, tool with { Executable = "../../bad.exe" }, tool with { Executable = "run.ps1" },
            tool with { InstalledExecutable = "../bad.exe" }, tool with { Manual = "run.cmd" }, tool with { Manual = "README.md:evil.md" },
            tool with { RepositoryUrl = "https://example.com/evil" }, tool with { RepositoryUrl = "https://github.com/KotorinChunChun/Tool/issues" },
            tool with { Capabilities = ["arbitrary-command"] }, tool with { Runtimes = null }, tool with { Summary = new string('x', 501) } })
            Reject(() => OnlineToolCatalog.Parse(Encode(invalid), root), "危険または未対応の定義を拒否：ケース" + ++invalidIndex);
        var missing = JsonNode.Parse(bytes)!; missing["tools"]![0]!.AsObject().Remove("summary");
        Reject(() => OnlineToolCatalog.Parse(Encoding.UTF8.GetBytes(missing.ToJsonString()), root), "省略値で必須設定を補完しない");

        var cache = Path.Combine(fixtures, "online-cache");
        var response = Encode(newTool, tool with { Summary = "説明を差し替え" });
        bool offline = false; int requests = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<byte[]> Fetch(CancellationToken token)
        {
            requests++; await gate.Task.WaitAsync(token);
            if (offline) throw new HttpRequestException("オフライン試験");
            return response;
        }
        var source = new ToolCatalogSource(root, cache, Fetch);
        check(source.Load().Length == bundled.Length, "キャッシュなしでは同梱一覧を即時表示");
        var first = source.Refresh(); var duplicate = source.Refresh();
        check(ReferenceEquals(first, duplicate) && requests == 1, "同時のconfig取得を一つに集約");
        gate.SetResult(); var result = await first;
        check(result.Accepted && result.Tools.Select(t => t.Id).SequenceEqual(new[] { newTool.Id, tool.Id }), "追加・削除・並び順を一括反映");
        check(source.KnownTools().Length == bundled.Length + 1, "非表示ツールも既存予約の照合用に保持");
        var cached = File.ReadAllBytes(Path.Combine(cache, "tool-catalog-cache.json"));
        var restarted = new ToolCatalogSource(root, cache, Fetch);
        check(restarted.Load().Length == 2 && restarted.KnownTools().Length == bundled.Length + 1 && restarted.Load()[1].Summary == "説明を差し替え", "再起動後も一覧・説明・旧登録を保持");
        offline = true; result = await restarted.Refresh();
        check(!result.Accepted && restarted.Load().Length == 2 && result.Detail.Contains("取得失敗"), "オフラインで最後の正常一覧を維持し理由を表示");
        check(File.ReadAllBytes(Path.Combine(cache, "tool-catalog-cache.json")).SequenceEqual(cached), "通信失敗でキャッシュを破壊しない");
        offline = false; response = Encoding.UTF8.GetBytes("invalid"); result = await restarted.Refresh();
        check(!result.Accepted && restarted.Load()[0].Id == newTool.Id && File.ReadAllBytes(Path.Combine(cache, "tool-catalog-cache.json")).SequenceEqual(cached), "壊れたJSONを部分採用・保存しない");
        response = Encode(newTool with { AppId = "KotorinToys.Different" }); result = await restarted.Refresh();
        check(!result.Accepted && restarted.Load().Length == 2, "既存IDの製品識別差し替えを拒否");
        response = Encode(tool with { Id = "alias-tool" }); result = await restarted.Refresh();
        check(!result.Accepted, "非表示の旧登録に別IDで同じ製品を重ねない");
        response = Encode(); result = await restarted.Refresh();
        check(result.Accepted && restarted.Load().Length == 0 && restarted.KnownTools().Length == bundled.Length + 1, "全件非表示でも設定照合用の登録を失わない");
        File.WriteAllText(Path.Combine(cache, "tool-catalog-cache.json"), "broken");
        check(new ToolCatalogSource(root, cache, Fetch).Load().Length == bundled.Length, "壊れたキャッシュは同梱一覧へフォールバック");
        File.WriteAllBytes(Path.Combine(cache, "tool-catalog-cache.json"), JsonSerializer.SerializeToUtf8Bytes(new CatalogCache(1, Path.GetFullPath(root), [tool with { AppId = "KotorinToys.Different" }], []), ToolCatalog.Json));
        check(new ToolCatalogSource(root, cache, Fetch).Load().Length == bundled.Length, "製品識別が壊れたキャッシュを採用しない");
        File.WriteAllBytes(Path.Combine(cache, "tool-catalog-cache.json"), JsonSerializer.SerializeToUtf8Bytes(new CatalogCache(1, Path.GetFullPath(root), [tool], [tool]), ToolCatalog.Json));
        check(new ToolCatalogSource(root, cache, Fetch).Load().Length == bundled.Length, "現在一覧と旧登録を重複させたキャッシュを採用しない");
        File.WriteAllBytes(Path.Combine(cache, "tool-catalog-cache.json"), JsonSerializer.SerializeToUtf8Bytes(new CatalogCache(1, fixtures, [newTool], []), ToolCatalog.Json));
        check(new ToolCatalogSource(root, cache, Fetch).Load().Length == bundled.Length, "他の配置ルートのキャッシュを使わない");
        var blocker = Path.Combine(fixtures, "cache-blocker"); File.WriteAllText(blocker, "not a directory");
        var blocked = new ToolCatalogSource(root, blocker, _ => Task.FromResult(Encode(newTool)));
        check(!(await blocked.Refresh()).Accepted && blocked.Load().Length == bundled.Length, "キャッシュ保存失敗時は一覧も切り替えない");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await source.Refresh(canceled.Token); throw new Exception("取消しませんでした。"); }
        catch (OperationCanceledException) { check(source.Load().Length == 2, "取消時に一覧を変更しない"); }

        using (var client = new ToolCatalogClient(new Handler((request, _) =>
        {
            check(request.RequestUri == OnlineToolCatalog.Source && request.Headers.CacheControl?.NoCache == true, "取得URLは固定HTTPS・キャッシュ再検証を指定");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }))) check((await client.Fetch()).SequenceEqual(bytes), "HTTP取得内容を改変せず検証層へ渡す");
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.Redirect, HttpStatusCode.NotModified })
        {
            int sent = 0;
            using var client = new ToolCatalogClient(new Handler((_, _) => { sent++; return Task.FromResult(new HttpResponseMessage(status)); }));
            try { await client.Fetch(); throw new Exception("HTTPエラーを拒否しませんでした。"); }
            catch (IOException) { check(sent == 1, "HTTP失敗・転送・未検証304を拒否：" + status); }
        }
        using (var client = new ToolCatalogClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[OnlineToolCatalog.MaximumBytes + 1]) }))))
        {
            try { await client.Fetch(); throw new Exception("容量超過を拒否しませんでした。"); }
            catch (IOException) { check(true, "HTTP容量上限を検査"); }
        }
        using (var client = new ToolCatalogClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ChunkedContent(new byte[OnlineToolCatalog.MaximumBytes + 1]) }))))
        {
            try { await client.Fetch(); throw new Exception("長さ未指定の容量超過を拒否しませんでした。"); }
            catch (IOException) { check(true, "Content-LengthのないHTTP応答も読込量で制限"); }
        }
        using (var client = new ToolCatalogClient(new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); })))
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            try { await client.Fetch(cancel.Token); throw new Exception("HTTP取得を取消しませんでした。"); }
            catch (OperationCanceledException) { check(true, "取得中の取消をHTTP処理へ伝播"); }
        }
        int installerRequests = 0;
        using (var client = new PublicReleaseClient(new Handler((_, _) => { installerRequests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); })))
        {
            var unpublished = await client.Check(newTool, null);
            check(installerRequests == 0 && !unpublished.Confirmed && unpublished.Package == null && unpublished.Detail.Contains("未公開"), "新規config登録の未公開製品は照合エラーにしない");
            var published = await client.Check(newTool with { DistributionStatus = "published" }, null);
            check(installerRequests == 0 && !published.Confirmed && published.Package == null && published.Detail.Contains("本体側の対応"), "新規製品をconfigだけでインストーラー許可リストへ追加しない");
        }
    }
}
