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
    bool exiting;
    public HostApplicationContext(string root, bool startup,string? settingsDirectory=null, bool hidden=false)
    {
        var settings=new HostSettingsStore(settingsDirectory??HostSettingsStore.DefaultDirectory);
        var source = new ToolCatalogSource(root, settings.DirectoryPath);
        var manager = new ToolManager(root, catalogSource: source);
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
        if (!startup) { if (!hidden) ShowDashboard(); }
        else
        {
            dispatcher.BeginInvoke(new Action(async()=>
            {
                try
                {
                    if(!hidden && settings.Load().ShowDashboardAtLogon)ShowDashboard();
                    await coordinator.StartViaTargets();
                    // 画面非表示のサインイン起動でも一覧を更新する。新規ツールを自動起動はしない。
                    if (!dashboard.Visible) await dashboard.RefreshAll();
                }
                catch(Exception ex){tray.BalloonTipText="自動起動を確認できません："+ex.Message;tray.ShowBalloonTip(5000);}
            }));
        }
    }
    public void ShowDashboard()
    {
        dashboard.Show();
        dashboard.WindowState = FormWindowState.Normal;
        dashboard.Activate();
    }
    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true;
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
