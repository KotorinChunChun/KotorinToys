using KotorinToys.Integration;

namespace KotorinToys;

internal sealed class HostApplicationContext : ApplicationContext
{
    readonly Control dispatcher = new();
    readonly ManagementEndpoint management;
    readonly NotifyIcon tray;
    readonly Icon icon = AppIcon.Create();
    readonly Dashboard dashboard;
    internal Dashboard Dashboard => dashboard;
    readonly CancellationTokenSource lifetime = new();
    readonly HostGuiUpdateCheck updates;
    readonly Task initialization;
    bool exiting;
    internal static ToolManager CreateManager(string root, string settingsDirectory, bool localCatalog = false) =>
        new(root, catalogSource: localCatalog ? null : new ToolCatalogSource(root, settingsDirectory));

    public HostApplicationContext(string root, bool startup,string? settingsDirectory=null, bool hidden=false, bool localCatalog=false,
        Func<CancellationToken, Task>? ensureTargets = null)
    {
        var settings=new HostSettingsStore(settingsDirectory??HostSettingsStore.DefaultDirectory);
        var manager = CreateManager(root, settings.DirectoryPath, localCatalog);
        var coordinator=new StartupCoordinator(settings,new TaskSchedulerGateway(),manager,Environment.ProcessPath!);
        var updateService = new HostSelfUpdateService(root, settings.DirectoryPath);
        dashboard = new Dashboard(root,settings,coordinator, toolManager: manager, selfUpdates: updateService,
            showSelfUpdater: () =>
            {
                updates!.ManualInteraction();
                using var form = new HostUpdateForm(updateService);
                form.ShowDialog(dashboard);
            });
        dashboard.FormClosing += (_, args) =>
        {
            if (!exiting && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; dashboard.Hide(); }
        };
        dashboard.Resize += (_, _) => HideMinimized(dashboard);
        var menu = new ContextMenuStrip();
        menu.Items.Add("管理画面を開く", null, (_, _) => ShowDashboard());
        menu.Items.Add("KotorinToysを終了", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Text = "KotorinToys", Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseDoubleClick += (_, _) => ShowDashboard();
        management = new("dashboard", dispatcher, ShowDashboard, ExitThread);
        updates = new(updateService, () => !exiting && !dashboard.IsDisposed && dashboard.Visible && !dashboard.OwnedForms.OfType<HostUpdateForm>().Any(),
            (release, channel) =>
            {
                using var form = new HostUpdateForm(updateService, initialRelease: release, initialChannel: channel);
                form.ShowDialog(dashboard);
            }, Notify, receipt => SelfUpdateFiles.Write(PathSafety.Resolve(settings.DirectoryPath, "updates/gui-check.json"), receipt));
        initialization = InitializeTargets();
        if (!startup) { if (!hidden) ShowDashboard(); }
        else if (!hidden && settings.Load().ShowDashboardAtLogon) ShowDashboard();

        async Task InitializeTargets()
        {
            await Task.Yield();
            try
            {
                await (ensureTargets?.Invoke(lifetime.Token) ?? coordinator.StartViaTargets(lifetime.Token));
                if (!exiting && !dashboard.Visible) await dashboard.RefreshAll();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { Notify("自動起動を確認できません：" + ex.Message); }
        }
    }
    void Notify(string message)
    {
        if (exiting) return;
        tray.BalloonTipText = message.Length > 255 ? message[..255] : message;
        tray.ShowBalloonTip(5000);
    }
    public void ShowDashboard()
    {
        PresentWindow(dashboard);
        dashboard.BeginInvoke(new Action(async () =>
        {
            await initialization;
            if (!exiting) await updates.Check(lifetime.Token);
        }));
    }

    internal static void PresentWindow(Form window)
    {
        window.Show();
        // 最小化時だけOSに復元を依頼し、最小化前の最大化状態も保つ。
        if (window.WindowState == FormWindowState.Minimized) ShowWindow(window.Handle, 9 /* SW_RESTORE */);
        window.BringToFront();
        window.Activate();
    }

    internal static void HideMinimized(Form window)
    {
        if (window.Visible && window.WindowState == FormWindowState.Minimized) window.Hide();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool ShowWindow(IntPtr window, int command);

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true;
        lifetime.Cancel();
        management.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        dashboard.Close();
        icon.Dispose();
        dispatcher.Dispose();
        base.ExitThreadCore();
    }
}
