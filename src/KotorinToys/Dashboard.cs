namespace KotorinToys;

internal sealed partial class Dashboard : BrandedForm
{
    readonly ToolManager manager;
    ToolDefinition[] tools;
    readonly SingleRowWheelGrid grid = new() { Name = "toolTable", Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
    readonly Action<string> notify;
    readonly Action<string, string> showDetails;
    readonly ToolLocations locations;
    readonly ContextMenuStrip detailMenu = new();
    readonly Dictionary<string, string> operationErrors = new();
    string? catalogError, stateError;
    readonly Button install = new() { Name = "installSelected", Text = "インストール", AutoSize = true, Enabled = false };
    readonly Button update = new() { Name = "updateSelected", Text = "アップデート", AutoSize = true, Enabled = false };
    readonly Button updateAll = new() { Name = "updateAll", Text = "すべて更新", AutoSize = true, Enabled = false };
    readonly Label updateStatus = new() { Name = "toolUpdateStatus", AutoSize = true, Visible = false, Margin = new(12, 2, 12, 8) };
    readonly Dictionary<string, InstallResult> updateResults = new();
    readonly Button uninstall = new() { Name = "uninstallSelected", Text = "アンインストール", AutoSize = true, Enabled = false };
    readonly Button github = new() { Name = "github", Text = "GitHubを開く", AutoSize = true, Enabled = false };
    readonly ToolTip tooltips = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    readonly FlowLayoutPanel actions = new() { Name = "selectedActions", Dock = DockStyle.Fill, AutoSize = true, Padding = new(8) };
    readonly FlowLayoutPanel hostActions = new() { Name = "hostActions", Dock = DockStyle.Fill, AutoSize = true, Padding = new(8, 0, 8, 0) };
    readonly Func<ToolDefinition, ReleasePackage?, CancellationToken, Task<PublicRelease>> releaseCheck;
    readonly Action<string?, ReleaseCatalog?> showInstaller;
    readonly Action<string, string, ReleaseCatalog?> showToolInstaller;
    readonly Action<string[], ReleaseCatalog?> showBatchInstaller;
    readonly Func<IReadOnlyList<ToolDefinition>, ReleaseCatalog, IProgress<InstallResult>, CancellationToken, Task<InstallResult[]>> runUpdates;
    readonly Func<string, bool> confirmUpdates;
    readonly string? updateSettings;
    ToolUpdateReport? restoredUpdate;
    bool BackgroundUpdateBusy => updateSettings != null && ToolUpdateJob.IsRunning(updateSettings);
    readonly Func<ToolDefinition, InstallationState> detectInstallation;
    readonly Action<Uri> openRepository;
    readonly Func<string, bool> fileExists;
    readonly Func<ToolDefinition, CancellationToken, Task<ToolState>> executionCheck;
    readonly Func<ToolDefinition,bool,Task<ToolState>> controlExecution;
    readonly IHostSelfUpdateService selfUpdates;
    readonly Action showSelfUpdater;
    readonly Action? showStartupSettings;
    readonly Func<ToolDefinition, CancellationToken, Task<StartupOverview>> startupCheck;
    Task? startupRefresh;
    Task? releaseRefresh;
    Task? stateRefresh, allRefresh;
    readonly CancellationTokenSource lifetime = new();
    ReleasePackage?[] bundledPackages;
    readonly ReleaseCatalog? bundledCatalog;
    InstallationState[] installations;
    ToolState[] states;
    PublicRelease[] releases;
    StartupOverview[] startupSnapshots;
    bool busy, checkingReleases, refreshingCatalog, resourcesDisposed;
    bool UiClosed => resourcesDisposed || IsDisposed || Disposing;
    internal bool UpdatingTools => preparingToolUpdates || BackgroundUpdateBusy;
    bool preparingToolUpdates;

    public Dashboard(string root, HostSettingsStore? settings = null, StartupCoordinator? startup = null,
        Func<ToolDefinition, ReleasePackage?, CancellationToken, Task<PublicRelease>>? releaseCheck = null,
        Action<string?, ReleaseCatalog?>? showInstaller = null,
        Func<ToolDefinition, InstallationState>? detectInstallation = null,
        ToolManager? toolManager = null,
        Action<Uri>? openRepository = null, IHostSelfUpdateService? selfUpdates = null, Action? showSelfUpdater = null,
        Action<string, string, ReleaseCatalog?>? showToolInstaller = null, Action<string>? notify = null,
        Func<ToolDefinition, CancellationToken, Task<StartupOverview>>? startupCheck = null,
        Func<string, bool>? fileExists = null,
        Func<ToolDefinition, CancellationToken, Task<ToolState>>? executionCheck = null,
        Action<string, string>? showDetails = null, DashboardViewStore? viewStore = null,
        Func<ToolDefinition,bool,Task<ToolState>>? controlExecution = null, ToolLocations? locations = null,
        Action<string[], ReleaseCatalog?>? showBatchInstaller = null,
        Func<IReadOnlyList<ToolDefinition>, ReleaseCatalog, IProgress<InstallResult>, CancellationToken, Task<InstallResult[]>>? runUpdates = null,
        Func<string, bool>? confirmUpdates = null)
    {
        manager = toolManager ?? new(root); tools = manager.Load();
        this.viewStore = viewStore ?? (settings == null ? null : new DashboardViewStore(settings.DirectoryPath));
        this.locations = locations ?? new();
        this.showDetails = showDetails ?? ((name, explanation) => { using var form = new OperationDetailsForm(name, explanation,
            OpenInstalledLocation, InstalledLocationAvailability()); form.ShowDialog(this); });
        this.selfUpdates = selfUpdates ?? new HostSelfUpdateService(settings?.DirectoryPath ?? HostSettingsStore.DefaultDirectory);
        this.showSelfUpdater = showSelfUpdater ?? (() => { using var form = new HostUpdateForm(this.selfUpdates); form.ShowDialog(this); });
        if (settings != null && startup != null)
            showStartupSettings = () =>
            {
                using var form = new StartupSettingsForm(settings, startup, manager.KnownTools());
                form.ShowDialog(this);
            };
        this.notify = notify ?? (message => MessageBox.Show(this, message, WindowTitle.Create("操作できません"), MessageBoxButtons.OK, MessageBoxIcon.Warning));
        this.startupCheck = startupCheck ?? (startup != null ? new StartupOverviewReader(startup).Read :
            (_, _) => Task.FromResult(new StartupOverview("未確認", "本体設定付きの通常起動で自動起動を照合できます。")));
        this.releaseCheck = releaseCheck ?? CheckRelease;
        this.showInstaller = showInstaller ?? ((id, catalog) => { using var form = new InstallationForm(manager, id, catalog, publications: PublicationSnapshot()); form.ShowDialog(this); });
        this.showToolInstaller = showToolInstaller ?? ((id, operation, catalog) =>
        { using var form = new InstallationForm(manager, id, catalog, operation, publications: PublicationSnapshot(), startWithoutConfirmation: operation == "install"); form.ShowDialog(this); });
        this.showBatchInstaller = showBatchInstaller ?? ((ids, catalog) =>
        { using var form = new InstallationForm(manager, releases: catalog, operation: "install", publications: PublicationSnapshot(),
            initialTargets: ids, startWithoutConfirmation: true); form.ShowDialog(this); });
        updateSettings = runUpdates == null ? settings?.DirectoryPath ?? HostSettingsStore.DefaultDirectory : null;
        this.runUpdates = runUpdates ?? ((targets, catalog, _, token) =>
            ToolUpdateJob.Start(manager.Root, updateSettings!, targets, catalog, token));
        this.confirmUpdates = confirmUpdates ?? (message => MessageBox.Show(this, message, WindowTitle.Create("アップデートの確認"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        this.detectInstallation = detectInstallation ?? manager.Installation;
        this.fileExists = fileExists ?? File.Exists;
        this.executionCheck = executionCheck ?? manager.Status;
        this.controlExecution = controlExecution ?? ((tool,starting)=>starting?manager.Start(tool):manager.Stop(tool));
        this.openRepository = openRepository ?? (uri => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        installations = tools.Select(this.detectInstallation).ToArray();
        states = tools.Select(_ => new ToolState("確認中")).ToArray();
        startupSnapshots = tools.Select(_ => new StartupOverview("未確認", "自動起動は未確認です。")).ToArray();
        ReleaseCatalog? bundled = null;
        try { bundled = ReleaseDownloader.Load(root, manager.KnownTools()); } catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { catalogError = "公開台帳を読めません：" + ex.Message; }
        bundledCatalog = bundled;
        bundledPackages = tools.Select(tool => bundled?.Packages.SingleOrDefault(p => p.Package.Id == tool.Id)).ToArray();
        releases = bundledPackages.Select(package => new PublicRelease(package, false, false, "公開未確認・同梱台帳")).ToArray();
        InitializeLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed) { resourcesDisposed = true; lifetime.Cancel(); timer.Dispose(); tooltips.Dispose(); detailMenu.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
