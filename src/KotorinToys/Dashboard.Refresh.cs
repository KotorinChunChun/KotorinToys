namespace KotorinToys;

internal sealed partial class Dashboard
{
    async Task RefreshStates(bool afterOperation = false)
    {
        if (refreshing || busy && !afterOperation || IsDisposed) return;
        refreshing = true;
        try
        {
            var execution = await Task.WhenAll(tools.Select(tool => manager.Status(tool, lifetime.Token)));
            if (IsDisposed || busy && !afterOperation) return;
            ApplySnapshot(tools.Select(detectInstallation).ToArray(), execution, releases);
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "状態取得失敗：" + ex.Message; }
        finally { refreshing = false; }
    }
    internal async Task RefreshAll()
    {
        if (busy || IsDisposed) return;
        await Task.WhenAll(RefreshStates(), RefreshReleases());
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
        checkingReleases = true; UpdateFooter(); releaseStatus.Text = "公開版：GitHubのリリースと配布台帳を確認中…";
        try
        {
            var published = await Task.WhenAll(tools.Select((tool, i) => releaseCheck(tool, bundledPackages[i], lifetime.Token)));
            if (IsDisposed) return;
            ApplySnapshot(installations, states, published);
            releaseStatus.Text = $"公開版：{published.Count(r => r.Confirmed)} / {tools.Length}件を照合済み（開発版を含む・未署名）";
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                releaseStatus.Text = "公開版の確認失敗：" + ex.Message;
                ApplySnapshot(installations, states, bundledPackages.Select(p => new PublicRelease(p, false, false, releaseStatus.Text)).ToArray());
            }
        }
        finally { checkingReleases = false; if (!IsDisposed) UpdateFooter(); }
    }
}
