using KotorinToys;

internal static class HostGuiUpdateCheckTests
{
    sealed class Fake : IHostSelfUpdateService
    {
        public string CurrentVersion => "0.1.16";
        public HostUpdateChannel Channel = HostUpdateChannel.Preview;
        public string? Blocked;
        public string? BlockReason() => Blocked;
        public bool Fail, Confirmed = true, WrongChannel;
        public string Version = "0.1.17";
        public int Queries;
        public TaskCompletionSource? Gate;
        public Task<HostUpdateChannel> DetectChannel(CancellationToken token) => Task.FromResult(Channel);
        public async Task<PublicRelease> Check(HostUpdateChannel channel, CancellationToken token)
        {
            Queries++;
            if (Gate != null) await Gate.Task.WaitAsync(token);
            if (Fail) throw new IOException("offline");
            return new(new(new("host", "KotorinToys", Version, "KotorinToys", $"KotorinToys-{Version}-setup.exe", new string('a', 64), "unsigned-public-build"), 4),
                Confirmed, WrongChannel ? channel != HostUpdateChannel.Preview : channel == HostUpdateChannel.Preview, "fixture");
        }
        public Task Handoff(ReleasePackage release, HostUpdateChannel from, HostUpdateChannel to, bool confirmed, IProgress<string> progress, CancellationToken token) => throw new Exception("checkerだけでは更新しない");
        public SelfUpdateResult? LastResult() => null;
    }
    public static async Task Run(Action<bool, string> check)
    {
        foreach (var channel in new[] { HostUpdateChannel.Preview, HostUpdateChannel.Stable })
        {
            var service = new Fake { Channel = channel }; int shown = 0; bool visible = false; var errors = new List<string>();
            var coordinator = new HostGuiUpdateCheck(service, () => visible, (r,c) => { check(c == channel && r.Prerelease == (channel == HostUpdateChannel.Preview), "GUIの自動更新は現在と同じ種類を引き継ぐ：" + channel); shown++; }, errors.Add);
            await coordinator.Check(default); check(service.Queries == 0, "非表示起動では更新確認ダイアログを出さない");
            visible = true; await coordinator.Check(default); await coordinator.Check(default);
            check(shown == 1 && errors.Count == 0, "GUI再表示で同じ版の更新ダイアログを重複表示しない");
            service.Version = "0.1.18"; await coordinator.Check(default); check(shown == 2, "より新しい版はGUI再表示時に再案内する");
        }
        foreach (var version in new[] { "0.1.16", "0.1.15" })
        {
            int shown = 0; var service = new Fake { Version = version };
            HostGuiUpdateReceipt? receipt = null;
            await new HostGuiUpdateCheck(service, () => true, (_,_) => shown++, _ => {}, r => receipt = r).Check(default);
            check(shown == 0, "同版・旧版はGUI表示時に自動更新しない：" + version);
            check(receipt is { Channel: HostUpdateChannel.Preview, Outcome: "新しい公開版なし" } && receipt.AvailableVersion == version,
                "実機でGUIの更新確認結果とチャネルを照合できる記録を残す");
        }
        foreach (var kind in new[] { "blocked", "unknown", "offline", "unconfirmed", "wrong" })
        {
            var service = new Fake { Blocked = kind == "blocked" ? "開発実行" : null, Channel = kind == "unknown" ? HostUpdateChannel.Unknown : HostUpdateChannel.Preview,
                Fail = kind == "offline", Confirmed = kind != "unconfirmed", WrongChannel = kind == "wrong" };
            int shown = 0; await new HostGuiUpdateCheck(service, () => true, (_,_) => shown++, _ => {}).Check(default);
            check(shown == 0, "確認不能・種別違いでは更新を開始しない：" + kind);
        }
        var pending = new Fake { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; bool open = true; int dialogs = 0;
        var checker = new HostGuiUpdateCheck(pending, () => open, (_,_) => dialogs++, _ => {});
        var task = checker.Check(default); await checker.Check(default); open = false; pending.Gate.SetResult(); await task;
        check(pending.Queries == 1 && dialogs == 0, "確認中の重複とGUIを閉じた後の遅延表示を防止");
        pending.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); open = true;
        task = checker.Check(default); checker.ManualInteraction(); pending.Gate.SetResult(); await task;
        check(dialogs == 0, "手動更新を開いて取消した後に遅延した自動更新を開始しない");
        pending.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); open = true;
        using var cancellation = new CancellationTokenSource(); task = checker.Check(cancellation.Token); cancellation.Cancel(); await task;
        check(dialogs == 0, "本体終了時の公開確認取消で更新を開始しない");
    }
}
