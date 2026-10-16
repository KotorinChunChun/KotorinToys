using System.Diagnostics;

namespace KotorinToys;

internal static class SelfUpdateWorker
{
    public static async Task<int> Run(string request)
    {
        var path = Path.GetFullPath(request);
        if (Path.GetFileName(path) != "request.json") throw new IOException("自己更新の操作記録名が不正です。");
        using var requestLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var plan = SelfUpdateFiles.Read<SelfUpdatePlan>(path);
        var job = Path.GetDirectoryName(path)!;
        SelfUpdateFiles.Validate(plan, job);
        if (!SelfUpdateFiles.SamePath(Environment.ProcessPath, PathSafety.Resolve(job, "worker/KotorinToys.Maintenance.exe")))
            throw new IOException("更新プロセスの実行先が専用領域ではありません。");
        using var lease = new FileStream(PathSafety.Resolve(plan.SettingsDirectory, "updates/self-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var claim = new FileStream(PathSafety.Resolve(job, "claimed"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var detector = new InstallationDetector();
        var manager = new ToolManager(plan.Root);
        var managed = HostProduct.Definition with { Id = "dashboard", AppId = "" };
        using var parent = Process.GetProcessById(plan.ParentPid);
        void ValidateParent(SelfUpdatePlan value)
        {
            if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != value.ParentStartUtcTicks ||
                parent.SessionId != Process.GetCurrentProcess().SessionId ||
                !SelfUpdateFiles.SamePath(parent.MainModule?.FileName, PathSafety.Resolve(value.Root, "KotorinToys.exe")))
                throw new IOException("更新元のPID・起動時刻・セッション・実行先が一致しません。");
        }
        async Task Ready()
        {
            SelfUpdateFiles.Write(PathSafety.Resolve(job, "ready.json"), new SelfUpdateReady(plan.Id, Environment.ProcessId));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var go = PathSafety.Resolve(job, "continue.json");
            while (!File.Exists(go))
            {
                var cancel = PathSafety.Resolve(job, "cancel.json");
                if (File.Exists(cancel) && SelfUpdateFiles.Read<SelfUpdateReady>(cancel) == new SelfUpdateReady(plan.Id, Environment.ProcessId))
                    throw new OperationCanceledException("本体が更新準備を取り消しました。");
                if (parent.HasExited) throw new IOException("更新開始前に本体が終了しました。");
                await Task.Delay(100, deadline.Token);
            }
            if (SelfUpdateFiles.Read<SelfUpdateReady>(go) != new SelfUpdateReady(plan.Id, Environment.ProcessId))
                throw new IOException("本体からの更新開始承認が一致しません。");
        }
        async Task<bool> Stop()
        {
            var status = await manager.Send(managed, "status");
            if (status.Pid != plan.ParentPid || status.ProtocolVersion < 2 || !status.Ready) return false;
            var reply = await manager.Send(managed, "stop", plan.ParentPid);
            if (reply.Result != "accepted") return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await parent.WaitForExitAsync(timeout.Token);
            return parent.HasExited;
        }
        async Task<bool> Restart()
        {
            var installed = detector.Detect(HostProduct.Definition);
            if (installed.State != "導入済み" || !SelfUpdateFiles.SamePath(installed.Directory, plan.Root) ||
                !SelfUpdateFiles.SamePath(installed.Executable, PathSafety.Resolve(plan.Root, "KotorinToys.exe")) ||
                installed.Version != plan.FromVersion && installed.Version != plan.Release.Package.Version) return false;
            var state = await manager.Status(managed);
            if (state.State == "実行中") return true;
            if (state.State != "停止中") return false;
            var start = new ProcessStartInfo(installed.Executable!) { UseShellExecute = false, WorkingDirectory = plan.Root };
            foreach (var arg in new[] { "--root", plan.Root, "--settings-dir", plan.SettingsDirectory }) start.ArgumentList.Add(arg);
            // --startupを付けず、子の経由起動を再実行しない。
            using var launched = Process.Start(start) ?? throw new IOException("更新後の本体を起動できません。");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!launched.HasExited)
            {
                var ready = await manager.Status(managed, timeout.Token);
                if (ready.State == "実行中" && ready.Pid == launched.Id) return true;
                await Task.Delay(200, timeout.Token);
            }
            return false;
        }
        void Record(SelfUpdateResult result)
        {
            SelfUpdateFiles.Write(PathSafety.Resolve(job, "result.json"), result);
            SelfUpdateFiles.Write(PathSafety.Resolve(plan.SettingsDirectory, "updates/latest.json"), result);
        }
        var operations = new SelfUpdateOperations(() => detector.Detect(HostProduct.Definition), ValidateParent, Ready, Stop,
            catalog => new InstallerAdapter(manager).Install(HostProduct.Definition, catalog, plan.Root), Restart, Record);
        var result = await SelfUpdateWorkflow.Run(plan, job, operations);
        return result.State == "成功" && result.Restarted ? 0 : 1;
    }
}
