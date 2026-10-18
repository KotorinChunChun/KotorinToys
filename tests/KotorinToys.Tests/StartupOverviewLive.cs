using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KotorinToys;

internal static class StartupOverviewLive
{
    // 明示した場合だけ実OSの読取を行う。登録・削除・起動APIは呼ばない。
    public static async Task<int> Run(string developmentRoot, string fixtures)
    {
        var store = new HostSettingsStore(HostSettingsStore.DefaultDirectory);
        var host = new InstallationDetector().Detect(HostProduct.Definition);
        var root = host.State == "導入済み" && host.Directory != null ? host.Directory : developmentRoot;
        var manager = new ToolManager(root, catalogSource: new ToolCatalogSource(root, store.DirectoryPath));
        var gateway = new TaskSchedulerGateway();
        var settings = store.Load();
        var coordinator = new StartupCoordinator(store, gateway, manager, host.Executable ?? Environment.ProcessPath!);
        var reader = new StartupOverviewReader(coordinator);
        Dictionary<string, string> Files() => new[] { "settings.json", "settings.json.bak", "last-startup-attempt.json", "startup-operation.json", "last-startup-operation.json", "removal-operation.json" }
            .ToDictionary(name => name, name =>
            {
                var path = Path.Combine(store.DirectoryPath, name);
                return File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "なし";
            });
        Dictionary<string, string> Tasks() => settings.OwnedTasks.Values.ToDictionary(task => task.Name, task =>
        {
            try
            {
                var actual = gateway.Read(task.Name);
                return actual == null ? "なし" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual.Xml + "\n" + actual.SecurityDescriptor)));
            }
            catch (Exception ex) { return "未確認：" + ex.Message; }
        });
        var filesBefore = Files(); var tasksBefore = Tasks();
        var timer = Stopwatch.StartNew();
        var tools = manager.Load();
        var rows = await Task.WhenAll(tools.Select(async tool => new { tool.Id, tool.Name, Startup = await reader.Read(tool, default) }));
        timer.Stop();
        var filesAfter = Files(); var tasksAfter = Tasks();
        var unchanged = filesBefore.All(pair => filesAfter.GetValueOrDefault(pair.Key) == pair.Value) &&
            tasksBefore.All(pair => tasksAfter.GetValueOrDefault(pair.Key) == pair.Value && !pair.Value.StartsWith("未確認："));
        foreach (var row in rows) Console.WriteLine(row.Name + "：" + row.Startup.Text);
        Console.WriteLine($"読取：{timer.Elapsed.TotalSeconds:F2}秒 / 設定・所有タスクの変更なし：{unchanged}");
        var receipt = new { Root = root, HostState = host.State, ElapsedMilliseconds = timer.ElapsedMilliseconds, Unchanged = unchanged,
            FilesBefore = filesBefore, FilesAfter = filesAfter, TasksBefore = tasksBefore, TasksAfter = tasksAfter, Rows = rows };
        File.WriteAllText(Path.Combine(fixtures, "startup-overview-live.json"), JsonSerializer.Serialize(receipt, ToolCatalog.Json));
        return unchanged && rows.Length == tools.Length ? 0 : 1;
    }
}
