using KotorinToys;

internal static class ToolUpdateTests
{
    public static async Task Run(string root, string fixtures, Action<bool, string> check)
    {
        var tool = ToolCatalog.Load(root).Single(t => t.Id == "smart-paste");
        var catalog = Path.Combine(fixtures, "installer", "packages.json");
        var registered = new InstallationState("導入済み", "0.1.3", "C:/専用導入先/SmartPaste", "C:/専用導入先/SmartPaste/SmartPaste.exe");
        int stops = 0, starts = 0, installs = 0;
        ToolState before = new("実行中", 42, LaunchOrigin: "host_via");
        ToolState stopped = new("停止中"), restarted = new("実行中");
        InstallResult outcome = new(tool.Id, "install", "成功", "導入照合済み");
        var coordinator = new ToolUpdateCoordinator(_ => registered, _ => Task.FromResult(before),
            _ => { stops++; return Task.FromResult(stopped); },
            (_, origin) => { starts++; check(origin == "host_via", "更新後は元の起動経路を渡す"); return Task.FromResult(restarted); },
            (_, _, directory) => { installs++; check(directory == registered.Directory, "更新は登録されたカスタム導入先を維持"); return Task.FromResult(outcome); });
        var results = await new InstallationQueue(new InstallerAdapter(new(root)), coordinator).Run([tool], "install", catalog);
        check(results.Single().State == "成功" && stops == 1 && starts == 1 && installs == 1, "一括導入から正常停止・更新・起動復帰へ接続");
        stopped = new("停止処理中");
        var result = await coordinator.Run(tool, catalog);
        check(result.State == "失敗" && installs == 1 && starts == 1, "停止未確認では導入・再起動をしない");
        stopped = new("停止中"); before = new("停止中");
        result = await coordinator.Run(tool, catalog);
        check(result.State == "成功" && starts == 1, "元が停止中なら更新後も停止を維持");
        before = new("実行中", 42, LaunchOrigin: "host_via");
        outcome = new(tool.Id, "install", "失敗", "模擬失敗");
        result = await coordinator.Run(tool, catalog);
        check(result.State == "失敗" && starts == 2 && result.Detail.Contains("復帰しました"), "更新失敗時も以前の起動状態を復帰");
        outcome = new(tool.Id, "install", "成功", "照合済み"); restarted = new("確認できない");
        result = await coordinator.Run(tool, catalog);
        check(result.State == "要確認", "更新成功でも再起動未確認を全件成功にしない");
        registered = registered with { Version = "9.0.0" };
        int previousInstalls = installs, previousStops = stops;
        result = await coordinator.Run(tool, catalog);
        check(result.State == "失敗" && installs == previousInstalls && stops == previousStops, "導入済みより古い版への変更を停止前に拒否");
    }
}
