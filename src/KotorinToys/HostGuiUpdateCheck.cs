namespace KotorinToys;

internal sealed record HostGuiUpdateReceipt(DateTimeOffset CheckedAt, string CurrentVersion,
    HostUpdateChannel Channel, string? AvailableVersion, string Outcome);

// GUIを開く経路だけで確認する。非表示・終了・重複要求では更新を開始しない。
internal sealed class HostGuiUpdateCheck(IHostSelfUpdateService service, Func<bool> visible,
    Action<PublicRelease, HostUpdateChannel> show, Action<string> failed, Action<HostGuiUpdateReceipt>? record = null)
{
    bool checking;
    int manualGeneration;
    public void ManualInteraction() => manualGeneration++;
    readonly HashSet<string> shown = new(StringComparer.Ordinal);
    public async Task Check(CancellationToken cancellation)
    {
        if (checking || !visible() || service.BlockReason() != null) return;
        checking = true;
        int generation = manualGeneration;
        try
        {
            var channel = await service.DetectChannel(cancellation);
            if (channel is not (HostUpdateChannel.Preview or HostUpdateChannel.Stable)) { Save(channel, null, "種類不明"); return; }
            var release = await service.Check(channel, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (!visible() || generation != manualGeneration || service.BlockReason() != null) return;
            if (!release.Confirmed || release.Package == null) { Save(channel, null, "公開未確認"); failed(ReleaseProblems.Explanation(release)); return; }
            if (release.Prerelease != (channel == HostUpdateChannel.Preview)) throw new IOException("公開版の種類が一致しません。");
            if (!HostProduct.IsNewer(release.Package, service.CurrentVersion)) { Save(channel, release.Package.Package.Version, "新しい公開版なし"); return; }
            if (!shown.Add(release.Package.Package.Version)) return;
            Save(channel, release.Package.Package.Version, "更新を案内");
            show(release, channel);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (visible()) failed("本体の更新を確認できません：" + ex.Message); }
        finally { checking = false; }
    }
    void Save(HostUpdateChannel channel, string? version, string outcome)
    {
        try { record?.Invoke(new(DateTimeOffset.UtcNow, service.CurrentVersion, channel, version, outcome)); }
        catch (Exception ex) { failed("本体の更新確認記録を保存できません：" + ex.Message); }
    }
}
