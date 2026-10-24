using KotorinToys;

internal static class StartupOverviewDashboardTests
{
    static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("自動起動列の試験が完了しません。");
        task.GetAwaiter().GetResult(); Application.DoEvents();
    }
    public static void Run(string root, string fixtures, Action<bool, string> check)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        try { RunUi(root, check); InFlight(root, fixtures, check); Closing(root, check); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    static void RunUi(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root); int reads = 0;
        bool failure = false;
        var expected = new[] { "本体経由", "OS（タスク）", "OS（スタートアップ）", "なし", "無効", "確認できない", "本体経由 / OS（タスク）" };
        var values = tools.Select((tool, index) => (tool.Id, Value: new StartupOverview(expected[index % expected.Length], tool.Name + "の登録詳細", index == 5)))
            .ToDictionary(pair => pair.Id, pair => pair.Value);
        TaskCompletionSource? gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<StartupOverview> Read(ToolDefinition tool, CancellationToken token)
        {
            reads++; if (gate != null) await gate.Task.WaitAsync(token);
            if (failure && tool.Id == tools[1].Id) throw new IOException("予約の読取拒否");
            return values[tool.Id];
        }
        using var form = new Dashboard(root, startupCheck: Read, detectInstallation:_=>new("導入済み","1.0.0",root,"fixture.exe"),
            releaseCheck: (_, fallback, _) => Task.FromResult(new PublicRelease(fallback, false, false, "fixture")));
        form.CreateControl(); form.PerformLayout();
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        var column = grid.Columns["startup"]!;
        check(column.HeaderText == "自動起動" && column.ReadOnly, "メイン一覧に読取専用の自動起動列を追加");
        check(grid.Rows.Cast<DataGridViewRow>().All(row => row.Cells["startup"].Value?.ToString() == "未確認"), "未取得を登録なしでなく未確認と表示");
        var first = form.RefreshStartups(); var duplicate = form.RefreshStartups();
        check(ReferenceEquals(first, duplicate) && reads == tools.Length && grid.Rows[0].Cells["startup"].Value?.ToString() == "確認中", "重複した自動起動照合を一つにまとめ取得中を表示");
        grid.CurrentCell = grid.Rows[2].Cells[0]; gate.SetResult(); Pump(first); gate = null;
        check(grid.Rows.Cast<DataGridViewRow>().Select(row => row.Cells["startup"].Value?.ToString()).SequenceEqual(tools.Select(tool => values[tool.Id].Text)), "本体経由・OS側・なし・無効・不明・重複を各行へ表示");
        check(grid.CurrentRow?.Index == 2 && grid.Rows[0].Cells["startup"].ToolTipText.Contains(tools[0].Name + "の登録詳細"), "選択を維持し自動起動セルに照合詳細を表示");
        check(grid.Rows[5].Cells["detail"].ToolTipText.Contains(tools[5].Name + "の登録詳細") == true, "確認不能の理由は詳細欄にも残す");
        var before = reads; Pump(form.RefreshAll());
        check(reads == before + tools.Length, "状態を更新には自動起動の再取得も含める");
        before = reads; Pump(form.OpenStartupSettings(() => values[tools[2].Id] = new("なし", "設定後の確認")));
        check(reads == before + tools.Length && grid.Rows[2].Cells["startup"].Value?.ToString() == "なし", "本体設定を閉じたら自動起動列を更新する");
        failure = true; Pump(form.RefreshStartups());
        check(grid.Rows[1].Cells["startup"].Value?.ToString() == "確認できない" && grid.Rows[1].Cells["detail"].ToolTipText.Contains("予約の読取拒否") == true &&
            grid.Rows[0].Cells["startup"].Value?.ToString() == values[tools[0].Id].Text, "1製品の読取例外で他製品の確認結果を失わない");
        form.Size = form.MinimumSize; form.PerformLayout();
        check(column.Width >= 85 && grid.ColumnHeadersHeight >= 40 && grid.Height >= 200, $"最小画面でも自動起動列幅・ヘッダー・検索追加後の一覧領域を確保（{grid.Width}×{grid.Height}）");
        check(!form.Controls.Find("overallStatus", true).Any() && grid.Parent is TableLayoutPanel layout && layout.RowCount == 4 && form.Controls.Find("viewBar",true).Length==1, "絞り込みを加えても下部の常設説明を復活させない");
    }
    static void InFlight(string root, string fixtures, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root); int calls = 0, configs = 0;
        var online = OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root, OnlineToolCatalog.RelativePath)), root);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new ToolCatalogSource(root, Path.Combine(fixtures, "startup-ui-config"), _ =>
        { configs++; return Task.FromResult(OnlineCatalogTests.Encode(online[1])); });
        async Task<StartupOverview> Read(ToolDefinition tool, CancellationToken token)
        {
            var current = ++calls;
            if (current <= tools.Length) await gate.Task.WaitAsync(token);
            return new(current <= tools.Length ? "本体経由" : "OS（タスク）", tool.Id);
        }
        using var form = new Dashboard(root, toolManager: new(root, catalogSource: source), startupCheck: Read, detectInstallation:_=>new("導入済み","1.0.0",root,"fixture.exe"),
            releaseCheck: (_, fallback, _) => Task.FromResult(new PublicRelease(fallback, false, false, "fixture")));
        form.CreateControl(); var pending = form.RefreshStartups(); var refresh = form.RefreshAll();
        check(configs == 0 && !refresh.IsCompleted, "旧一覧の自動起動照合が完了するまでconfig差替を待つ");
        gate.SetResult(); Pump(pending); Pump(refresh);
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        check(grid.Rows.Count == 1 && grid.Rows[0].Cells["tool"].Value?.ToString() == tools[1].Name &&
            grid.Rows[0].Cells["startup"].Value?.ToString() == "OS（タスク）" && calls == tools.Length + 1, "一覧が減っても旧照合結果を別アプリへ転用しない");
    }
    static void Closing(string root, Action<bool, string> check)
    {
        int calls = 0, canceled = 0;
        async Task<StartupOverview> Read(ToolDefinition _, CancellationToken token)
        {
            calls++;
            try { await Task.Delay(Timeout.Infinite, token); return new("なし", ""); }
            finally { if (token.IsCancellationRequested) canceled++; }
        }
        using var form = new Dashboard(root, startupCheck: Read);
        var pending = form.RefreshStartups(); form.Dispose(); Pump(pending);
        check(calls == ToolCatalog.Load(root).Length && canceled == calls && form.IsDisposed, "破棄時は全照合を取消し破棄したフォームを更新しない");
        Pump(form.RefreshStartups()); check(calls == canceled, "破棄後は自動起動の新しい照合を開始しない");
    }
}
