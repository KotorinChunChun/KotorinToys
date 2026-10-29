using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using KotorinToys;

internal static class PPPaintIntegrationTests
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder title, int count);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    delegate bool EnumWindow(IntPtr window, IntPtr parameter);

    static IntPtr FindWindow(int pid)
    {
        IntPtr found = 0;
        EnumWindow callback = (window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
            if (owner == pid && title.ToString() == "PPPaint - ver 0.2.0") found = window;
            return true;
        };
        EnumWindows(callback, 0); GC.KeepAlive(callback); return found;
    }

    public static async Task<int> Run()
    {
        var root = KotorinToys.Program.FindRoot();
        var directory = Path.Combine(root, "dev", "history", "v0.1.16", "logs", "pppaint-ipc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settingsDirectory = Path.Combine(directory, "settings");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settingsDirectory.Replace('/', '\\').ToLowerInvariant()))).ToLowerInvariant()[..16];
        var tool = PPPaintRegistrationTests.Load(root) with { Id = "pppaint-" + key };
        var manager = new ToolManager(root, (candidate, info) =>
        {
            if (candidate.Id != tool.Id) throw new IOException("試験対象以外は起動しません。");
            info.ArgumentList.Add("--settings-dir"); info.ArgumentList.Add(settingsDirectory);
        });
        var messages = new List<string>(); int? owned = null;
        void Check(bool ok, string message) { if (!ok) throw new IOException(message); messages.Add("PASS: " + message); Console.WriteLine(messages[^1]); }
        ProcessStartInfo Standalone(params string[] args)
        {
            var executable = manager.Executable(tool);
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) };
            info.ArgumentList.Add("--settings-dir"); info.ArgumentList.Add(settingsDirectory);
            foreach (var arg in args) info.ArgumentList.Add(arg);
            return info;
        }
        async Task<bool> WrongPidRejected(string command)
        {
            try { return (await manager.Send(tool, command, owned + 1)).Result == "refused"; }
            catch (IOException) { return true; }
        }
        try
        {
            PPPaintRegistrationTests.Run(root, Check);
            Check(File.Exists(manager.Executable(tool)), "PPPaintの管理用実EXEが存在");
            Check((await manager.Status(tool)).State == "停止中", "専用設定と管理窓口の開始前は停止中");
            Check((await manager.Start(tool, false, "via_kotorintoys")).State == "確認できない", "本体の実行入口でも自動起動を拒否");
            var started = await manager.Start(tool); owned = started.Pid;
            Check(started.State == "実行中" && owned.HasValue, "本体ToolManagerからPPPaintを通常起動: " + started.Detail);
            using var process = Process.GetProcessById(owned!.Value);
            Check(started.Version == tool.Version && started.ProtocolVersion == 2 && started.LaunchOrigin == "host_manual", "製品版・管理契約2・起動経路を確認");
            var window = FindWindow(owned.Value);
            Check(window != 0 && IsWindowVisible(window), "通常起動で主画面と共通タイトルを表示");
            var status = await manager.Send(tool, "status");
            Check(status.Ready && status.Capabilities!.SequenceEqual(["status", "show", "stop"]), "準備完了と3操作の管理能力を確認");
            Check(await WrongPidRejected("show") && await WrongPidRejected("stop") &&
                (await manager.Send(tool, "stop")).Result == "refused", "誤PIDと対象PID欠落の操作を拒否");
            Check((await manager.Send(tool, "shortcuts", owned)).Result == "unsupported", "未対応操作を成功扱いしない");
            Check((await manager.Start(tool)).Pid == owned, "再起動要求を既存PIDへ集約");
            ShowWindow(window, 6);
            Check((await manager.Start(tool)).Pid == owned && !IsIconic(window), "最小化した同じ画面を復元");
            using (var duplicate = Process.Start(Standalone())!)
            {
                await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Check(duplicate.ExitCode == 0 && !process.HasExited && (await manager.Status(tool)).Pid == owned, "単体の二重起動も既存PIDへ集約");
            }
            Check((await manager.Stop(tool)).State == "停止中" && process.HasExited, "空の編集画面を正常終了し管理窓口を解放"); owned = null;
            using (var background = Process.Start(Standalone("--startup"))!)
            {
                await background.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(background.ExitCode == 2, "単体EXEも無表示常駐起動を拒否");
            }
            using var manual = Process.Start(Standalone())!; owned = manual.Id;
            var limit = DateTime.UtcNow.AddSeconds(30); ToolState manualState;
            do { manualState = await manager.Status(tool); if (manualState.State == "実行中") break; await Task.Delay(200); } while (DateTime.UtcNow < limit);
            Check(manualState.State == "実行中" && manualState.Pid == owned && manualState.LaunchOrigin == "standalone" &&
                IsWindowVisible(FindWindow(owned.Value)), "単体の通常再起動で主画面を表示");
            Check(PostMessage(FindWindow(owned.Value), 0x0010, 0, 0), "通常のウィンドウ終了を依頼");
            await manual.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Check((await manager.Status(tool)).State == "停止中", "画面を閉じると実プロセスも終了し常駐しない"); owned = null;
            Console.WriteLine($"PASS: {messages.Count} 件 / {directory}"); return 0;
        }
        catch (Exception error) { messages.Add("FAIL: " + error); Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (owned.HasValue) try
            {
                var state = await manager.Status(tool);
                if (state.Pid == owned && state.State == "実行中") messages.Add("後始末: " + (await manager.Stop(tool)).State);
            }
            catch (Exception error) { messages.Add("正常終了未確認: " + error.Message); }
            File.WriteAllLines(Path.Combine(directory, "result.txt"), messages);
        }
    }
}
