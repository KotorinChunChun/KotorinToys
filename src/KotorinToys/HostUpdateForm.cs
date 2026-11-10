namespace KotorinToys;

internal sealed class HostUpdateForm : BrandedForm
{
    readonly IHostSelfUpdateService service;
    readonly Func<string, bool> confirmSwitch;
    readonly Label current = new() { Name = "hostCurrentVersion", AutoSize = true };
    readonly Label available = new() { Name = "hostAvailableVersion", AutoSize = true, Text = "公開バージョン：確認中" };
    readonly Label status = new() { Name = "hostUpdateStatus", AutoSize = true };
    readonly Label blocked = new() { Name = "hostUpdateBlockedReason", AutoSize = true, ForeColor = Color.DarkRed };
    readonly Button stable = new() { Name = "switchToStable", Text = "通常版に切り替える", AutoSize = true };
    readonly Button preview = new() { Name = "switchToPreview", Text = "開発版に切り替える", AutoSize = true };
    readonly Button log = new() { Name = "openHostUpdateLog", Text = "ログを開く", AutoSize = true };
    readonly Button install = new() { Name = "installHostUpdate", Text = "アップデート", AutoSize = true };
    readonly Button close = new() { Name = "closeHostUpdate", Text = "閉じる", AutoSize = true };
    readonly CancellationTokenSource lifetime = new();
    readonly ToolTip hints = new();
    PublicRelease? offered;
    HostUpdateChannel currentChannel = HostUpdateChannel.Unknown, targetChannel = HostUpdateChannel.Unknown;
    bool checking, installing, transferred, disposed;
    bool Busy => checking || installing || transferred;

