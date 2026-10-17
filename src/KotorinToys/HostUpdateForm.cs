namespace KotorinToys;

internal sealed class HostUpdateForm : BrandedForm
{
    readonly IHostSelfUpdateService service;
    readonly Label current = new() { Name = "hostCurrentVersion", AutoSize = true };
    readonly Label available = new() { Name = "hostAvailableVersion", AutoSize = true, Text = "公開版：未確認" };
    readonly Label status = new() { Name = "hostUpdateStatus", AutoSize = true, MaximumSize = new(710, 0) };
    readonly Label blocked = new() { Name = "hostUpdateBlockedReason", AutoSize = true, MaximumSize = new(710, 0), ForeColor = Color.DarkRed };
    readonly Label previousResult = new() { Name = "hostLastUpdate", AutoSize = true, MaximumSize = new(710, 0), Visible = false };
    readonly CheckBox preview = new() { Name = "hostIncludePrerelease", Text = "開発版（prerelease）も更新対象にする", AutoSize = true };
    readonly CheckBox consent = new() { Name = "hostUpdateConsent", Text = "未署名の配布物であることを確認し、本体の終了・更新・再起動に同意する", AutoSize = true };
    readonly Button refresh = new() { Name = "checkHostUpdate", Text = "公開版を再確認", AutoSize = true };
    readonly Button install = new() { Name = "installHostUpdate", Text = "ダウンロードして本体を更新", AutoSize = true, Enabled = false };
    readonly Button cancel = new() { Name = "cancelHostUpdate", Text = "取得・準備を取り消す", AutoSize = true, Enabled = false };
    readonly Button close = new() { Text = "閉じる", AutoSize = true };
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? active;
    PublicRelease? offered;
    bool running, transferred, closeRequested;

