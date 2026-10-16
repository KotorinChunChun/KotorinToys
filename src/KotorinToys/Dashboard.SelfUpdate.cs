namespace KotorinToys;

internal sealed partial class Dashboard
{
    internal void OpenSelfUpdate()
    {
        if (IsDisposed || !TryBeginOperation()) return;
        try { showSelfUpdater(); }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "本体アップデートを開けません：" + ex.Message; }
        finally { EndOperation(); }
    }

    void RefreshSelfUpdateResult()
    {
        var result = selfUpdates.LastResult();
        if (result == null) return;
        var startup = result.Restarted ? "本体の再起動確認済み" : "本体の起動確認前";
        selfUpdateStatus.Text = $"本体：{selfUpdates.CurrentVersion} / 前回の更新：{result.FromVersion} → {result.ToVersion} / {result.State} / {startup}\n{result.Detail}";
    }
}
