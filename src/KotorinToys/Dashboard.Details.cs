namespace KotorinToys;

internal sealed partial class Dashboard
{
    internal void OpenSelectedDetailAction()
    {
        if (busy || refreshingCatalog || IsDisposed) return;
        var index = SelectedIndex;
        if (index < 0) return;
        if (RowFor(index).Cells["detail"] is not DataGridViewButtonCell) { OpenSelectedDetails(); return; }
        try { locations.OpenRunning(states[index]); }
        catch (Exception error) when (ToolLocations.LocationError(error)) { notify("配置場所を開けません：" + error.Message); }
    }

    ActionAvailability InstalledLocationAvailability() => SelectedIndex is var index && index >= 0 && !busy && !refreshingCatalog
        ? locations.Installed(installations[index]) : ActionAvailability.Deny("対象を確認できません。");

    internal void OpenInstalledLocation()
    {
        if (busy || refreshingCatalog || IsDisposed) return;
        var index = SelectedIndex;
        if (index < 0) return;
        try { locations.OpenInstalled(detectInstallation(tools[index])); }
        catch (Exception error) when (ToolLocations.LocationError(error)) { notify("正式導入先を開けません：" + error.Message); }
    }

    internal void OpenSelectedDetails()
    {
        if (busy || refreshingCatalog || IsDisposed) return;
        var index = SelectedIndex;
        if (index < 0 || index >= tools.Length) return;
        var tool = tools[index]; var row = RowFor(index);
        var reasons = new[] { "start", "stop", installations[index].State=="修復が必要"?"repair":"install", "update", "uninstall" }.Select(operation =>
        {
            var rule = Availability(index, operation);
            var label = operation switch { "start" => "起動", "stop" => "停止", "install" => "導入", "repair" => "修復", "update" => "更新", _ => "削除" };
            return rule.Enabled ? null : label + "不可：" + rule.Reason;
        });
        var explanation = string.Join("\r\n\r\n", new[] { tool.Name + "：" + states[index].State,
            "導入Ver：" + row.Cells["installed"].Value + " / 公開情報：" + row.Cells["update"].ToolTipText,
            row.Cells["detail"].ToolTipText, "自動起動：" + startupSnapshots[index].Text + "\r\n" + startupSnapshots[index].Detail,
            string.Join("\r\n", reasons.Where(text => text != null)) }.Where(text => !string.IsNullOrWhiteSpace(text)));
        showDetails(tool.Name, explanation);
    }
}