    public HostUpdateForm(IHostSelfUpdateService service)
    {
        this.service = service;
        Text = WindowTitle.Create("本体アップデート"); Size = new(800, 560); MinimumSize = new(740, 500);
        Font = new("Yu Gothic UI", 11); StartPosition = FormStartPosition.CenterParent;
        current.Text = "現在の本体：" + service.CurrentVersion;
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(14) };
        content.Controls.Add(current); content.Controls.Add(available); content.Controls.Add(preview);
        content.Controls.Add(new Label { AutoSize = true, MaximumSize = new(710, 0), Text =
            "KotorinChunChun/KotorinToysのGitHub Releasesから取得します。製品・版・容量・SHA-256を照合しますが、電子署名による発行元確認ではありません。\n" +
            "設定・履歴・自動起動予約は保持し、子ツールは停止しません。検証後に本体だけを終了・更新・再起動します。OSの自動再起動はしません。\n" +
            "取消できるのは取得・準備までです。更新開始後の強制中断は行いません。失敗・再起動待ちは結果として記録します。" });
        content.Controls.Add(consent); content.Controls.Add(status); content.Controls.Add(blocked); content.Controls.Add(previousResult);
        content.ClientSizeChanged += (_, _) =>
        {
            var width = Math.Max(100, content.ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            foreach (var label in content.Controls.OfType<Label>()) label.MaximumSize = new(width, 0);
        };
        if (service.LastResult() is { } last)
        {
            previousResult.Text = $"前回の更新：{last.FromVersion} → {last.ToVersion} / {last.State}\n{last.Detail}" +
                (last.Restarted ? "\n本体の再起動確認済み" : "\n本体の起動確認前");
            previousResult.Visible = true;
        }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new(10) };
        buttons.Controls.AddRange([refresh, install, cancel, close]); Controls.Add(content); Controls.Add(buttons);
        refresh.Click += async (_, _) => await RefreshRelease();
        preview.CheckedChanged += async (_, _) => await RefreshRelease();
        consent.CheckedChanged += (_, _) => UpdateButtons();
        install.Click += async (_, _) => await InstallUpdate();
        cancel.Click += (_, _) => CancelOperation();
        close.Click += (_, _) => Close();
        FormClosing += (_, args) =>
        {
            if (args.CloseReason != CloseReason.UserClosing || !running && !transferred) return;
            args.Cancel = true;
            if (!transferred) { closeRequested = true; active?.Cancel(); }
        };
        Shown += async (_, _) => await RefreshRelease();
        UpdateButtons();
    }

    internal void CancelOperation() => active?.Cancel();

    internal async Task RefreshRelease()
    {
        if (running || transferred || IsDisposed) return;
        offered = null; consent.Checked = false; running = true; UpdateButtons();
        status.Text = "本体のGitHub Releasesを確認中…";
        available.Text = "公開版：確認中";
        var includePreview = preview.Checked;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); active = cancellation;
        try
        {
            var result = await service.Check(includePreview, cancellation.Token);
            if (IsDisposed) return;
            if (includePreview != preview.Checked) { available.Text = "公開版：未確認"; status.Text = "対象が変更されました。公開版を再確認してください。"; return; }
            if (!result.Confirmed || result.Package == null)
            { available.Text = "公開版：未確認"; status.Text = result.Detail; return; }
            HostProduct.Validate(result.Package);
            if (result.Prerelease && !includePreview) throw new IOException("開発版を更新対象にしていません。");
            offered = result;
            available.Text = "公開版：" + ReleaseDisplay.Version(result.Package) + (result.Prerelease ? "（開発版）" : "（正式版）");
            status.Text = HostProduct.IsNewer(result.Package, service.CurrentVersion) ? "新しい公開版があります。" :
                Version.Parse(result.Package.Package.Version) == Version.Parse(service.CurrentVersion) ? "確認した公開版と同じ版です。更新は不要です。" : "公開版より新しい本体を使用しています。古い版へ戻しません。";
        }
        catch (OperationCanceledException) { if (!IsDisposed) { available.Text = "公開版：未確認"; status.Text = "公開確認を取り消しました。"; } }
        catch (Exception ex) { if (!IsDisposed) { offered = null; available.Text = "公開版：未確認"; status.Text = "本体の公開版を確認できません：" + ex.Message; } }
        finally { EndOperation(); }
    }

    internal async Task InstallUpdate()
    {
        if (running || transferred || IsDisposed) return;
        if (!CanInstall(service.BlockReason())) { UpdateButtons(); return; }
        var selected = offered!.Package!;
        running = true; UpdateButtons();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); active = cancellation;
        var acceptingProgress = true;
        try
        {
            status.Text = "本体の更新を準備しています…";
            await service.Handoff(selected, preview.Checked, new Progress<string>(text =>
            {
                if (acceptingProgress && !IsDisposed && running && ReferenceEquals(active, cancellation) && !cancellation.IsCancellationRequested) status.Text = text;
            }), cancellation.Token);
            acceptingProgress = false;
            if (IsDisposed) return;
            transferred = true;
            status.Text = "更新プロセスへ引き継ぎました。本体だけを終了・更新・再起動します。";
        }
        catch (OperationCanceledException) { acceptingProgress = false; if (!IsDisposed) { offered = null; status.Text = "取得・準備を取り消しました。本体は終了していません。"; } }
        catch (Exception ex) { acceptingProgress = false; if (!IsDisposed) { offered = null; status.Text = "更新を開始できません：" + ex.Message; } }
        finally { acceptingProgress = false; EndOperation(); }
    }

    bool CanInstall(string? reason) => consent.Checked && offered is { Confirmed: true, Package: not null } &&
        HostProduct.IsNewer(offered.Package, service.CurrentVersion) && reason == null;

    void UpdateButtons()
    {
        var reason = service.BlockReason();
        blocked.Text = reason ?? ""; blocked.Visible = reason != null;
        refresh.Enabled = preview.Enabled = consent.Enabled = !running && !transferred;
        install.Enabled = !running && !transferred && CanInstall(reason);
        cancel.Enabled = running && !transferred; close.Enabled = !transferred;
    }

    void EndOperation()
    {
        active = null; running = false;
        if (IsDisposed) return;
        UpdateButtons();
        if (closeRequested && !transferred) Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { lifetime.Cancel(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
