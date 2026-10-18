using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using KotorinToys;

// 実OS保守は明示オプション専用。通常のテスト実行には含めない。
internal static class StartupRootMaintenance
{
    static readonly string[] Targets = ["live-text-marker", "smart-paste", "ss-capture"];
    sealed record SavedFile(string Path, string Hash, string Copy);
    sealed record Backup(string OldRoot, string InstalledRoot, string HostExe, HostSettings Settings,
        TaskOwnership Task, SavedFile[] Files, string[] AbsentDirectories, Dictionary<string, ToolState> Before);
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
    static string LogicalSettings(HostSettings settings) => JsonSerializer.Serialize(settings with { OwnedTasks = new() }, ToolCatalog.Json);
    static StartupCoordinator Coordinator(HostSettingsStore store, IStartupGateway gateway, string root, string exe) =>
        new(store, gateway, new ToolManager(root, catalogSource: new ToolCatalogSource(root, store.DirectoryPath)), exe);

    // 新ルートでは旧定義を復元できない場合も、検証済みの旧コンテキストで復旧する。
    internal static async Task<StartupResult> ChangeWithRecovery(StartupCoordinator current, StartupCoordinator previous, ToolDefinition tool)
    {
        var result = await current.SetMode(tool, "via_kotorintoys");
        if (result.Success) return result;
        var recovery = await previous.Recover();
        return new(false, result.Detail + " / 旧ルートでの復旧：" + recovery.Detail);
    }

