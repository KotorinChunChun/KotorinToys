using KotorinToys;

internal static class ToolInstallationRunnerTests
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root);
        var catalog = ReleaseDownloader.Load(root);
        var supported = tools.First(t => t.DistributionStatus == "published");
        // 元の欠落・未公開条件を固定したfixture。実製品の公開後も負の回帰を残す。
        var unsupported = tools.Where(t => new[] { "shortcut-key-counter", "capchun-screen", "chulip-video", "chunap-tool" }.Contains(t.Id))
            .Select(t => t with { DistributionStatus = "unpublished" }).ToArray();
        int downloads = 0, executions = 0;
        var order = new List<string>(); bool failDownload = false;
        var runner = new ToolInstallationRunner(new(root),
            (source, tool, _, _) =>
            {
                downloads++; order.Add("download:" + tool.Id);
                check(source.Packages.Length == 1 && source.Packages[0].Package.Version == tool.Version, "取得対象は1製品の照合済み固定版");
                if (failDownload) throw new IOException("取得失敗の注入");
                return Task.FromResult("test-packages.json");
            },
            (tool, operation, path, _) =>
            {
                executions++; order.Add("execute:" + tool.Id);
                return Task.FromResult(new InstallResult(tool.Id, operation, "成功", "偽の導入処理"));
            });
        var results = await runner.Run(unsupported, "install", catalog, null);
        check(results.Length == 4 && results.All(r => r.State == "失敗" && (r.Detail.Contains("未公開") || r.Detail.Contains("未対応"))), "添付の4製品は例外文でなく未公開・未対応の理由を各行へ表示");
        check(downloads == 0 && executions == 0, "未対応製品ではダウンロードもインストーラー実行もしない");
        results = await runner.Run([unsupported[0], supported, unsupported[1]], "install", catalog, null);
        check(results.Select(r => r.State).SequenceEqual(["失敗", "成功", "失敗"]) && downloads == 1 && executions == 1, "未対応と対応製品の混在でも対応製品だけ導入できる");
        check(order.SequenceEqual(["download:" + supported.Id, "execute:" + supported.Id]), "製品ごとに取得・実行を完結する");
        results = await runner.Run([supported], "install", new(1, []), null);
        check(results[0].Detail.Contains("照合済み") && executions == 1, "公開台帳欠落でもSingle例外を出さず実行を拒否");
        failDownload = true;
        results = await runner.Run([supported, unsupported[0]], "install", catalog, null);
        check(results.Length == 2 && results[0].Detail == "取得失敗の注入" && results[1].Detail.Contains("未公開"), "1製品の取得失敗を次の製品へ波及させない");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        results = await runner.Run([supported, unsupported[0]], "install", catalog, null, cancellation: cancel.Token);
        check(results.All(r => r.State == "未実行") && executions == 1, "取消後は残りを未実行とし強制停止しない");
        var progressRows = new List<InstallResult>();
        results = await runner.Run([supported, supported], "uninstall", null, null, new ImmediateProgress<InstallResult>(progressRows.Add));
        check(results.Length == 1 && executions == 2, "削除は公開配布情報不要・重複対象を一度だけ処理");
        check(progressRows.Single(r => r.State == "処理中").Detail == "実行中です", "公開配布の実行中メッセージを簡潔にする");
        failDownload = false;
        results = await runner.Run([unsupported[0]], "install", null, "local-packages.json");
        check(results[0].State == "成功" && downloads == 2, "開発者のローカル台帳は公開前でも利用可能");
        var msix = tools.Single(t => t.Id == "http-selector");
        check(ToolInstallationRunner.UnsupportedReason(msix) == null, "固定IdentityのMSIXだけを専用導入経路へ許可する");
        check(ToolInstallationRunner.UnsupportedReason(msix with { Msix = new("Other_1234567890123", MsixPolicy.Publisher) }) != null, "未知のFamilyを本体導入許可へ追加しない");
        check(ToolInstallationRunner.UnsupportedReason(msix with { Msix = null }) != null, "HttpSelectorのMSIX必須条件をconfigで取り外せない");
        results = await runner.Run(tools.Where(t => t.Id is "capchun-screen" or "chulip-video" or "chunap-tool").ToArray(), "install", catalog, null);
        check(results.Length == 3 && results.All(r => r.State == "成功"), "公開済み追加3製品は共通取得・導入処理へ接続できる");
        var package = new LocalPackage(msix.Id, msix.Name, "0.0.3.3", msix.AppId, MsixPolicy.FileName("0.0.3.3"), new string('a', 64), "signed-msix", "msix");
        results = await runner.Run([msix], "install", new(1, [new(package, 100)]), null);
        check(results.Single().State == "成功", "MSIX公開配布も取得・専用実行へ接続する");
        check(ToolInstallationRunner.PublicReason(msix, new(1, [new(package with { Kind = "inno" }, 100)])) != null, "MSIXにEXE種別の台帳を渡さない");
    }
}
