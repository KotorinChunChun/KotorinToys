namespace KotorinToys;

internal sealed partial class Dashboard
{
    internal Task RefreshStartups()
    {
        if (IsDisposed) return Task.CompletedTask;
        if (startupRefresh is { IsCompleted: false }) return startupRefresh;
        return startupRefresh = RefreshStartupsCore();
    }

    async Task RefreshStartupsCore()
    {
        var current = tools;
        startupSnapshots = current.Select(_ => new StartupOverview("確認中", "自動起動を確認しています。")).ToArray();
        UpdateDetails();
        try
        {
            async Task<StartupOverview> Read(ToolDefinition tool)
            {
                try { return await startupCheck(tool, lifetime.Token); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
                catch (Exception ex) { return new("確認できない", tool.Name + "の自動起動を読み取れません：" + ex.Message, true); }
            }
            var snapshots = await Task.WhenAll(current.Select(Read));
            if (IsDisposed || !ReferenceEquals(current, tools)) return;
            startupSnapshots = snapshots; UpdateDetails();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    internal async Task OpenStartupSettings(Action showSettings)
    {
        if (!TryBeginOperation()) return;
        try { showSettings(); if (!IsDisposed) await RefreshStartups(); }
        catch (Exception ex) { ShowOperationFailure("自動起動設定を確認できません：" + ex.Message); }
        finally { EndOperation(); }
    }
}
