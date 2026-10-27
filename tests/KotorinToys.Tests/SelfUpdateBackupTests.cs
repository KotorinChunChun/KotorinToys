using System.Security.Cryptography;
using KotorinToys;

internal static class SelfUpdateBackupTests
{
    public static async Task Run(string fixtures, Action<bool, string> check)
    {
        var settings = Path.Combine(fixtures, "channel-backup-profile-" + Guid.NewGuid().ToString("N"));
        var installed = Path.Combine(fixtures, "channel-backup-install");
        Directory.CreateDirectory(Path.Combine(settings, "history"));
        Directory.CreateDirectory(Path.Combine(settings, "updates"));
        File.WriteAllText(Path.Combine(settings, "settings.json"), "settings-example");
        File.WriteAllText(Path.Combine(settings, "history", "item.json"), "history-example");
        File.WriteAllText(Path.Combine(settings, "updates", "latest.json"), "last-result-example");
        var bytes = "old-version-fixture"u8.ToArray();
        var package = new LocalPackage("host", "KotorinToys", "0.1.1", "KotorinToys", "KotorinToys-0.1.1-setup.exe",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "unsigned-public-build");
        var transition = new HostChannelTransition(HostUpdateChannel.Preview, HostUpdateChannel.Stable, "backup/manifest.json");
        var plan = new SelfUpdatePlan(1, Guid.NewGuid().ToString("N"), installed, settings, 12345, DateTime.UtcNow.Ticks,
            "0.2.0", new(package, bytes.Length), "download-" + new string('0', 32) + "/packages.json", transition);
        var job = PathSafety.Resolve(settings, "updates/" + plan.Id);
        var catalog = PathSafety.Resolve(job, plan.Catalog); Directory.CreateDirectory(Path.GetDirectoryName(catalog)!);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(catalog)!, package.File), bytes);
        SelfUpdateFiles.Write(catalog, new PackageCatalog(1, "published", [package]));
        await SelfUpdateBackup.Create(settings, job, default);
        var backup = PathSafety.Resolve(job, "backup");
        var snapshot = PathSafety.Resolve(backup, "files/settings.json");
        check(File.ReadAllText(snapshot) == "settings-example" && File.ReadAllText(PathSafety.Resolve(backup, "files/history/item.json")) == "history-example" &&
            File.ReadAllText(PathSafety.Resolve(backup, "files/updates/latest.json")) == "last-result-example", "切替前に設定・履歴・前回結果を退避");
        check(!Directory.Exists(Path.Combine(backup, "files", "updates", plan.Id)), "退避中の更新ジョブは再帰コピーしない");
        using (await SelfUpdateBackup.LockAndVerify(job, default))
        {
            bool locked = false;
            try { using var edit = new FileStream(snapshot, FileMode.Open, FileAccess.Write, FileShare.Read); } catch (IOException) { locked = true; }
            check(locked, "退避ファイルは検証後に更新完了までロック");
        }
        async Task<(SelfUpdateResult Result, List<string> Order)> RunWorkflow()
        {
            string version = "0.2.0"; var order = new List<string>();
            var operations = new SelfUpdateOperations(
                () => new("導入済み", version, installed, Path.Combine(installed, "KotorinToys.exe")),
                _ => order.Add("parent"), () => { order.Add("ready"); return Task.CompletedTask; },
                () => { order.Add("stop"); return Task.FromResult(true); },
                _ => { order.Add("install"); version = "0.1.1"; return Task.FromResult(new InstallResult("host", "install", "成功", "fixture", 0)); },
                () => { order.Add("restart"); return Task.FromResult(true); }, _ => { });
            return (await SelfUpdateWorkflow.Run(plan, job, operations), order);
        }
        var success = await RunWorkflow();
        check(success.Result.State == "成功" && success.Order.SequenceEqual(new[] { "parent", "ready", "parent", "stop", "install", "restart" }) &&
            File.ReadAllText(Path.Combine(settings, "history", "item.json")) == "history-example", "確認済みの古い通常版へ退避後に切り替え、元の履歴を保持");
        File.WriteAllText(snapshot, "tampered");
        var tampered = await RunWorkflow();
        check(tampered.Result.State == "失敗" && !tampered.Order.Contains("ready") && !tampered.Order.Contains("stop") && !tampered.Order.Contains("install"),
            "退避の改変を検出したら本体を終了しない");
        var manifestPath = PathSafety.Resolve(backup, "manifest.json");
        SelfUpdateFiles.Write(manifestPath, new SelfUpdateBackupManifest(1, [new("../manifest.json", 1, new string('0', 64))]));
        bool traversal = false;
        try { using var locked = await SelfUpdateBackup.LockAndVerify(job, default); } catch (IOException) { traversal = true; }
        check(traversal, "退避一覧からfilesの外へ移動できない");
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool canceled = false;
        try { await SelfUpdateBackup.Create(settings, Path.Combine(settings, "updates", Guid.NewGuid().ToString("N")), cancel.Token); }
        catch (OperationCanceledException) { canceled = true; }
        check(canceled, "設定退避の途中でも取消を受け付ける");
    }
}
