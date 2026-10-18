namespace KotorinToys;

// Innoの引数をMSIXへ渡さない。OS操作・正常停止・データ保持を独立して検証できる。
internal sealed class MsixInstaller(
    Func<ToolDefinition, InstallationState> detect,
    Func<ToolDefinition, Task<ToolState>> stop,
    IMsixDeployment deployment,
    MsixDataStore data,
    Func<string, StartupResult> removeReservations,
    Func<string, StartupResult> recoverReservations,
    Func<string, MsixSigner>? signature = null)
{
    public static MsixInstaller Create(ToolManager manager)
    {
        var reservations = new RemovalReservations(new(HostSettingsStore.DefaultDirectory), new TaskSchedulerGateway());
        return new(manager.Installation, tool => manager.Stop(tool), new MsixDeployment(), MsixDataStore.Default(),
            reservations.Remove, id => reservations.Recover(true, id));
    }
    public async Task<InstallResult> Install(ToolDefinition tool, string catalog)
    {
        bool deployed = false;
        try
        {
            var package = PackageCatalogReader.Load(catalog).Packages.SingleOrDefault(p => p.Id == tool.Id) ?? throw new IOException("MSIX配布台帳に対象がありません。");
            string file = PathSafety.Resolve(Path.GetDirectoryName(Path.GetFullPath(catalog))!, package.File);
            using var verified = await MsixPackageVerifier.Open(tool, package, file, signature);
            using var lease = data.AcquireLease();
            var before = detect(tool);
            if (!MsixPolicy.Installed(before) && before.State != "未導入") throw new IOException(before.Detail);
            if (MsixPolicy.Installed(before) && Version.Parse(before.Version!) > Version.Parse(package.Version)) throw new IOException("MSIXのダウングレードは行いません。");
            var pending = before.State == "未導入" ? await Task.Run(data.Pending) : null;
            if (pending != null) await Task.Run(() => data.PreflightRestore(pending));
            // 未導入でも、旧開発EXEや別Identityのプロセスが残る場合は安全側で中止する。
            var stopped = await stop(tool);
            if (stopped.State != "停止中") throw new IOException("正常停止が未確認のためMSIXを導入しません：" + stopped.Detail);
            var snapshot = MsixPolicy.Installed(before) ? await Task.Run(data.Capture) : null;
            await deployment.Install(file); deployed = true;
            var actual = detect(tool);
            if (!MsixPolicy.Installed(actual) || actual.Version != package.Version) throw new IOException("展開後のMSIX登録・版を照合できません。");
            if (snapshot != null) await Task.Run(() => data.VerifyPreserved(snapshot));
            if (pending != null) await Task.Run(() => data.Restore(pending));
            return new(tool.Id, "install", "成功", $"MSIX {actual.Version} の登録・設定保持を確認しました。");
        }
        catch (Exception ex) { return new(tool.Id, "install", deployed ? "要確認" : "失敗", MsixDeployment.Failure(ex)); }
    }
    public async Task<InstallResult> Uninstall(ToolDefinition tool)
    {
        bool reservationsRemoved = false, removed = false;
        try
        {
            if (!MsixPolicy.Supports(tool)) throw new IOException("未対応のMSIX製品です。");
            using var lease = data.AcquireLease();
            var installed = detect(tool);
            if (!MsixPolicy.Installed(installed)) throw new IOException("署名付きMSIXの現在ユーザー登録を確認できません。");
            if (!Version.TryParse(installed.Version, out var version) || version.Revision < 0) throw new IOException("削除対象MSIXの版が不正です。");
            var stopped = await stop(tool);
            if (stopped.State != "停止中") throw new IOException("正常停止が未確認のため削除しません：" + stopped.Detail);
            var snapshot = await Task.Run(data.Capture);
            await Task.Run(() => data.Retain(snapshot));
            var cleanup = removeReservations(tool.Id);
            if (!cleanup.Success) throw new IOException("削除前の管理予約を整理できません：" + cleanup.Detail);
            reservationsRemoved = true;
            // 正常停止・退避中に別操作で版が変わった場合は、別の版を削除しない。
            var current = detect(tool);
            if (!MsixPolicy.Installed(current) || current.Version != installed.Version) throw new IOException("削除対象MSIXの登録が変更されました。");
            string fullName = $"{MsixPolicy.Name}_{installed.Version}_x64__xtxrnffqrdk4w";
            await deployment.Remove(fullName); removed = true;
            if (detect(tool).State != "未導入") throw new IOException("MSIX削除後に登録が残っています。");
            return new(tool.Id, "uninstall", "成功", "MSIXを削除しました。設定・履歴は本体のユーザー領域へ保持し、再導入時に復元します。");
        }
        catch (Exception ex)
        {
            string detail = MsixDeployment.Failure(ex);
            if (reservationsRemoved && !removed)
            {
                var recovery = recoverReservations(tool.Id); detail += " / " + recovery.Detail;
            }
            return new(tool.Id, "uninstall", removed ? "要確認" : "失敗", detail);
        }
    }
}
