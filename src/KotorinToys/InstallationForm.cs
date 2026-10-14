namespace KotorinToys;

internal sealed class InstallationForm : Form
{
    readonly ToolManager manager;
    readonly ToolDefinition[] tools;
    readonly CheckedListBox targets = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly DataGridView results = new() { Name = "installationResults", Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
    readonly Label completion = new() { Name = "installationSummary", Dock = DockStyle.Fill, AutoSize = true, Padding = new(8),
        Text = "操作するツールを選択してください。" };
    readonly FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new(8) };
    const string PublicSource = "GitHub Releasesの検証対象版を取得します。製品・版・容量・SHA-256を照合します。未署名の開発版です。設定・履歴は保持します。";
    readonly Label source = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new(8), Text = PublicSource };
    readonly Button cancel = new() { Name = "cancelRemaining", Text = "残りをキャンセル", AutoSize = true, Enabled = false };
    readonly Button close = new() { Name = "closeInstallation", Text = "閉じる", AutoSize = true };
    readonly TableLayoutPanel detail = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
    string? catalogPath;
    CancellationTokenSource? cancellation;
    bool running;

    public InstallationForm(ToolManager toolManager, string? initialTarget = null)
    {
        manager = toolManager; tools = manager.Load();
        Text = WindowTitle.Create("導入・削除"); Size = new(980, 600); MinimumSize = new(850, 520);
        Font = new("Yu Gothic UI", 11); StartPosition = FormStartPosition.CenterParent;
        foreach (var tool in tools) targets.Items.Add(tool.Name + "：" + manager.Installation(tool).State, tool.Id == initialTarget);
        results.Columns.Add("product", "ツール"); results.Columns.Add("state", "結果"); results.Columns.Add("detail", "理由・次の操作");
        results.Columns[0].FillWeight = 24; results.Columns[1].FillWeight = 23; results.Columns[2].FillWeight = 53;
        results.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        results.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        results.ColumnHeadersDefaultCellStyle.Padding = new(4, 8, 4, 8);
        var showLog = new CheckBox { Text = "詳細ログを表示", AutoSize = true, Dock = DockStyle.Fill };
        detail.RowStyles.Add(new(SizeType.Percent, 100)); detail.RowStyles.Add(new(SizeType.AutoSize)); detail.RowStyles.Add(new(SizeType.Absolute, 0));
        detail.Controls.Add(results, 0, 0); detail.Controls.Add(showLog, 0, 1); detail.Controls.Add(log, 0, 2);
        showLog.CheckedChanged += (_, _) => { detail.RowStyles[2].Height = showLog.Checked ? 150 : 0; };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        content.ColumnStyles.Add(new(SizeType.Absolute, 260)); content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.Controls.Add(targets, 0, 0); content.Controls.Add(detail, 1, 0);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(source, 0, 0); layout.Controls.Add(completion, 0, 1);
        layout.Controls.Add(content, 0, 2); layout.Controls.Add(buttons, 0, 3); Controls.Add(layout);
        var published = new Button { Text = "GitHub Releasesを使用", AutoSize = true };
        published.Click += (_, _) => { catalogPath = null; source.Text = PublicSource; };
        var choose = new Button { Text = "配布台帳を選択", AutoSize = true };
        choose.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "信頼できるローカルビルドのpackages.json", Filter = "配布台帳|packages.json", CheckFileExists = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            try { InstallerAdapter.Load(picker.FileName); catalogPath = picker.FileName; source.Text = "ローカル配布台帳：" + catalogPath + "\n未署名です。設定・履歴は保持します。"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, WindowTitle.Create("配布台帳の確認")); }
        };
        buttons.Controls.Add(published); buttons.Controls.Add(choose);
        Add("選択をインストール / 更新", "install"); Add("選択をアンインストール", "uninstall");
        cancel.Click += (_, _) => cancellation?.Cancel(); buttons.Controls.Add(cancel);
        close.Click += (_, _) => Close(); buttons.Controls.Add(close);
        FormClosing += (_, e) =>
        {
            if (!running) return;
            e.Cancel = true; cancellation?.Cancel();
            completion.Text = "キャンセル要求中：進行中の処理が終わるまでお待ちください。強制終了はしません。";
        };
    }
    void SetRunning(bool value)
    {
        running = value; targets.Enabled = !value;
        foreach (Control control in buttons.Controls) control.Enabled = !value;
        cancel.Enabled = value; close.Enabled = !value;
    }
    void AppendLog(string text) { if (!IsDisposed) log.AppendText(text + "\r\n"); }
    internal void ShowSummary(InstallationSummary summary)
    {
        results.Rows.Clear();
        foreach (var row in summary.Rows) results.Rows.Add(row.Name, row.State == "成功" ? "完了" : row.State, row.Detail);
        completion.Text = summary.Title + "\n" + summary.Detail;
        completion.ForeColor = summary.AllSucceeded ? Color.DarkGreen : Color.DarkRed;
    }
    void RefreshTargets()
    {
        for (int i = 0; i < tools.Length; i++)
        {
            var selected = targets.GetItemChecked(i);
            targets.Items[i] = tools[i].Name + "：" + manager.Installation(tools[i]).State;
            targets.SetItemChecked(i, selected);
        }
    }
    void Add(string label, string operation)
    {
        var button = new Button { Text = label, AutoSize = true }; buttons.Controls.Add(button);
        button.Click += async (_, _) => await Run(operation);
    }
    async Task Run(string operation)
    {
        if (running) return;
        var selected = targets.CheckedIndices.Cast<int>().Select(i => tools[i]).ToArray();
        if (selected.Length == 0) { completion.Text = "対象を選択してください。"; return; }
        var confirmation = string.Join("、", selected.Select(t => t.Name)) + "\n" +
            (operation == "install" ? "インストール / 更新" : "アンインストール") +
            "します。設定・履歴を保持し、正常終了の拒否時は中止します。\n" +
            (operation == "install" ? catalogPath == null ? "GitHubから検証対象版を取得し、SHA-256を照合した未署名の開発版を実行します。" : "未署名のローカルビルドを実行します。"
            : "対象の管理予約を整理します。外部予約は変更しません。");
        if (MessageBox.Show(this, confirmation, WindowTitle.Create("対象の確認"), MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        SetRunning(true); cancellation = new(); var current = cancellation;
        log.Clear(); results.Rows.Clear(); completion.ForeColor = SystemColors.ControlText;
        foreach (var tool in selected) results.Rows.Add(tool.Name, "待機中", "");
        completion.Text = "処理中：完了するまでお待ちください。";
        InstallResult[] final = []; string? interruption = null;
        try
        {
            var progress = new Progress<InstallResult>(result =>
            {
                if (IsDisposed || !running || cancellation != current) return;
                var index = Array.FindIndex(selected, tool => tool.Id == result.Id);
                if (index < 0) return;
                results.Rows[index].Cells[1].Value = result.State; results.Rows[index].Cells[2].Value = result.Detail;
                AppendLog(selected[index].Name + "：" + result.State + " / " + result.Detail);
            });
            var prepared = catalogPath;
            if (operation == "install" && prepared == null)
            {
                using var downloader = new ReleaseDownloader();
                prepared = await downloader.Prepare(ReleaseDownloader.Load(manager.Root), selected,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KotorinToys", "packages"),
                    new Progress<string>(message => { if (running && cancellation == current) { completion.Text = message; AppendLog(message); } }), current.Token);
            }
            final = await new InstallationQueue(new InstallerAdapter(manager)).Run(selected, operation, prepared, progress, current.Token);
        }
        catch (OperationCanceledException) { interruption = "取得または残りの処理をキャンセルしました。進行中のインストーラーは強制終了しません。"; }
        catch (Exception ex) { interruption = "操作を開始／完了できませんでした：" + ex.Message; }
        finally
        {
            SetRunning(false); ShowSummary(InstallationSummary.Create(selected, operation, final, interruption));
            if (interruption != null) AppendLog(interruption);
            cancellation = null; current.Dispose(); RefreshTargets();
        }
    }
}
