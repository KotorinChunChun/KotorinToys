namespace KotorinToys;

internal sealed record StartupOverview(string Text, string Detail, bool NeedsAttention = false)
{
    public static StartupOverview Create(StartupState managed, IReadOnlyList<ExternalStartupState> external)
    {
        var routes = new List<string>(); var notes = new List<string>();
        bool issue = managed.ActualState is not ("登録済み" or "なし");
        if (managed.ActualState == "登録済み")
        {
            if (managed.DesiredMode == "via_kotorintoys") routes.Add("本体経由");
            else if (managed.DesiredMode == "direct_task") routes.Add("OS（タスク）");
            else issue = true;
        }
        var desired = managed.DesiredMode == "via_kotorintoys" ? "本体経由" : managed.DesiredMode == "direct_task" ? "OS（タスク）" : managed.DesiredMode ?? "未確認";
        if (managed.DesiredMode != "none" || managed.ActualState != "なし")
            notes.Add("本体設定：" + desired + " / " + managed.ActualState + " / " + managed.Detail);
        foreach (var item in external)
        {
            if (item.State == "有効") routes.Add(item.Source == "外部タスク" ? "OS（タスク）" : "OS（スタートアップ）");
            if (item.State is not ("有効" or "無効" or "対象外")) issue = true;
            notes.Add($"{item.Source}：{item.Name}（{item.State}） / {item.Detail}");
        }
        var distinct = routes.Distinct().ToArray();
        var pending = managed.ActualState != "登録済み" && managed.DesiredMode is "via_kotorintoys" or "direct_task" ? desired + "（要確認）" : null;
        var text = distinct.Length > 0 ? string.Join(" / ", distinct) + (issue ? "（一部未確認）" : "") :
            pending ?? (issue ? "確認できない" : external.Any(e => e.State == "無効") ? "無効" : "なし");
        if (notes.Count == 0) notes.Add("本体の自動起動設定・ログオン／PC起動タスク・Run・スタートアップショートカットに有効な登録は見つかりませんでした。");
        notes.Add("登録の有効性の確認です。実際にサインイン後の起動が成功することを保証するものではありません。");
        return new(text, string.Join("\n", notes), issue);
    }
}

internal sealed class StartupOverviewReader(Func<ToolDefinition, Task<StartupState>> managed,
    Func<ToolDefinition, Task<ExternalStartupState[]>> external)
{
    public StartupOverviewReader(StartupCoordinator coordinator) : this(coordinator.State, coordinator.ExternalReservations) { }
    public async Task<StartupOverview> Read(ToolDefinition tool, CancellationToken cancellation)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var own = managed(tool); var outside = external(tool);
            await Task.WhenAll(own, outside).WaitAsync(cancellation);
            return StartupOverview.Create(await own, await outside);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new("確認できない", "自動起動の読取に失敗しました：" + ex.Message, true); }
    }
}
