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
        var missing = ToolCatalog.Load(root).Single(t => t.Id == "ss-capture");
        result = await coordinator.Run(missing, catalog);
        check(result.State == "失敗" && result.Detail.Contains("配布台帳に対象製品がありません") && installs == previousInstalls && stops == previousStops,
            "ローカル台帳の対象欠落も日本語の理由で停止前に拒否する");

        var old = new ToolState("実行中", 123, LaunchOrigin: "standalone", RunningExecutable: "C:/debug/SmartPaste.exe", ProcessStartedAtUtc: DateTime.UtcNow);
        var fresh = new InstallationState("未導入", null, null, null);
        var after = new InstallationState("導入済み", "0.1.4", "C:/installed", "C:/installed/SmartPaste.exe");
        var events = new List<string>(); bool conflict = false, denyStop = false, failInstall = false;
        var replace = new ToolUpdateCoordinator(_ => fresh, _ => Task.FromResult(old), _ => throw new Exception("通常停止は使用しない"),
            (_, origin) => { events.Add("restart:" + origin); return Task.FromResult(new ToolState("実行中")); },
            (_, _, directory) => { events.Add("install:" + (directory ?? "default")); fresh = after; return Task.FromResult(new InstallResult(tool.Id, "install", failInstall ? "失敗" : "成功", "結果")); },
            stopExpected: (_, expected) => { events.Add("stop:" + expected.Pid); return Task.FromResult(new ToolState(denyStop ? "確認できない" : "停止中", Detail: "終了不可")); },
            preserveSidecars: (_, source, target) => { events.Add("data:" + source + "->" + target); if (conflict) throw new IOException("設定が異なります"); });
        result = await replace.Run(tool, catalog);
        check(result.State == "成功" && events.SequenceEqual(["stop:123", "install:default", "data:C:/debug/SmartPaste.exe->C:/installed/SmartPaste.exe", "restart:standalone"]),
            "別配置を照合して停止後、正式導入・設定保全・元経路で起動する");
        fresh = new("未導入", null, null, null); events.Clear(); conflict = true;
        result = await replace.Run(tool, catalog);
        check(result.State == "要確認" && result.Detail.Contains("設定が異なります") && !events.Any(e => e.StartsWith("restart:")),
            "設定競合時は両方を保持して新アプリを自動起動しない");
        fresh = new("未導入", null, null, null); events.Clear(); conflict = false; denyStop = true;
        result = await replace.Run(tool, catalog);
        check(result.State == "失敗" && events.SequenceEqual(["stop:123"]) && result.Detail.Contains("終了不可"), "正常終了できない場合は導入しない");
        fresh = new("未導入", null, null, null); events.Clear(); denyStop = false; failInstall = true;
        result = await replace.Run(tool, catalog);
        check(result.State == "失敗" && !events.Any(e => e.StartsWith("restart:")) && result.Detail.Contains("元アプリは停止したまま"),
            "別配置の導入失敗時は別製品を自動起動しない");
    }
}
