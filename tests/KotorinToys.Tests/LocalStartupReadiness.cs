using KotorinToys;

internal static class LocalStartupReadiness
{
    public static async Task<int> Reapply(string root, string report)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var store = new HostSettingsStore(HostSettingsStore.DefaultDirectory);
        var before = store.Load();
        var expected = new[] { "live-text-marker", "smart-paste", "ss-capture" };
        if (before.StartupModes.Count != 3 || expected.Any(id => before.StartupModes.GetValueOrDefault(id) != "via_kotorintoys") ||
            before.OwnedTasks.Count != 1 || !before.OwnedTasks.ContainsKey("host")) throw new IOException("確認済みの共有予約構成ではありません。");
        SelfUpdateFiles.Write(Path.GetFullPath(report) + ".before.json", before);
        var manager = new ToolManager(root);
        var coordinator = new StartupCoordinator(store, new TaskSchedulerGateway(), manager, Path.Combine(root, "KotorinToys.exe"));
        var result = await coordinator.SetHost(before.HostStartup, before.ShowDashboardAtLogon);
        SelfUpdateFiles.Write(Path.GetFullPath(report), result);
        Console.WriteLine(result.Detail);
        return result.Success ? 0 : 1;
    }
    public static async Task<int> Run(string root, string report)
    {
        var manager = new ToolManager(Path.GetFullPath(root));
        var store = new HostSettingsStore(HostSettingsStore.DefaultDirectory);
        var coordinator = new StartupCoordinator(store, new TaskSchedulerGateway(), manager, Path.Combine(root, "KotorinToys.exe"));
        var modes = store.Load().StartupModes;
        var targets = new List<object>();
        foreach (var tool in manager.Load().Where(t => modes.GetValueOrDefault(t.Id) == "via_kotorintoys"))
        {
            var startup = await coordinator.State(tool);
            var state = await manager.Status(tool);
            targets.Add(new { tool.Id, Installation = manager.Installation(tool), Startup = startup, Execution = state });
            Console.WriteLine(tool.Name + " : " + startup.ActualState + " / " + state.State + " / PID=" + state.Pid);
        }
        SelfUpdateFiles.Write(Path.GetFullPath(report), new { ObservedAt = DateTimeOffset.UtcNow, Targets = targets });
        return 0;
    }
}
