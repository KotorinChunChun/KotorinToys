using System.Diagnostics;
using System.Security.Cryptography;
using KotorinToys;

internal static class ChuwitchInstallAcceptance
{
    internal static async Task<int> Run(string root, string catalog, string run)
    {
        var logs = Path.Combine(root, "dev", "history", "v" + HostProduct.CurrentVersion, "logs");
        run = Path.GetFullPath(run);
        catalog = Path.GetFullPath(catalog);
        Require(Path.IsPathFullyQualified(run) && Path.GetRelativePath(logs, run) is var relative &&
            relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative) &&
            !Directory.Exists(run), "未作成の版別ログ配下の試験フォルダーを指定してください。");
        Require(!Path.IsPathRooted(Path.GetRelativePath(logs, catalog)) &&
            !Path.GetRelativePath(logs, catalog).StartsWith("..", StringComparison.Ordinal), "版別ログ配下の配布台帳を指定してください。");
        var destination = Path.Combine(run, "app");
        var tool = OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root, OnlineToolCatalog.RelativePath)), root)
            .Single(item => item.Id == "chuwitch-window");
        var manager = new ToolManager(root, fixedTools: [tool]);
        var adapter = new InstallerAdapter(manager, diagnosticDirectory: run);
        Require(manager.Installation(tool).State == "未導入" && (await manager.Status(tool)).State == "停止中",
            "既存の正式導入または稼働中のアプリがあります。試験を開始しません。");
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "kotorichun", "chuwitchwindow", "config", "config.json");
        var before = Hash(settings);
        var retainedLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "kotorichun", "chuwitchwindow", "data", "logs");
        var logNames = Directory.Exists(retainedLogs) ? Directory.GetFiles(retainedLogs) : [];
        try
        {
            var result = await adapter.Install(tool, catalog, destination);
            Require(result.State == "成功", result.Detail);
            var installed = manager.Installation(tool);
            Require(installed.Directory == destination && installed.Version == tool.Version && installed.Uninstaller != null,
                "導入先・版・削除プログラムが一致しません。");
            Require(File.Exists(Path.Combine(manager.DocumentRoot(tool), tool.Manual)), "同梱の説明書がありません。");
            Console.WriteLine("PASS: 正式登録・インストール先・版・説明書");

            var running = await manager.Start(tool, showWindow: false);
            Require(running is { State: "実行中", Pid: not null, ProtocolVersion: 2 } &&
                running.Version == tool.Version && running.LaunchOrigin == "host_manual",
                "非表示の管理起動に失敗しました。" + running.Detail);
            using var process = Process.GetProcessById(running.Pid!.Value);
            var settingsTitle = $"ChuwitchWindow - ver {tool.Version}";
            bool SettingsVisible() => AddedToolIntegrationTests.IsWindowVisible(
                AddedToolIntegrationTests.FindWindow(process, settingsTitle));
            Require(!SettingsVisible(), "非表示の管理起動で設定画面が表示されました。");
            var shown = await manager.Start(tool, showWindow: true);
            Require(shown.State == "実行中" && shown.Pid == running.Pid, "設定画面の表示に失敗しました。" + shown.Detail);
            for (var attempt = 0; attempt < 50 && !SettingsVisible(); ++attempt)
                await Task.Delay(100);
            Require(SettingsVisible(), "同じPIDで設定画面を表示できません。");
            Require((await manager.Stop(tool)).State == "停止中", "管理窓口から正常終了できません。");
            Console.WriteLine("PASS: 無表示の管理起動・同じPIDで設定画面表示・正常停止");
        }
        finally
        {
            if (manager.Installation(tool).Directory == destination)
            {
                var result = await adapter.Uninstall(tool);
                Require(result.State == "成功" && manager.Installation(tool).State == "未導入", result.Detail);
            }
        }
        Require(Hash(settings) == before && logNames.All(File.Exists), "既存の設定またはログに変更がありました。");
        Console.WriteLine("PASS: 一時インストールの解除・既存設定とログの保持");
        return 0;
    }

    static string? Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;

    static void Require(bool condition, string message)
    {
        if (!condition) throw new IOException(message);
    }
}
