using System.Security.Cryptography;

namespace KotorinToys;

internal sealed record SelfUpdateOperations(Func<InstallationState> Installation, Action<SelfUpdatePlan> ValidateParent,
    Func<Task> Ready, Func<Task<bool>> StopParent, Func<string, Task<InstallResult>> Install,
    Func<Task<bool>> Restart, Action<SelfUpdateResult> Record);

// OS操作は差し替え可能な境界へ隔離する。停止確認より前にインストーラーを実行しない。
internal static class SelfUpdateWorkflow
{
    public static async Task<SelfUpdateResult> Run(SelfUpdatePlan plan, string job, SelfUpdateOperations operations, CancellationToken cancellation = default)
    {
        SelfUpdateFiles.Validate(plan, job);
        var stopped = false;
        var result = new SelfUpdateResult(1, plan.Id, plan.Root, plan.FromVersion, plan.Release.Package.Version,
            "失敗", "更新処理を開始できませんでした。", false, null, DateTimeOffset.UtcNow);
        try
        {
            var catalogPath = PathSafety.Resolve(job, plan.Catalog);
            using var catalogLock = new FileStream(catalogPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (catalogLock.Length > 65536) throw new IOException("更新用配布台帳の容量が不正です。");
            var catalog = PackageCatalogReader.Load(catalogPath);
            if (catalog.DistributionStatus != "published" || catalog.Packages.Length != 1 || catalog.Packages[0] != plan.Release.Package)
                throw new IOException("更新計画と検証済み配布台帳が一致しません。");
            var file = PathSafety.Resolve(Path.GetDirectoryName(catalogPath)!, plan.Release.Package.File);
            // 本体終了前に再検証し、実行終了までファイルの変更・削除を禁止する。
            using var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (locked.Length != plan.Release.Length || Convert.ToHexString(await SHA256.HashDataAsync(locked, cancellation)).ToLowerInvariant() != plan.Release.Package.Sha256)
                throw new IOException("更新インストーラーの容量・SHA-256が一致しません。");
            using var backup = plan.Transition == null ? null : await SelfUpdateBackup.LockAndVerify(job, cancellation);
            SelfUpdateFiles.ValidateInstallation(operations.Installation(), plan);
            operations.ValidateParent(plan);
            cancellation.ThrowIfCancellationRequested();
            await operations.Ready();
            cancellation.ThrowIfCancellationRequested();
            SelfUpdateFiles.ValidateInstallation(operations.Installation(), plan);
            operations.ValidateParent(plan);
            if (!await operations.StopParent()) throw new IOException("本体の正常終了が未確認のため更新しません。");
            stopped = true;
            // この時点以降、実行中インストーラーの強制取消は行わない。
            var installed = await operations.Install(catalogPath);
            if (installed.State == "成功") SelfUpdateFiles.ValidateInstallation(operations.Installation(), plan, true);
            result = result with { State = installed.State, Detail = installed.Detail, ExitCode = installed.ExitCode };
        }
        catch (Exception ex)
        {
            result = result with { State = ex is OperationCanceledException ? "取消" : "失敗", Detail = ex.Message };
        }
        // 再起動された本体から、起動直後にも結果を読めるよう先に記録する。
        TryRecord(operations, result);
        if (stopped)
        {
            try
            {
                result = result with { Restarted = await operations.Restart() };
                if (!result.Restarted) result = result with { State = "要確認", Detail = result.Detail + " / 本体の再起動を確認できません。更新記録を確認してください。" };
            }
            catch (Exception ex) { result = result with { State = "要確認", Detail = result.Detail + " / 再起動失敗：" + ex.Message }; }
        }
        result = result with { CompletedAt = DateTimeOffset.UtcNow };
        TryRecord(operations, result);
        return result;
    }

    static void TryRecord(SelfUpdateOperations operations, SelfUpdateResult result)
    {
        try { operations.Record(result); }
        catch (Exception ex) { Console.Error.WriteLine("更新結果を保存できません：" + ex.Message); }
    }
}