    public static async Task<int> Run(string operation, string backupDirectory, string fixtures)
    {
        backupDirectory = Path.GetFullPath(backupDirectory);
        var allowed = @"C:\AI\env\KotorinToys\startup-backups\";
        Require(backupDirectory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) &&
            Path.GetDirectoryName(backupDirectory)!.Equals(allowed.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase), "専用の退避子フォルダーを指定してください。");
        Require(operation is "prepare" or "repair" or "verify", "未対応の保守操作です。");
        var store = new HostSettingsStore(HostSettingsStore.DefaultDirectory);
        var gateway = new TaskSchedulerGateway();
        var installed = new InstallationDetector().Detect(HostProduct.Definition);
        Require(installed.State == "導入済み" && installed.Directory != null && installed.Executable != null, "本体の正式導入を確認できません。");
        var root = installed.Directory!; var exe = installed.Executable!;
        var manager = new ToolManager(root, catalogSource: new ToolCatalogSource(root, store.DirectoryPath));
        var tools = Targets.Select(id => manager.Load().Single(t => t.Id == id)).ToArray();
        Require(tools.All(t => manager.Installation(t).State == "導入済み"), "3ツールの正式導入を確認できません。");
        var coordinator = Coordinator(store, gateway, root, exe);
        var settings = store.Load();
        Require(store.Journal == null && !store.RemovalPending, "未完了の予約・削除操作があります。");
        Require(settings.OwnedTasks.Count == 1 && settings.OwnedTasks.ContainsKey("host") &&
            settings.StartupModes.Count == 3 && Targets.All(id => settings.StartupModes.GetValueOrDefault(id) == "via_kotorintoys"), "想定した3対象・共有予約ではありません。変更しません。");
        var owned = settings.OwnedTasks["host"];
        var actual = gateway.Read(owned.Name);
        Require(actual?.Fingerprint == owned.Fingerprint && actual.SecurityDescriptor == owned.SecurityDescriptor, "所有予約が外部変更されています。");
        var backupFile = Path.Combine(backupDirectory, "before.json");
        Backup saved;
        if (operation == "prepare")
        {
            Require(!Directory.Exists(backupDirectory), "既存の退避先を上書きしません。");
            var directory = Directory.CreateDirectory(backupDirectory);
            var security = directory.GetAccessControl(AccessControlSections.Access);
            var user = WindowsIdentity.GetCurrent().User!;
            security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                security.AddAccessRule(new(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(security);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var action = XDocument.Parse(actual!.Xml).Descendants(ns + "Exec").Single();
            Require(action.Element(ns + "Command")?.Value == exe, "共有予約のEXEが正式導入先ではありません。");
            var match = Regex.Match(action.Element(ns + "Arguments")?.Value ?? "", "--root \\\"([^\\\"]+)\\\" --settings-dir ");
            Require(match.Success, "旧ルートを一意に読み取れません。");
            var oldRoot = match.Groups[1].Value;
            var previous = Coordinator(store, gateway, oldRoot, exe);
            foreach (var tool in tools) Require((await previous.State(tool)).ActualState == "登録済み", "旧ルートでも所有定義を検証できません。");
            var files = new List<SavedFile>(); var absent = new List<string>();
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directories = new[] { Path.Combine(local, "LiveTextMarker"), Path.Combine(local, "PointerUnderline"),
                Path.Combine(local, "SmartPaste"), Path.Combine(manager.Installation(tools[2]).Directory!, "data") };
            var paths = Directory.GetFiles(store.DirectoryPath, "*.json").ToList();
            paths.AddRange(Directory.GetFiles(store.DirectoryPath, "*.json.bak"));
            foreach (var path in directories)
                if (Directory.Exists(path)) paths.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories)); else absent.Add(path);
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var copy = Path.Combine(backupDirectory, files.Count + ".backup");
                File.Copy(path, copy); var hash = Hash(path);
                Require(hash == Hash(copy), "退避中に設定が変更されました。");
                files.Add(new(path, hash, copy));
            }
            var before = new Dictionary<string, ToolState>();
            foreach (var tool in tools) before[tool.Id] = await manager.Status(tool);
            saved = new(oldRoot, root, exe, settings, new(actual.Name, actual.Fingerprint, actual.Xml, actual.SecurityDescriptor), files.ToArray(), absent.ToArray(), before);
            File.WriteAllText(backupFile, JsonSerializer.Serialize(saved, ToolCatalog.Json));
            Console.WriteLine($"非公開退避：{files.Count}ファイル。旧予約の所有条件と3ツールの正式導入を確認しました。");
        }
        else
        {
            saved = JsonSerializer.Deserialize<Backup>(File.ReadAllText(backupFile), ToolCatalog.Json)!;
            Require(saved.InstalledRoot == root && saved.HostExe == exe && LogicalSettings(saved.Settings) == LogicalSettings(settings), "退避後に導入先または希望設定が変更されています。");
            foreach (var file in saved.Files) Require(Hash(file.Copy) == file.Hash, "退避ファイルが変更されています。");
            if (operation == "repair")
            {
                Require(actual!.Fingerprint == saved.Task.Fingerprint, "退避後に予約が変更されています。");
                foreach (var file in saved.Files) Require(File.Exists(file.Path) && Hash(file.Path) == file.Hash, "退避後に設定・履歴が変更されています。");
                var previous = Coordinator(store, gateway, saved.OldRoot, exe);
                foreach (var tool in tools) Require((await previous.State(tool)).ActualState == "登録済み", "変更前の所有条件が一致しません。");
                var result = await ChangeWithRecovery(coordinator, previous, tools[0]);
                Require(result.Success, result.Detail);
            }
            else
            {
                foreach (var tool in tools) Require((await coordinator.State(tool)).ActualState == "登録済み", "予約の切替が未完了です。");
                // 子ツールは停止しない。既存プロセスの履歴を保持したまま再利用を照合する。
                var host = HostProduct.Definition with { Id = "dashboard", AppId = "", ToolRoot = "" };
                var hostManager = new ToolManager(root);
                var priorHost = await hostManager.Status(host);
                Require(priorHost.State is "実行中" or "停止中", "本体の正常終了可否を確認できません。");
                if (priorHost.State == "実行中") Require((await hostManager.Stop(host)).State == "停止中", "本体が正常終了を拒否したか、終了を確認できません。");
                var start = DateTimeOffset.UtcNow;
                try
                {
                    var task = store.Load().OwnedTasks["host"];
                    gateway.RunOwned(task.Name, task.Fingerprint);
                    var deadline = DateTimeOffset.UtcNow.AddSeconds(50);
                    while (DateTimeOffset.UtcNow < deadline)
                    {
                        var attempt = store.LastAttempt;
                        if (attempt?.AttemptedAt >= start && attempt.Items.Length == 3) break;
                        await Task.Delay(500);
                    }
                    var final = store.LastAttempt;
                    Require(final?.AttemptedAt >= start && final.Items.Length == 3 && final.Items.All(i => i.Result == "準備完了" && i.Pid.HasValue), "実タスクからの最新起動記録が3件すべて準備完了になりません。");
                    var hostState = await hostManager.Status(host);
                    Require(hostState.State == "実行中" && hostState.Pid != priorHost.Pid && hostState.LaunchOrigin == "via_kotorintoys", "実タスクからの本体起動を確認できません。");
                    foreach (var tool in tools)
                    {
                        var state = await manager.Status(tool);
                        Require(state.State == "実行中" && final!.Items.Single(i => i.ToolId == tool.Id).Pid == state.Pid, "ツールの準備完了と記録PIDが一致しません。");
                        if (saved.Before[tool.Id].Pid is { } oldPid) Require(state.Pid == oldPid, "既存ツールのPIDが変わりました。");
                        else Require(state.LaunchOrigin == "via_kotorintoys", "ツールの起動元が本体経由ではありません。");
                        Console.WriteLine($"{tool.Name}：準備完了 / PID {state.Pid} / {state.LaunchOrigin} / {manager.Executable(tool)}");
                    }
                }
                catch
                {
                    if ((await hostManager.Status(host)).State == "停止中") await hostManager.Start(host, false);
                    throw;
                }
            }
        }
        Require(LogicalSettings(saved.Settings) == LogicalSettings(store.Load()), "利用者の自動起動設定が変化しました。");
        var userFiles = saved.Files.Where(f => !Path.GetDirectoryName(f.Path)!.Equals(store.DirectoryPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(userFiles.All(f => File.Exists(f.Path) && Hash(f.Path) == f.Hash), "ツール設定・履歴の内容が変化しました。");
        Require(saved.AbsentDirectories.All(d => !Directory.Exists(d) || !Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Any()), "未作成だった個別設定が追加されました。内容確認が必要です。");
        var states = new List<object>();
        foreach (var tool in tools) states.Add(new { tool.Id, Startup = await coordinator.State(tool), Execution = await manager.Status(tool) });
        var taskAfter = store.Load().OwnedTasks["host"];
        if (operation != "prepare") Require(states.Count == 3 && (await coordinator.State(tools[0])).ActualState == "登録済み" && gateway.Read(taskAfter.Name)?.Fingerprint == taskAfter.Fingerprint, "登録後の予約照合に失敗しました。");
        var outcome = gateway.Outcome(taskAfter.Name);
        var receipt = new { Operation = operation, BackupDirectory = backupDirectory, InstalledRoot = root, saved.OldRoot, SettingsPreserved = true,
            PreservedFiles = userFiles.Select(f => new { f.Path, f.Hash }), TaskState = outcome.State, TaskResult = outcome.Result, States = states, Attempt = operation == "verify" ? store.LastAttempt : null };
        File.WriteAllText(Path.Combine(fixtures, "startup-root-" + operation + ".json"), JsonSerializer.Serialize(receipt, ToolCatalog.Json));
        Console.WriteLine(operation + "：完了。利用者設定・既存の個別設定／履歴は保持されています。");
        return 0;
    }
}
