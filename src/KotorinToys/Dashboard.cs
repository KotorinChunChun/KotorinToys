namespace KotorinToys;

internal sealed partial class Dashboard : BrandedForm
{
    readonly ToolManager manager;
    ToolDefinition[] tools;
    readonly DataGridView grid = new() { Name = "toolTable", Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
    readonly Label overall = new() { Name = "overallStatus", AutoSize = true, Dock = DockStyle.Fill };
    readonly Label selection = new() { Name = "selectedStatus", AutoSize = true, Dock = DockStyle.Fill };
    readonly Label operation = new() { Name = "lastOperation", AutoSize = true, Dock = DockStyle.Fill,
        Text = "本体を閉じても各ツールは動作します。" };
    readonly Label releaseStatus = new() { AutoSize = true, Dock = DockStyle.Fill, Text = "公開版：未確認（同梱台帳を表示）" };
    readonly Label catalogStatus = new() { Name = "catalogStatus", AutoSize = true, Dock = DockStyle.Fill };
    readonly Label selfUpdateStatus = new() { Name = "selfUpdateStatus", AutoSize = true, Dock = DockStyle.Fill };
    readonly Button install = new() { Name = "installSelected", Text = "インストール", AutoSize = true, Enabled = false };
    readonly Button update = new() { Name = "updateSelected", Text = "アップデート", AutoSize = true, Enabled = false };
    readonly Button uninstall = new() { Name = "uninstallSelected", Text = "アンインストール", AutoSize = true, Enabled = false };
    readonly Button github = new() { Name = "github", Text = "GitHubを開く", AutoSize = true, Enabled = false };
    readonly ToolTip tooltips = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    readonly FlowLayoutPanel actions = new() { Name = "selectedActions", Dock = DockStyle.Fill, AutoSize = true, Padding = new(8) };
    readonly FlowLayoutPanel hostActions = new() { Name = "hostActions", Dock = DockStyle.Fill, AutoSize = true, Padding = new(8, 0, 8, 0) };
    readonly Func<ToolDefinition, ReleasePackage?, CancellationToken, Task<PublicRelease>> releaseCheck;
    readonly Action<string?, ReleaseCatalog?> showInstaller;
    readonly Action<string, string, ReleaseCatalog?> showToolInstaller;
    readonly Func<ToolDefinition, InstallationState> detectInstallation;
    readonly Action<Uri> openRepository;
    readonly IHostSelfUpdateService selfUpdates;
    readonly Action showSelfUpdater;
    Task? releaseRefresh;
    Task? stateRefresh, allRefresh;
    readonly CancellationTokenSource lifetime = new();
    ReleasePackage?[] bundledPackages;
    readonly ReleaseCatalog? bundledCatalog;
    InstallationState[] installations;
    ToolState[] states;
    PublicRelease[] releases;
    bool busy, checkingReleases, refreshingCatalog, resourcesDisposed;

    public Dashboard(string root, HostSettingsStore? settings = null, StartupCoordinator? startup = null,
        Func<ToolDefinition, ReleasePackage?, CancellationToken, Task<PublicRelease>>? releaseCheck = null,
        Action<string?, ReleaseCatalog?>? showInstaller = null,
        Func<ToolDefinition, InstallationState>? detectInstallation = null,
        ToolManager? toolManager = null,
        Action<Uri>? openRepository = null, IHostSelfUpdateService? selfUpdates = null, Action? showSelfUpdater = null,
        Action<string, string, ReleaseCatalog?>? showToolInstaller = null)
    {
        manager = toolManager ?? new(root); tools = manager.Load();
        this.selfUpdates = selfUpdates ?? new HostSelfUpdateService(root, settings?.DirectoryPath ?? HostSettingsStore.DefaultDirectory);
        this.showSelfUpdater = showSelfUpdater ?? (() => { using var form = new HostUpdateForm(this.selfUpdates); form.ShowDialog(this); });
        selfUpdateStatus.Text = "本体：" + this.selfUpdates.CurrentVersion + " / 本体アップデートから公開版を確認できます。";
        RefreshSelfUpdateResult();
        this.releaseCheck = releaseCheck ?? CheckRelease;
        this.showInstaller = showInstaller ?? ((id, catalog) => { using var form = new InstallationForm(manager, id, catalog); form.ShowDialog(this); });
        this.showToolInstaller = showToolInstaller ?? ((id, operation, catalog) =>
        { using var form = new InstallationForm(manager, id, catalog, operation); form.ShowDialog(this); });
        this.detectInstallation = detectInstallation ?? manager.Installation;
        this.openRepository = openRepository ?? (uri => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        installations = tools.Select(this.detectInstallation).ToArray();
        states = tools.Select(_ => new ToolState("確認中")).ToArray();
        ReleaseCatalog? bundled = null;
        try { bundled = ReleaseDownloader.Load(root); } catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { releaseStatus.Text = "公開台帳を読めません：" + ex.Message; }
        bundledCatalog = bundled;
        catalogStatus.Text = manager.CatalogSource?.Detail ?? "ツール一覧：ローカル登録";
        bundledPackages = tools.Select(tool => bundled?.Packages.SingleOrDefault(p => p.Package.Id == tool.Id)).ToArray();
        releases = bundledPackages.Select(package => new PublicRelease(package, false, false, "公開未確認・同梱台帳")).ToArray();
        InitializeLayout(settings, startup);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed) { resourcesDisposed = true; lifetime.Cancel(); timer.Dispose(); tooltips.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
