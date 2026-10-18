using System.Xml.Linq;
using KotorinToys;

internal static class StartupOverviewTests
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var none = new StartupState("none", "なし");
        var via = new StartupState("via_kotorintoys", "登録済み", "共有予約を照合済み");
        ExternalStartupState TaskEntry(string state = "有効") => new("外部タスク", "外部の起動予約", "fixture", state);
        check(StartupOverview.Create(none, []).Text == "なし", "本体もOSも登録なしの場合だけなしにする");
        check(StartupOverview.Create(via, []).Text == "本体経由", "希望設定だけでなく照合済みの本体経由を表示");
        check(StartupOverview.Create(new("direct_task", "登録済み"), []).Text == "OS（タスク）", "本体管理の直接予約もOSタスクに分類");
        check(StartupOverview.Create(none, [TaskEntry()]).Text == "OS（タスク）", "外部の有効なタスクを表示");
        foreach (var source in new[] { "Run登録", "スタートアップ" })
            check(StartupOverview.Create(none, [new(source, "起動項目", "fixture")]).Text == "OS（スタートアップ）", "OSの有効な起動登録を表示：" + source);
        var both = StartupOverview.Create(via, [TaskEntry(), new("Run登録", "例", "fixture")]);
        check(both.Text == "本体経由 / OS（タスク） / OS（スタートアップ）", "重複した自動起動経路を隠さず併記");
        check(StartupOverview.Create(new("direct_task", "登録済み"), [TaskEntry()]).Text == "OS（タスク）", "同じ方式の重複表示を抑え詳細は残す");
        check(StartupOverview.Create(none, [TaskEntry("無効")]).Text == "無効", "無効な予約だけでは自動起動有効と表示しない");
        check(StartupOverview.Create(none, [TaskEntry("対象外")]).Text == "なし", "手動専用・別ユーザーの予約を有効な自動起動にしない");
        check(StartupOverview.Create(none, [TaskEntry("未確認")]).Text == "確認できない", "読取失敗を登録なしにしない");
        var mixed = StartupOverview.Create(via, [TaskEntry("未確認")]);
        check(mixed.Text.Contains("本体経由") && mixed.Text.Contains("一部未確認") && mixed.NeedsAttention, "一部の登録を読めなくても確認済み経路と不明を両方保持");
        check(StartupOverview.Create(new("via_kotorintoys", "要対応", "共有予約がありません"), []).NeedsAttention, "希望方式だけがあり実予約がない場合は有効にしない");
        check(StartupOverview.Create(none, [TaskEntry()]).Detail.Contains("外部の起動予約") && both.Detail.Contains("共有予約を照合済み"), "登録元・予約名・照合理由をセル詳細に残す");

        foreach (var flag in new byte[] { 2, 3, 6, 7, 255 })
        {
            var bytes = new byte[12]; bytes[0] = flag;
            check(ExternalStartupDetector.ApprovalState(bytes) == (flag is 2 or 6 ? "有効" : flag is 3 or 7 ? "無効" : "未確認"), "StartupApprovedの既知状態だけを判定：" + flag);
        }
        check(ExternalStartupDetector.ApprovalState(null) == "有効", "無効化情報のない既存Run項目は登録に従う");
        check(new object[] { new byte[] { 2 }, "2", new byte[13], new byte[] { 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } }
            .All(value => ExternalStartupDetector.ApprovalState(value) == "未確認"), "未知の形式は有効にも無効にも推定しない");

        const string sid = "S-1-5-21-123-456-789-1001";
        var exe = Path.Combine(root, "test startup", "app.exe");
        XDocument Xml(string enabled = "true", string trigger = "LogonTrigger", string triggerEnabled = "true", string? user = sid, bool withTrigger = true)
        {
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            return new(new XElement(ns + "Task", new XElement(ns + "Settings", new XElement(ns + "Enabled", enabled)),
                new XElement(ns + "Triggers", withTrigger ? new XElement(ns + trigger, new XElement(ns + "Enabled", triggerEnabled), user == null ? null : new XElement(ns + "UserId", user)) : null),
                new XElement(ns + "Actions", new XElement(ns + "Exec", new XElement(ns + "Command", exe)))));
        }
        check(TaskSchedulerGateway.ClassifyExternal(Xml(), exe, sid) == "有効", "ログオンタスクの有効条件を照合");
        check(TaskSchedulerGateway.ClassifyExternal(Xml(enabled: "false"), exe, sid) == "無効", "タスク全体の無効化を反映");
        check(TaskSchedulerGateway.ClassifyExternal(Xml(triggerEnabled: "false"), exe, sid) == "無効", "トリガー側の無効化を反映");
        check(TaskSchedulerGateway.ClassifyExternal(Xml(withTrigger: false), exe, sid) == "対象外", "手動専用タスクを自動起動にしない");
        check(TaskSchedulerGateway.ClassifyExternal(Xml(user: "S-1-5-21-123-456-789-9999"), exe, sid) == "対象外", "別ユーザーのログオン予約を除外");
        check(TaskSchedulerGateway.ClassifyExternal(Xml(trigger: "BootTrigger", user: null), exe, sid) == "有効", "PC起動時の自動タスクを検出");
        check(TaskSchedulerGateway.ClassifyExternal(Xml(trigger: "CalendarTrigger", user: null), exe, sid) == "有効", "定期実行タスクの有効な自動トリガーも検出");
        check(TaskSchedulerGateway.ClassifyExternal(Xml(trigger: "CustomTrigger", user: null), exe, sid) == "未確認", "未知のトリガーを登録なしにしない");
        check(TaskSchedulerGateway.ClassifyExternal(Xml(), exe + ".other", sid) == null, "同名や部分一致で別の実行先を採用しない");

        var tool = ToolCatalog.Load(root)[0]; int reads = 0;
        var reader = new StartupOverviewReader(_ => { reads++; return Task.FromResult(via); }, _ => { reads++; return Task.FromResult(new[] { TaskEntry() }); });
        check((await reader.Read(tool, default)).Text.Contains("本体経由 / OS") && reads == 2, "読取専用の2経路を集約する");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); bool canceled = false;
        try { await reader.Read(tool, cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
        check(canceled && reads == 2, "取消後はOSの読取を開始しない");
        reader = new(_ => throw new IOException("読取拒否の試験"), _ => Task.FromResult(Array.Empty<ExternalStartupState>()));
        var failed = await reader.Read(tool, default);
        check(failed.Text == "確認できない" && failed.Detail.Contains("読取拒否の試験"), "読取例外を具体的な原因にする");
    }
}
