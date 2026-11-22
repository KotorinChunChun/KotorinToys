using System.ComponentModel;
using System.Reflection;
using KotorinToys;

internal static class DashboardTrayTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root);
        var target = tools.Single(tool => tool.Id == "live-text-marker");
        var missing = tools.Single(tool => tool.Id == "ss-capture");
        var unused = tools.Single(tool => tool.Id == "smart-paste");
        var starts = new List<string>();
        var notices = new List<string>();
        var confirmation = 0;
        var hostUpdates = 0;
        var approve = false;
        bool exists = true;
        var pending = new TaskCompletionSource<InstallResult[]>();
        using var dashboard = new Dashboard(root, detectInstallation: tool => tool.Id == target.Id || tool.Id == missing.Id
            ? new("導入済み", "0.0.1", root, tool.InstalledExecutable)
            : new("未導入", null, null, null),
            executionCheck: (_, _) => Task.FromResult(new ToolState("停止中")),
            releaseCheck: (_, package, _) => Task.FromResult(new PublicRelease(package, package != null, false, "fixture")),
            fileExists: name => exists && name == target.InstalledExecutable,
            controlExecution: (tool, _) => { starts.Add(tool.Id); return Task.FromResult(new ToolState("実行中")); },
            showSelfUpdater: () => hostUpdates++, notify: notices.Add,
            confirmUpdates: _ => { confirmation++; return approve; },
            runUpdates: (apps, _, _, _) => pending.Task);
        dashboard.CreateControl();
        dashboard.RefreshAll().GetAwaiter().GetResult();
        using var menu = dashboard.CreateTrayMenu(() => { }, () => { });
        var openApps = (ToolStripMenuItem)menu.Items["openApps"]!;
        var grid = (DataGridView)dashboard.Controls.Find("toolTable", true).Single();
        var selected = grid.Rows.Cast<DataGridViewRow>().Single(row => Equals(row.Tag, unused.Id));
        grid.CurrentCell = selected.Cells["tool"];

        check(openApps.DropDownItems[target.Id]!.Enabled && openApps.DropDownItems[unused.Id] == null &&
            openApps.DropDownItems[missing.Id] is { Enabled: false, ToolTipText: var reason } && reason?.Contains("実行ファイル") == true,
            "トレイは未導入を省き、実行ファイルなしの導入済みアプリには無効理由を表示");
        openApps.DropDownItems[target.Id]!.PerformClick();
        check(starts.SequenceEqual([target.Id]) && ReferenceEquals(grid.CurrentRow, selected) && !dashboard.Visible,
            "トレイから一覧の選択や表示を変えずに、選んだアプリだけを起動");

        exists = false;
        openApps.DropDownItems[target.Id]!.PerformClick();
        check(starts.Count == 1 && notices.Last().Contains("実行ファイル"), "トレイの選択後にも起動条件を再確認");
        exists = true;
        menu.Items["hostUpdate"]!.PerformClick();
        check(hostUpdates == 1 && !dashboard.Visible, "トレイから管理画面を出さずに本体更新を開く");

        var update = menu.Items["updateAll"]!;
        update.PerformClick();
        check(confirmation == 1 && dashboard.CanOperate && notices.Last().Contains("取り消しました"),
            "トレイのすべて更新は一度確認し、取消結果を通知");
        approve = true;
        update.PerformClick();
        var opening = typeof(ContextMenuStrip).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!;
        opening.Invoke(menu, [new CancelEventArgs()]);
        check(confirmation == 2 && !openApps.Enabled && !update.Enabled && !menu.Items["hostUpdate"]!.Enabled,
            "トレイから更新処理中は再実行・アプリ起動・本体更新をグレーアウト");
        pending.SetResult([new(target.Id, "update", "成功", "検証用更新完了")]);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!dashboard.CanOperate && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        opening.Invoke(menu, [new CancelEventArgs()]);
        check(openApps.Enabled && update.Enabled && notices.Last().Contains("成功 1件") && !dashboard.Visible,
            "トレイからの更新が終わると操作を復旧し、管理画面を開かずに結果を通知");
    }
}
