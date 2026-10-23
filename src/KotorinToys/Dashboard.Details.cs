namespace KotorinToys;

internal sealed partial class Dashboard
{
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
