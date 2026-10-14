using System.Diagnostics;
using KotorinToys;

internal static class ShortcutKeyCounterIntegrationTests
{
    static string Prepare(string root)
    {
        // 別配置・旧版を含め、既存の通常プロセスには一切操作しない。
        foreach (var process in Process.GetProcessesByName("ShortcutKeyCounter"))
        {
            using (process) { throw new IOException("ShortcutKeyCounterが既に動いています。隔離試験は開始しません。"); }
        }
        var directory = Path.Combine(root, "dev", "history", "v0.1.0", "logs", "shortcut-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("SHORTCUT_KEY_COUNTER_TEST_LOCALAPPDATA", directory);
        return directory;
    }

    public static async Task<int> Run()
    {
        var root = KotorinToys.Program.FindRoot(); var directory = Prepare(root);
        var manager = new ToolManager(root); var tool = manager.Load().Single(t => t.Id == "shortcut-key-counter");
        var messages = new List<string>(); int count = 0;
        void Check(bool value, string name) { if (!value) throw new IOException(name); count++; messages.Add("PASS: " + name); }
        try
        {
            Check((await manager.Status(tool)).State == "停止中", "開始前は停止中");
            var state = await manager.Start(tool, false);
            Check(state.State == "実行中" && state.Pid.HasValue && state.ProtocolVersion == 2, "Rust管理窓口を.NETのCurrentUserOnlyで照合");
            Check(state.LaunchOrigin == "host_manual" && state.Version == tool.Version, "実際の起動経路とCargo製品版");
            var status = await manager.Send(tool, "status");
            Check(status.Ready && status.Capabilities?.SequenceEqual(["status", "show", "stop"]) == true, "UI応答後の準備完了・管理能力");
            var wrongShow = await manager.Send(tool, "show", state.Pid!.Value + 1);
            Check(wrongShow.Result == "refused", "別PIDへの画面要求を拒否");
            var wrongStop = await manager.Send(tool, "stop", state.Pid.Value + 1);
            Check(wrongStop.Result == "refused", "別PIDへの停止を拒否");
            Check((await manager.Send(tool, "shortcuts", state.Pid)).Result == "unsupported", "未対応操作を成功としない");
            Check((await manager.Start(tool, false)).Pid == state.Pid, "既存PIDを維持して二重起動しない");
            Check((await manager.Start(tool, true)).Pid == state.Pid, "起動済みRustプロセスの画面表示完了を確認");
            Check((await manager.Start(tool, true)).Pid == state.Pid, "再度の画面表示でも同一PIDを維持");
            Check((await manager.Stop(tool)).State == "停止中", "正常終了を実プロセスで確認");
            var restarted = await manager.Start(tool, false, "via_kotorintoys");
            Check(restarted.State == "実行中" && restarted.Pid != state.Pid && restarted.LaunchOrigin == "via_kotorintoys", "再起動と経路変更を確認");
            Check((await manager.Stop(tool)).State == "停止中", "再起動した試験プロセスも正常終了");
            Check(File.Exists(Path.Combine(directory, "ShortcutKeyCounter", "ShortcutKeyCounter.db")), "隔離保存先だけに検証DBを生成");
            messages.Add($"PASS: {count} 件"); return 0;
        }
        catch (Exception ex) { messages.Add("FAIL: " + ex); return 1; }
        finally
        {
            try { if ((await manager.Status(tool)).State == "実行中") await manager.Stop(tool); }
            finally { File.WriteAllLines(Path.Combine(directory, "result.txt"), messages); foreach (var message in messages) Console.WriteLine(message); Console.WriteLine("検証ログ: " + directory); }
        }
    }

    // 通常本体のMutex・トレイ・設定・予約を置き換えず、同じ実フォームを確認する。
    public static int Panel()
    {
        var root = KotorinToys.Program.FindRoot(); var directory = Prepare(root);
        Console.WriteLine("隔離画面の検証保存先: " + directory);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new Dashboard(root);
        Application.Run(form);
        return 0;
    }

    public static async Task<int> StopOwned(int expectedPid)
    {
        var manager = new ToolManager(KotorinToys.Program.FindRoot());
        var tool = manager.Load().Single(t => t.Id == "shortcut-key-counter");
        var state = await manager.Status(tool);
        if (state.Pid != expectedPid || state.State != "実行中")
            throw new IOException("検証で起動したPIDと現在の管理対象が一致しません。停止しません。");
        var stopped = await manager.Stop(tool);
        if (stopped.State != "停止中") throw new IOException("正常停止未確認：" + stopped.Detail);
        Console.WriteLine("検証で起動したPIDだけを正常終了しました：" + expectedPid);
        return 0;
    }
}
