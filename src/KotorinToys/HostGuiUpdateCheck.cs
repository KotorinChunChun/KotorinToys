namespace KotorinToys;

// GUIを開く経路だけで確認する。非表示・終了・重複要求では更新を開始しない。
internal sealed class HostGuiUpdateCheck(IHostSelfUpdateService service, Func<bool> visible,
    Action<PublicRelease, HostUpdateChannel> show, Action<string> failed)
{
    bool checking;
    readonly HashSet<string> shown = new(StringComparer.Ordinal);
    public async Task Check(CancellationToken cancellation)
    {
        if (checking || !visible() || service.BlockReason() != null) return;
        checking = true;
        try
        {
            var channel = await service.DetectChannel(cancellation);
            if (channel is not (HostUpdateChannel.Preview or HostUpdateChannel.Stable)) return;
            var release = await service.Check(channel, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (!visible() || service.BlockReason() != null) return;
            if (!release.Confirmed || release.Package == null) { failed(ReleaseProblems.Explanation(release)); return; }
            if (release.Prerelease != (channel == HostUpdateChannel.Preview)) throw new IOException("公開版の種類が一致しません。");
            if (!HostProduct.IsNewer(release.Package, service.CurrentVersion) || !shown.Add(release.Package.Package.Version)) return;
            show(release, channel);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (visible()) failed("本体の更新を確認できません：" + ex.Message); }
        finally { checking = false; }
    }
}
