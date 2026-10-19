namespace KotorinToys;

internal delegate Task<InstallResult[]> InstallationExecution(ToolDefinition[] targets, string operation,
    ReleaseCatalog? catalog, string? localCatalog, IProgress<InstallResult>? progress,
    IProgress<string>? downloadProgress, CancellationToken cancellation);

internal sealed class InstallationForm : BrandedForm
{
    readonly ToolManager manager;
    readonly ToolDefinition[] tools;
    readonly ReleaseCatalog? publicCatalog;
    readonly string? singleOperation;
    readonly Func<string, bool> confirm;
    readonly InstallationExecution execute;
    readonly Func<ToolDefinition, InstallationState> detectInstallation;
    readonly Func<ToolDefinition, Task<ToolState>> executionState;
    readonly Func<string, bool> fileExists;
    readonly Dictionary<string, ToolState> executionStates = new();
    readonly ToolTip hints = new();
    readonly CheckedListBox targets = new() { Name = "installationTargets", Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
    readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly DataGridView results = new() { Name = "installationResults", Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
    readonly Label completion = new() { Name = "installationSummary", Dock = DockStyle.Fill, AutoSize = true, Padding = new(8),
        Text = "操作するツールを選択してください。" };
    readonly FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new(8) };
    const string PublicSource = "GitHub Releasesの検証版をインストールします。";
    readonly Label source = new() { Name = "installationSource", Dock = DockStyle.Fill, AutoSize = true, Padding = new(8), Text = PublicSource };
    readonly Button cancel = new() { Name = "cancelRemaining", Text = "残りをキャンセル", AutoSize = true, Enabled = false };
    readonly Button close = new() { Name = "closeInstallation", Text = "閉じる", AutoSize = true };
    readonly Label media = new() { Name = "ffmpegState", AutoSize = true, Dock = DockStyle.Fill, Padding = new(8) };
    readonly TableLayoutPanel detail = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
    string? catalogPath;
    CancellationTokenSource? cancellation;
    bool running, singleStarted, checkingAvailability;
    Task? availabilityRefresh;

    public InstallationForm(ToolManager toolManager, string? initialTarget = null, ReleaseCatalog? releases = null,
        string? operation = null, Func<string, bool>? confirm = null, InstallationExecution? execute = null,
        Func<ToolDefinition, InstallationState>? detectInstallation = null,
        Func<ToolDefinition, Task<ToolState>>? executionState = null, Func<string, bool>? fileExists = null)
    {
        manager = toolManager; tools = manager.Load();
        this.detectInstallation = detectInstallation ?? manager.Installation;
        this.executionState = executionState ?? (tool => manager.Status(tool));
        this.fileExists = fileExists ?? File.Exists;
        foreach (var tool in tools) executionStates[tool.Id] = new("確認中");
        if (operation != null && (operation is not ("install" or "update" or "uninstall") || tools.All(t => t.Id != initialTarget)))
            throw new ArgumentException("単体操作の対象・種別が不正です。");
        singleOperation = operation;
        if (operation == "uninstall") source.Text = "選択したツールをアンインストールします。";
        this.confirm = confirm ?? (text => MessageBox.Show(this, text, WindowTitle.Create("対象の確認"), MessageBoxButtons.OKCancel) == DialogResult.OK);
        this.execute = execute ?? ((selected, verb, catalog, path, progress, download, token) =>
            new ToolInstallationRunner(manager).Run(selected, verb, catalog, path, progress, download, token));
        publicCatalog = releases;
        if (publicCatalog != null) ReleaseDownloader.Validate(publicCatalog, manager.KnownTools());
        Text = WindowTitle.Create(operation == null ? "詳細設定（開発者向け）" :
            operation == "install" ? "インストール" : operation == "update" ? "アップデート" : "アンインストール");
        Size = new(980, 600); MinimumSize = new(850, 520);
        Font = new("Yu Gothic UI", 11); StartPosition = FormStartPosition.CenterParent;
        foreach (var tool in tools) targets.Items.Add(TargetText(tool), tool.Id == initialTarget);
        if (operation != null) completion.Text = tools.Single(t => t.Id == initialTarget).Name + "：操作内容を確認してください。";
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
        content.ColumnStyles.Add(new(SizeType.Absolute, operation == null ? 300 : 0)); content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.Controls.Add(targets, 0, 0); content.Controls.Add(detail, 1, 0);
        targets.Visible = media.Visible = operation == null;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(source, 0, 0); layout.Controls.Add(completion, 0, 1);
        layout.Controls.Add(content, 0, 2); layout.Controls.Add(media, 0, 3); layout.Controls.Add(buttons, 0, 4); Controls.Add(layout);
        if (operation == null) AddAdvancedButtons();
        cancel.Text = operation == null ? "残りをキャンセル" : "キャンセル";
        cancel.Click += (_, _) => { cancellation?.Cancel(); ActionHints.Apply(cancel, ActionAvailability.Deny("キャンセル要求済みです。進行中の処理の終了を待っています。"), hints); }; buttons.Controls.Add(cancel);
        close.Click += (_, _) => Close(); buttons.Controls.Add(close);
        FormClosing += (_, e) =>
        {
            if (!running) return;
            e.Cancel = true; cancellation?.Cancel();
            completion.Text = "キャンセル要求中：進行中の処理が終わるまでお待ちください。強制終了はしません。";
        };
        targets.ItemCheck += (_, e) => UpdateAdvancedButtons(e.Index, e.NewValue == CheckState.Checked);
        Shown += async (_, _) => { if (operation != null) await RunSelected(); else await RefreshAvailability(); };
        ActionHints.Attach(buttons, hints);
        Disposed += (_, _) => hints.Dispose();
        RefreshMedia();
        SetRunning(false);
    }

    void AddAdvancedButtons()
    {
        source.Text = "開発者向け：一括操作・依存ツールの導入・ローカル配布物の検証を行います。\n" + PublicSource;
        var published = new Button { Name = "useGithubReleases", Text = "取得元をGitHub Releasesに戻す", AutoSize = true };
        published.Click += (_, _) => { catalogPath = null; source.Text = PublicSource; RefreshTargets(); };
        var choose = new Button { Name = "chooseLocalCatalog", Text = "ローカル配布台帳を選択", AutoSize = true };
        choose.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "信頼できるローカルビルドのpackages.json", Filter = "配布台帳|packages.json", CheckFileExists = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            try { InstallerAdapter.Load(picker.FileName, manager.KnownTools()); catalogPath = picker.FileName; source.Text = "ローカル配布台帳：" + catalogPath; RefreshTargets(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, WindowTitle.Create("配布台帳の確認")); }
        };
        buttons.Controls.Add(published); buttons.Controls.Add(choose);
        var select = new Button { Name = "selectInstallable", Text = "導入可能な全件を選択", AutoSize = true };
        select.Click += (_, _) =>
        {
            for (int i = 0; i < tools.Length; i++)
                targets.SetItemChecked(i, InstallAvailability(tools[i], "install").Enabled);
        };
        buttons.Controls.Add(select);
        Add("選択を一括インストール / 更新", "install"); Add("選択を一括アンインストール", "uninstall");
        var ffmpeg = new Button { Name = "installFfmpeg", Text = "FFmpegを導入 / 修復", AutoSize = true };
        ffmpeg.Click += async (_, _) => await InstallFfmpeg(); buttons.Controls.Add(ffmpeg);
    }
    void SetRunning(bool value)
    {
        running = value; targets.Enabled = !value;
        foreach (Control control in buttons.Controls) control.Enabled = !value;
        cancel.Enabled = value; close.Enabled = !value;
        cancel.AccessibleDescription = value ? "取得または未着手の処理をキャンセルします。実行中のインストーラーは強制終了しません。" : "処理中だけキャンセルできます。";
        close.AccessibleDescription = value ? "処理の完了後に閉じられます。" : "この画面を閉じます。";
        UpdateAdvancedButtons();
    }

