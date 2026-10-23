using KotorinToys;

internal static class LocalCatalogTests
{
    public static void Run(string root, string fixtures, Action<bool, string> check)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            var settings = new HostSettingsStore(Path.Combine(fixtures, "local-catalog-settings"));
            var source = new ToolCatalogSource(root, settings.DirectoryPath, _ => Task.FromResult(OnlineCatalogTests.Encode()));
            source.Refresh().GetAwaiter().GetResult();
            var cache = Path.Combine(settings.DirectoryPath, "tool-catalog-cache.json");
            var before = File.ReadAllBytes(cache);
            var normal = HostApplicationContext.CreateManager(root, settings.DirectoryPath);
            check(normal.CatalogSource != null && normal.Load().Length == 0,
                "本体の通常起動は従来のオンライン一覧と正常キャッシュを使用");
            var local = HostApplicationContext.CreateManager(root, settings.DirectoryPath, localCatalog: true);
            var expected = ToolCatalog.Load(root);
            check(local.CatalogSource == null && local.Load().Select(t => t.Id).SequenceEqual(expected.Select(t => t.Id)) &&
                local.Load().Any(t => t.Id == "operation-recorder"),
                "ローカル指定起動は配信用一覧の非表示を採用せずOperationRecorderを保持");
            using var form = new Dashboard(root, settings, toolManager: local,
                releaseCheck: (_, fallback, _) => Task.FromResult(new PublicRelease(fallback, false, false, "隔離試験")),
                detectInstallation: _ => new("未導入", null, null, null),
                executionCheck: (_, _) => Task.FromResult(new ToolState("停止中")),
                startupCheck: (_, _) => Task.FromResult(new StartupOverview("未確認", "隔離試験")));
            form.CreateControl();
            var refresh = form.RefreshAll();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!refresh.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
            if (!refresh.IsCompleted) throw new TimeoutException("ローカル一覧の更新確認が完了しません。");
            refresh.GetAwaiter().GetResult();
            var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
            check(grid.Rows.Count == expected.Length && grid.Rows.Cast<DataGridViewRow>().Any(row => Equals(row.Tag, "operation-recorder")),
                "ローカル一覧の状態更新後もOperationRecorderの行を保持");
            check(File.ReadAllBytes(cache).SequenceEqual(before),
                "ローカル一覧の更新はオンライン一覧キャッシュを書き換えない");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
