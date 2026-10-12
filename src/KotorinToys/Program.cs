namespace KotorinToys;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var rootIndex = Array.IndexOf(args, "--root");
        var root = rootIndex >= 0 && rootIndex + 1 < args.Length ? Path.GetFullPath(args[rootIndex + 1]) : FindRoot();
        if (args.Contains("--self-test")) { Environment.ExitCode = SelfTest.Run(root); return; }
        using var mutex = new Mutex(true, @"Local\KotorinToys.Dashboard", out var first);
        if (!first) { if (!args.Contains("--startup")) Integration.ManagementEndpoint.ShowExisting("dashboard"); return; }
        using var context = new HostApplicationContext(root, args.Contains("--startup"));
        Application.Run(context);
    }
    internal static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "catalog", "tools.json")) && Directory.Exists(Path.Combine(dir.FullName, "tools")))
                return dir.FullName;
        throw new DirectoryNotFoundException("catalog/tools.json と tools を持つプロジェクトルートが見つかりません。--root で指定してください。");
    }
}
