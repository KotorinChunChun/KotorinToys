using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using KotorinToys;

internal static class MdVisualizerIntegrationTests
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder title, int count);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    static IntPtr[] FindWindows(int pid)
    {
        var found = new List<IntPtr>();
        EnumWindow callback = (window, _) => {
            GetWindowThreadProcessId(window, out var owner);
            var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
            if (owner == pid && title.ToString().Contains("MdVisualizer - ver 0.0.19")) found.Add(window);
            return true;
        };
        EnumWindows(callback, 0); GC.KeepAlive(callback); return found.ToArray();
    }
    static IntPtr FindWindow(int pid) => FindWindows(pid).FirstOrDefault();
    public static async Task<int> Run()
    {
        var root = KotorinToys.Program.FindRoot();
        var directory = Path.Combine(root, "dev", "history", "v0.1.11", "logs", "mdvisualizer-ipc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settingsDirectory = Path.Combine(directory, "settings.md");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settingsDirectory.Replace('/', '\\').ToLowerInvariant()))).ToLowerInvariant()[..16];
        var manager = new ToolManager(root, (tool, info) => {
            if (tool.Id != "md-visualizer-" + key) throw new IOException("試験対象以外は起動しません。");
            info.ArgumentList.Add("--settings-dir"); info.ArgumentList.Add(settingsDirectory);
        });
        var tool = manager.Load().Single(t => t.Id == "md-visualizer") with { Id = "md-visualizer-" + key };
        var messages = new List<string>(); int? owned = null;
        void Check(bool ok, string message) { if (!ok) throw new IOException(message); messages.Add("PASS: " + message); Console.WriteLine(messages[^1]); }
        ProcessStartInfo Standalone(params string[] args)
        {
            var info = new ProcessStartInfo(manager.Executable(tool)) { UseShellExecute = false };
            info.ArgumentList.Add("--settings-dir"); info.ArgumentList.Add(settingsDirectory);
            foreach (var arg in args) info.ArgumentList.Add(arg);
            return info;
        }
        try
        {
            var existing = Process.GetProcessesByName("mdvisualizer");
            try { Check(existing.All(p => !string.Equals(p.MainModule?.FileName, manager.Executable(tool), StringComparison.OrdinalIgnoreCase)), "試験EXEは未起動で、別配置の既存版とは識別子・管理窓口・設定を分離"); } finally { foreach (var p in existing) p.Dispose(); }
            Check((await manager.Status(tool)).State == "停止中", "開始前は停止中");
            Check((await manager.Start(tool, false, "via_kotorintoys")).State == "確認できない", "本体の実行入口でも自動起動を拒否");
            var started = await manager.Start(tool); owned = started.Pid;
            Check(started.State == "実行中" && owned.HasValue, "本体ToolManagerから実EXEを通常起動: " + started.Detail);
            using var process = Process.GetProcessById(owned!.Value);
            Check(started.Version == tool.Version && started.ProtocolVersion == 2 && started.LaunchOrigin == "host_manual", "製品版・管理契約2・起動経路を確認");
            var window = FindWindow(owned.Value);
            Check(window != 0 && IsWindowVisible(window), "通常起動で主画面と共通タイトルを表示");
            var state = await manager.Send(tool, "status");
            Check(state.Ready && state.Capabilities!.SequenceEqual(["status", "show", "stop"]), "frontend準備完了と3操作の能力を確認");
            Check((await manager.Send(tool, "show", owned + 1)).Result == "refused" &&
                (await manager.Send(tool, "stop", owned + 1)).Result == "refused" &&
                (await manager.Send(tool, "stop")).Result == "refused", "誤PIDと対象PID欠落の操作を拒否");
            Check((await manager.Send(tool, "shortcuts", owned)).Result == "unsupported", "未対応操作を成功扱いしない");
            Check((await manager.Start(tool)).Pid == owned, "再度起動しても同じPIDを保持");
            ShowWindow(window, 6);
            Check((await manager.Start(tool)).Pid == owned && !IsIconic(window), "最小化した同じ画面を復元");
            using (var secondary = Process.Start(Standalone())!) {
                await secondary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Check(!process.HasExited && (await manager.Status(tool)).Pid == owned, "引数なしの二重起動も既存PIDへ集約");
            }
            var document = Path.Combine(directory, "連携試験.md");
            File.WriteAllText(document, "# 連携試験\n専用の試験文書です。\n");
            using (var documentLaunch = Process.Start(Standalone(document))!) {
                await documentLaunch.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                var documentDeadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < documentDeadline && (FindWindows(owned.Value).Length != 2 || !(await manager.Send(tool, "status")).Ready)) await Task.Delay(200);
                Check(FindWindows(owned.Value).Length == 2 && (await manager.Send(tool, "status")).Ready, "関連付け相当の文書起動で同じPIDに2画面を作成し全画面の準備を確認");
            }
            Check((await manager.Stop(tool)).State == "停止中" && process.HasExited, "全画面の終了確認を受けて実プロセスが正常終了"); owned = null;
            Check(File.Exists(Path.Combine(directory, "settings.md", "settings.json")), "設定を文書風の名前でも専用フォルダーへ保存");
            using (var background = Process.Start(Standalone("--startup"))!) {
                await background.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(background.ExitCode == 2, "単体EXEも無表示常駐起動を拒否");
            }
            using var manual = Process.Start(Standalone())!; owned = manual.Id;
            var limit = DateTime.UtcNow.AddSeconds(30); ToolState manualState;
            do { manualState = await manager.Status(tool); if (manualState.State == "実行中") break; await Task.Delay(200); } while (DateTime.UtcNow < limit);
            Check(manualState.State == "実行中" && manualState.Pid == owned && manualState.LaunchOrigin == "standalone" && IsWindowVisible(FindWindow(owned.Value)), "別PIDで通常再起動し画面を表示");
            Check(PostMessage(FindWindow(owned.Value), 0x0010, 0, 0), "通常のウィンドウ終了を依頼");
            await manual.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Check((await manager.Status(tool)).State == "停止中", "最後の画面を閉じるとプロセスが終了し常駐しない"); owned = null;
            Console.WriteLine($"PASS: {messages.Count} 件 / {directory}"); return 0;
        }
        catch (Exception error) { messages.Add("FAIL: " + error); Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (owned.HasValue) try {
                var state = await manager.Status(tool);
                if (state.Pid == owned && state.State == "実行中") messages.Add("後始末: " + (await manager.Stop(tool)).State);
            } catch (Exception error) { messages.Add("正常終了未確認: " + error.Message); }
            File.WriteAllLines(Path.Combine(directory, "result.txt"), messages);
        }
    }
}
