using KotorinToys;
using System.Security.Principal;

internal static class StandaloneStartupTests
{
    public static void Run(string root, string fixtures, Action<bool, string> check)
    {
        var store = new HostSettingsStore(Path.Combine(fixtures, "standalone-" + Guid.NewGuid().ToString("N")));
        var gateway = new Gateway();
        var tool = new ToolDefinition("ss-capture", "SSCapture", "SSCapture.exe", "README.md",
            AppId: "KotorinToys.SSCapture", InstalledExecutable: "SSCapture.exe", Capabilities: ["status", "show", "stop", "silent-start"]);
        string exe = Path.Combine(fixtures, "SSCapture.exe");
        bool installed = true;
        ExternalStartupState[] external = [];
        var service = new StandaloneStartup(store, gateway, tool, exe, () => installed, externalReservations: () => external);
        check(!service.State().Enabled && gateway.Tasks.Count == 0, "単体の初期状態はOFFで予約を作らない");
        var initial = store.Load();
        store.Save(initial with { StartupModes = new() { ["smart-paste"] = "via_kotorintoys" } });
        installed = false;
        check(!service.State().CanChange && !service.SetEnabled(true).Success, "開発・未導入先を予約しない");
        installed = true;
        external = [new("スタートアップ", "旧SSCapture.lnk", "旧登録")];
        check(!service.SetEnabled(true).Success && gateway.Tasks.Count == 0, "旧lnk等の外部予約に重ねて登録しない");
        external = [];
        check(service.SetEnabled(true).Success && service.State().Enabled, "正式導入の単体から直接予約");
        check(gateway.Tasks.Count == 1 && store.Load().StartupModes["smart-paste"] == "via_kotorintoys", "別製品の希望を再計画しない");
        var own = store.Load().OwnedTasks[tool.Id];
        check(own.Xml.Contains("--startup --launch-origin direct_task"), "自動起動は共通の無表示引数");
        check(service.SetEnabled(true).Success && gateway.Tasks.Count == 1, "ONの再適用で二重登録しない");
        gateway.Tasks[own.Name] = gateway.Tasks[own.Name] with { Xml = own.Xml.Replace("PT0S", "PT72H") };
        check(!service.State().CanChange && !service.SetEnabled(false).Success && gateway.Tasks.Count == 1, "外部変更予約は削除しない");
        gateway.Tasks[own.Name] = new(own.Name, own.Xml, own.SecurityDescriptor);
        check(service.SetEnabled(false).Success && !service.State().Enabled && gateway.Tasks.Count == 0, "OFFで当該所有予約だけ解除");
        check(store.Load().StartupModes["smart-paste"] == "via_kotorintoys", "解除でも別製品の希望を保持");
        gateway.FailAfterCreate = true;
        check(!service.SetEnabled(true).Success && gateway.Tasks.Count == 0 && store.Journal == null, "登録直後の失敗を操作記録から復元");
        check(service.SetEnabled(true).Success, "復元後に再登録できる");
        var before = store.Load();
        own = before.OwnedTasks[tool.Id];
        store.SaveJournal(new(Guid.NewGuid().ToString("N"), tool.Id, before, "中断", new(), new(), true));
        gateway.Tasks.Remove(own.Name);
        check(!service.State().CanChange, "中断した操作があれば新規変更を拒否");
        var coordinator = new StartupCoordinator(store, gateway, new ToolManager(fixtures), exe);
        check(coordinator.Recover().GetAwaiter().GetResult().Success && service.State().Enabled, "単体設定の中断を本体の復旧から戻せる");
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        var current = store.Load();
        var plan = new TaskPlan("KotorinToys." + sid + ".host", exe, "--startup", fixtures, sid, "KotorinToys:" + current.OwnerToken + ":host");
        var host = gateway.Create(plan);
        store.Save(current with { StartupModes = new() { [tool.Id] = "via_kotorintoys" }, OwnedTasks = new() { ["host"] = new(host.Name, host.Fingerprint, host.Xml) } });
        var via = service.State();
        check(via.Enabled && !via.CanChange && !service.SetEnabled(true).Success, "本体経由なら単体で直接予約を重ねない");
        gateway.Tasks.Remove(host.Name);
        check(!service.State().Enabled && !service.State().CanChange, "希望だけで有効と表示しない");
        DefinedProduct(root, fixtures, check);
    }

