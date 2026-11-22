using System.Diagnostics;
using System.Text.Json;
using KotorinToys;

internal static class AgentTasktrayViewerTests
{
    public static void Registration(string root, Action<bool, string> check)
    {
        var tool = ToolCatalog.Load(root).Single(t => t.Id == "agent-tasktray-viewer");
        var online = OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "config", "tools.json")), root).Single(t => t.Id == tool.Id);
        check(tool is { Name: "AgentTasktrayViewer", Version: "0.0.1", AppId: "KotorinToys.AgentTasktrayViewer", DistributionStatus: "unpublished" }, "独立した未公開ツールとして登録");
        check(JsonSerializer.Serialize(tool with { ToolRoot = tool.ToolRoot.Replace('\\', '/') }, ToolCatalog.Json) == JsonSerializer.Serialize(online, ToolCatalog.Json), "ローカル・配信用定義が一致");
        var child = new ToolManager(root).DevelopmentRoot(tool);
        check(child == Path.Combine(root, "tools", "AgentTasktrayViewer") && File.Exists(Path.Combine(child, ".git")), "独立Gitのサブモジュールへ解決");
        check(new[] { tool.Manual, tool.Readme, tool.Shortcuts }.All(p => File.Exists(PathSafety.Resolve(child, p))), "利用説明とショートカット定義が実在");
        check(tool.Capabilities!.SequenceEqual(["status", "show", "stop", "silent-start"]), "常駐ツールの共通管理能力を宣言");
        check(tool.Runtimes!.Any(r => r.Kind == "dotnet-desktop" && r.Major == 10), ".NET 10 Desktopの依存を宣言");
        check(ToolRepository.Find(tool) == null && ProductIdentity.FromTool(tool) == null, "公開URLやInno導入方式を推測しない");
        check(!ToolActionPolicy.Evaluate("install", tool, new("未導入", null, null, null), new("停止中")).Enabled, "未公開製品のインストールを許可しない");
    }

    public static async Task<int> Integration()
    {
        var root = KotorinToys.Program.FindRoot();
        var manager = new ToolManager(root);
        var tool = manager.Load().Single(t => t.Id == "agent-tasktray-viewer");
        var directory = Path.Combine(root, "dev", "history", "v0.1.11", "logs", "agent-tasktray-viewer-" + Guid.NewGuid().ToString("N"));
        var owned = new List<Process>();
        int count = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new IOException(message);
            Console.WriteLine("PASS: " + message); count++;
        }
        Process Launch(params string[] extra)
        {
            var start = new ProcessStartInfo(manager.Executable(tool)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(manager.Executable(tool)) };
            foreach (var arg in extra) start.ArgumentList.Add(arg);
            start.ArgumentList.Add("--settings-dir"); start.ArgumentList.Add(directory);
            start.ArgumentList.Add("--passive");
            var process = Process.Start(start) ?? throw new IOException("試験プロセスを起動できません。");
            owned.Add(process); return process;
        }
        async Task<ToolState> Ready(Process process)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                var state = await manager.Status(tool);
                if (state.State == "実行中" && state.Pid == process.Id) return state;
                await Task.Delay(100);
            }
            throw new IOException("試験プロセスの管理窓口が準備完了になりません。");
        }
        try
        {
            Registration(root, Check);
            Check((await manager.Status(tool)).State == "停止中" && Process.GetProcessesByName("AgentTasktrayViewer").Length == 0, "既存アプリが停止中であることを確認して試験");
            Directory.CreateDirectory(directory);
            var silent = Launch("--startup", "--launch-origin", "host_manual");
            var state = await Ready(silent);
            Check(state.Version == tool.Version && state.ProtocolVersion == 2 && state.LaunchOrigin == "host_manual", "実版・管理契約2・起動経路が一致");
            silent.Refresh();
            Check(silent.MainWindowHandle == IntPtr.Zero, "startupは設定画面を表示しない");
            var hiddenDuplicate = Launch("--hidden");
            await hiddenDuplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            silent.Refresh();
            Check(hiddenDuplicate.ExitCode == 0 && silent.MainWindowHandle == IntPtr.Zero, "hiddenの二重起動も画面を開かない");
            Check((await manager.Send(tool, "show", silent.Id + 1)).Result == "refused" && (await manager.Send(tool, "stop", silent.Id + 1)).Result == "refused", "誤PIDの画面表示と停止を拒否");
            Check((await manager.Start(tool, true)).Pid == silent.Id, "本体から既存PIDの設定画面を開く");
            silent.Refresh();
            Check(silent.MainWindowHandle != IntPtr.Zero && silent.MainWindowTitle == "設定 - AgentTasktrayViewer - ver 0.0.1", "設定画面の実ハンドルと共通タイトルを確認");
            var duplicate = Launch();
            await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(duplicate.ExitCode == 0 && (await manager.Status(tool)).Pid == silent.Id, "通常の二重起動も既存PIDを維持");
            Check((await manager.Stop(tool)).State == "停止中", "本体から正常停止して管理窓口を解放");
            await silent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(silent.ExitCode == 0 && File.Exists(Path.Combine(directory, "settings.json")), "終了コード0と隔離設定の保存を確認");
            using (var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "settings.json"))))
                Check(!settings.RootElement.GetProperty("StartWithWindows").GetBoolean(), "自動起動は新規OFFのまま");
            var manual = Launch("--launch-origin", "standalone");
            Check((await Ready(manual)).LaunchOrigin == "standalone", "通常の単体起動経路を確認");
            manual.Refresh();
            Check(manual.MainWindowHandle != IntPtr.Zero && manual.MainWindowTitle == "設定 - AgentTasktrayViewer - ver 0.0.1", "通常起動は設定画面を表示");
            Check((await manager.Stop(tool)).State == "停止中", "再起動した試験プロセスも正常停止");
            await manual.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(manual.ExitCode == 0 && !File.Exists(Path.Combine(directory, "app.log")), "再起動・終了後にクラッシュなし");
            Console.WriteLine($"PASS: {count} 件。保存先: {directory}");
            return 0;
        }
        finally
        {
            // 終了漏れがあっても、この試験が直接起動したPID以外は操作しない。
            foreach (var process in owned)
            {
                if (!process.HasExited)
                {
                    try { await manager.Send(tool, "stop", process.Id); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (Exception) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                }
                process.Dispose();
            }
        }
    }
}
