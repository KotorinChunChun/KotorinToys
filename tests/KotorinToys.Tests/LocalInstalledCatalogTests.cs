using KotorinToys;

internal static class LocalInstalledCatalogTests
{
    internal static async Task ProbeInstalled(string root)
    {
        var source = new ToolCatalogSource(root, HostSettingsStore.DefaultDirectory);
        var manager = new ToolManager(root, catalogSource: source);
        var tool = manager.Load().Single(t => t.Id == "office-assist");
        var installed = manager.Installation(tool);
        var state = await manager.Status(tool).WaitAsync(TimeSpan.FromSeconds(8));
        if (installed.State != "導入済み" || state.State != "実行中" || state.LaunchOrigin != "host_manual")
            throw new IOException($"導入・実行状態が一致しません: {installed.State} / {state.State} / {state.Detail}");
        Console.WriteLine($"PASS: OfficeAssist {installed.Version} / PID {state.Pid} / {state.LaunchOrigin} / 管理契約{state.ProtocolVersion}");
        var allStates = await Task.WhenAll(manager.Load().Select(t => manager.Status(t))).WaitAsync(TimeSpan.FromSeconds(12));
        Console.WriteLine($"PASS: 本体一覧の状態取得 {allStates.Length}件");
    }

    internal static async Task Run(string root, string fixtures, Action<bool, string> check)
    {
        var remote = OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "config", "tools.json")), root)[0];
        var local = remote with { Id = "office-assist", Name = "OfficeAssist", AppId = "KotorinToys.OfficeAssist",
            ToolRoot = "tools/OfficeAssist", Executable = "artifacts/OfficeAssist.exe", InstalledExecutable = "OfficeAssist.exe",
            RepositoryUrl = "", DistributionStatus = "unpublished", Msix = null };
        var dir = Path.Combine(fixtures, "local-installed");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "local-tools.json");
        void Write(params ToolDefinition[] definitions) => File.WriteAllBytes(path, OnlineCatalogTests.Encode(definitions));
        Write(local);
        bool installed = true;
        var source = new ToolCatalogSource(root, dir, _ => Task.FromResult(OnlineCatalogTests.Encode(remote)), _ => installed);
        check(source.Load().Count(t => t.Id == local.Id) == 1, "導入済みローカル定義を同梱一覧へ追加");
        var refresh = await source.Refresh();
        check(refresh.Accepted && refresh.Tools.Any(t => t.Id == local.Id) && source.Load().Any(t => t.Id == local.Id), "配信一覧の更新後もローカル定義を表示");
        check(source.KnownTools().Count(t => t.Id == local.Id) == 1, "予約照合にローカル定義を含める");
        var cached = File.ReadAllText(Path.Combine(dir, "tool-catalog-cache.json"));
        check(!cached.Contains("office-assist"), "公開キャッシュにローカル定義を書き込まない");
        installed = false;
        check(source.Load().All(t => t.Id != local.Id), "未導入のローカル定義を表示しない");
        installed = true;
        Write(local with { Id = remote.Id });
        check(source.Load().Count(t => t.Id == remote.Id) == 1 && source.Load().All(t => t.Name != local.Name), "配信IDの乗っ取りを拒否");
        Write(local with { AppId = remote.AppId });
        check(source.Load().Count(t => t.AppId == remote.AppId) == 1, "配信製品識別の重複を拒否");
        Write(local with { RepositoryUrl = "https://github.com/KotorinChunChun/OfficeAssist" });
        check(source.Load().All(t => t.Id != local.Id), "公開先を持つローカル定義を拒否");
        File.WriteAllText(path, "invalid");
        check(source.Load().Length == 1 && source.Load()[0].Id == remote.Id, "壊れたローカル定義で配信一覧を壊さない");
        Write(local);
        var restarted = new ToolCatalogSource(root, dir, _ => throw new HttpRequestException(), _ => true);
        check(restarted.Load().Any(t => t.Id == local.Id) && !(await restarted.Refresh()).Accepted && restarted.Load().Any(t => t.Id == local.Id), "再起動・オフラインでもローカル導入を保持");
    }
}
