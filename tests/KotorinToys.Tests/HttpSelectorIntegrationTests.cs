using System.Diagnostics;
using KotorinToys;

internal static class HttpSelectorIntegrationTests
{
    const string ToolId = "http-selector";
    const string ProductVersion = "0.0.3.2";
    sealed record OwnedProcess(int Pid, DateTime StartedUtc, string Executable);

    public static async Task<int> RunMsix()
    {
        RequireNoExistingProcess();
        var root = KotorinToys.Program.FindRoot(); var manager = new ToolManager(root);
        var tool = manager.Load().Single(t => t.Id == ToolId);
        var installed = manager.Installation(tool);
        if (installed.State != "導入済み" || installed.Version != ProductVersion) throw new IOException("署名付きMSIX 0.0.3.2を先に導入してください: " + installed.Detail);
        var owned = new List<OwnedProcess>(); var count = 0;
        void Check(bool value, string description) { if (!value) throw new IOException(description); count++; Console.WriteLine("PASS: " + description); }
        try
        {
            Check((await manager.Status(tool)).State == "停止中", "MSIX開始前は停止中");
            var before = DateTime.UtcNow;
            var state = await manager.Start(tool, false);
            Check(state.State == "実行中" && state.Pid.HasValue, "パッケージ起動APIでMSIXの管理窓口が準備完了: " + state.Detail);
            var first = InspectProcess(state.Pid!.Value, installed.Executable!, before); owned.Add(first);
            using var process = Process.GetProcessById(first.Pid);
            Check(MsixInstallation.MatchesProcess(process, tool.Msix!), "実プロセスが指定Package FamilyのIDを保持");
            Check(state.Version == ProductVersion && state.LaunchOrigin == "host_manual", "MSIX表示版・管理版0.0.3.2と起動経路");
            Check(process.MainWindowHandle == 0, "正式MSIXも無表示開始");
            Check((await manager.Start(tool, true)).Pid == first.Pid, "MSIX既存PIDを維持して画面を表示");
            var show = await manager.Send(tool, "show", first.Pid);
            Check(show.Result == "completed", "MSIX再表示のUI応答");
            Check((await manager.Send(tool, "stop", first.Pid + 1)).Result == "refused", "MSIXの別PID操作を拒否");
            Check((await manager.Stop(tool)).State == "停止中", "MSIXの正常停止を実プロセスで確認");
            var restartedAt = DateTime.UtcNow;
            var second = await manager.Start(tool, false, "via_kotorintoys");
            Check(second.State == "実行中" && second.Pid.HasValue, "MSIXをKotorinToys経由で再起動");
            owned.Add(InspectProcess(second.Pid!.Value, installed.Executable!, restartedAt));
            Check(second.LaunchOrigin == "via_kotorintoys", "MSIX再起動の経路を照合");
            Check((await manager.Stop(tool)).State == "停止中", "再起動したMSIXも正常停止");
            Console.WriteLine($"PASS: MSIX管理 {count} 件"); return 0;
        }
        finally
        {
            foreach (var item in owned)
            {
                if (!TryOpenOwned(item, out var process)) continue;
                using (process) { await RequireOwnedCurrent(manager, tool, item); await manager.Stop(tool); }
            }
        }
    }

