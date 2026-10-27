namespace KotorinToys;

// UIからの更新では正式導入先を維持し、正常停止・起動状態復帰を行う。
internal sealed class ToolUpdateCoordinator(
    Func<ToolDefinition, InstallationState> installation,
    Func<ToolDefinition, Task<ToolState>> status,
    Func<ToolDefinition, Task<ToolState>> stop,
    Func<ToolDefinition, string, Task<ToolState>> restart,
    Func<ToolDefinition, string, string?, Task<InstallResult>> install,
    Func<string, MsixSigner>? msixSignature = null,
    Func<IReadOnlyCollection<ToolDefinition>>? definitions = null,
    Func<ToolDefinition, ToolState, Task<ToolState>>? stopExpected = null,
    Action<ToolDefinition, string, string>? preserveSidecars = null)
{
    public ToolUpdateCoordinator(ToolManager manager, InstallerAdapter adapter) : this(manager.Installation,
        tool => manager.StatusForReplacement(tool), tool => manager.Stop(tool),
        (tool, origin) => manager.Start(tool, false, origin), (tool, catalog, directory) => adapter.Install(tool, catalog, directory),
        definitions: manager.KnownTools, stopExpected: (tool, state) => manager.StopForReplacement(tool, state),
        preserveSidecars: ReplacementData.Preserve) { }

    public async Task<InstallResult> Run(ToolDefinition tool, string catalog, bool repair = false)
    {
        ToolState? before = null; bool stopped = false, alternate = false;
        InstallResult result;
        try
        {
            var actual = installation(tool);
            if (repair && !Version.TryParse(actual.Version, out _)) throw new IOException("修復前の登録版を確認できません。");
            if (repair ? tool.Msix != null || actual.State != "修復が必要" || !actual.Repairable || actual.Directory == null :
                !MsixPolicy.Installed(actual) && actual.State != "未導入") throw new IOException("導入先と製品識別を確認できません：" + actual.Detail);
            var package = InstallerAdapter.Load(catalog, definitions?.Invoke() ?? [tool]).Packages.SingleOrDefault(p => p.Id == tool.Id)
                ?? throw new IOException("配布台帳に対象製品がありません：" + tool.Name);
            if ((MsixPolicy.Installed(actual) || repair) && Version.TryParse(actual.Version, out var installedVersion) &&
                Version.TryParse(package.Version, out var requestedVersion) && installedVersion > requestedVersion)
                throw new IOException("導入済み版より古い公開版へ戻す操作は行いません。");
            using var verified = tool.Msix != null ? await MsixPackageVerifier.Open(tool, package,
                PathSafety.Resolve(Path.GetDirectoryName(Path.GetFullPath(catalog))!, package.File), msixSignature) : null;
            before = await status(tool);
            if (repair && before.State != "停止中") throw new IOException("修復は正常に停止中と確認できる場合だけ行います。");
            if (before.State is not ("実行中" or "停止中")) throw new IOException("更新前の起動状態を確認できません：" + before.Detail);
            if (before.State == "実行中")
            {
                alternate = before.RunningExecutable != null &&
                    !string.Equals(before.RunningExecutable, actual.Executable, StringComparison.OrdinalIgnoreCase);
                var state = stopExpected == null ? await stop(tool) : await stopExpected(tool, before);
                if (state.State != "停止中") throw new IOException("正常停止が未確認のため更新しません：" + state.Detail);
                stopped = true;
            }
            result = await install(tool, catalog, MsixPolicy.Installed(actual) || repair ? actual.Directory : null);
            if (alternate && result.State == "成功" && before?.RunningExecutable != null && preserveSidecars != null)
            {
                try
                {
                    var updated = installation(tool);
                    if (!MsixPolicy.Installed(updated) || updated.Executable == null)
                        throw new IOException("導入後の実行ファイルを確認できません。");
                    preserveSidecars(tool, before.RunningExecutable, updated.Executable);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    result = result with { State = "要確認", Detail = result.Detail + " / 設定の引き継ぎを確認してください：" + ex.Message };
                }
            }
        }
        catch (Exception ex) { result = new(tool.Id, "install", "失敗", ex.Message); }
        // データ復元・展開後登録が要確認のMSIXは、自動起動でデータを変更させない。
        if (stopped && before != null && alternate && result.State != "成功")
            result = result with { Detail = result.Detail + " / 別配置の元アプリは停止したままです。元の設定・履歴を確認してから起動してください。" };
        if (stopped && before != null && (!alternate || result.State == "成功") && !(tool.Msix != null && result.State == "要確認"))
        {
            try
            {
                var restored = await restart(tool, before.LaunchOrigin);
                if (restored.State != "実行中")
                    result = result with { State = result.State == "成功" ? "要確認" : result.State, Detail = result.Detail + " / 起動状態の復帰を確認できません：" + restored.Detail };
                else result = result with { Detail = result.Detail + " / 元の起動状態へ復帰しました。" };
            }
            catch (Exception ex) { result = result with { State = result.State == "成功" ? "要確認" : result.State, Detail = result.Detail + " / 再起動失敗：" + ex.Message }; }
        }
        return result;
    }
}
