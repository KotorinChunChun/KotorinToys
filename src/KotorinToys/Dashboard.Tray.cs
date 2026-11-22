namespace KotorinToys;

internal sealed partial class Dashboard
{
    internal void StartMonitoring() => timer.Start();

    internal ContextMenuStrip CreateTrayMenu(Action showDashboard, Action exitHost)
    {
        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        menu.Items.Add("管理画面を開く", null, (_, _) => showDashboard());
        var openApps = new ToolStripMenuItem("アプリを開く") { Name = "openApps" };
        openApps.DropDown.ShowItemToolTips = true;
        menu.Items.Add(openApps);

        var refresh = new ToolStripMenuItem("状態を更新", null, async (_, _) =>
        {
            if (!CanOperate) return;
            await RefreshAll();
            if (!UiClosed && !Visible) notify(catalogError ?? stateError ?? "一覧とアプリの状態を確認しました。");
        }) { Name = "refresh" };
        var updateApps = new ToolStripMenuItem("すべて更新", null, async (_, _) =>
            await UpdateTools(all: true, notifyWhenHidden: true)) { Name = "updateAll" };
        menu.Items.Add(refresh);
        menu.Items.Add(updateApps);
        menu.Items.Add(new ToolStripSeparator());
        var startup = showStartupSettings == null ? null : new ToolStripMenuItem("本体設定・自動起動", null,
            async (_, _) => await OpenStartupSettings(showStartupSettings)) { Name = "startupSettings" };
        if (startup != null) menu.Items.Add(startup);
        var hostUpdate = new ToolStripMenuItem("本体アップデート", null, (_, _) => OpenSelfUpdate()) { Name = "hostUpdate" };
        menu.Items.Add(hostUpdate);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("KotorinToysを終了", null, (_, _) => exitHost());

        void UpdateItems()
        {
            bool available = CanOperate;
            var hostAction = available ? ActionAvailability.Allow("") :
                ActionAvailability.Deny("処理中です。完了するまでお待ちください。");
            Apply(refresh, hostAction);
            Apply(hostUpdate, hostAction);
            if (startup != null) Apply(startup, hostAction);
            Apply(updateApps, available ? UpdateAvailability([], all: true) : hostAction);
            Apply(openApps, hostAction);

            while (openApps.DropDownItems.Count > 0)
            {
                var previous = openApps.DropDownItems[0];
                openApps.DropDownItems.RemoveAt(0);
                previous.Dispose();
            }
            if (!available) return;

            foreach (int index in Enumerable.Range(0, tools.Length).OrderBy(i => tools[i].Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var tool = tools[index];
                var action = Availability(index, "start");
                if (!action.Enabled && installations[index].State == "未導入" && states[index].State != "実行中") continue;
                var item = new ToolStripMenuItem(tool.Name, null, async (_, _) => await Execute(DashboardCommand.Start, tool.Id))
                { Name = tool.Id };
                Apply(item, action);
                openApps.DropDownItems.Add(item);
            }
            if (openApps.DropDownItems.Count == 0)
                openApps.DropDownItems.Add(new ToolStripMenuItem("開けるアプリがありません") { Enabled = false });
        }

        static void Apply(ToolStripMenuItem item, ActionAvailability action)
        {
            item.Enabled = action.Enabled;
            item.ToolTipText = item.AccessibleDescription = action.Reason;
        }

        UpdateItems();
        menu.Opening += (_, _) => UpdateItems();
        return menu;
    }
}
