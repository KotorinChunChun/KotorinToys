using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using KotorinToys;

internal static class ReplacementTests
{
    public static async Task<int> Serve(string id, string mode)
    {
        var pipeName = "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + id;
        while (true)
        {
            using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, leaveOpen: true);
            using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            var request = await reader.ReadLineAsync() ?? "";
            var stop = request != "status" && JsonDocument.Parse(request).RootElement.GetProperty("command").GetString() == "stop";
            var pid = mode == "spoof" ? Environment.ProcessId + 1 : Environment.ProcessId;
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { pid, ready = true, protocolVersion = 2,
                result = stop ? mode == "refuse" ? "refused" : "accepted" : "completed", detail = stop && mode == "refuse" ? "テスト用の終了拒否" : "", launchOrigin = "standalone" }));
            if (stop && mode != "refuse") return 0;
        }
    }

    public static async Task Run(string root, string fixtures, Action<bool, string> check)
    {
        var directory = Path.Combine(fixtures, "replacement");
        var oldRoot = Path.Combine(directory, "old"); var newRoot = Path.Combine(directory, "new");
        Directory.CreateDirectory(Path.Combine(oldRoot, "data")); Directory.CreateDirectory(newRoot);
        var tool = new ToolDefinition("ss-capture", "SSCapture", "SSCapture.exe", "README.md");
        var source = Path.Combine(oldRoot, "SSCapture.exe"); var target = Path.Combine(newRoot, "SSCapture.exe");
        var oldSettings = Path.Combine(oldRoot, "data/settings.json");
        var newSettings = Path.Combine(newRoot, "data/settings.json");
        File.WriteAllText(oldSettings, "元の設定");
        File.WriteAllText(Path.Combine(oldRoot, "data/last-region.json"), "元の履歴");
        ReplacementData.Preserve(tool, source, target);
        check(File.ReadAllText(oldSettings) == "元の設定" && File.ReadAllText(newSettings) == "元の設定" &&
            File.ReadAllText(Path.Combine(newRoot, "data/last-region.json")) == "元の履歴", "別配置の既知の設定と履歴を元を残して引き継ぐ");
        File.WriteAllText(newSettings, "導入先の設定");
        try { ReplacementData.Preserve(tool, source, target); check(false, "設定競合を検出"); }
        catch (IOException) { check(File.ReadAllText(oldSettings) == "元の設定" && File.ReadAllText(newSettings) == "導入先の設定", "設定競合は双方を上書きしない"); }

        var exe = Path.Combine(AppContext.BaseDirectory, "KotorinToys.Tests.exe");
        check(File.Exists(exe), "隔離テスト用EXEがある");
        foreach (var mode in new[] { "good", "refuse", "spoof" })
        {
            var id = "replacement-test-" + Guid.NewGuid().ToString("N");
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--replacement-fixture"); start.ArgumentList.Add(id); start.ArgumentList.Add(mode);
            using var process = Process.Start(start) ?? throw new IOException("隔離テスト用プロセスを起動できません。");
            try
            {
                var isolated = new ToolDefinition(id, "隔離テスト", "missing/KotorinToys.Tests.exe", "README.md",
                    InstalledExecutable: "KotorinToys.Tests.exe", Capabilities: ["status", "stop"]);
                var manager = new ToolManager(root);
                await Task.Delay(800);
                var state = await manager.StatusForReplacement(isolated);
                if (mode == "spoof")
                {
                    check(state.State == "確認できない" && !process.HasExited, "管理窓口と応答のPID不一致は停止しない");
                    continue;
                }
                check(state.State == "実行中" && state.Pid == process.Id && state.RunningExecutable == exe && state.ProcessStartedAtUtc != null,
                    "別配置の実プロセスと管理窓口を照合：" + mode + " / " + state.State + " / " + state.Detail + " / " + state.RunningExecutable);
                var stopped = await manager.StopForReplacement(isolated, state);
                if (mode == "good")
                    check(stopped.State == "停止中" && process.HasExited, "照合した実プロセスだけを正常終了する");
                else
                    check(stopped.State == "実行中" && stopped.Detail.Contains("終了拒否") && !process.HasExited, "終了拒否時にプロセスを残す");
            }
            finally
            {
                if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            }
        }
    }
}
