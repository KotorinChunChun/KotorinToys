using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using KotorinToys;

// 0.1.3更新後の実サインイン確認。通常回帰からOS操作を呼ばない。
internal static class UpgradeRebootValidation
{
    internal static readonly string[] TargetIds = ["live-text-marker", "smart-paste", "ss-capture"];
    sealed record Plan(string Sid, string ExpectedVersion, string Root, DateTimeOffset BeforeBootUtc,
        DateTimeOffset ArmedAt, string SettingsHash, Dictionary<string,string> DataHashes,
        string SharedName, string SharedFingerprint, TaskOwnership Probe);
    static string Sid => WindowsIdentity.GetCurrent().User!.Value;
    static DateTimeOffset BootUtc => DateTimeOffset.UtcNow.AddMilliseconds(-Environment.TickCount64);
    static string Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "absent";
    static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
    internal static bool IsNewBoot(DateTimeOffset before, DateTimeOffset current) => current > before.AddSeconds(30);
    internal static bool OwnProbe(string name, string sid) => Regex.IsMatch(name,
        "^KotorinToys\\.Test\\.[a-f0-9]{32}\\." + Regex.Escape(sid) + "\\.fixture$");
    static ToolDefinition Dashboard => HostProduct.Definition with { Id = "dashboard", AppId = "" };
    static (InstallationState Host, HostSettingsStore Store, ToolManager Manager, ToolDefinition[] Tools) Context()
    {
        var installed = new InstallationDetector().Detect(HostProduct.Definition);
        Require(installed.State == "導入済み" && installed.Directory != null, "正式導入本体を確認できません。");
        var store = new HostSettingsStore(HostSettingsStore.DefaultDirectory);
        var manager = new ToolManager(installed.Directory!, catalogSource: new ToolCatalogSource(installed.Directory!, store.DirectoryPath));
        var tools = TargetIds.Select(id => manager.Load().Single(t => t.Id == id)).ToArray();
        return (installed, store, manager, tools);
    }
    static Dictionary<string,string> DataHashes(ToolManager manager, ToolDefinition[] tools)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directories = new[] { Path.Combine(local,"LiveTextMarker"), Path.Combine(local,"PointerUnderline"),
            Path.Combine(local,"SmartPaste"), Path.Combine(manager.Installation(tools.Single(t => t.Id == "ss-capture")).Directory!,"data") };
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            files.Add(Path.Combine(directory,"settings.json"));
            if (Directory.Exists(directory)) foreach (var file in Directory.GetFiles(directory,"*",SearchOption.AllDirectories)) files.Add(file);
        }
        return files.Order(StringComparer.OrdinalIgnoreCase).ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
    }
    internal static bool SameData(Dictionary<string,string> before, Dictionary<string,string> after) =>
        before.Count == after.Count && before.All(p => after.TryGetValue(p.Key,out var hash) && hash == p.Value);
    public static async Task<int> StopHost()
    {
        var context = Context();
        var manager = new ToolManager(context.Host.Directory!);
        var state = await manager.Stop(Dashboard);
        Require(state.State == "停止中", "本体の正常終了を確認できません：" + state.Detail);
        Console.WriteLine("正式導入本体のみ正常終了しました。子ツールは停止していません。");
        return 0;
    }
    public static async Task<int> Arm(string directory)
    {
        directory = ValidateDirectory(directory);
        Require(!Directory.Exists(directory), "既存の再起動検証記録は上書きしません。");
        var (host,store,manager,tools) = Context();
        Require(host.Version == HostProduct.CurrentVersion && HostProduct.CurrentVersion == "0.1.3", "0.1.3の正式更新を先に完了してください。");
        var settings = store.Load(); var gateway = new TaskSchedulerGateway();
        Require(store.Journal == null && !store.RemovalPending && settings.OwnedTasks.Count == 1 &&
            settings.StartupModes.Count == 3 && TargetIds.All(id => settings.StartupModes.GetValueOrDefault(id) == "via_kotorintoys"), "共有予約3対象の設定ではありません。");
        var shared = settings.OwnedTasks["host"];
        Require(gateway.Read(shared.Name)?.Fingerprint == shared.Fingerprint, "共有予約の所有条件が変化しました。");
        var coordinator = new StartupCoordinator(store,gateway,manager,host.Executable!);
        foreach (var tool in tools) Require(manager.Installation(tool).State == "導入済み" && (await coordinator.State(tool)).ActualState == "登録済み", "正式導入・予約が一致しません：" + tool.Name);
        var stopped = new List<ToolDefinition>(); var hostManager = new ToolManager(host.Directory!);
        bool hostStopped = false;
        try
        {
            foreach (var tool in tools)
            {
                var before = await manager.Status(tool);
                Require(before.State is "実行中" or "停止中", "子ツールの状態を確認できません：" + tool.Name);
                if (before.State == "停止中") continue;
                Require((await manager.Stop(tool)).State == "停止中", "子ツールが正常終了を拒否しました。再起動しません：" + tool.Name);
                stopped.Add(tool);
            }
            Require((await hostManager.Stop(Dashboard)).State == "停止中", "本体が正常終了を拒否しました。再起動しません。");
            hostStopped = true;
            Directory.CreateDirectory(directory);
            var nonce = Guid.NewGuid().ToString("N");
            var taskPlan = new TaskPlan("KotorinToys.Test." + nonce + "." + Sid + ".fixture", Environment.ProcessPath!,
                "--post-upgrade-boot-check " + StartupCoordinator.Quote(Path.Combine(directory,"plan.json")), AppContext.BaseDirectory, Sid, "KotorinToys:Test:" + nonce);
            var task = gateway.Create(taskPlan);
            try
            {
                var plan = new Plan(Sid,HostProduct.CurrentVersion,host.Directory!,BootUtc,DateTimeOffset.UtcNow,
                    Hash(Path.Combine(store.DirectoryPath,"settings.json")),DataHashes(manager,tools), shared.Name,shared.Fingerprint,
                    new(task.Name,task.Fingerprint,task.Xml,task.SecurityDescriptor));
                File.WriteAllText(Path.Combine(directory,"plan.json"),JsonSerializer.Serialize(plan,ToolCatalog.Json));
                File.WriteAllText(Path.Combine(directory,"status.txt"),"準備完了。PC再起動と次回サインインを待っています。手動開始は再起動証跡にしません。");
            }
            catch { gateway.Delete(task.Name,task.Fingerprint); throw; }
        }
        catch
        {
            if (hostStopped) await hostManager.Start(Dashboard,false);
            foreach (var tool in stopped) await manager.Start(tool,false,"via_kotorintoys");
            throw;
        }
        Console.WriteLine("再起動前記録と次回サインイン検証予約を保存しました：" + directory);
        return 0;
    }
    static string ValidateDirectory(string directory)
    {
        var logs = Path.Combine(KotorinToys.Program.FindRoot(),"dev","history","v" + HostProduct.CurrentVersion,"logs");
        var resolved = PathSafety.Resolve(logs,Path.GetRelativePath(logs,Path.GetFullPath(directory)));
        Require(Path.GetDirectoryName(resolved)!.Equals(logs,StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(Path.GetFileName(resolved),"^reboot-upgrade-[a-f0-9]{32}$"), "現行版の専用再起動記録先ではありません。");
        return resolved;
    }
    public static async Task<int> ReadyToReboot(string path)
    {
        path = Path.Combine(ValidateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!),"plan.json");
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(path),ToolCatalog.Json)!;
        Require(plan.Sid == Sid && plan.ExpectedVersion == HostProduct.CurrentVersion && OwnProbe(plan.Probe.Name,Sid) &&
            !IsNewBoot(plan.BeforeBootUtc,BootUtc), "現在の再起動前記録ではありません。");
        var (host,store,manager,tools) = Context(); var gateway = new TaskSchedulerGateway();
        Require(host.Version == plan.ExpectedVersion && host.Directory == plan.Root &&
            gateway.Read(plan.SharedName)?.Fingerprint == plan.SharedFingerprint && gateway.Read(plan.Probe.Name)?.Fingerprint == plan.Probe.Fingerprint,
            "再起動直前の本体または所有予約の照合に失敗しました。");
        Require(Hash(Path.Combine(store.DirectoryPath,"settings.json")) == plan.SettingsHash && SameData(plan.DataHashes,DataHashes(manager,tools)), "準備後に設定・履歴が変化しました。");
        var states = await Task.WhenAll(new[] { new ToolManager(host.Directory!).Status(Dashboard) }.Concat(tools.Select(t => manager.Status(t))));
        Require(states.All(s => s.State == "停止中"), "対象アプリが再び動いています。自動再起動要求は中止します。");
        Console.WriteLine("再起動直前の本体0.1.3・設定保持・予約照合・正常停止を確認しました。");
        return 0;
    }
    public static async Task<int> Check(string path)
    {
        path = Path.Combine(ValidateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!),"plan.json");
        var directory = Path.GetDirectoryName(path)!;
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(path),ToolCatalog.Json)!;
        Require(plan.Sid == Sid && plan.ExpectedVersion == HostProduct.CurrentVersion && OwnProbe(plan.Probe.Name,Sid) &&
            new ScheduledTaskState(plan.Probe.Name,plan.Probe.Xml,plan.Probe.SecurityDescriptor).Fingerprint == plan.Probe.Fingerprint,
            "再起動検証記録の所有条件が不一致です。");
        if (!IsNewBoot(plan.BeforeBootUtc,BootUtc))
        {
            File.WriteAllText(Path.Combine(directory,"status.txt"),"未検証：新しいカーネル起動がありません。次回サインインを待ちます。");
            return 2; // 手動試験では検証予約を消費しない。
        }
        var gateway = new TaskSchedulerGateway();
        try
        {
            var (host,store,manager,tools) = Context();
            Require(host.Version == plan.ExpectedVersion && host.Directory == plan.Root, "再起動後の正式本体の版・導入先が一致しません。");
            Require(gateway.Read(plan.SharedName)?.Fingerprint == plan.SharedFingerprint, "本体共有予約が変更されています。");
            var hostManager = new ToolManager(host.Directory!);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(100); ToolState[] states = [];
            while (DateTimeOffset.UtcNow < deadline)
            {
                states = await Task.WhenAll(new[] { hostManager.Status(Dashboard) }.Concat(tools.Select(t => manager.Status(t))));
                if (states.Length == 4 && states.All(s => s.State == "実行中" && s.LaunchOrigin == "via_kotorintoys" && s.Pid.HasValue)) break;
                await Task.Delay(500);
            }
            Require(states.Length == 4 && states.All(s => s.State == "実行中" && s.LaunchOrigin == "via_kotorintoys" && s.Pid.HasValue) && states[0].Version == plan.ExpectedVersion,
                "再起動後の本体0.1.3・対象3ツールの本体経由準備完了を確認できません。");
            foreach (var state in states)
            {
                using var process = Process.GetProcessById(state.Pid!.Value);
                Require(process.StartTime.ToUniversalTime() >= BootUtc.UtcDateTime.AddSeconds(-5), "再起動後の新しい実プロセスではありません。");
            }
            var coordinator = new StartupCoordinator(store,gateway,manager,host.Executable!);
            foreach (var tool in tools) Require(manager.Installation(tool).State == "導入済み" && (await coordinator.State(tool)).ActualState == "登録済み", "再起動後の正式導入・予約に不一致があります。");
            var attempt = store.LastAttempt;
            Require(attempt != null && attempt.AttemptedAt >= BootUtc && attempt.Items.Length == 3 &&
                attempt.Items.All(i => i.Result == "準備完了" && TargetIds.Contains(i.ToolId) && states.Skip(1).Any(s => s.Pid == i.Pid)), "再起動後の起動記録とPIDが一致しません。");
            Require(Hash(Path.Combine(store.DirectoryPath,"settings.json")) == plan.SettingsHash && SameData(plan.DataHashes,DataHashes(manager,tools)), "再起動前後の設定・履歴ファイルが一致しません。");
            File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new { Outcome="pass",ObservedAt=DateTimeOffset.UtcNow,
                BeforeBootUtc=plan.BeforeBootUtc,AfterBootUtc=BootUtc,Host=host,States=states,Attempt=attempt,SettingsPreserved=true,DataHashes=plan.DataHashes },ToolCatalog.Json));
            File.WriteAllText(Path.Combine(directory,"status.txt"),"合格：PC再起動後の実サインインで正式本体0.1.3・3ツールの本体経由起動・準備完了・設定履歴保持を確認しました。");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory,"failure.txt"),ex.ToString());
            File.WriteAllText(Path.Combine(directory,"status.txt"),"失敗：再起動後の検証未完了。failure.txtを確認してください。");
            return 1;
        }
        finally
        {
            if (gateway.Read(plan.Probe.Name)?.Fingerprint == plan.Probe.Fingerprint) gateway.Delete(plan.Probe.Name,plan.Probe.Fingerprint);
            else File.WriteAllText(Path.Combine(directory,"cleanup.txt"),"検証予約が不在または外部変更されています。自動削除していません。");
        }
    }
    public static void Tests(Action<bool,string> check)
    {
        var before=DateTimeOffset.UtcNow.AddHours(-1);
        check(!IsNewBoot(before,before.AddSeconds(1)),"更新後再起動受入は同じカーネルの手動実行を拒否");
        check(IsNewBoot(before,before.AddMinutes(2)),"更新後再起動受入は新しいカーネル起動を要求");
        check(OwnProbe("KotorinToys.Test."+Guid.NewGuid().ToString("N")+"."+Sid+".fixture",Sid),"更新後検証の専用予約名だけ許可");
        check(!OwnProbe("KotorinToys."+Sid+".host",Sid),"更新後検証は本体共有予約を片付け対象にしない");
        check(!OwnProbe("KotorinToys.Test."+Guid.NewGuid().ToString("N")+".S-1-5-21-999.fixture",Sid),"更新後検証は別ユーザーの予約を許可しない");
        check(TargetIds.Length==3 && TargetIds.Distinct().Count()==3 && !TargetIds.Contains("http-selector"),"9件一覧のうち設定済み3ツールだけを再起動受入の対象にする");
        check(SameData(new(){{"a","hash"}},new(){{"a","hash"}}) && !SameData(new(){{"a","hash"}},new(){{"a","changed"}}),"再起動前後の履歴変更を検出");
        check(!SameData(new(){{"a","absent"}},new(){{"a","absent"},{"b","new"}}),"再起動前後に追加された個別設定も検出");
    }
}
