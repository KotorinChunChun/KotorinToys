using KotorinToys;

internal static class DashboardRefreshTests
{
    static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("更新画面の試験が完了しません。");
        task.GetAwaiter().GetResult();
    }

    public static void Run(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root); int requests = 0, dialogs = 0;
        bool failure = false, newer = true;
        string? selected = null; ReleaseCatalog? offered = null;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PublicRelease> Fetch(ToolDefinition tool, ReleasePackage? fallback, CancellationToken token)
        {
            requests++; await gate.Task.WaitAsync(token);
            if (failure) throw new IOException("照合失敗の注入");
            if (fallback is null) return new(null, false, false, "未公開");
            return new(newer ? new(fallback.Package with { Version = "9.0.0", File = tool.Name + "-9.0.0-setup.exe" }, fallback.Length) : fallback,
                true, false, "最新照合");
        }
        using var form = new Dashboard(root, releaseCheck: Fetch,
            showToolInstaller: (id, operation, catalog) => { dialogs++; selected = id; offered = catalog; check(operation == "update", "アップデートは単体操作画面へ接続"); },
            detectInstallation: tool => new("導入済み", tool.Version, null, "fixture.exe"), notify: _ => { }, fileExists: _ => true,
            executionCheck: (_, _) => Task.FromResult(new ToolState("停止中")));
        form.CreateControl(); form.PerformLayout();
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        var selectedRow = form.Controls.Find("selectedActions", true).Single();
        var hostRow = form.Controls.Find("hostActions", true).Single();
        var update = (Button)form.Controls.Find("updateSelected", true).Single();
        check(form.Controls.Find("start", true).Single().Text == "起動 / 設定を開く" && form.Controls.Find("stop", true).Single().Text == "停止させる" &&
            form.Controls.Find("manual", true).Single().Text == "使い方を開く" && update.Text == "アップデート", "選択ツールの操作名を追加依頼どおり変更");
        check(!selectedRow.Controls.Cast<Control>().Concat(hostRow.Controls.Cast<Control>()).Any(c => c.Text == "公開版を確認"), "独立した公開版確認ボタンを削除");
        check(form.Controls.Find("start", true).Single().Parent == selectedRow && update.Parent == selectedRow &&
            form.Controls.Find("refresh", true).Single().Parent == hostRow && selectedRow.Bottom <= hostRow.Top, "選択アプリ操作と本体全体操作は別行");
        grid.CurrentCell = grid.Rows[1].Cells[0];
        check(!update.Enabled, "公開版未照合のアップデートは無効");
        var first = form.RefreshReleases(); var second = form.RefreshReleases();
        check(ReferenceEquals(first, second) && requests == tools.Length, "同時の公開照合要求を一つの通信へ集約");
        gate.SetResult(); Pump(first);
        gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int before = requests;
        var updating = form.OpenInstallation(true);
        check(!selectedRow.Enabled && !hostRow.Enabled, "更新処理中は選択操作と本体操作の両方を抑止");
        var duplicate = form.OpenInstallation(true);
        check(duplicate.IsCompletedSuccessfully && requests == before + tools.Length && dialogs == 0, "処理中の再操作で照合と導入画面を重複しない");
        grid.CurrentCell = grid.Rows[2].Cells[0]; // 通信中の選択変更を模擬。
        gate.SetResult(); Pump(updating);
        check(requests == before + tools.Length && dialogs == 1, "更新は公開情報を再取得してから導入画面へ進む");
        check(selected == tools[1].Id && offered?.Packages.Single(p => p.Package.Id == selected).Package.Version == "9.0.0", "途中で行を変えても押した製品の最新台帳を渡す");
        check(selectedRow.Enabled && hostRow.Enabled, "更新処理後に両操作行を復旧");
        newer = false; grid.CurrentCell = grid.Rows[1].Cells[0];
        Pump(form.OpenInstallation(true));
        check(dialogs == 1 && !update.Enabled, "同じ公開版なら導入せずアップデートを無効にする");
        newer = true; Pump(form.RefreshAll());
        failure = true;
        Pump(form.OpenInstallation(true));
        check(dialogs == 1 && grid.Rows[1].Cells["update"].Value?.ToString() == "未対応", "照合例外時に古い確認済み情報で更新しない");
        check(grid.Rows[1].Cells["detail"].ToolTipText.Contains("照合失敗の注入") == true, "下部表示を削除しても公開照合の具体的な失敗を表へ残す");
        check(selectedRow.Enabled && hostRow.Enabled && !update.Enabled, "照合失敗後はアップデートを無効にし状態更新で再確認できる");
        failure = false; before = requests;
        Pump(form.RefreshAll());
        check(requests == before + tools.Length, "状態更新にも公開版の再取得を含む");
        CheckClosingDuringRefresh(root, tools.Length, check);
        RepositoryButtonTests.Run(root, check);
    }

    static void CheckClosingDuringRefresh(string root, int toolCount, Action<bool, string> check)
    {
        int started = 0, canceled = 0;
        async Task<PublicRelease> Wait(ToolDefinition tool, ReleasePackage? fallback, CancellationToken token)
        {
            started++;
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { if (token.IsCancellationRequested) canceled++; }
            return new(fallback, false, false, "待機終了");
        }
        using var closing = new Dashboard(root, releaseCheck: Wait);
        var pending = closing.RefreshReleases();
        closing.Dispose(); Pump(pending);
        check(started == toolCount && canceled == toolCount && closing.IsDisposed, "画面破棄で全照合を中止し破棄済み画面を更新しない");
        Pump(closing.RefreshReleases()); Pump(closing.RefreshAll());
        check(started == toolCount, "破棄後の更新要求は新しい通信を開始しない");
    }
}
