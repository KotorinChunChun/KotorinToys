namespace KotorinToys;

// UIからの更新では正式導入先を維持し、正常停止・起動状態復帰を行う。
internal sealed class ToolUpdateCoordinator(
    Func<ToolDefinition, InstallationState> installation,
    Func<ToolDefinition, Task<ToolState>> status,
    Func<ToolDefinition, Task<ToolState>> stop,
    Func<ToolDefinition, string, Task<ToolState>> restart,
    Func<ToolDefinition, string, string?, Task<InstallResult>> install,
    Func<string, MsixSigner>? msixSignature = null,
    Func<IReadOnlyCollection<ToolDefinition>>? definitions = null)
{
    public ToolUpdateCoordinator(ToolManager manager, InstallerAdapter adapter) : this(manager.Installation,
        tool => manager.Status(tool), tool => manager.Stop(tool),
        (tool, origin) => manager.Start(tool, false, origin), (tool, catalog, directory) => adapter.Install(tool, catalog, directory),
        definitions: manager.KnownTools) { }

    public async Task<InstallResult> Run(ToolDefinition tool, string catalog)
    {
        ToolState? before = null; bool stopped = false;
        InstallResult result;
        try
        {
            var actual = installation(tool);
            if (!MsixPolicy.Installed(actual) && actual.State != "未導入") throw new IOException(actual.Detail);
            var package = InstallerAdapter.Load(catalog, definitions?.Invoke() ?? [tool]).Packages.SingleOrDefault(p => p.Id == tool.Id)
                ?? throw new IOException("配布台帳に対象製品がありません：" + tool.Name);
            if (MsixPolicy.Installed(actual) && Version.TryParse(actual.Version, out var installedVersion) &&
                Version.TryParse(package.Version, out var requestedVersion) && installedVersion > requestedVersion)
                throw new IOException("導入済み版より古い公開版へ戻す操作は行いません。");
            using var verified = tool.Msix != null ? await MsixPackageVerifier.Open(tool, package,
                PathSafety.Resolve(Path.GetDirectoryName(Path.GetFullPath(catalog))!, package.File), msixSignature) : null;
            before = await status(tool);
            if (before.State is not ("実行中" or "停止中")) throw new IOException("更新前の起動状態を確認できません：" + before.Detail);
            if (before.State == "実行中")
            {
                var state = await stop(tool);
                if (state.State != "停止中") throw new IOException("正常停止が未確認のため更新しません：" + state.Detail);
                stopped = true;
            }
            result = await install(tool, catalog, MsixPolicy.Installed(actual) ? actual.Directory : null);
        }
        catch (Exception ex) { result = new(tool.Id, "install", "失敗", ex.Message); }
        // データ復元・展開後登録が要確認のMSIXは、自動起動でデータを変更させない。
        if (stopped && before != null && !(tool.Msix != null && result.State == "要確認"))
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
