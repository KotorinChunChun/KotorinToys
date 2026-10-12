using KotorinToys.Integration;

namespace KotorinToys;

internal sealed class HostApplicationContext : ApplicationContext
{
    readonly Control dispatcher = new();
    readonly ManagementEndpoint management;
    readonly NotifyIcon tray;
    readonly Dashboard dashboard;
    internal Dashboard Dashboard => dashboard;
    bool exiting;
    public HostApplicationContext(string root, bool startup,string? settingsDirectory=null)
    {
        var settings=new HostSettingsStore(settingsDirectory??HostSettingsStore.DefaultDirectory);
        var coordinator=new StartupCoordinator(settings,new TaskSchedulerGateway(),new ToolManager(root),Environment.ProcessPath!);
        dashboard = new Dashboard(root,settings,coordinator);
        dashboard.FormClosing += (_, args) =>
        {
            if (!exiting && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; dashboard.Hide(); }
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("管理画面を開く", null, (_, _) => ShowDashboard());
        menu.Items.Add("KotorinToysを終了", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Text = "KotorinToys", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.MouseDoubleClick += (_, _) => ShowDashboard();
        management = new("dashboard", dispatcher, ShowDashboard, ExitThread);
        if (!startup) ShowDashboard();
        else
        {
            dispatcher.BeginInvoke(new Action(async()=>
            {
                try
                {
                    if(settings.Load().ShowDashboardAtLogon)ShowDashboard();
                    await coordinator.StartViaTargets();
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
        dispatcher.Dispose();
        base.ExitThreadCore();
    }
}
