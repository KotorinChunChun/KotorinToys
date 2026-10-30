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
        dashboard = new Dashboard(root,settings,coordinator, toolManager: manager);
        dashboard.FormClosing += (_, args) =>
        {
            if (!exiting && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; dashboard.Hide(); }
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("管理画面を開く", null, (_, _) => ShowDashboard());
        menu.Items.Add("KotorinToysを終了", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Text = "KotorinToys", Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseDoubleClick += (_, _) => ShowDashboard();
        management = new("dashboard", dispatcher, ShowDashboard, ExitThread);
        var updateService = new HostSelfUpdateService(root, settings.DirectoryPath);
        updates = new(updateService, () => !exiting && !dashboard.IsDisposed && dashboard.Visible && !dashboard.OwnedForms.OfType<HostUpdateForm>().Any(),
            (release, channel) =>
            {
                using var form = new HostUpdateForm(updateService, initialRelease: release, initialChannel: channel);
                form.ShowDialog(dashboard);
            }, Notify);
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
        dashboard.Show();
        dashboard.WindowState = FormWindowState.Normal;
        dashboard.Activate();
        dashboard.BeginInvoke(new Action(async () =>
        {
            await initialization;
            if (!exiting) await updates.Check(lifetime.Token);
        }));
    }
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
