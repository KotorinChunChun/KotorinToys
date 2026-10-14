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
    readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    readonly FlowLayoutPanel actions = new() { Name = "selectedActions", Dock = DockStyle.Fill, AutoSize = true, Padding = new(8) };
    readonly FlowLayoutPanel hostActions = new() { Name = "hostActions", Dock = DockStyle.Fill, AutoSize = true, Padding = new(8, 0, 8, 0) };
    readonly Func<ToolDefinition, ReleasePackage?, CancellationToken, Task<PublicRelease>> releaseCheck;
    readonly Action<string?, ReleaseCatalog?> showInstaller;
    readonly Func<ToolDefinition, InstallationState> detectInstallation;
    Task? releaseRefresh;
    readonly CancellationTokenSource lifetime = new();
    readonly ReleasePackage?[] bundledPackages;
    InstallationState[] installations;
    ToolState[] states;
    PublicRelease[] releases;
    bool busy, refreshing, checkingReleases, resourcesDisposed;

    public Dashboard(string root, HostSettingsStore? settings = null, StartupCoordinator? startup = null,
        Func<ToolDefinition, ReleasePackage?, CancellationToken, Task<PublicRelease>>? releaseCheck = null,
        Action<string?, ReleaseCatalog?>? showInstaller = null,
        Func<ToolDefinition, InstallationState>? detectInstallation = null,
        ToolManager? toolManager = null)
    {
        manager = toolManager ?? new(root); tools = manager.Load();
        this.releaseCheck = releaseCheck ?? CheckRelease;
        this.showInstaller = showInstaller ?? ((id, catalog) => { using var form = new InstallationForm(manager, id, catalog); form.ShowDialog(this); });
        this.detectInstallation = detectInstallation ?? manager.Installation;
        installations = tools.Select(this.detectInstallation).ToArray();
        states = tools.Select(_ => new ToolState("確認中")).ToArray();
        ReleaseCatalog? bundled = null;
        try { bundled = ReleaseDownloader.Load(root); } catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { releaseStatus.Text = "公開台帳を読めません：" + ex.Message; }
        bundledPackages = tools.Select(tool => bundled?.Packages.SingleOrDefault(p => p.Package.Id == tool.Id)).ToArray();
        releases = bundledPackages.Select(package => new PublicRelease(package, false, false, "公開未確認・同梱台帳")).ToArray();
        Text = WindowTitle.Create(); Size = new(1180, 680); MinimumSize = new(980, 540);
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
        actions.Controls.Add(new Label { Text = "選択中アプリ：", AutoSize = true, Margin = new(3, 8, 8, 0) });
        hostActions.Controls.Add(new Label { Text = "本体全体：", AutoSize = true, Margin = new(3, 8, 8, 0) });
        foreach (var (label, command) in new[] { ("起動設定を開く", "start"), ("停止させる", "stop"), ("使い方", "manual") })
        {
            var button = new Button { Text = label, AutoSize = true, Name = command };
            button.Click += async (_, _) => await Execute(command); actions.Controls.Add(button);
        }
        var refresh = new Button { Text = "状態を更新", Name = "refresh", AutoSize = true };
        refresh.Click += async (_, _) => await RefreshAll(); hostActions.Controls.Add(refresh);
        if (settings != null && startup != null)
        {
            var button = new Button { Text = "本体設定・自動起動", Name = "hostSettings", AutoSize = true };
            button.Click += (_, _) => { using var form = new StartupSettingsForm(settings, startup, tools); form.ShowDialog(this); }; hostActions.Controls.Add(button);
        }
        var installation = new Button { Text = "導入・削除 / FFmpeg", AutoSize = true };
        installation.Click += async (_, _) => await OpenInstallation(false); hostActions.Controls.Add(installation);
        update.Click += async (_, _) => await OpenInstallation(true); actions.Controls.Add(update);
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new(10) };
        footer.Controls.Add(overall); footer.Controls.Add(selection); footer.Controls.Add(releaseStatus); footer.Controls.Add(operation);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(grid, 0, 0); layout.Controls.Add(actions, 0, 1); layout.Controls.Add(hostActions, 0, 2); layout.Controls.Add(footer, 0, 3); Controls.Add(layout);
        ApplySnapshot(installations, states, releases);
        Shown += async (_, _) => { timer.Start(); await RefreshAll(); };
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
        // 最新情報が未取得・同じ版でも、押して再照合できる。導入は照合後に判断する。
        update.Enabled = !busy && !checkingReleases && tools[i].DistributionStatus == "published" && installations[i].State == "導入済み";
    }
    async Task Execute(string command)
    {
        if (busy) return;
        busy = true; actions.Enabled = hostActions.Enabled = false;
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
        finally { busy = false; if (!IsDisposed) { actions.Enabled = hostActions.Enabled = true; UpdateFooter(); } }
    }
    internal async Task OpenInstallation(bool updateOnly)
    {
        if (busy || IsDisposed) return;
        var index = grid.CurrentRow?.Index ?? 0;
        busy = true; actions.Enabled = hostActions.Enabled = false;
        try
        {
            if (updateOnly)
            {
                await RefreshReleases();
                await RefreshStates(true);
                if (IsDisposed) return;
                if (!ToolVersionView.Create(installations[index], releases[index]).CanUpdate)
                {
                    operation.Text = tools[index].Name + "：" + ToolVersionView.Create(installations[index], releases[index]).Update;
                    return;
                }
            }
            var packages = releases.Where(r => r.Package != null).Select(r => r.Package!).ToArray();
            var catalog = packages.Length > 0 ? new ReleaseCatalog(1, packages) : null;
            showInstaller(updateOnly ? tools[index].Id : null, catalog);
            await RefreshStates(true);
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "導入画面を開けません：" + ex.Message; }
        finally { busy = false; if (!IsDisposed) { actions.Enabled = hostActions.Enabled = true; UpdateFooter(); } }
    }
    async Task RefreshStates(bool afterOperation = false)
    {
        if (refreshing || busy && !afterOperation || IsDisposed) return;
        refreshing = true;
        try
        {
            var execution = await Task.WhenAll(tools.Select(tool => manager.Status(tool, lifetime.Token)));
            if (IsDisposed || busy && !afterOperation) return;
            ApplySnapshot(tools.Select(detectInstallation).ToArray(), execution, releases);
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "状態取得失敗：" + ex.Message; }
        finally { refreshing = false; }
    }
    internal async Task RefreshAll()
    {
        if (busy || IsDisposed) return;
        await Task.WhenAll(RefreshStates(), RefreshReleases());
    }
    static async Task<PublicRelease> CheckRelease(ToolDefinition tool, ReleasePackage? fallback, CancellationToken cancellation)
    {
        using var client = new PublicReleaseClient();
        return await client.Check(tool, fallback, cancellation);
    }
    internal Task RefreshReleases()
    {
        if (IsDisposed) return Task.CompletedTask;
        if (releaseRefresh is { IsCompleted: false }) return releaseRefresh;
        return releaseRefresh = RefreshReleasesCore();
    }
    async Task RefreshReleasesCore()
    {
        if (checkingReleases || IsDisposed) return;
        checkingReleases = true; UpdateFooter(); releaseStatus.Text = "公開版：GitHubのリリースと配布台帳を確認中…";
        try
        {
            var published = await Task.WhenAll(tools.Select((tool, i) => releaseCheck(tool, bundledPackages[i], lifetime.Token)));
            if (IsDisposed) return;
            ApplySnapshot(installations, states, published);
            releaseStatus.Text = $"公開版：{published.Count(r => r.Confirmed)} / {tools.Length}件を照合済み（開発版を含む・未署名）";
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                releaseStatus.Text = "公開版の確認失敗：" + ex.Message;
                ApplySnapshot(installations, states, bundledPackages.Select(p => new PublicRelease(p, false, false, releaseStatus.Text)).ToArray());
            }
        }
        finally { checkingReleases = false; if (!IsDisposed) UpdateFooter(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed) { resourcesDisposed = true; lifetime.Cancel(); timer.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
