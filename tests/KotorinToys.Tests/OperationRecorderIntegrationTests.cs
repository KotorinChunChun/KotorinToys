using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using KotorinToys;

internal static class OperationRecorderIntegrationTests
{
    const string ToolId = "operation-recorder";
    sealed record OwnedProcess(int Pid, DateTime StartedUtc, string Executable);
    sealed record WindowState(IntPtr Handle, string Title, bool Visible);
    delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(IntPtr window);

    sealed class Session
    {
        public string DirectoryPath { get; }
        public string SettingsPath => Path.Combine(DirectoryPath, "settings");
        public ToolManager Manager { get; }
        public ToolDefinition Tool { get; }
        public List<string> Messages { get; } = [];
        readonly List<OwnedProcess> owned = [];
        DateTime? launchRequestedUtc;
        readonly string executable;

        public Session(string root, string purpose)
        {
            // 古い版・別配置も含め、通常利用中のインスタンスには一切操作しない。
            var existing = Process.GetProcessesByName("OperationRecorder");
            try
            {
                if (existing.Length != 0)
                    throw new IOException("OperationRecorderが既に動いています。既存プロセスを変更せず隔離試験を中止します。");
            }
            finally { foreach (var process in existing) process.Dispose(); }
            DirectoryPath = Path.Combine(root, "dev", "history", "v0.1.9", "logs",
                "operation-recorder-" + purpose + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Manager = new ToolManager(root, (tool, start) =>
            {
                RequireTarget(tool);
                launchRequestedUtc = DateTime.UtcNow;
                Configure(start);
            });
            Tool = Manager.Load().Single(tool => tool.Id == ToolId);
            executable = Path.GetFullPath(Manager.Executable(Tool));
            var expected = Path.GetFullPath(Path.Combine(Manager.DevelopmentRoot(Tool), Tool.Executable));
            if (!string.Equals(executable, expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("正式導入先が選ばれています。今回の隔離試験では開発配置だけを使用します。");
            if (!File.Exists(executable)) throw new IOException("検証対象の開発EXEがありません: " + executable);
        }

        void Configure(ProcessStartInfo start)
        {
            start.ArgumentList.Add("--settings-dir");
            start.ArgumentList.Add(SettingsPath);
            start.ArgumentList.Add("--passive");
        }

        public static void RequireTarget(ToolDefinition tool)
        {
            if (tool.Id != ToolId) throw new IOException("この隔離試験ではOperationRecorderだけ操作できます。");
        }

        public async Task<ToolState> Start(bool show, string origin = "host_manual")
        {
            var before = await Manager.Status(Tool);
            if (before.Pid is int existingPid && !Owns(existingPid))
                throw new IOException("試験外のPIDを検出したため起動・表示を行いません。");
            launchRequestedUtc = null;
            var state = await Manager.Start(Tool, show, origin);
            if (state.Pid is int pid && launchRequestedUtc is DateTime requested)
            {
                using var process = Process.GetProcessById(pid);
                // 起動フックを通った新規プロセスだけ所有する。PID再利用時は開始時刻でも区別する。
                if (process.StartTime.ToUniversalTime() < requested.AddSeconds(-1) ||
                    !string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("起動したEXEと管理口のPIDが一致しません。所有プロセスとして扱いません。");
                Own(process);
            }
            if (state.Pid is int reported && !Owns(reported))
                throw new IOException("管理口が試験外のPIDを返しました。操作を続行しません。");
            return state;
        }

        void Own(Process process) => owned.Add(new(process.Id, process.StartTime.ToUniversalTime(), executable));

        public bool Owns(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return owned.Any(item => item.Pid == pid && item.StartedUtc == process.StartTime.ToUniversalTime() &&
                    string.Equals(item.Executable, process.MainModule?.FileName, StringComparison.OrdinalIgnoreCase));
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        public async Task<ToolState> Stop()
        {
            var state = await Manager.Status(Tool);
            if (state.Pid is not int pid || !Owns(pid))
                throw new IOException("試験で所有したPIDではないため停止しません。");
            return await Manager.Stop(Tool);
        }

        public Process StartDirect()
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) };
            Configure(start);
            var process = Process.Start(start) ?? throw new IOException("開発EXEを直接起動できませんでした。");
            Own(process);
            return process;
        }

        public async Task Cleanup()
        {
            foreach (var item in owned.DistinctBy(item => item.Pid))
            {
                if (!Owns(item.Pid)) continue;
                try
                {
                    // 再照合と停止の間に他インスタンスへ入れ替わっても、expectedPidで終了を拒否する。
                    var reply = await Manager.Send(Tool, "stop", item.Pid);
                    Messages.Add($"後始末 PID {item.Pid}: {reply.Result} {reply.Detail}");
                    if (reply.Result != "accepted") continue;
                    using var process = Process.GetProcessById(item.Pid);
                    if (process.StartTime.ToUniversalTime() != item.StartedUtc) continue;
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                }
                catch (ArgumentException) { }
                catch (Exception error) { Messages.Add($"後始末未確認 PID {item.Pid}: {error.Message}"); }
            }
            File.WriteAllLines(Path.Combine(DirectoryPath, "result.txt"), Messages);
            Console.WriteLine("検証ログ: " + DirectoryPath);
        }
    }

    static WindowState[] Windows(int pid)
    {
        var result = new List<WindowState>();
        EnumWindowsCallback callback = (window, parameter) =>
        {
            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId != pid) return true;
            var title = new StringBuilder(GetWindowTextLength(window) + 1);
            _ = GetWindowText(window, title, title.Capacity);
            result.Add(new(window, title.ToString(), IsWindowVisible(window)));
            return true;
        };
        _ = EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return result.ToArray();
    }

