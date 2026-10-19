namespace KotorinToys;

internal sealed partial class Dashboard
{
    Task RefreshStates(bool afterOperation = false)
    {
        if ((busy || refreshingCatalog) && !afterOperation || IsDisposed) return Task.CompletedTask;
        if (stateRefresh is { IsCompleted: false }) return stateRefresh;
        return stateRefresh = RefreshStatesCore(afterOperation);
    }

    async Task RefreshStatesCore(bool afterOperation)
    {
        var current = tools;
        try
        {
            var execution = await Task.WhenAll(current.Select(tool => executionCheck(tool, lifetime.Token)));
            if (IsDisposed || busy && !afterOperation || !ReferenceEquals(current, tools)) return;
            stateError = null;
            ApplySnapshot(current.Select(detectInstallation).ToArray(), execution, releases);
        }
        catch (Exception ex) { if (!IsDisposed) { stateError = "状態取得失敗：" + ex.Message; UpdateDetails(); } }
    }

    internal Task RefreshAll()
    {
        if (busy || IsDisposed) return Task.CompletedTask;
        if (allRefresh is { IsCompleted: false }) return allRefresh;
        return allRefresh = RefreshAllCore();
    }

    async Task RefreshAllCore()
    {
        refreshingCatalog = true;
        actions.Enabled = hostActions.Enabled = false;
        UpdateActions();
        try
        {
            // 旧一覧の非同期取得を完了させてから配列と行をまとめて切り替える。
            await Task.WhenAll(stateRefresh ?? Task.CompletedTask, releaseRefresh ?? Task.CompletedTask, startupRefresh ?? Task.CompletedTask);
            if (IsDisposed) return;
            if (manager.CatalogSource is { } source)
            {
                var result = await source.Refresh(lifetime.Token);
                if (IsDisposed) return;
                if (result.Accepted) ReplaceTools(result.Tools);
                catalogError = result.Accepted ? null : result.Detail;
                UpdateDetails();
            }
            await Task.WhenAll(RefreshStates(true), RefreshReleases(), RefreshStartups());
        }
        catch (Exception ex) { if (!IsDisposed) { catalogError = "一覧更新失敗・現在の一覧を維持：" + ex.Message; UpdateDetails(); } }
        finally
        {
            refreshingCatalog = false;
            if (!IsDisposed) { actions.Enabled = hostActions.Enabled = true; UpdateActions(); }
        }
    }

    void ReplaceTools(ToolDefinition[] next)
    {
        string? selected = grid.CurrentRow is { Index: var index } && index < tools.Length ? tools[index].Id : null;
        tools = next;
        installations = tools.Select(detectInstallation).ToArray();
        states = tools.Select(_ => new ToolState("確認中")).ToArray();
        startupSnapshots = tools.Select(_ => new StartupOverview("未確認", "自動起動は未確認です。")).ToArray();
        bundledPackages = tools.Select(tool => bundledCatalog?.Packages.SingleOrDefault(p => p.Package.Id == tool.Id &&
            p.Package.Name == tool.Name && p.Package.AppId == tool.AppId)).ToArray();
        releases = bundledPackages.Select(p => new PublicRelease(p, false, false, "公開未確認・同梱台帳")).ToArray();
        grid.Rows.Clear();
        foreach (var tool in tools) grid.Rows.Add(tool.Name, tool.Summary, "", "", "", "確認中", "未確認", "");
        if (tools.Length > 0)
        {
            var row = Array.FindIndex(tools, t => t.Id == selected);
            grid.CurrentCell = grid.Rows[Math.Max(0, row)].Cells["tool"];
        }
        ApplySnapshot(installations, states, releases);
    }

    static async Task<PublicRelease> CheckRelease(ToolDefinition tool, ReleasePackage? fallback, CancellationToken cancellation)
    {
        using var client = new PublicReleaseClient();
        return await client.Check(tool, fallback, cancellation);
    }

    internal Task RefreshReleases()
    {
        if (IsDisposed) return Task.CompletedTask;
        if (releaseRefresh is { IsCompleted: false }) return releaseRefresh;
        return releaseRefresh = RefreshReleasesCore();
    }

    async Task RefreshReleasesCore()
    {
        if (checkingReleases || IsDisposed) return;
        var current = tools;
        var fallback = bundledPackages;
        checkingReleases = true; UpdateActions();
        try
        {
            var published = await Task.WhenAll(current.Select((tool, i) => releaseCheck(tool, fallback[i], lifetime.Token)));
            if (IsDisposed || !ReferenceEquals(current, tools)) return;
            ApplySnapshot(installations, states, published);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && ReferenceEquals(current, tools))
            {
                var error = "公開版の確認失敗：" + ex.Message;
                ApplySnapshot(installations, states, fallback.Select(p => new PublicRelease(p, false, false, error)).ToArray());
            }
        }
        finally { checkingReleases = false; if (!IsDisposed) UpdateActions(); }
    }
}
