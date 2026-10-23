using System.Security.Principal;
using System.Xml.Linq;

namespace KotorinToys;

internal sealed record StandaloneStartupState(bool Enabled, bool CanChange, string Detail);

// 単体アプリと本体は同じ設定・タスク・操作ロックを使う。他製品は再計画しない。
internal sealed class StandaloneStartup(HostSettingsStore store, IStartupGateway gateway, ToolDefinition tool,
    string executable, Func<bool> installedHere, string prefix = "KotorinToys",
    Func<ExternalStartupState[]>? externalReservations = null)
{
    readonly string sid = WindowsIdentity.GetCurrent().User!.Value;
    string Name => prefix + "." + sid + "." + tool.Id;
    TaskPlan Plan(HostSettings settings) => new(Name, executable, "--startup --launch-origin direct_task",
        Path.GetDirectoryName(executable)!, sid, "KotorinToys:" + settings.OwnerToken + ":" + tool.Id);

    public StandaloneStartupState State()
    {
        try
        {
            if (store.Journal != null || store.RemovalPending) throw new IOException("未完了の予約操作があります。KotorinToysで復旧してください。");
            var settings = store.Load();
            string mode = settings.StartupModes.GetValueOrDefault(tool.Id, "none");
            if (mode == "via_kotorintoys")
            {
                if (!settings.OwnedTasks.TryGetValue("host", out var host)) throw new IOException("本体経由の所有予約を確認できません。KotorinToysで確認してください。");
                ValidateOwnership(settings, "host", host, prefix);
                if (gateway.Read(host.Name)?.Fingerprint != host.Fingerprint) throw new IOException("本体経由の実予約が所有記録と一致しません。KotorinToysで確認してください。");
                return new(true, false, "KotorinToys経由の自動起動が設定されています。変更は本体の「本体設定・自動起動」で行ってください。");
            }
            var actual = gateway.Read(Name);
            if (actual != null && (!settings.OwnedTasks.TryGetValue(tool.Id, out var own) || own.Name != Name || actual.Fingerprint != own.Fingerprint))
                throw new IOException("同名の外部予約または予約の外部変更を検出しました。変更しません。");
            if (mode == "direct_task" && (actual == null || !TaskSchedulerGateway.Matches(actual, Plan(settings))))
                throw new IOException("自動起動の希望と実予約が一致しません。KotorinToysで確認してください。");
            if (mode == "none" && (actual != null || settings.OwnedTasks.ContainsKey(tool.Id))) throw new IOException("希望なしですが予約または所有記録が残っています。KotorinToysで確認してください。");
            bool installed = installedHere();
            return new(mode == "direct_task", installed, !installed ? "自動起動の設定には、この配置への正式インストールが必要です。" :
                mode == "direct_task" ? "Windowsサインイン時に画面を表示せず起動します。" : "自動起動はOFFです。Windowsサインイン時に画面を表示せず起動できます。");
        }
        catch (Exception ex) { return new(false, false, ex.Message); }
    }

    public StartupResult SetEnabled(bool enabled)
    {
        try
        {
            using var lease = store.AcquireLease();
            if (ProductIdentity.FromTool(tool) == null || tool.Capabilities?.Contains("silent-start") != true)
                throw new IOException("共通の自動起動契約を確認できません。");
            var state = State();
            if (!state.CanChange) return new(false, state.Detail);
            if (state.Enabled == enabled) return new(true, state.Detail);
            if (enabled)
            {
                var external = externalReservations?.Invoke() ?? ExternalStartupDetector.Detect(executable)
                    .Concat(gateway is TaskSchedulerGateway real ? real.FindExternal(executable, store.Load().OwnedTasks.Values.Select(t => t.Name)) : [])
                    .ToArray();
                if (external.Any(item => item.State is not ("無効" or "対象外")))
                    throw new IOException("外部の自動起動予約または未確認の登録があります。二重登録を避けるため、先に元の登録を確認してください。");
            }
            // チェックの表示時と予約操作直前の双方で正式配置を確認する。
            if (!installedHere()) throw new IOException("正式導入先を確認できません。");
            var before = store.Load();
            var journal = new StartupJournal(Guid.NewGuid().ToString("N"), tool.Id, before, "単体設定開始", new(), new(), true);
            store.SaveJournal(journal);
            try
            {
                var modes = new Dictionary<string, string>(before.StartupModes);
                var owned = new Dictionary<string, TaskOwnership>(before.OwnedTasks);
                if (enabled)
                {
                    var plan = Plan(before);
                    journal.PlannedTasks[tool.Id] = plan;
                    store.SaveJournal(journal);
                    var created = gateway.Create(plan);
                    journal.CreatedTasks[tool.Id] = new(created.Name, created.Fingerprint, created.Xml, created.SecurityDescriptor);
                    store.SaveJournal(journal);
                    if (!TaskSchedulerGateway.Matches(created, plan) || gateway.Read(Name)?.Fingerprint != created.Fingerprint)
                        throw new IOException("登録後の予約条件が一致しません。");
                    modes[tool.Id] = "direct_task";
                    owned[tool.Id] = journal.CreatedTasks[tool.Id];
                }
                else
                {
                    var own = before.OwnedTasks[tool.Id];
                    ValidateOwnership(before, tool.Id, own, prefix);
                    gateway.Delete(own.Name, own.Fingerprint);
                    modes[tool.Id] = "none";
                    owned.Remove(tool.Id);
                }
                store.Save(before with { StartupModes = modes, OwnedTasks = owned });
                store.CompleteJournal();
                return new(true, enabled ? "自動起動をONにしました。次回サインインから画面を表示せず起動します。" : "自動起動をOFFにしました。現在のアプリは終了しません。");
            }
            catch (Exception ex)
            {
                var restored = Restore(store, gateway, journal, prefix, [tool]);
                return new(false, ex.Message + " / " + restored.Detail);
            }
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    internal static StartupResult Restore(HostSettingsStore store, IStartupGateway gateway, StartupJournal journal, string prefix = "KotorinToys",
        IReadOnlyCollection<ToolDefinition>? definitions = null)
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User!.Value;
            if (!journal.SingleToolOperation || ProductIdentity.FromId(journal.ToolId, definitions) == null ||
                journal.PlannedTasks.Keys.Any(id => id != journal.ToolId) || journal.CreatedTasks.Keys.Any(id => id != journal.ToolId))
                throw new IOException("単体設定の復元記録が不正です。");
            foreach (var pair in journal.PlannedTasks)
            {
                var plan = pair.Value;
                if (plan.Name != prefix + "." + sid + "." + journal.ToolId || plan.Sid != sid ||
                    plan.Marker != "KotorinToys:" + journal.Before.OwnerToken + ":" + journal.ToolId)
                    throw new IOException("復元対象の識別が不正です。");
                var actual = gateway.Read(plan.Name);
                if (actual == null) continue;
                var expected = journal.CreatedTasks.GetValueOrDefault(pair.Key)?.Fingerprint;
                if (expected != null ? actual.Fingerprint != expected : !TaskSchedulerGateway.Matches(actual, plan))
                    throw new IOException("新規予約が外部変更されています。復元しません。");
                gateway.Delete(actual.Name, actual.Fingerprint);
            }
            if (journal.Before.OwnedTasks.TryGetValue(journal.ToolId, out var own))
            {
                ValidateOwnership(journal.Before, journal.ToolId, own, prefix);
                var actual = gateway.Read(own.Name);
                if (actual != null && actual.Fingerprint != own.Fingerprint) throw new IOException("旧予約が外部変更されています。");
                actual ??= gateway.Restore(own.Name, own.Xml, own.SecurityDescriptor);
                if (actual.Fingerprint != own.Fingerprint) throw new IOException("復元後の予約が一致しません。");
            }
            store.Save(journal.Before);
            store.CompleteJournal();
            return new(true, "変更前の予約と設定へ復元しました。");
        }
        catch (Exception ex) { return new(false, "復元未確認: " + ex.Message); }
    }

    static void ValidateOwnership(HostSettings settings, string id, TaskOwnership own, string prefix)
    {
        var recorded = new ScheduledTaskState(own.Name, own.Xml, own.SecurityDescriptor);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        if (own.Name != prefix + "." + WindowsIdentity.GetCurrent().User!.Value + "." + id || recorded.Fingerprint != own.Fingerprint ||
            XDocument.Parse(own.Xml).Root?.Element(ns + "RegistrationInfo")?.Element(ns + "Description")?.Value != "KotorinToys:" + settings.OwnerToken + ":" + id)
            throw new IOException("予約の所有条件が一致しません。");
    }
}