    static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        do
        {
            if (condition()) return true;
            await Task.Delay(100);
        } while (deadline.Elapsed < TimeSpan.FromSeconds(10));
        return condition();
    }

    static async Task<ToolState> WaitRunning(Session session, int expectedPid)
    {
        var deadline = Stopwatch.StartNew();
        ToolState state;
        do
        {
            state = await session.Manager.Status(session.Tool);
            if (state is { State: "実行中" } && state.Pid == expectedPid) return state;
            await Task.Delay(100);
        } while (deadline.Elapsed < TimeSpan.FromSeconds(20));
        return state;
    }

    public static async Task<int> Run()
    {
        var session = new Session(KotorinToys.Program.FindRoot(), "ipc");
        var manager = session.Manager; var tool = session.Tool;
        int count = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new IOException(message);
            count++; session.Messages.Add("PASS: " + message); Console.WriteLine(session.Messages[^1]);
        }
        try
        {
            Check(tool.Version == "1.4.1", "開発中の製品版1.4.1の登録を使用");
            Check((await manager.Status(tool)).State == "停止中", "開始前は停止中");
            var state = await session.Start(false);
            Check(state is { State: "実行中", Pid: not null, ProtocolVersion: 2, LaunchOrigin: "host_manual" } && state.Version == tool.Version,
                "実EXEの準備完了・管理プロトコル2・製品版・手動起動経路: " + state.Detail);
            var pid = state.Pid!.Value;
            using var running = Process.GetProcessById(pid);
            var title = tool.Name + " - ver " + tool.Version;
            var status = await manager.Send(tool, "status", pid);
            Check(status.Ready && status.Pid == pid && status.Capabilities?.SequenceEqual(["status", "show", "stop"]) == true,
                "statusで同じPIDのUI準備完了と管理能力を応答");
            Check(await WaitUntil(() => Windows(pid).Any(window => window.Title == title)), "非表示のメインウィンドウ生成を確認");
            Check(!Windows(pid).Any(window => window.Visible && window.Title.Length != 0), "startupは設定画面・初回質問を表示しない");
            var wrongPid = pid == int.MaxValue ? pid - 1 : int.MaxValue;
            Check((await manager.Send(tool, "show", wrongPid)).Result == "refused", "誤PIDへの画面表示を拒否");
            Check((await manager.Send(tool, "stop", wrongPid)).Result == "refused", "誤PIDへの停止を拒否");
            Check((await manager.Send(tool, "shortcuts", pid)).Result == "unsupported", "未対応操作を成功としない");
            Check((await session.Start(false)).Pid == pid, "既存PIDを維持して二重起動しない");
            Check((await session.Start(true)).Pid == pid, "既存PIDへ画面表示を要求");
            Check(await WaitUntil(() => Windows(pid).Any(window => window.Title == title && window.Visible)), "実画面の表示と共通タイトルを確認");
            using (var duplicate = session.StartDirect())
            {
                await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(duplicate.ExitCode == 0 && (await manager.Status(tool)).Pid == pid,
                    "直接二重起動を正常終了し既存PIDを維持");
            }
            Check((await session.Stop()).State == "停止中" && running.HasExited, "親の正常停止が実プロセス終了まで確認");
            var settingsFile = Path.Combine(session.SettingsPath, "settings.json");
            Check(File.Exists(settingsFile), "専用settings-dirへ設定を保存");
            using (var settings = JsonDocument.Parse(File.ReadAllText(settingsFile)))
            {
                var outDir = settings.RootElement.GetProperty("OutDir").GetString();
                Check(!string.IsNullOrWhiteSpace(outDir) && Path.GetFullPath(outDir).StartsWith(
                    Path.GetFullPath(session.SettingsPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                    "記録先も専用settings-dir内に隔離");
            }
            var restarted = await session.Start(false, "via_kotorintoys");
            Check(restarted is { State: "実行中", LaunchOrigin: "via_kotorintoys" } && restarted.Pid != pid,
                "新しいPIDへ再起動し経路を更新");
            Check((await session.Stop()).State == "停止中", "再起動した所有プロセスも正常停止");
            // 起動モードの引数を付けない通常起動。隔離とホットキー抑止の引数だけ渡す。
            using (var standalone = session.StartDirect())
            {
                var manual = await WaitRunning(session, standalone.Id);
                Check(manual.State == "実行中" && manual.Pid == standalone.Id && manual.Version == tool.Version && manual.ProtocolVersion == 2,
                    "通常起動も同じ管理プロトコルで準備完了");
                Check(await WaitUntil(() => Windows(standalone.Id).Any(window => window.Title == title && window.Visible)),
                    "起動モードの引数なしでは利用者向け画面を表示");
                Check((await session.Stop()).State == "停止中" && standalone.HasExited, "通常起動した所有プロセスを正常停止");
            }
            session.Messages.Add($"PASS: {count} 件");
            return 0;
        }
        catch (Exception error) { session.Messages.Add("FAIL: " + error); Console.Error.WriteLine(error); return 1; }
        finally { await session.Cleanup(); }
    }

    // 通常本体のMutex・設定・OS予約を使わず、実Dashboardと対象ツールだけを確認する。
    public static int Panel()
    {
        var root = KotorinToys.Program.FindRoot();
        var session = new Session(root, "panel");
        try
        {
            if (session.Manager.Status(session.Tool).GetAwaiter().GetResult().State != "停止中")
                throw new IOException("OperationRecorderの管理口が既に応答しています。隔離画面は開始しません。");
            var settings = new HostSettingsStore(Path.Combine(session.DirectoryPath, "host"));
            var startup = new StartupCoordinator(settings, new ReadOnlyGateway(), session.Manager, Environment.ProcessPath!);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            void Disabled() => MessageBox.Show("隔離確認画面ではこの操作は行いません。", WindowTitle.Create("操作確認"));
            var area = Screen.PrimaryScreen!.WorkingArea;
            using var form = new Dashboard(root, settings, startup,
                toolManager: session.Manager,
                releaseCheck: (_, _, _) => Task.FromResult(new PublicRelease(null, false, false, "隔離試験では公開先へ接続しません。")),
                showInstaller: (_, _) => Disabled(), showToolInstaller: (_, _, _) => Disabled(),
                showSelfUpdater: Disabled, openRepository: _ => Disabled(),
                controlExecution: async (tool, starting) =>
                {
                    Session.RequireTarget(tool);
                    return starting ? await session.Start(true) : await session.Stop();
                })
            {
                StartPosition = FormStartPosition.Manual,
                Bounds = new(area.Left + area.Width / 2, area.Top, area.Width - area.Width / 2, area.Height)
            };
            Console.WriteLine("隔離確認画面。記録保存先: " + session.DirectoryPath);
            Application.Run(form);
            return 0;
        }
        finally { Task.Run(session.Cleanup).GetAwaiter().GetResult(); }
    }

    sealed class ReadOnlyGateway : IStartupGateway
    {
        public ScheduledTaskState? Read(string name) => null;
        public ScheduledTaskState Create(TaskPlan plan) => throw new IOException("隔離確認ではOS予約を変更しません。");
        public ScheduledTaskState Restore(string name, string xml, string securityDescriptor = "") => throw new IOException("隔離確認ではOS予約を変更しません。");
        public void Delete(string name, string expectedFingerprint) => throw new IOException("隔離確認ではOS予約を変更しません。");
    }
}
