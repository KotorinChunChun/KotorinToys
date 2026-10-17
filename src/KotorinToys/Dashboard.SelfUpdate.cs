namespace KotorinToys;

internal sealed partial class Dashboard
{
    internal void OpenSelfUpdate()
    {
        if (IsDisposed || !TryBeginOperation()) return;
        try { showSelfUpdater(); }
        catch (Exception ex) { ShowOperationFailure("本体アップデートを開けません：" + ex.Message); }
        finally { EndOperation(); }
    }
}
