using System.Security.Cryptography;
using KotorinToys;

internal static class SelfUpdateWorkflowTests
{
    public static async Task Run(string fixtures, Action<bool, string> check)
    {
        HostSelfUpdateEligibilityTests.Run(fixtures, check);
        await SelfUpdateBackupTests.Run(fixtures, check);
        var settings = Path.Combine(fixtures, "self-update-profile");
        var root = Path.Combine(fixtures, "self-update-installed");
        var bytes = "インストーラーではないfixture"u8.ToArray();
        var package = new LocalPackage("host", "KotorinToys", "0.1.1", "KotorinToys", "KotorinToys-0.1.1-setup.exe",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "unsigned-public-build");
        SelfUpdatePlan NewPlan() => new(1, Guid.NewGuid().ToString("N"), root, settings, 12345, DateTime.UtcNow.Ticks,
            "0.1.0", new(package, bytes.Length), "download-" + new string('0', 32) + "/packages.json");
        async Task Scenario(string name, bool stop = true, string installState = "成功", bool restart = true,
            bool corrupt = false, bool wrongCatalog = false, bool wrongParent = false, bool wrongRoot = false,
            bool readyFailure = false, bool installFailure = false, bool cancel = false, bool wrongInstalledVersion = false, bool checkLocks = false)
        {
            var plan = NewPlan(); var job = PathSafety.Resolve(settings, "updates/" + plan.Id);
            var catalog = PathSafety.Resolve(job, plan.Catalog); Directory.CreateDirectory(Path.GetDirectoryName(catalog)!);
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(catalog)!, package.File), corrupt ? [1] : bytes);
            SelfUpdateFiles.Write(catalog, new PackageCatalog(1, "published", [wrongCatalog ? package with { Version = "0.1.2", File = "KotorinToys-0.1.2-setup.exe" } : package]));
            var order = new List<string>(); var results = new List<SelfUpdateResult>(); string version = "0.1.0";
            using var cancellation = new CancellationTokenSource();
            var operations = new SelfUpdateOperations(
                () => new("導入済み", version, wrongRoot ? fixtures : root, Path.Combine(root, "KotorinToys.exe")),
                _ => { order.Add("parent"); if (wrongParent && order.Count(x => x == "parent") == 2) throw new IOException("PID再利用相当"); },
                () =>
                {
                    order.Add("ready");
                    if (checkLocks)
                        foreach (var lockedPath in new[] { catalog, Path.Combine(Path.GetDirectoryName(catalog)!, package.File) })
                        {
                            bool blocked = false;
                            try { using var edit = new FileStream(lockedPath, FileMode.Open, FileAccess.Write, FileShare.Read); }
                            catch (IOException) { blocked = true; }
                            check(blocked, "承認待ちでも検証済み台帳・インストーラーを書き換えさせない");
                        }
                    if (readyFailure) throw new IOException("準備応答失敗");
                    if (cancel) cancellation.Cancel();
                    return Task.CompletedTask;
                },
                () => { order.Add("stop"); return Task.FromResult(stop); },
                _ =>
                {
                    order.Add("install"); if (installFailure) throw new IOException("導入失敗");
                    if (installState == "成功" && !wrongInstalledVersion) version = "0.1.1";
                    return Task.FromResult(new InstallResult("host", "install", installState, "fixture", installState == "成功" ? 0 : 3010));
                },
                () => { order.Add("restart"); return Task.FromResult(restart); },
                results.Add);
            var result = await SelfUpdateWorkflow.Run(plan, job, operations, cancellation.Token);
            if (corrupt || wrongCatalog || wrongRoot || readyFailure || wrongParent || cancel)
                check(!order.Contains("install") && !order.Contains("restart") && !order.Contains("stop") && result.State != "成功", name);
            else if (!stop)
                check(!order.Contains("install") && !order.Contains("restart") && result.State == "失敗", name);
            else if (installFailure || wrongInstalledVersion)
                check(order.IndexOf("stop") < order.IndexOf("install") && order.Contains("restart") && result.State == "失敗" && result.Restarted, name);
            else
                check(order.SequenceEqual(new[] { "parent", "ready", "parent", "stop", "install", "restart" }) &&
                    result.State == (restart ? installState : "要確認") && result.Restarted == restart &&
                    results.Count == 2 && results[0].Restarted == false && results[1] == result, name);
        }
        await Scenario("自己更新は再検証・準備承認・正常終了・導入・再起動の順序を守る");
        await Scenario("正常終了拒否では導入・再起動を実行しない", stop: false);
        await Scenario("再検証hash不一致では本体を停止しない", corrupt: true);
        await Scenario("計画と台帳の不一致では本体を停止しない", wrongCatalog: true);
        await Scenario("準備完了の失敗では本体を停止しない", readyFailure: true);
        await Scenario("承認待ちの間に親が変わったら停止しない", wrongParent: true);
        await Scenario("正式導入先が変わったら停止しない", wrongRoot: true);
        await Scenario("開始承認前の取消は停止・導入をしない", cancel: true);
        await Scenario("導入例外でも既存本体の安全な再起動を試みる", installFailure: true);
        await Scenario("成功終了コードだけで版不一致を成功にしない", wrongInstalledVersion: true);
        await Scenario("3010は再起動待ちのまま記録しOSを再起動しない", installState: "再起動待ち");
        await Scenario("更新成功でも本体起動未確認は要確認にする", restart: false);
        await Scenario("検証済みファイルを実行終了まで固定して自己更新する", checkLocks: true);
        foreach (var invalid in new[] { NewPlan() with { SchemaVersion = 2 }, NewPlan() with { Id = "../bad" },
            NewPlan() with { ParentPid = 0 }, NewPlan() with { ParentStartUtcTicks = 0 }, NewPlan() with { Catalog = "../../installer.exe" },
            NewPlan() with { FromVersion = "0.1.1" }, NewPlan() with { Root = settings }, NewPlan() with { Root = "C:\\" } })
        {
            var job = Path.Combine(settings, "updates", invalid.Id);
            bool rejected = false; try { SelfUpdateFiles.Validate(invalid, job); } catch (IOException) { rejected = true; }
            check(rejected, "自己更新の形式・nonce・親識別・パス・版を検証");
        }
        var valid = NewPlan(); bool wrongJob = false;
        try { SelfUpdateFiles.Validate(valid, Path.Combine(settings, "updates", Guid.NewGuid().ToString("N"))); } catch (IOException) { wrongJob = true; }
        check(wrongJob, "別jobの計画を転用しない");
        var source = Path.Combine(fixtures, "clone-source"); var clone = Path.Combine(fixtures, "clone-job");
        Directory.CreateDirectory(Path.Combine(source, "maintenance", "nested"));
        File.WriteAllBytes(Path.Combine(source, "maintenance", "worker.dll"), bytes);
        File.WriteAllText(Path.Combine(source, "maintenance", "nested", "runtime.json"), "{}");
        await HostSelfUpdateService.CopyWorker(source, clone, default);
        check(File.ReadAllBytes(Path.Combine(clone, "worker", "worker.dll")).SequenceEqual(bytes) &&
            File.Exists(Path.Combine(clone, "worker", "nested", "runtime.json")), "更新プロセスの依存ファイルを導入先外へhash照合して複製");
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); bool canceledCopy = false;
        try { await HostSelfUpdateService.CopyWorker(source, Path.Combine(fixtures, "canceled-clone"), canceled.Token); } catch (OperationCanceledException) { canceledCopy = true; }
        check(canceledCopy, "更新プロセスの準備にも取消を伝播");
        var service = new HostSelfUpdateService(root, settings);
        check(service.BlockReason() != null, "開発プロセスは正式導入の本体を自己更新しない");
        var receipt = new SelfUpdateResult(1, valid.Id, root, "0.1.0", "0.1.1", "成功", "test", true, 0, DateTimeOffset.UtcNow);
        SelfUpdateFiles.Write(Path.Combine(settings, "updates", "latest.json"), receipt);
        check(service.LastResult() == receipt, "次回起動時の更新結果を保存先から読める");
        SelfUpdateFiles.Write(Path.Combine(settings, "updates", "latest.json"), receipt with { Root = fixtures });
        check(service.LastResult() == null, "別配置の更新結果を表示しない");
    }
}
