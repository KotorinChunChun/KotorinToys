namespace KotorinToys;

internal sealed class Dashboard : BrandedForm
{
    readonly ToolManager manager;
    readonly ToolDefinition[] tools;
    readonly DataGridView grid = new() { Name = "toolTable", Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
    readonly Label overall = new() { Name = "overallStatus", AutoSize = true, Dock = DockStyle.Fill };
    readonly Label selection = new() { Name = "selectedStatus", AutoSize = true, Dock = DockStyle.Fill };
    readonly Label operation = new() { Name = "lastOperation", AutoSize = true, Dock = DockStyle.Fill,
        Text = "本体を閉じても各ツールは動作します。" };
    readonly Label releaseStatus = new() { AutoSize = true, Dock = DockStyle.Fill, Text = "公開版：未確認（同梱台帳を表示）" };
    readonly Button update = new() { Name = "updateSelected", Text = "選択ツールを更新", AutoSize = true, Enabled = false };
    readonly Button checkRelease = new() { Text = "公開版を確認", AutoSize = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new(8) };
    readonly CancellationTokenSource lifetime = new();
    readonly ReleasePackage?[] bundledPackages;
    InstallationState[] installations;
    ToolState[] states;
    PublicRelease[] releases;
    bool busy, refreshing, checkingReleases, resourcesDisposed;

    public Dashboard(string root, HostSettingsStore? settings = null, StartupCoordinator? startup = null)
    {
        manager = new(root); tools = manager.Load();
        installations = tools.Select(manager.Installation).ToArray();
        states = tools.Select(_ => new ToolState("確認中")).ToArray();
        ReleaseCatalog? bundled = null;
        try { bundled = ReleaseDownloader.Load(root); } catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { releaseStatus.Text = "公開台帳を読めません：" + ex.Message; }
        bundledPackages = tools.Select(tool => bundled?.Packages.SingleOrDefault(p => p.Package.Id == tool.Id)).ToArray();
        releases = bundledPackages.Select(package => new PublicRelease(package, false, false, "公開未確認・同梱台帳")).ToArray();
        Text = WindowTitle.Create(); Size = new(1180, 620); MinimumSize = new(980, 480);
        Font = new("Yu Gothic UI", 11); StartPosition = FormStartPosition.CenterScreen;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.ColumnHeadersDefaultCellStyle.Padding = new(4, 8, 4, 8);
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        foreach (var (name, label, weight) in new[] { ("tool", "ツール", 16), ("summary", "ひとこと解説", 24),
            ("installed", "導入済み版", 10), ("published", "公開版", 14), ("update", "更新", 13), ("state", "現在の状態", 12), ("detail", "詳細", 18) })
        {
            var column = grid.Columns[grid.Columns.Add(name, label)]; column.FillWeight = weight;
            column.MinimumWidth = name == "summary" ? 150 : name == "tool" ? 125 : 80;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        foreach (var tool in tools) grid.Rows.Add(tool.Name, tool.Summary, "", "", "", "確認中", "");
        grid.SelectionChanged += (_, _) => UpdateFooter();
        grid.CurrentCellChanged += (_, _) => UpdateFooter();
        grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || busy) return;
            grid.CurrentCell = grid.Rows[e.RowIndex].Cells["tool"]; await Execute("start");
        };
        foreach (var (label, command) in new[] { ("起動 / 画面を開く", "start"), ("正常停止", "stop"), ("状態を更新", "refresh"), ("使い方", "manual") })
        {
            var button = new Button { Text = label, AutoSize = true, Name = command };
            button.Click += async (_, _) => await Execute(command); actions.Controls.Add(button);
        }
        if (settings != null && startup != null)
        {
            var button = new Button { Text = "本体設定・自動起動", AutoSize = true };
            button.Click += (_, _) => { using var form = new StartupSettingsForm(settings, startup, tools); form.ShowDialog(this); }; actions.Controls.Add(button);
        }
        var installation = new Button { Text = "導入・削除 / FFmpeg", AutoSize = true };
        installation.Click += async (_, _) => await OpenInstallation(false); actions.Controls.Add(installation);
        update.Click += async (_, _) => await OpenInstallation(true); actions.Controls.Add(update);
        checkRelease.Click += async (_, _) => await RefreshReleases(); actions.Controls.Add(checkRelease);
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new(10) };
        footer.Controls.Add(overall); footer.Controls.Add(selection); footer.Controls.Add(releaseStatus); footer.Controls.Add(operation);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(grid, 0, 0); layout.Controls.Add(actions, 0, 1); layout.Controls.Add(footer, 0, 2); Controls.Add(layout);
        ApplySnapshot(installations, states, releases);
        Shown += async (_, _) => { timer.Start(); await RefreshStates(); await RefreshReleases(); };
        timer.Tick += async (_, _) => { if (!busy) await RefreshStates(); };
    }

    internal void ApplySnapshot(InstallationState[] actual, ToolState[] execution, PublicRelease[] published)
    {
        if (actual.Length != tools.Length || execution.Length != tools.Length || published.Length != tools.Length) throw new ArgumentException("状態件数が一致しません。");
        installations = actual; states = execution; releases = published;
        for (int i = 0; i < tools.Length; i++)
        {
            var version = ToolVersionView.Create(actual[i], published[i]); var row = grid.Rows[i];
            row.Cells["installed"].Value = version.Installed; row.Cells["published"].Value = version.Published;
            row.Cells["published"].ToolTipText = published[i].Detail;
            row.Cells["update"].Value = version.Update; row.Cells["state"].Value = execution[i].State;
            row.Cells["detail"].Value = execution[i].Detail;
        }
        UpdateFooter();
    }
    void UpdateFooter()
    {
        overall.Text = DashboardStatus.Summary(states);
        int i = grid.CurrentRow?.Index ?? -1;
        if (i < 0 || i >= tools.Length) { selection.Text = "選択：ツールを選択してください。"; update.Enabled = false; return; }
        var version = ToolVersionView.Create(installations[i], releases[i]);
        selection.Text = $"選択：{tools[i].Name} — {states[i].State} / 導入済み版 {version.Installed} / 公開版 {version.Published}";
        if (!releases[i].Confirmed) selection.Text += "\n" + releases[i].Detail;
        update.Enabled = !busy && !checkingReleases && version.CanUpdate;
    }
    async Task Execute(string command)
    {
        if (command == "refresh") { await RefreshStates(); return; }
        if (busy) return;
        busy = true; actions.Enabled = false;
        try
        {
            var index = grid.CurrentRow?.Index ?? 0; var tool = tools[index];
            if (command == "manual")
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PathSafety.Resolve(manager.DocumentRoot(tool), tool.Manual)) { UseShellExecute = true });
            else
            {
                grid.Rows[index].Cells["state"].Value = command == "start" ? "起動処理中" : "停止処理中";
                var state = command == "start" ? await manager.Start(tool) : await manager.Stop(tool);
                operation.Text = $"最終操作：{tool.Name} / {state.State} {state.Detail}";
            }
            await RefreshStates(true);
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "操作失敗：" + ex.Message; }
        finally { busy = false; if (!IsDisposed) { actions.Enabled = true; UpdateFooter(); } }
    }
    async Task OpenInstallation(bool updateOnly)
    {
        if (busy || updateOnly && checkingReleases) return;
        var index = grid.CurrentRow?.Index ?? 0;
        if (updateOnly && !ToolVersionView.Create(installations[index], releases[index]).CanUpdate) return;
        busy = true; actions.Enabled = false;
        try
        {
            var packages = releases.Where(r => r.Package != null).Select(r => r.Package!).ToArray();
            var catalog = packages.Length > 0 ? new ReleaseCatalog(1, packages) : null;
            using var form = new InstallationForm(manager, updateOnly ? tools[index].Id : null, catalog);
            form.ShowDialog(this);
            await RefreshStates(true);
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "導入画面を開けません：" + ex.Message; }
        finally { busy = false; if (!IsDisposed) { actions.Enabled = true; UpdateFooter(); } }
    }
    async Task RefreshStates(bool afterOperation = false)
    {
        if (refreshing || busy && !afterOperation || IsDisposed) return;
        refreshing = true;
        try
        {
            var execution = await Task.WhenAll(tools.Select(tool => manager.Status(tool, lifetime.Token)));
            if (IsDisposed || busy && !afterOperation) return;
            ApplySnapshot(tools.Select(manager.Installation).ToArray(), execution, releases);
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "状態取得失敗：" + ex.Message; }
        finally { refreshing = false; }
    }
    async Task RefreshReleases()
    {
        if (checkingReleases || IsDisposed) return;
        checkingReleases = true; checkRelease.Enabled = false; UpdateFooter(); releaseStatus.Text = "公開版：GitHubのリリースと配布台帳を確認中…";
        try
        {
            using var client = new PublicReleaseClient();
            var published = await Task.WhenAll(tools.Select((tool, i) => client.Check(tool, bundledPackages[i], lifetime.Token)));
            if (IsDisposed) return;
            ApplySnapshot(installations, states, published);
            releaseStatus.Text = $"公開版：{published.Count(r => r.Confirmed)} / {tools.Length}件を照合済み（開発版を含む・未署名）";
        }
        catch (Exception ex) { if (!IsDisposed) releaseStatus.Text = "公開版の確認失敗：" + ex.Message; }
        finally { checkingReleases = false; if (!IsDisposed) { checkRelease.Enabled = true; UpdateFooter(); } }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed) { resourcesDisposed = true; lifetime.Cancel(); timer.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
