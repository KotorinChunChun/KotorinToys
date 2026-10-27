namespace KotorinToys;

internal sealed partial class Dashboard
{
    void InitializeLayout(HostSettingsStore? settings, StartupCoordinator? startup)
    {
        Text = WindowTitle.Create(); Size = new(1180, 680); MinimumSize = new(980, 540);
        Font = new("Yu Gothic UI", 11); StartPosition = FormStartPosition.CenterScreen;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.ColumnHeadersDefaultCellStyle.Padding = new(4, 8, 4, 8);
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        foreach (var (name, label, weight) in new[] { ("tool", "ツール", 16), ("summary", "ひとこと解説", 24),
            ("category", "カテゴリ", 15), ("installed", "導入Ver", 9), ("update", "更新", 10), ("state", "現在の状態", 11), ("startup", "自動起動", 10), ("detail", "詳細", 12) })
        {
            var column = grid.Columns[grid.Columns.Add(name, label)]; column.FillWeight = weight;
            column.MinimumWidth = name == "summary" ? 150 : name == "tool" ? 125 : name == "startup" ? 85 : 70;
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
            if (name is "installed" or "published" or "update" or "detail") column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        }
        foreach (var tool in tools) AddToolRow(tool);
        InitializeView();
        grid.SelectionChanged += (_, _) => UpdateActions();
        grid.CurrentCellChanged += (_, _) => UpdateActions();
        grid.CellMouseClick += (_, e) => { if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex == grid.Columns["detail"]!.Index) OpenSelectedDetailAction(); };
        detailMenu.Items.Add("詳細を表示", null, (_, _) => OpenSelectedDetails());
        detailMenu.Opening += (_, e) => e.Cancel = busy || refreshingCatalog || SelectedIndex < 0;
        grid.ContextMenuStrip = detailMenu;
        grid.CellMouseDown += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) grid.CurrentCell = grid.Rows[e.RowIndex].Cells["detail"]; };
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && grid.CurrentCell?.OwningColumn?.Name == "detail")
            { e.Handled = e.SuppressKeyPress = true; OpenSelectedDetailAction(); }
        };
        grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || busy || e.ColumnIndex == grid.Columns["detail"]!.Index) return;
            grid.CurrentCell = grid.Rows[e.RowIndex].Cells["tool"]; await Execute(DashboardCommand.Start);
        };
        actions.Controls.Add(new Label { Text = "選択中アプリ：", AutoSize = true, Margin = new(3, 8, 8, 0) });
        hostActions.Controls.Add(new Label { Text = "本体全体：", AutoSize = true, Margin = new(3, 8, 8, 0) });
        foreach (var (label, command, name) in new[] { ("起動 / 設定を開く", DashboardCommand.Start, "start"),
            ("停止させる", DashboardCommand.Stop, "stop"), ("使い方を開く", DashboardCommand.Manual, "manual") })
        {
            var button = new Button { Text = label, AutoSize = true, Name = name };
            button.Click += async (_, _) => await Execute(command); actions.Controls.Add(button);
        }
        github.Click += (_, _) => OpenRepository(); actions.Controls.Add(github);
        var refresh = new Button { Text = "状態を更新", Name = "refresh", AutoSize = true };
        refresh.Click += async (_, _) => await RefreshAll(); hostActions.Controls.Add(refresh);
        if (settings != null && startup != null)
        {
            var button = new Button { Text = "本体設定・自動起動", Name = "hostSettings", AutoSize = true };
            button.Click += async (_, _) => await OpenStartupSettings(() =>
            { using var form = new StartupSettingsForm(settings, startup, manager.KnownTools()); form.ShowDialog(this); }); hostActions.Controls.Add(button);
        }
        var installation = new Button { Name = "advancedSettings", Text = "一括インストール", AutoSize = true };
        installation.Click += async (_, _) => await OpenInstallation(false); hostActions.Controls.Add(installation);
        var hostUpdate = new Button { Name = "hostUpdate", Text = "本体アップデート", AutoSize = true };
        hostUpdate.Click += (_, _) => OpenSelfUpdate(); hostActions.Controls.Add(hostUpdate);
        install.Click += async (_, _) => await OpenToolInstallation(install.Text == "修復" ? "repair" : "install"); actions.Controls.Add(install);
        update.Click += async (_, _) => await OpenToolInstallation("update"); actions.Controls.Add(update);
        uninstall.Click += async (_, _) => await OpenToolInstallation("uninstall"); actions.Controls.Add(uninstall);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(viewBar,0,0); layout.Controls.Add(grid, 0, 1); layout.Controls.Add(actions, 0, 2); layout.Controls.Add(hostActions, 0, 3); Controls.Add(layout);
        ActionHints.Attach(actions, tooltips); ActionHints.Attach(hostActions, tooltips);
        ActionHints.Attach(layout, tooltips);
        ApplySnapshot(installations, states, releases);
        Shown += async (_, _) => { timer.Start(); await RefreshAll(); };
        timer.Tick += async (_, _) => { if (!busy) await RefreshStates(); };
    }
}