    static void DefinedProduct(string root, string fixtures, Action<bool, string> check)
    {
        var tool = ToolCatalog.Load(root)[0] with
        {
            Id = "standalone-defined", Name = "StandaloneDefined", AppId = "KotorinToys.StandaloneDefined",
            InstalledExecutable = "StandaloneDefined.exe", Executable = "StandaloneDefined.exe",
            ToolRoot = "tools/StandaloneDefined", Msix = null, Version = "1.0.0", DistributionStatus = "published",
            RepositoryUrl = "https://github.com/KotorinChunChun/StandaloneDefined"
        };
        var directory = Path.Combine(fixtures, "standalone-defined");
        var source = new ToolCatalogSource(root, Path.Combine(directory, "cache"), _ => Task.FromResult(OnlineCatalogTests.Encode(tool)));
        check(source.Refresh().GetAwaiter().GetResult().Accepted && ProductIdentity.FromId(tool.Id) == null,
            "定義追加製品を固定許可表に登録せず受け入れる");
        var store = new HostSettingsStore(Path.Combine(directory, "settings"));
        var gateway = new Gateway();
        var initial = store.Load();
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        var other = gateway.Create(new("unrelated-task", "other.exe", "", directory, sid, "external"));
        store.Save(initial with { StartupModes = new() { ["smart-paste"] = "via_kotorintoys" } });
        var service = new StandaloneStartup(store, gateway, tool, Path.Combine(directory, tool.InstalledExecutable),
            () => true, externalReservations: () => []);
        gateway.FailAfterCreate = true;
        check(!service.SetEnabled(true).Success && store.Journal == null && gateway.Tasks.Count == 1 &&
            store.Load().StartupModes["smart-paste"] == "via_kotorintoys", "新規定義でも登録直後の失敗を単体から復元し別製品を保持");
        check(service.SetEnabled(true).Success, "定義追加製品の正常な単体予約を作成できる");
        var before = store.Load();
        var own = before.OwnedTasks[tool.Id];
        var journal = new StartupJournal(Guid.NewGuid().ToString("N"), tool.Id, before, "中断", new(), new(), true);
        store.SaveJournal(journal);
        gateway.Tasks.Remove(own.Name);
        check(!StandaloneStartup.Restore(store, gateway, journal).Success && store.Journal != null,
            "未知製品の復元を定義なしでは許可しない");
        check(!StandaloneStartup.Restore(store, gateway, journal, definitions: [tool with { AppId = "KotorinToys.SmartPaste" }]).Success &&
            !StandaloneStartup.Restore(store, gateway, journal, definitions: [tool, tool]).Success && gateway.Tasks.Count == 1,
            "識別不一致と重複定義の復元を副作用なしで拒否");
        var coordinator = new StartupCoordinator(store, gateway, new ToolManager(root, catalogSource: source), "host.exe");
        check(coordinator.Recover().GetAwaiter().GetResult().Success && service.State().Enabled &&
            gateway.Tasks[other.Name].Fingerprint == other.Fingerprint && store.Load().StartupModes["smart-paste"] == "via_kotorintoys",
            "本体は検証済み既知定義から新規製品の予約だけ復元");
        var plan = new TaskPlan(own.Name, Path.Combine(directory, tool.InstalledExecutable), "--startup --launch-origin direct_task", directory,
            sid, "KotorinToys:" + before.OwnerToken + ":" + tool.Id);
        var planned = journal with { PlannedTasks = new() { [tool.Id] = plan }, CreatedTasks = new() { [tool.Id] = own } };
        var foreign = gateway.Tasks[own.Name] with { Xml = own.Xml.Replace("PT0S", "PT72H") };
        gateway.Tasks[own.Name] = foreign;
        store.SaveJournal(planned);
        check(!coordinator.Recover().GetAwaiter().GetResult().Success && gateway.Tasks[own.Name].Fingerprint == foreign.Fingerprint &&
            store.Journal != null, "定義追加でも外部変更された予約を削除しない");
        gateway.Tasks[own.Name] = new(own.Name, own.Xml, own.SecurityDescriptor);
        var invalid = planned with { PlannedTasks = new() { [tool.Id] = plan with { Sid = "S-1-0-0" } } };
        store.SaveJournal(invalid);
        check(!coordinator.Recover().GetAwaiter().GetResult().Success && gateway.Tasks.Count == 2,
            "SID不一致の新規製品復元は他の予約も変更しない");
    }
    sealed class Gateway : IStartupGateway
    {
        internal readonly Dictionary<string, ScheduledTaskState> Tasks = new();
        internal bool FailAfterCreate;
        public ScheduledTaskState? Read(string name) => Tasks.GetValueOrDefault(name);
        public ScheduledTaskState Create(TaskPlan plan)
        {
            if (Tasks.ContainsKey(plan.Name)) throw new IOException("同名予約");
            var state = Tasks[plan.Name] = new(plan.Name, StartupTests.Xml(plan));
            if (FailAfterCreate) { FailAfterCreate = false; throw new IOException("登録後に失敗"); }
            return state;
        }
        public ScheduledTaskState Restore(string name, string xml, string securityDescriptor = "") => Tasks[name] = new(name, xml, securityDescriptor);
        public void Delete(string name, string expectedFingerprint)
        {
            if (Read(name)?.Fingerprint != expectedFingerprint) throw new IOException("外部変更");
            Tasks.Remove(name);
        }
    }
}
