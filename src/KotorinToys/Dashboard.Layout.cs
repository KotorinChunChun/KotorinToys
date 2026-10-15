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
            grid.CurrentCell = grid.Rows[e.RowIndex].Cells["tool"]; await Execute(DashboardCommand.Start);
        };
        actions.Controls.Add(new Label { Text = "選択中アプリ：", AutoSize = true, Margin = new(3, 8, 8, 0) });
        hostActions.Controls.Add(new Label { Text = "本体全体：", AutoSize = true, Margin = new(3, 8, 8, 0) });
        foreach (var (label, command, name) in new[] { ("起動設定を開く", DashboardCommand.Start, "start"),
            ("停止させる", DashboardCommand.Stop, "stop"), ("使い方", DashboardCommand.Manual, "manual") })
        {
            var button = new Button { Text = label, AutoSize = true, Name = name };
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
}