    internal Task RefreshAvailability()
    {
        if (IsDisposed) return Task.CompletedTask;
        if (availabilityRefresh is { IsCompleted: false }) return availabilityRefresh;
        return availabilityRefresh = RefreshAvailabilityCore();
    }
    async Task RefreshAvailabilityCore()
    {
        checkingAvailability = true; UpdateAdvancedButtons();
        try
        {
            var states = await Task.WhenAll(tools.Select(async tool =>
            { try { return await executionState(tool); } catch (Exception ex) { return new ToolState("確認できない", Detail: ex.Message); } }));
            if (IsDisposed) return;
            for (int i = 0; i < tools.Length; i++) executionStates[tools[i].Id] = states[i];
        }
        finally { checkingAvailability = false; if (!IsDisposed) UpdateAdvancedButtons(); }
    }

    ActionAvailability InstallAvailability(ToolDefinition tool, string operation)
    {
        var installed = detectInstallation(tool); var state = executionStates[tool.Id];
        ReleasePackage? package = publicCatalog?.Packages.SingleOrDefault(p => p.Package.Id == tool.Id);
        if (catalogPath != null && operation is not ("uninstall" or "ffmpeg"))
        {
            try
            {
                var local = InstallerAdapter.Load(catalogPath, manager.KnownTools()).Packages.SingleOrDefault(p => p.Id == tool.Id);
                if (local == null || !fileExists(PathSafety.Resolve(Path.GetDirectoryName(catalogPath)!, local.File)))
                    return ActionAvailability.Deny("選択したローカル台帳に配布物がありません。");
                package = new(local with { Signature = "unsigned-public-build" }, 1);
            }
            catch (Exception ex) { return ActionAvailability.Deny("ローカル配布台帳を確認できません：" + ex.Message); }
        }
        if (operation == "install" && installed.State != "未導入") operation = "update";
        bool exe = installed.Executable != null && fileExists(installed.Executable);
        return ToolActionPolicy.Evaluate(operation, catalogPath != null ? tool with { DistributionStatus = "published" } : tool, installed, state,
            package == null ? null : new(package, true, true, "配布台帳照合済み"), exe,
            uninstallerExists: installed.Uninstaller != null && fileExists(installed.Uninstaller), busy: running || checkingAvailability);
    }