    public static async Task<int> Run()
    {
        // 別配置・旧版・別セッションも含め、既存HttpSelectorがあれば試験を開始しない。
        RequireNoExistingProcess();
        var root = KotorinToys.Program.FindRoot();
        var catalog = new ToolManager(root);
        var registered = catalog.Load().Single(tool => tool.Id == ToolId);
        if (registered.Version != ProductVersion ||
            !string.Equals(registered.ToolRoot.Replace('\\', '/'), "tools/HttpSelector", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(registered.Executable.Replace('\\', '/'), "target/management/debug/HttpSelector.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("HttpSelector v0.0.3.2の管理用Debug配置を確認できません。試験は開始しません。");

        // 正式導入を優先する通常検出へ影響させず、この試験内だけ開発EXEに固定する。
        var tool = registered with { AppId = "", Msix = null };
        var executable = Path.GetFullPath(catalog.Executable(tool));
        if (!File.Exists(executable))
            throw new IOException("先にHttpSelectorを --profile dev --target-dir target/management でビルドしてください。");
        var directory = Path.GetFullPath(Path.Combine(catalog.DevelopmentRoot(tool), "dev", "history", "v0.0.3.2", "logs", "management-live-" + Guid.NewGuid().ToString("N")));
        var dataDirectory = Path.Combine(directory, "data");
        Directory.CreateDirectory(dataDirectory);
        var manager = new ToolManager(root, (definition, start) =>
        {
            if (definition.Id != ToolId || !string.Equals(Path.GetFullPath(start.FileName), executable, StringComparison.OrdinalIgnoreCase) ||
                !start.ArgumentList.Contains("--startup"))
                throw new IOException("試験対象ではない起動を拒否しました。");
            RequireNoExistingProcess();
            start.Environment["HTTPSELECTOR_TEST_DATA_DIR"] = dataDirectory;
            start.Environment["HTTPSELECTOR_TEST_NO_SCAN"] = "1";
            // Debug専用の受理条件を満たさないEXEは、保存先へ触る前に拒否する。
            start.ArgumentList.Add("--test-isolated");
        });
        var messages = new List<string>();
        var owned = new List<OwnedProcess>();
        var count = 0;
        var exitCode = 1;
        void Record(string message) { messages.Add(message); Console.WriteLine(message); }
        void Check(bool value, string message)
        {
            if (!value) throw new IOException(message);
            count++;
            Record("PASS: " + message);
        }
        async Task<OwnedProcess> StartOwned(string origin)
        {
            var requestedUtc = DateTime.UtcNow;
            var state = await manager.Start(tool, false, origin);
            Check(state.State == "実行中" && state.Pid.HasValue && state.ProtocolVersion == 2,
                "実EXEの準備完了と管理契約2: " + state.Detail);
            var process = InspectProcess(state.Pid!.Value, executable, requestedUtc);
            owned.Add(process);
            Record($"所有PID: {process.Pid} / 開始UTC: {process.StartedUtc:O} / {process.Executable}");
            Check(state.LaunchOrigin == origin && state.Version == ProductVersion,
                "実プロセスの版0.0.3.2と起動経路 " + origin);
            return process;
        }
        try
        {
            Check((await manager.Status(tool)).State == "停止中", "開始前は停止中");
            var first = await StartOwned("host_manual");
            using var running = Process.GetProcessById(first.Pid);
            running.Refresh();
            Check(!running.HasExited && running.MainWindowHandle == 0, "--startupでは可視メイン画面を開かない");
            var status = await manager.Send(tool, "status");
            Check(status.Pid == first.Pid && status.Ready && status.ProtocolVersion == 2 &&
                status.Result == "completed" && status.Version == ProductVersion && status.LaunchOrigin == "host_manual" &&
                status.Capabilities?.SequenceEqual(["status", "show", "stop"]) == true,
                ".NET CurrentUserOnlyでRust窓口の準備状態・PID・版・管理能力を照合");
            Check((await manager.Send(tool, "show", first.Pid + 1)).Result == "refused", "異なるPIDへの表示要求を拒否");
            Check((await manager.Send(tool, "stop", first.Pid + 1)).Result == "refused", "異なるPIDへの停止要求を拒否");
            Check((await manager.Send(tool, "shortcuts", first.Pid)).Result == "unsupported", "未対応操作を成功としない");
            Check((await manager.Start(tool, false)).Pid == first.Pid, "Start(false)は既存PIDを再利用");
            Check((await manager.Start(tool, true)).Pid == first.Pid, "Start(true)が同一PIDの表示完了を待つ");
            running.Refresh();
            Check(running.MainWindowHandle != 0 && running.MainWindowTitle == "HttpSelector - ver " + ProductVersion,
                "管理表示した実ウィンドウと4成分の共通タイトル");
            var shownAgain = await manager.Send(tool, "show", first.Pid);
            Check(shownAgain.Pid == first.Pid && shownAgain.Result == "completed", "再表示は同一PIDでcompletedを返す");
            await RequireOwnedCurrent(manager, tool, first);
            var stopped = await manager.Stop(tool);
            running.Refresh();
            Check(stopped.State == "停止中" && running.HasExited, "停止受理後に実プロセスの正常終了を確認");

            var second = await StartOwned("via_kotorintoys");
            Check(second.Pid != first.Pid || second.StartedUtc != first.StartedUtc, "終了したプロセスとは異なる実体へ再起動");
            using var restarted = Process.GetProcessById(second.Pid);
            restarted.Refresh();
            Check(restarted.MainWindowHandle == 0, "再起動も無表示で開始");
            await RequireOwnedCurrent(manager, tool, second);
            var stoppedAgain = await manager.Stop(tool);
            restarted.Refresh();
            Check(stoppedAgain.State == "停止中" && restarted.HasExited, "再起動した試験プロセスも正常終了");
            Check(File.Exists(Path.Combine(dataDirectory, "HttpSelector.db")) &&
                File.Exists(Path.Combine(dataDirectory, "HttpSelector.db.sig")), "隔離データ位置にDBと署名を生成");
            Record($"PASS: {count} 件");
            exitCode = 0;
        }
        catch (Exception error)
        {
            Record("FAIL: " + error);
        }
        finally
        {
            // 試験で準備完了した実体だけをPID・開始時刻・パスで照合して正常停止する。
            // 準備未確認や停止拒否の場合は強制終了せず、失敗状態と保存先を残す。
            foreach (var process in owned)
            {
                try
                {
                    if (!TryOpenOwned(process, out var current)) continue;
                    using (current)
                    {
                        await RequireOwnedCurrent(manager, tool, process);
                        var stop = await manager.Send(tool, "stop", process.Pid);
                        if (stop.Pid != process.Pid || stop.Result != "accepted")
                            throw new IOException("所有プロセスの正常停止が受理されませんでした: " + stop.Detail);
                        await current!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                        Record("後始末: 所有PID " + process.Pid + " が正常終了");
                    }
                }
                catch (Exception error)
                {
                    exitCode = 1;
                    Record("状態を保持: PID " + process.Pid + " / " + error.Message);
                }
            }
            Record("検証ログ: " + directory);
            File.WriteAllLines(Path.Combine(directory, "result.txt"), messages);
        }
        return exitCode;
    }

    static void RequireNoExistingProcess()
    {
        var existing = Process.GetProcessesByName("HttpSelector");
        try
        {
            if (existing.Length != 0)
                throw new IOException("HttpSelectorが既に動いています。既存プロセスを操作せず隔離試験を中止します。");
        }
        finally
        {
            foreach (var process in existing) process.Dispose();
        }
    }

    static OwnedProcess InspectProcess(int pid, string executable, DateTime requestedUtc)
    {
        using var process = Process.GetProcessById(pid);
        using var current = Process.GetCurrentProcess();
        var startedUtc = process.StartTime.ToUniversalTime();
        if (process.HasExited || process.SessionId != current.SessionId || startedUtc < requestedUtc.AddSeconds(-1) ||
            !string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
            throw new IOException("試験で起動したプロセスの実体を確認できません。操作を継続しません。");
        return new(pid, startedUtc, executable);
    }

    static bool TryOpenOwned(OwnedProcess expected, out Process? process)
    {
        Process candidate;
        try { candidate = Process.GetProcessById(expected.Pid); }
        catch (ArgumentException) { process = null; return false; }
        try
        {
            if (candidate.HasExited || candidate.StartTime.ToUniversalTime() != expected.StartedUtc ||
                !string.Equals(candidate.MainModule?.FileName, expected.Executable, StringComparison.OrdinalIgnoreCase))
            {
                candidate.Dispose();
                process = null;
                return false;
            }
            process = candidate;
            return true;
        }
        catch
        {
            candidate.Dispose();
            process = null;
            throw;
        }
    }

    static async Task RequireOwnedCurrent(ToolManager manager, ToolDefinition tool, OwnedProcess expected)
    {
        if (!TryOpenOwned(expected, out var process))
            throw new IOException("所有している実プロセスではありません。停止しません。");
        using (process)
        {
            var state = await manager.Status(tool);
            if (state.Pid != expected.Pid || state.State is not ("実行中" or "停止処理中"))
                throw new IOException("管理窓口と試験所有PIDが一致しません。停止しません。");
        }
    }
}
