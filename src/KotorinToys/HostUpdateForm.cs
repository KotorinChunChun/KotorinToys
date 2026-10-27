namespace KotorinToys;

internal sealed class HostUpdateForm : BrandedForm
{
    readonly IHostSelfUpdateService service;
    readonly Func<string, bool> confirmSwitch;
    readonly Label current = new() { Name = "hostCurrentVersion", AutoSize = true };
    readonly Label available = new() { Name = "hostAvailableVersion", AutoSize = true, Text = "公開版：未確認" };
    readonly Label status = new() { Name = "hostUpdateStatus", AutoSize = true, MaximumSize = new(710, 0) };
    readonly Label blocked = new() { Name = "hostUpdateBlockedReason", AutoSize = true, MaximumSize = new(710, 0), ForeColor = Color.DarkRed };
    readonly Label previousResult = new() { Name = "hostLastUpdate", AutoSize = true, MaximumSize = new(710, 0), Visible = false };
    readonly CheckBox switchChannel = new() { Name = "switchHostChannel", Text = "通常版・開発版を切り替える", AutoSize = true };
    readonly ComboBox channel = new() { Name = "hostTargetChannel", DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    readonly Button refresh = new() { Name = "checkHostUpdate", Text = "公開版を再確認", AutoSize = true };
    readonly Button install = new() { Name = "installHostUpdate", Text = "アップデート", AutoSize = true, Enabled = false };
    readonly Button cancel = new() { Name = "cancelHostUpdate", Text = "取り消す", AutoSize = true, Enabled = false };
    readonly Button close = new() { Text = "閉じる", AutoSize = true };
    readonly CancellationTokenSource lifetime = new();
    readonly ToolTip hints = new();
    CancellationTokenSource? active;
    PublicRelease? offered;
    HostUpdateChannel currentChannel = HostUpdateChannel.Unknown, targetChannel = HostUpdateChannel.Unknown;
    bool running, transferred, closeRequested, changingSelection;

    public HostUpdateForm(IHostSelfUpdateService service, Func<string, bool>? confirmSwitch = null)
    {
        this.service = service;
        this.confirmSwitch = confirmSwitch ?? (message => MessageBox.Show(this, message, WindowTitle.Create("版の切り替え"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes);
        Text = WindowTitle.Create("本体アップデート"); Size = new(800, 500); MinimumSize = new(740, 450);
        Font = new("Yu Gothic UI", 11); StartPosition = FormStartPosition.CenterParent;
        current.Text = "現在の本体：" + service.CurrentVersion;
        channel.Items.AddRange(["通常版", "開発版"]); channel.SelectedIndex = 0;
        var selection = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        selection.Controls.Add(switchChannel); selection.Controls.Add(channel);
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(14) };
        content.Controls.Add(current); content.Controls.Add(available); content.Controls.Add(selection);
        content.Controls.Add(status); content.Controls.Add(blocked); content.Controls.Add(previousResult);
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
        ActionHints.Attach(buttons, hints);
        hints.SetToolTip(switchChannel, "別の種類の最新版を選びます。切り替え前に設定・履歴を退避し、確認します。");
        hints.SetToolTip(channel, "切り替え先の種類を選びます。");
        refresh.Click += async (_, _) => await RefreshRelease();
        switchChannel.CheckedChanged += async (_, _) =>
        {
            if (changingSelection) return;
            if (switchChannel.Checked && currentChannel != HostUpdateChannel.Unknown)
            {
                changingSelection = true;
                channel.SelectedIndex = currentChannel == HostUpdateChannel.Preview ? 0 : 1;
                changingSelection = false;
            }
            await RefreshRelease();
        };
        channel.SelectedIndexChanged += async (_, _) => { if (!changingSelection && switchChannel.Checked) await RefreshRelease(); };
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

    static string ChannelName(HostUpdateChannel value) => value switch { HostUpdateChannel.Stable => "通常版", HostUpdateChannel.Preview => "開発版", _ => "不明" };
    HostUpdateChannel SelectedChannel => channel.SelectedIndex == 1 ? HostUpdateChannel.Preview : HostUpdateChannel.Stable;
    internal void CancelOperation() => active?.Cancel();

    internal async Task RefreshRelease()
    {
        if (running || transferred || IsDisposed) return;
        offered = null; currentChannel = targetChannel = HostUpdateChannel.Unknown; running = true; UpdateButtons();
        status.Text = "公開版を確認中…"; available.Text = "公開版：確認中";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); active = cancellation; UpdateButtons();
        try
        {
            currentChannel = await service.DetectChannel(cancellation.Token);
            if (IsDisposed) return;
            current.Text = $"現在の本体：{service.CurrentVersion}（{ChannelName(currentChannel)}）";
            targetChannel = switchChannel.Checked ? SelectedChannel : currentChannel;
            if (targetChannel == HostUpdateChannel.Unknown)
            {
                available.Text = "公開版：未確認";
                status.Text = "現在の版の種類を判定できません。切り替え先を選んで確認してください。";
                return;
            }
            if (switchChannel.Checked && targetChannel == currentChannel)
            {
                available.Text = "公開版：未確認"; status.Text = "現在と異なる種類を選んでください。"; return;
            }
            var result = await service.Check(targetChannel, cancellation.Token);
            if (IsDisposed) return;
            if (!result.Confirmed || result.Package == null)
            { available.Text = "公開版：未確認"; status.Text = ReleaseProblems.Explanation(result); return; }
            HostProduct.Validate(result.Package);
            if (result.Prerelease != (targetChannel == HostUpdateChannel.Preview)) throw new IOException("公開版の種類が一致しません。");
            offered = result;
            available.Text = "公開版：" + ReleaseDisplay.Version(result.Package) + "（" + ChannelName(targetChannel) + "）";
            status.Text = switchChannel.Checked ? "切り替えできます。" : HostProduct.IsNewer(result.Package, service.CurrentVersion) ? "新しい公開版があります。" :
                Version.Parse(result.Package.Package.Version) == Version.Parse(service.CurrentVersion) ? "最新版です。" : "現在の版より新しい公開版はありません。";
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
        bool switching = switchChannel.Checked;
        if (switching)
        {
            bool older = Version.Parse(selected.Package.Version) < Version.Parse(service.CurrentVersion);
            string warning = older ? "\n現在より古い版に戻ります。設定の互換性にご注意ください。" : "";
            string message = $"{ChannelName(currentChannel)} {service.CurrentVersion} → {ChannelName(targetChannel)} {selected.Package.Version}{warning}\n\n設定・履歴を退避して、本体を切り替えます。\n配布物は未署名です。切り替えますか？";
            if (!confirmSwitch(message)) return;
        }
        running = true; UpdateButtons();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); active = cancellation; UpdateButtons();
        var acceptingProgress = true;
        try
        {
            status.Text = "本体の更新を準備しています…";
            await service.Handoff(selected, currentChannel, targetChannel, switching, new Progress<string>(text =>
            {
                if (acceptingProgress && !IsDisposed && running && ReferenceEquals(active, cancellation) && !cancellation.IsCancellationRequested) status.Text = text;
            }), cancellation.Token);
            acceptingProgress = false;
            if (IsDisposed) return;
            transferred = true; status.Text = "本体を更新して再起動します。";
        }
        catch (OperationCanceledException) { acceptingProgress = false; if (!IsDisposed) { offered = null; status.Text = "取得・準備を取り消しました。"; } }
        catch (Exception ex) { acceptingProgress = false; if (!IsDisposed) { offered = null; status.Text = "更新を開始できません：" + ex.Message; } }
        finally { acceptingProgress = false; EndOperation(); }
    }

    bool CanInstall(string? reason) => offered is { Confirmed: true, Package: not null } && reason == null &&
        targetChannel is HostUpdateChannel.Stable or HostUpdateChannel.Preview &&
        (switchChannel.Checked ? currentChannel != targetChannel : currentChannel == targetChannel && HostProduct.IsNewer(offered.Package, service.CurrentVersion));

    void UpdateButtons()
    {
        var reason = service.BlockReason();
        blocked.Text = reason ?? ""; blocked.Visible = reason != null;
        refresh.Enabled = switchChannel.Enabled = !running && !transferred;
        channel.Enabled = switchChannel.Checked && !running && !transferred;
        install.Text = switchChannel.Checked ? "切り替え" : "アップデート";
        install.Enabled = !running && !transferred && CanInstall(reason);
        cancel.Enabled = running && !transferred && active?.IsCancellationRequested != true; close.Enabled = !transferred;
        install.AccessibleDescription = running || transferred ? "処理中、または更新プロセスへ引継ぎ済みです。" : reason ??
            (offered is not { Confirmed: true, Package: not null } ? "先に公開版を確認してください。" : !CanInstall(null) ? "更新できる公開版がありません。" :
            "未署名の配布物を照合し、本体だけを更新・再起動します。設定・履歴と子ツールは保持します。");
        cancel.AccessibleDescription = cancel.Enabled ? "取得・準備を取り消します。" : "取消可能な処理がありません。";
        hints.SetToolTip(install, install.AccessibleDescription); hints.SetToolTip(cancel, cancel.AccessibleDescription);
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
        if (disposing && !IsDisposed) { lifetime.Cancel(); lifetime.Dispose(); hints.Dispose(); }
        base.Dispose(disposing);
    }
}
