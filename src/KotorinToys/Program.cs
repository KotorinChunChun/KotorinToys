namespace KotorinToys;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var root = args.Length == 2 && args[0] == "--root" ? Path.GetFullPath(args[1]) : FindRoot();
        if (args.Contains("--self-test")) { Environment.ExitCode = SelfTest.Run(FindRoot()); return; }
        using var mutex = new Mutex(true, @"Local\KotorinToys.Dashboard", out var first);
        if (!first) return;
        Application.Run(new Dashboard(root));
    }
    internal static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "catalog", "tools.json")) && Directory.Exists(Path.Combine(dir.FullName, "tools")))
                return dir.FullName;
        throw new DirectoryNotFoundException("catalog/tools.json と tools を持つプロジェクトルートが見つかりません。--root で指定してください。");
    }
}