    void UpdateAdvancedButtons(int changing = -1, bool newChecked = false)
    {
        if (singleOperation != null) return;
        bool busy = running || checkingAvailability;
        var selected = tools.Where((_, i) => i == changing ? newChecked : targets.GetItemChecked(i)).ToArray();
        foreach (var (name, operation) in new[] { ("batchInstall", "install"), ("batchUninstall", "uninstall") })
        {
            if (buttons.Controls.Find(name, false).SingleOrDefault() is not Button button) continue;
            var decisions = selected.Select(t => (Tool: t, Rule: InstallAvailability(t, operation))).ToArray();
            var blocked = decisions.FirstOrDefault(x => !x.Rule.Enabled);
            ActionHints.Apply(button, busy ? ActionAvailability.Deny("処理中です。完了するまでお待ちください。") : selected.Length == 0 ?
                ActionAvailability.Deny("先に対象アプリへチェックを入れてください。") : blocked.Tool != null ?
                ActionAvailability.Deny(blocked.Tool.Name + "：" + blocked.Rule.Reason) : ActionAvailability.Allow("チェックしたアプリを順番に処理します。"), hints);
        }
        if (buttons.Controls.Find("selectInstallable", false).SingleOrDefault() is Button select)
            ActionHints.Apply(select, busy ? ActionAvailability.Deny("処理中です。") : tools.Any(t => InstallAvailability(t, "install").Enabled) ?
                ActionAvailability.Allow("今インストール・更新できるアプリだけを選択します。") : ActionAvailability.Deny("現在インストール・更新できるアプリがありません。"), hints);
        if (buttons.Controls.Find("installFfmpeg", false).SingleOrDefault() is Button ffmpeg)
        {
            var tool = tools.SingleOrDefault(t => t.Id == "ss-capture");
            ActionHints.Apply(ffmpeg, tool == null ? ActionAvailability.Deny("一覧にSSCaptureがありません。") : InstallAvailability(tool, "ffmpeg"), hints);
        }
        if (buttons.Controls.Find("useGithubReleases", false).SingleOrDefault() is Button github)
            ActionHints.Apply(github, busy ? ActionAvailability.Deny("処理中です。") : catalogPath == null ?
                ActionAvailability.Deny("すでにGitHub Releasesを使用しています。") : ActionAvailability.Allow("ローカル台帳の選択を解除します。インストールは開始しません。"), hints);
        if (buttons.Controls.Find("chooseLocalCatalog", false).SingleOrDefault() is Button choose)
            ActionHints.Apply(choose, busy ? ActionAvailability.Deny("処理中です。") : ActionAvailability.Allow("信頼できる開発用配布台帳を選択します。通常はGitHub Releasesを使用してください。"), hints);
    }
    void AppendLog(string text) { if (!IsDisposed) log.AppendText(text + "\r\n"); }
    internal void ShowSummary(InstallationSummary summary)
    {
        results.Rows.Clear();
        foreach (var row in summary.Rows) results.Rows.Add(row.Name, row.State == "成功" ? "完了" : row.State, row.Detail);
        completion.Text = summary.Title + (string.IsNullOrEmpty(summary.Detail) ? "" : "\n" + summary.Detail);
        completion.ForeColor = summary.AllSucceeded ? Color.DarkGreen : Color.DarkRed;
        AppendLog(summary.Title);
        foreach (var row in summary.Rows) AppendLog(row.Name + "：" + row.State + " / " + row.Detail);
    }
    void RefreshTargets()
    {
        for (int i = 0; i < tools.Length; i++)
        {
            var selected = targets.GetItemChecked(i);
            targets.Items[i] = TargetText(tools[i]);
            targets.SetItemChecked(i, selected);
        }
        RefreshMedia();
        UpdateAdvancedButtons();
    }
    string TargetText(ToolDefinition tool)
    {
        var item = publicCatalog?.Packages.SingleOrDefault(p => p.Package.Id == tool.Id);
        string? reason = catalogPath == null ? ToolInstallationRunner.PublicReason(tool, publicCatalog) : ToolInstallationRunner.UnsupportedReason(tool);
        return tool.Name + "：" + detectInstallation(tool).State + (catalogPath == null && item != null ? " → " + ReleaseDisplay.Version(item) : "") +
            (reason == null ? "" : " / " + reason);
    }
    void RefreshMedia()
    {
        var tool = tools.SingleOrDefault(t => t.Id == "ss-capture");
        media.Text = "SSCaptureの録画用 FFmpeg / FFprobe：" + (tool == null ? "対象ツールなし" : FfmpegInstaller.State(detectInstallation(tool)));
    }
    async Task InstallFfmpeg()
    {
        if (running) return;
        SetRunning(true);
        await RefreshAvailability();
        if (IsDisposed) return;
        SetRunning(false);
        var tool = tools.SingleOrDefault(t => t.Id == "ss-capture");
        if (tool == null) { completion.Text = "現在の一覧にSSCaptureがありません。"; return; }
        var allowed = InstallAvailability(tool, "ffmpeg");
        if (!allowed.Enabled) { completion.Text = allowed.Reason; return; }
        var installed = detectInstallation(tool);
        if (installed.State != "導入済み") { completion.Text = "FFmpegの前にSSCaptureを正式インストールしてください。"; return; }
        var prompt = $"FFmpeg / FFprobe {FfmpegInstaller.Version}（約110 MB、Gyan GPLv3ビルド）を配布元から直接取得します。\n" +
            $"配置先：{installed.Directory}\\tools\n既存の録画用ファイルはバックアップ付きで置き換えます。設定・履歴・PATHは変更しません。\n" +
            "容量・固定SHA-256を照合しますが、Authenticodeによる発行元確認ではありません。\nSSCaptureを本体の「停止させる」で停止してから実行してください。";
        if (MessageBox.Show(this, prompt, WindowTitle.Create("FFmpeg導入の確認"), MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        SetRunning(true); cancellation = new(); var current = cancellation; log.Clear();
        results.Rows.Clear(); results.Rows.Add("FFmpeg / FFprobe", "処理中", "固定配布物の取得・検証・配置を行います。");
        completion.ForeColor = SystemColors.ControlText; completion.Text = "FFmpeg導入の準備中…";
        InstallResult result;
        try
        {
            using var installer = new FfmpegInstaller();
            var text = await installer.Install(installed, () => manager.Status(tool),
                new Progress<string>(message => { if (running && cancellation == current) { completion.Text = message; AppendLog(message); } }), current.Token);
            result = new(tool.Id, "ffmpeg", "成功", text);
        }
        catch (OperationCanceledException) { result = new(tool.Id, "ffmpeg", current.IsCancellationRequested ? "未実行" : "失敗",
            current.IsCancellationRequested ? "取得・展開を取り消しました。既存ファイルは変更していません。" : "FFmpeg取得・展開が時間上限に達しました。既存ファイルは変更していません。"); }
        catch (Exception ex) { result = new(tool.Id, "ffmpeg", "失敗", ex.Message); }
        finally { SetRunning(false); cancellation = null; current.Dispose(); RefreshMedia(); }
        ShowSummary(InstallationSummary.Create([tool], "ffmpeg", [result]));
    }
    void Add(string label, string operation)
    {
        var button = new Button { Name = operation == "install" ? "batchInstall" : "batchUninstall", Text = label, AutoSize = true }; buttons.Controls.Add(button);
        button.Click += async (_, _) => await Run(operation);
    }
    internal Task RunSelected() => singleOperation == null ? Task.CompletedTask : Run(singleOperation == "uninstall" ? "uninstall" : "install");
    async Task Run(string operation)
    {
        if (running || IsDisposed || singleOperation != null && singleStarted) return;
        if (singleOperation != null) singleStarted = true;
        var selected = targets.CheckedIndices.Cast<int>().Select(i => tools[i]).ToArray();
        if (selected.Length == 0) { completion.Text = "対象を選択してください。"; return; }
        SetRunning(true);
        await RefreshAvailability();
        SetRunning(false);
        var denied = selected.Select(t => (Tool: t, Rule: InstallAvailability(t, singleOperation ?? operation))).FirstOrDefault(x => !x.Rule.Enabled);
        if (denied.Tool != null) { completion.Text = denied.Tool.Name + "：" + denied.Rule.Reason; return; }
        string confirmation;
        try { confirmation = ReleaseDisplay.Confirmation(selected, operation, detectInstallation, publicCatalog, catalogPath, manager.KnownTools()); }
        catch (Exception ex) { completion.Text = "操作内容を確認できません：" + ex.Message; return; }
        if (!confirm(confirmation)) { completion.Text = "操作をキャンセルしました。変更していません。"; return; }
        SetRunning(true); cancellation = new(); var current = cancellation;
        log.Clear(); results.Rows.Clear(); completion.ForeColor = SystemColors.ControlText;
        AppendLog(catalogPath == null ? "GitHub Releases：製品・版・容量・SHA-256を照合します。未署名の配布物です。" : "ローカル配布台帳：" + catalogPath + "（未署名）");
        AppendLog("設定・履歴を保持し、正常終了の拒否時は中止します。");
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
            final = await execute(selected, operation, publicCatalog, catalogPath, progress,
                new Progress<string>(message => { if (!IsDisposed && running && cancellation == current) { completion.Text = message; AppendLog(message); } }), current.Token);
        }
        catch (OperationCanceledException) { interruption = current.IsCancellationRequested
            ? "取得または残りの処理をキャンセルしました。進行中のインストーラーは強制終了しません。" : "取得処理が時間上限に達しました。インストーラーは実行していません。"; }
        catch (Exception ex) { interruption = "操作を開始／完了できませんでした：" + ex.Message; }
        finally
        {
            if (!IsDisposed)
            {
                SetRunning(false); ShowSummary(InstallationSummary.Create(selected, operation, final, interruption));
                if (interruption != null) AppendLog(interruption);
                RefreshTargets();
            }
            cancellation = null; current.Dispose();
        }
    }
}
