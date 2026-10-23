namespace KotorinToys;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var rootIndex = Array.IndexOf(args, "--root");
        var root = rootIndex >= 0 && rootIndex + 1 < args.Length ? Path.GetFullPath(args[rootIndex + 1]) : FindRoot();
        var settingsIndex=Array.IndexOf(args,"--settings-dir");
        var settingsDirectory=settingsIndex>=0 && settingsIndex+1<args.Length?Path.GetFullPath(args[settingsIndex+1]):HostSettingsStore.DefaultDirectory;
        if (args.Contains("--self-test")) { Environment.ExitCode = SelfTest.Run(root); return; }
        using var mutex = new Mutex(true, @"Local\KotorinToys.Dashboard", out var first);
        if (!first) { if (!args.Contains("--startup") && !args.Contains("--hidden")) Integration.ManagementEndpoint.ShowExisting("dashboard"); return; }
        using var context = new HostApplicationContext(root, args.Contains("--startup"),settingsDirectory, args.Contains("--hidden"), args.Contains("--local-catalog"));
        Application.Run(context);
    }
    internal static string FindRoot()
    {
        string? bundledRoot = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "catalog", "tools.json")) && Directory.Exists(Path.Combine(dir.FullName, "tools")))
                return dir.FullName;
            if (bundledRoot == null && File.Exists(Path.Combine(dir.FullName, OnlineToolCatalog.RelativePath))) bundledRoot = dir.FullName;
        }
        if (bundledRoot != null) return bundledRoot;
        throw new DirectoryNotFoundException("catalog/tools.json と tools を持つプロジェクトルートが見つかりません。--root で指定してください。");
    }
}