    public HostUpdateForm(IHostSelfUpdateService service, Func<string, bool>? confirmSwitch = null,
        PublicRelease? initialRelease = null, HostUpdateChannel initialChannel = HostUpdateChannel.Unknown)
    {
        this.service = service;
        this.confirmSwitch = confirmSwitch ?? (message => MessageBox.Show(this, message, WindowTitle.Create("版の切り替え"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes);
        Text = WindowTitle.Create("本体アップデート"); Size = new(600, 360); MinimumSize = new(500, 320);
        Font = new("Yu Gothic UI", 11); StartPosition = FormStartPosition.CenterParent;
        current.Text = "現バージョン：" + service.CurrentVersion;

        var selection = new FlowLayoutPanel { AutoSize = true, Margin = new(0, 10, 0, 8) };
        selection.Controls.AddRange([stable, preview]);
        var logRow = new FlowLayoutPanel { AutoSize = true };
        logRow.Controls.AddRange([new Label { Text = "更新ログ：", AutoSize = true, Margin = new(3, 8, 3, 0) }, log]);
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(14) };
        content.Controls.AddRange([current, available, status, selection, logRow, blocked]);
        content.ClientSizeChanged += (_, _) =>
        {
            var width = Math.Max(100, content.ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            foreach (var label in content.Controls.OfType<Label>()) label.MaximumSize = new(width, 0);
            selection.MaximumSize = new(width, 0);
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new(10), FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([close, install]); Controls.Add(content); Controls.Add(buttons);
        ActionHints.Attach(buttons, hints); ActionHints.Attach(selection, hints);
        stable.Click += async (_, _) => await SwitchTo(HostUpdateChannel.Stable);
        preview.Click += async (_, _) => await SwitchTo(HostUpdateChannel.Preview);
        install.Click += async (_, _) => await InstallUpdate();
        log.Click += (_, _) => OpenLog();
        close.Click += (_, _) => Close();
        FormClosing += (_, args) =>
        {
            if (args.CloseReason == CloseReason.UserClosing && (installing || transferred)) args.Cancel = true;
        };

        if (initialRelease != null)
        {
            if (!initialRelease.Confirmed || initialRelease.Package == null ||
                initialChannel is not (HostUpdateChannel.Stable or HostUpdateChannel.Preview) ||
                initialRelease.Prerelease != (initialChannel == HostUpdateChannel.Preview) ||
                !HostProduct.IsNewer(initialRelease.Package, service.CurrentVersion))
                throw new IOException("自動更新の公開版が不正です。");
            currentChannel = targetChannel = initialChannel;
            ShowCurrentVersion();
            ShowOffer(initialRelease);
        }
        Shown += async (_, _) => { if (initialRelease == null) await RefreshRelease(); else await InstallUpdate(); };
        UpdateButtons();
    }

    static string ChannelName(HostUpdateChannel value) => value switch { HostUpdateChannel.Stable => "通常版", HostUpdateChannel.Preview => "開発版", _ => "種類不明" };
    void ShowCurrentVersion() => current.Text = $"現バージョン：{service.CurrentVersion}（{ChannelName(currentChannel)}）";

    void ShowOffer(PublicRelease result)
    {
        offered = result;
        available.Text = "公開バージョン：" + ReleaseDisplay.Version(result.Package!) + "（" + ChannelName(targetChannel) + "）";
        status.Text = targetChannel != currentChannel ? "切り替えできます" :
            HostProduct.IsNewer(result.Package!, service.CurrentVersion) ? "新しい更新があります" :
            Version.Parse(result.Package!.Package.Version) == Version.Parse(service.CurrentVersion) ? "最新なので更新は不要です" :
            "現在より新しい公開版はありません";
    }

    internal async Task SwitchTo(HostUpdateChannel target)
    {
        if (Busy || disposed || target == currentChannel || service.BlockReason() != null) return;
        if (target is not (HostUpdateChannel.Stable or HostUpdateChannel.Preview)) return;
        var originalChannel = currentChannel;
        var originalOffer = targetChannel == currentChannel ? offered : null;
        await RefreshRelease(target);
        if (disposed) return;
        if (offered != null && targetChannel == target && await InstallOffer(true)) return;
        if (disposed) return;
        if (originalOffer != null && currentChannel == originalChannel)
        {
            var error = offered == null ? status.Text : null;
            targetChannel = currentChannel;
            ShowOffer(originalOffer);
            if (error != null) status.Text = error;
            UpdateButtons();
        }
        else if (offered != null) await RefreshRelease();
    }

    internal Task RefreshRelease() => RefreshRelease(null);

    async Task RefreshRelease(HostUpdateChannel? requestedChannel)
    {
        if (Busy || disposed) return;
        offered = null; checking = true;
        status.Text = "公開版を確認中…"; available.Text = "公開バージョン：確認中"; UpdateButtons();
        try
        {
            var channel = await service.DetectChannel(lifetime.Token);
            if (disposed) return;
            currentChannel = channel;
            ShowCurrentVersion();
            targetChannel = requestedChannel ?? currentChannel;
            if (targetChannel == HostUpdateChannel.Unknown)
            {
                available.Text = "公開バージョン：未確認";
                status.Text = "現在の版の種類を判定できません。";
                return;
            }
            var result = await service.Check(targetChannel, lifetime.Token);
            if (disposed) return;
            if (!result.Confirmed || result.Package == null)
            { available.Text = "公開バージョン：未確認"; status.Text = ReleaseProblems.Explanation(result); return; }
            HostProduct.Validate(result.Package);
            if (result.Prerelease != (targetChannel == HostUpdateChannel.Preview)) throw new IOException("公開版の種類が一致しません。");
            ShowOffer(result);
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception ex) { if (!disposed) { offered = null; available.Text = "公開バージョン：未確認"; status.Text = "公開版を確認できません：" + ex.Message; } }
        finally { checking = false; if (!disposed) UpdateButtons(); }
    }

    internal async Task InstallUpdate() => await InstallOffer(false);

    async Task<bool> InstallOffer(bool switching)
    {
        if (Busy || disposed || !CanInstall(service.BlockReason(), switching)) return true;
        var selected = offered!.Package!;
        if (switching)
        {
            bool older = Version.Parse(selected.Package.Version) < Version.Parse(service.CurrentVersion);
            string warning = older ? "\n現在より古い版に戻ります。設定の互換性にご注意ください。" : "";
            string message = $"{ChannelName(currentChannel)} {service.CurrentVersion} → {ChannelName(targetChannel)} {selected.Package.Version}{warning}\n\n切り替えてもよろしいですか？";
            if (!confirmSwitch(message)) return false;
        }
        installing = true; UpdateButtons();
        bool acceptingProgress = true;
        try
        {
            status.Text = "アップデートを準備中…";
            await service.Handoff(selected, currentChannel, targetChannel, switching, new Progress<string>(text =>
            {
                if (acceptingProgress && !disposed && installing) status.Text = text;
            }), lifetime.Token);
            acceptingProgress = false;
            if (!disposed) { transferred = true; status.Text = "本体を更新して再起動します。"; }
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception ex) { acceptingProgress = false; if (!disposed) { status.Text = "更新を開始できません：" + ex.Message; } }
        finally { acceptingProgress = false; installing = false; if (!disposed) UpdateButtons(); }
        return true;
    }

    bool CanInstall(string? reason, bool switching = false) => offered is { Confirmed: true, Package: not null } && reason == null &&
        targetChannel is HostUpdateChannel.Stable or HostUpdateChannel.Preview &&
        (switching ? currentChannel != targetChannel : currentChannel == targetChannel && HostProduct.IsNewer(offered.Package, service.CurrentVersion));

    void UpdateButtons()
    {
        var reason = service.BlockReason();
        blocked.Text = reason ?? ""; blocked.Visible = reason != null;
        stable.Enabled = !Busy && reason == null && currentChannel != HostUpdateChannel.Stable;
        preview.Enabled = !Busy && reason == null && currentChannel != HostUpdateChannel.Preview;
        install.Enabled = !Busy && CanInstall(reason);
        close.Enabled = !installing && !transferred;
        log.Enabled = !installing && !transferred;
        hints.SetToolTip(install, reason ?? "本体を更新して再起動します。");
    }

    void OpenLog()
    {
        var result = service.LastResult();
        var text = result == null ? "保存された更新ログはありません。" :
            $"{result.CompletedAt.ToLocalTime():yyyy/MM/dd HH:mm:ss}\r\n{result.FromVersion} → {result.ToVersion} / {result.State}\r\n{result.Detail}" +
            (result.ExitCode is int code ? $"\r\n終了コード：{code}" : "");
        using var dialog = new OperationDetailsForm("更新ログ", text);
        dialog.ShowDialog(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; lifetime.Cancel(); lifetime.Dispose(); hints.Dispose(); }
        base.Dispose(disposing);
    }
}
