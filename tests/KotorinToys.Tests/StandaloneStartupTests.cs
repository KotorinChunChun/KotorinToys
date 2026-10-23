using KotorinToys;
using System.Security.Principal;

internal static class StandaloneStartupTests
{
    public static void Run(string fixtures, Action<bool, string> check)
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
