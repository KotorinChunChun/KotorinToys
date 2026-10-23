using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using KotorinToys;

// 通常のテストでは実行しない。指定された実配布物だけを、版別ログ内の専用配置で確認する。
internal static class OperationRecorderReleaseAcceptance
{
    const string ToolId = "operation-recorder";
    const string ProductVersion = "1.3.2";
    internal static ToolDefinition Definition => new(ToolId, "OperationRecorder",
        "artifacts/management/bin/OperationRecorderApp/debug/OperationRecorder.exe", "docs/kotorintoys-integration.md",
        Summary: "PC操作と前後の画面を記録し、画像付きの操作手順書を作成する", Category: "操作記録",
        Version: ProductVersion, ToolRoot: "tools/OperationRecorder", AppId: "KotorinToys.OperationRecorder",
        InstalledExecutable: "OperationRecorder.exe", Runtimes: [], DistributionStatus: "published",
        SupportedEnvironment: "windows11-x64", Capabilities: ["status", "show", "stop", "silent-start"],
        InvestigationStatus: "confirmed", RepositoryUrl: "https://github.com/KotorinChunChun/OperationRecorder");

    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static Dictionary<string, string> Hashes(string path) => Directory.Exists(path)
        ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToDictionary(file => file, Hash)
        : new();
    static bool SameHashes(Dictionary<string, string> before, string path)
    {
        var after = Hashes(path);
        return before.Count == after.Count && before.All(pair => after.GetValueOrDefault(pair.Key) == pair.Value);
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new IOException(message);
        Console.WriteLine("PASS: " + message);
    }
    static void NoProcesses()
    {
        var processes = Process.GetProcessesByName("OperationRecorder");
        try { Require(processes.Length == 0, "試験外のOperationRecorderプロセスなし"); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    static string OutputDirectory(string root, string output)
    {
        var directory = Path.GetFullPath(output);
        var logs = Path.GetFullPath(Path.Combine(root, "dev", "history", "v0.1.9", "logs"));
        Require(directory.StartsWith(logs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "実行結果と試験配置はv0.1.9のlogs内に限定");
        return directory;
    }

    sealed record OwnedProcess(int Pid, DateTime StartedUtc, string Executable);
    sealed class Session(ToolManager manager, string settings)
    {
        readonly List<OwnedProcess> owned = [];
        public DateTime? LaunchRequestedUtc { get; set; }
        bool Owns(int pid)
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
        public void Configure(ToolDefinition tool, ProcessStartInfo start)
        {
            if (tool.Id != ToolId) throw new IOException("対象外のツールは起動しません。");
            NoProcesses();
            LaunchRequestedUtc = DateTime.UtcNow;
            start.ArgumentList.Add("--settings-dir"); start.ArgumentList.Add(settings);
            start.ArgumentList.Add("--passive");
        }
        public async Task<ToolState> Start(ToolDefinition tool, string origin = "host_manual")
        {
            var before = await manager.Status(tool);
            if (before.Pid is int existing && !Owns(existing)) throw new IOException("試験外のPIDは操作しません。");
            LaunchRequestedUtc = null;
            var state = await manager.Start(tool, false, origin);
            if (state.Pid is int pid && LaunchRequestedUtc is DateTime requested)
            {
                using var process = Process.GetProcessById(pid);
                var executable = manager.Installation(tool).Executable;
                Require(process.StartTime.ToUniversalTime() >= requested.AddSeconds(-1) &&
                    string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase),
                    "正式導入先の新規プロセスと管理PIDが一致");
                owned.Add(new(pid, process.StartTime.ToUniversalTime(), executable!));
            }
            Require(state.Pid is int reported && Owns(reported), "起動応答は試験が所有するPID");
            return state;
        }
        public async Task<ToolState> Stop(ToolDefinition tool)
        {
            var state = await manager.Status(tool);
            if (state.State == "停止中") return state;
            if (state.Pid is not int pid || !Owns(pid)) throw new IOException("試験外のPIDは停止しません。");
            return await manager.Stop(tool);
        }
        public async Task Cleanup(ToolDefinition tool)
        {
            foreach (var item in owned.DistinctBy(item => item.Pid))
            {
                if (!Owns(item.Pid)) continue;
                var reply = await manager.Send(tool, "stop", item.Pid);
                Require(reply.Result == "accepted", "所有PIDだけへ正常終了を要求");
                try
                {
                    using var process = Process.GetProcessById(item.Pid);
                    if (process.StartTime.ToUniversalTime() == item.StartedUtc)
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                }
                catch (ArgumentException) { }
            }
        }
    }

    public static async Task<int> Lifecycle(string root, string catalog, string output)
    {
        output = OutputDirectory(root, output);
        var tool = Definition;
        Require(new InstallationDetector().Detect(tool).State == "未導入", "既存の正式OperationRecorder導入なし");
        NoProcesses();
        var store = new HostSettingsStore(HostSettingsStore.DefaultDirectory);
        var before = store.Load();
        Require(store.Journal == null && !store.RemovalPending, "本体の未完了操作なし");
        Require(before.StartupModes.GetValueOrDefault(ToolId, "none") == "none" && !before.OwnedTasks.ContainsKey(ToolId),
            "対象の既存自動起動設定・所有予約なし");
        Require(before.HostStartup || before.StartupModes.Values.Contains("via_kotorintoys") || !before.OwnedTasks.ContainsKey("host"),
            "削除試験で他製品の共有予約を解除しない条件");
        var gateway = new TaskSchedulerGateway();
        var taskName = "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + ToolId;
        Require(gateway.Read(taskName) == null, "対象のOS予約なし");
        var ownedTasks = before.OwnedTasks.Values.ToDictionary(item => item.Name, item => gateway.Read(item.Name)?.Fingerprint);
        // 本体設定や既存ショートカットは秘密を含み得るため、バックアップをログに書かない。
        var paths = new[] { "settings.json", "settings.json.bak", "last-removal-operation.json" }
            .Select(file => Path.Combine(store.DirectoryPath, file))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "OperationRecorder.lnk"));
        var originals = paths.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        var productShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "OperationRecorder.lnk");
        Require(originals[productShortcut] == null, "既存ショートカットがない専用導入試験");
        var hadSettings = originals[Path.Combine(store.DirectoryPath, "settings.json")] != null;
        bool HostUnchanged()
        {
            var current = store.Load();
            return JsonSerializer.Serialize(hadSettings ? before : before with { OwnerToken = current.OwnerToken }, ToolCatalog.Json) ==
                JsonSerializer.Serialize(current, ToolCatalog.Json);
        }
        var userSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OperationRecorder");
        var userHashes = Hashes(userSettings);
        var target = Path.Combine(output, "installed");
        Require(!Directory.Exists(target), "新規の専用導入先");
        Directory.CreateDirectory(output);
        var source = new ToolCatalogSource(root, Path.Combine(output, "catalog-cache"),
            _ => Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(new Catalog(1, [tool]), ToolCatalog.Json)));
        Require((await source.Refresh()).Accepted, "明示した製品定義を検証し隔離カタログへ採用");
        Session? session = null;
        var manager = new ToolManager(root, (definition, start) => session!.Configure(definition, start), source);
        var isolatedSettings = Path.Combine(output, "settings");
        session = new(manager, isolatedSettings);
        var adapter = new InstallerAdapter(manager);
        var receipts = new List<object>();
        var outcome = 1;
        try
        {
            var packages = InstallerAdapter.Load(catalog, [tool]);
            Require(packages.Packages.Length == 1 && packages.Packages[0].Id == ToolId && packages.Packages[0].Version == ProductVersion,
                "対象製品だけの1.3.2実配布台帳");
            var installed = await adapter.Install(tool, catalog, target);
            receipts.Add(new { Step = "新規導入", Result = installed });
            Require(installed.State == "成功" && manager.Installation(tool) is { State: "導入済み", Version: ProductVersion } &&
                string.Equals(manager.Installation(tool).Directory, target, StringComparison.OrdinalIgnoreCase),
                "本体InstallerAdapterで正式導入・製品識別・実EXE版・指定先を検出");
            var executableHash = Hash(manager.Installation(tool).Executable!);
            var state = await session.Start(tool);
            Require(state is { State: "実行中", Version: ProductVersion, ProtocolVersion: 2, LaunchOrigin: "host_manual" },
                "導入版の管理プロトコル2と手動起動経路で準備完了");
            var status = await manager.Send(tool, "status", state.Pid);
            Require(status.Ready && status.Pid == state.Pid && status.Capabilities?.SequenceEqual(["status", "show", "stop"]) == true,
                "導入版status応答のPID・準備完了・管理能力が一致");
            Require((await manager.Send(tool, "show", state.Pid)).Result == "completed", "導入版show要求が完了");
            await Task.Delay(2000);
            using (var visible = Process.GetProcessById(state.Pid!.Value))
                Require(visible.MainWindowHandle != IntPtr.Zero && visible.MainWindowTitle == "OperationRecorder - ver " + ProductVersion,
                    "導入版の実ウィンドウと共通タイトルを確認");
            Require((await session.Stop(tool)).State == "停止中", "導入版を正常停止");
            var settingsFile = Path.Combine(isolatedSettings, "settings.json");
            Require(File.Exists(settingsFile), "試験設定を専用settings-dirへ保存");
            using (var json = JsonDocument.Parse(File.ReadAllText(settingsFile)))
                Require(Path.GetFullPath(json.RootElement.GetProperty("OutDir").GetString()!).StartsWith(
                    isolatedSettings + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "記録出力先も試験配置内に隔離");
            var settingsHash = Hash(settingsFile);
            var history = Path.Combine(isolatedSettings, "records", "preserved-sample.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(history)!);
            File.WriteAllText(history, "再導入・更新・削除による利用者記録の保持確認");
            var historyHash = Hash(history);
            void Preserved() => Require(Hash(settingsFile) == settingsHash && Hash(history) == historyHash && SameHashes(userHashes, userSettings),
                "専用設定・記録と通常利用者設定をハッシュ一致で保持");
            var repaired = await adapter.Install(tool, catalog, target);
            receipts.Add(new { Step = "同版再導入", Result = repaired });
            Require(repaired.State == "成功" && Hash(manager.Installation(tool).Executable!) == executableHash, "同版再導入後の実EXE一致");
            Preserved();
            var running = await session.Start(tool, "via_kotorintoys");
            var updater = new ToolUpdateCoordinator(manager.Installation, definition => manager.Status(definition), session.Stop,
                (definition, origin) => session.Start(definition, origin),
                (definition, path, directory) => adapter.Install(definition, path, directory), definitions: manager.KnownTools);
            var updated = await updater.Run(tool, catalog);
            receipts.Add(new { Step = "同版更新経路", Result = updated });
            var restored = await manager.Status(tool);
            Require(updated.State == "成功" && restored is { State: "実行中", LaunchOrigin: "via_kotorintoys" } && restored.Pid != running.Pid,
                "本体更新経路で正常停止・同版導入・起動状態と起動経路を復帰");
            Require((await session.Stop(tool)).State == "停止中", "更新後の所有PIDを正常停止");
            Preserved();
            var removed = await adapter.Uninstall(tool);
            receipts.Add(new { Step = "削除", Result = removed });
            Require(removed.State == "成功" && manager.Installation(tool).State == "未導入", "本体経由の削除後に正式登録なし");
            Preserved();
            var reinstalled = await adapter.Install(tool, catalog, target);
            receipts.Add(new { Step = "削除後の再導入", Result = reinstalled });
            Require(reinstalled.State == "成功" && manager.Installation(tool).Version == ProductVersion, "保持データを残した再導入と正式検出");
            Preserved();
            Require((await session.Start(tool)).State == "実行中" && (await session.Stop(tool)).State == "停止中",
                "再導入後も本体から準備完了・正常停止");
            Preserved();
            outcome = 0;
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), error.ToString()); Console.Error.WriteLine(error); }
        finally
        {
            try
            {
                await session.Cleanup(tool);
                NoProcesses();
                var actual = manager.Installation(tool);
                if (actual.State != "未導入")
                {
                    Require(string.Equals(actual.Directory, target, StringComparison.OrdinalIgnoreCase), "試験導入先だけを後始末");
                    Require((await adapter.Uninstall(tool)).State == "成功", "試験導入を正常削除");
                }
                Require(manager.Installation(tool).State == "未導入", "試験前の未導入状態へ復帰");
                Require(SameHashes(userHashes, userSettings), "通常利用者設定の全ファイルを保持");
                Require(gateway.Read(taskName) == null && ownedTasks.All(pair => gateway.Read(pair.Key)?.Fingerprint == pair.Value),
                    "対象のOS予約を新設せず既存の所有予約を保持");
                using var lease = store.AcquireLease();
                Require(HostUnchanged(), "外部変更がないことと本体設定の意味的保持を確認");
                Require(!File.Exists(productShortcut), "試験の削除後に外部ショートカットが追加されていない");
                var backupPath = Path.Combine(store.DirectoryPath, "settings.json.bak");
                if (File.Exists(backupPath))
                {
                    var currentBackup = File.ReadAllBytes(backupPath);
                    var settingsPath = Path.Combine(store.DirectoryPath, "settings.json");
                    var currentSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
                    Require(new[] { originals[backupPath], originals[settingsPath], currentSettings }
                        .Any(bytes => bytes != null && currentBackup.SequenceEqual(bytes)),
                        "本体バックアップの外部変更を検出した場合は復元を見送る");
                }
                var completed = Path.Combine(store.DirectoryPath, "last-removal-operation.json");
                if (File.Exists(completed) && (originals[completed] == null || !File.ReadAllBytes(completed).SequenceEqual(originals[completed]!)))
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(completed));
                    Require(json.RootElement.GetProperty("targetId").GetString() == ToolId, "対象製品の削除記録だけを復元");
                }
                foreach (var pair in originals)
                {
                    if (pair.Value == null) { if (File.Exists(pair.Key)) File.Delete(pair.Key); }
                    else { Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!); File.WriteAllBytes(pair.Key, pair.Value); }
                }
                Require(originals.All(pair => pair.Value == null ? !File.Exists(pair.Key) : File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)),
                    "本体設定・削除記録・既存ショートカットを元のバイトへ復元");
            }
            catch (Exception error)
            {
                outcome = 1;
                await File.WriteAllTextAsync(Path.Combine(output, "cleanup-failure.txt"), error.ToString());
                Console.Error.WriteLine(error);
            }
            await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                Outcome = outcome == 0 ? "pass" : "fail", Tool = ToolId, Version = ProductVersion,
                Results = receipts, PreservedUserFiles = userHashes.Count, IsolatedSettings = true,
                StartupRegistration = false, UpdateEvidence = "同じ公開版による更新経路。異なる版からの更新は未実施。"
            }, ToolCatalog.Json));
        }
        return outcome;
    }

    // 公開完了後、本体と同じ匿名取得・サイズ・SHA-256照合を実行する。製品は起動しない。
    public static async Task<int> Online(string root, string output)
    {
        output = OutputDirectory(root, output);
        Directory.CreateDirectory(output);
        using var catalogClient = new ToolCatalogClient();
        var catalogBytes = await catalogClient.Fetch();
        var publicTools = OnlineToolCatalog.Parse(catalogBytes, root);
        var matches = publicTools.Where(tool => tool.Id == ToolId).ToArray();
        Require(matches.Length == 1, "公開mainの一覧configにOperationRecorderが一意に登録済み");
        var tool = matches.Single();
        Require(tool.DistributionStatus == "published" && tool.Version == ProductVersion &&
            tool.Name == Definition.Name && tool.AppId == Definition.AppId &&
            tool.RepositoryUrl == Definition.RepositoryUrl,
            "公開mainの登録が公開状態・製品名・AppId・配布先・1.3.2に一致");
        await File.WriteAllBytesAsync(Path.Combine(output, "published-tools.json"), catalogBytes);
        using var client = new PublicReleaseClient();
        var release = await client.Check(tool, null);
        Require(release.Confirmed && release.Package?.Package.Version == ProductVersion && release.Package.PublishedAt != null,
            "本体PublicReleaseClientでOperationRecorder公開版・公開日・製品別台帳を確認");
        using var downloader = new ReleaseDownloader();
        var catalog = await downloader.Prepare(new(1, [release.Package!]), [tool], output, new Progress<string>(Console.WriteLine));
        Require(File.Exists(catalog), "公開setupを匿名取得してサイズ・SHA-256を照合");
        await File.WriteAllTextAsync(Path.Combine(output, "receipt.json"), JsonSerializer.Serialize(new { Release = release, Catalog = catalog }, ToolCatalog.Json));
        Console.WriteLine("実配布台帳: " + catalog);
        return 0;
    }
}
