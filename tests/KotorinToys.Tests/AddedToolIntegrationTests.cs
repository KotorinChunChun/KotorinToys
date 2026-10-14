using System.Diagnostics;
using System.Text.Json;
using KotorinToys;

internal static class AddedToolIntegrationTests
{
    // 通常本体のMutex・通知領域・保存先を変更しない最終画面確認。
    public static int Panel()
    {
        var root = KotorinToys.Program.FindRoot();
        var directory = Path.Combine(root, "dev", "history", "v0.1.0", "logs", "N3-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manager = new ToolManager(root, (tool, start) =>
        {
            if (tool.Id is not ("capchun-screen" or "chulip-video" or "chunap-tool"))
                throw new IOException("この検証画面では新規3件だけ起動できます。");
            start.ArgumentList.Add("--settings-dir"); start.ArgumentList.Add(Path.Combine(directory, tool.Id));
            if (tool.Id == "capchun-screen") start.ArgumentList.Add("--passive");
        });
        var store = new HostSettingsStore(Path.Combine(directory, "host"));
        var coordinator = new StartupCoordinator(store, new ReadOnlyGateway(), manager, Environment.ProcessPath!);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using var form = new Dashboard(root, store, coordinator, toolManager: manager);
        Console.WriteLine("検証用画面。設定保存先: " + directory);
        Application.Run(form);
        return 0;
    }

    sealed class ReadOnlyGateway : IStartupGateway
    {
        public ScheduledTaskState? Read(string name) => null;
        public ScheduledTaskState Create(TaskPlan plan) => throw new IOException("検証画面では予約を変更しません。");
        public ScheduledTaskState Restore(string name, string xml, string securityDescriptor = "") => throw new IOException("検証画面では予約を変更しません。");
        public void Delete(string name, string expectedFingerprint) => throw new IOException("検証画面では予約を変更しません。");
    }

    public static async Task<int> Run()
    {
        var root = KotorinToys.Program.FindRoot();
        var directory = Path.Combine(root, "dev", "history", "v0.1.0", "logs", "N3-ipc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manager = new ToolManager(root, (tool, start) =>
        {
            start.ArgumentList.Add("--settings-dir");
            start.ArgumentList.Add(Path.Combine(directory, tool.Id));
            // CapchunScreenだけホットキーを取得しない。ChunapToolは対象未選択なら外部窓を操作しない。
            if (tool.Id == "capchun-screen") start.ArgumentList.Add("--passive");
        });
        var tools = manager.Load().Where(tool => tool.Id is "capchun-screen" or "chulip-video" or "chunap-tool").ToArray();
        var messages = new List<string>();
        var owned = new List<ToolDefinition>();
        int count = 0;
        void Check(bool result, string message)
        {
            if (!result) throw new IOException(message);
            count++; messages.Add("PASS " + message); Console.WriteLine(messages[^1]);
        }
        try
        {
            Check(tools.Length == 3, "新規3件だけを試験対象にする");
            // 全対象の初期状態を確認してから起動する。既存利用中のプロセスは終了しない。
            foreach (var tool in tools)
                Check((await manager.Status(tool)).State == "停止中", tool.Name + " 開始前は停止中");
            foreach (var tool in tools)
            {
                owned.Add(tool);
                var state = await manager.Start(tool, false);
                Check(state.State == "実行中" && state.Pid.HasValue, tool.Name + " 実EXEの準備完了: " + state.Detail);
                Check(state.Version == tool.Version && state.LaunchOrigin == "host_manual" && state.ProtocolVersion == 2, tool.Name + " 実版・起動経路・プロトコル一致");
                var status = await manager.Send(tool, "status", state.Pid);
                Check(status.Ready && status.Capabilities?.SequenceEqual(["status", "show", "stop"]) == true, tool.Name + " UI応答と能力を確認");
                using var running = Process.GetProcessById(state.Pid!.Value);
                Check(!running.HasExited && running.MainWindowHandle == 0, tool.Name + " startupでメイン画面を表示しない");
                Check((await manager.Send(tool, "show", state.Pid + 1)).Result == "refused", tool.Name + " 誤PID表示を拒否");
                Check((await manager.Send(tool, "stop", state.Pid + 1)).Result == "refused", tool.Name + " 誤PID停止を拒否");
                Check((await manager.Start(tool, false)).Pid == state.Pid, tool.Name + " 既存PIDを再利用");
                Check((await manager.Start(tool, true)).Pid == state.Pid, tool.Name + " 同じPIDに画面表示を要求");
                running.Refresh();
                Check(running.MainWindowHandle != 0 && running.MainWindowTitle == tool.Name + " - ver " + tool.Version, tool.Name + " 実ウィンドウ・共通タイトル");
                // 同一バイナリの直接二重起動も新しい常駐を作らず既存画面へ渡す。
                using (var duplicate = Process.Start(new ProcessStartInfo(manager.Executable(tool)) { UseShellExecute = false })!)
                {
                    await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    Check(duplicate.ExitCode == 0 && (await manager.Status(tool)).Pid == state.Pid, tool.Name + " 直接二重起動でも既存PIDを維持");
                }
                Check((await manager.Stop(tool)).State == "停止中" && running.HasExited, tool.Name + " 正常停止を実プロセスで確認");
                if (tool.Id != "chunap-tool")
                {
                    var settings = Path.Combine(directory, tool.Id, "settings.json");
                    Check(File.Exists(settings) && JsonDocument.Parse(File.ReadAllText(settings)).RootElement.ValueKind == JsonValueKind.Object, tool.Name + " 隔離設定へ正常保存");
                }
                var restarted = await manager.Start(tool, false, "via_kotorintoys");
                Check(restarted.State == "実行中" && restarted.Pid != state.Pid && restarted.LaunchOrigin == "via_kotorintoys", tool.Name + " 再起動と経路変更");
                Check((await manager.Stop(tool)).State == "停止中", tool.Name + " 再起動した試験プロセスも正常停止");
            }
            Console.WriteLine($"PASS: {count} 件");
            return 0;
        }
        catch (Exception error) { messages.Add("FAIL " + error); Console.Error.WriteLine(error); return 1; }
        finally
        {
            foreach (var tool in owned)
            {
                try
                {
                    var state = await manager.Status(tool);
                    if (state.State == "実行中") messages.Add(tool.Name + " 後始末: " + (await manager.Stop(tool)).State);
                }
                catch (Exception error) { messages.Add(tool.Name + " 復元未確認: " + error.Message); }
            }
            File.WriteAllLines(Path.Combine(directory, "result.txt"), messages);
        }
    }
}
