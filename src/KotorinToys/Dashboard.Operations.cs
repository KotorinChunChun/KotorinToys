namespace KotorinToys;

internal sealed partial class Dashboard
{
    internal void OpenRepository()
    {
        if (busy || IsDisposed) return;
        var index = grid.CurrentRow?.Index ?? -1;
        if (index < 0 || index >= tools.Length) return;
        var tool = tools[index];
        var repository = ToolRepository.Find(tool);
        if (repository == null) { ShowOperationFailure(tool.Name + "：GitHub公開先が未設定です。", tool.Id); return; }
        if (!TryBeginOperation()) return;
        try
        {
            openRepository(repository);
            operationErrors.Remove(tool.Id); UpdateDetails();
        }
        catch (Exception ex) { ShowOperationFailure("GitHubを開けません：" + ex.Message, tool.Id); }
        finally { EndOperation(); }
    }

    async Task Execute(DashboardCommand command)
    {
        if (!TryBeginOperation()) return;
        try
        {
            var index = grid.CurrentRow?.Index ?? -1;
            if (index < 0 || index >= tools.Length) return;
            var tool = tools[index]; operationErrors.Remove(tool.Id);
            await RefreshStates(true);
            var availability = Availability(index, command.ToString().ToLowerInvariant(), true);
            if (!availability.Enabled) { ShowOperationFailure(tool.Name + "：" + availability.Reason, tool.Id); return; }
            if (command == DashboardCommand.Manual)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PathSafety.Resolve(manager.DocumentRoot(tool), tool.Manual)) { UseShellExecute = true });
            else
            {
                grid.Rows[index].Cells["state"].Value = command == DashboardCommand.Start ? "起動処理中" : "停止処理中";
                var state = command == DashboardCommand.Start ? await manager.Start(tool) : await manager.Stop(tool);
                ReportExecution(tool, command == DashboardCommand.Start, state);
            }
            await RefreshStates(true);
        }
        catch (Exception ex) { ShowOperationFailure("操作失敗：" + ex.Message); }
        finally { EndOperation(); }
    }
    internal void ReportExecution(ToolDefinition tool, bool starting, ToolState state)
    {
        if (state.State != (starting ? "実行中" : "停止中"))
            ShowOperationFailure($"{tool.Name}：{state.State}\n{state.Detail}", tool.Id);
    }
    internal async Task OpenInstallation(bool updateOnly)
    {
        if (updateOnly) { await OpenToolInstallation("update"); return; }
        if (!TryBeginOperation()) return;
        try
        {
            await RefreshReleases();
            if (!IsDisposed) showInstaller(null, ConfirmedCatalog());
            await RefreshStates(true);
        }
        catch (Exception ex) { ShowOperationFailure("詳細設定を開けません：" + ex.Message); }
        finally { EndOperation(); }
    }

    ReleaseCatalog? ConfirmedCatalog()
    {
        var packages = releases.Where(r => r.Confirmed && r.Package != null).Select(r => r.Package!).ToArray();
        return packages.Length > 0 ? new(1, packages) : null;
    }

    internal async Task OpenToolInstallation(string requested)
    {
        if (requested is not ("install" or "update" or "repair" or "uninstall")) throw new ArgumentException("未対応の操作です。", nameof(requested));
        if (busy || refreshingCatalog || checkingReleases || IsDisposed) return;
        var index = grid.CurrentRow?.Index ?? -1;
        if (index < 0 || index >= tools.Length) return;
        var tool = tools[index];
        if (requested == "update" && !ToolVersionView.Create(installations[index], releases[index]).CanUpdate) return;
        if (!TryBeginOperation()) return;
        try
        {
            if (requested != "uninstall") await RefreshReleases();
            await RefreshStates(true);
            if (IsDisposed) return;
            var actual = installations[index];
            var availability = Availability(index, requested, true);
            if (!availability.Enabled) { ShowOperationFailure(tool.Name + "：" + availability.Reason, tool.Id); return; }
            var catalog = ConfirmedCatalog();
            string? reason = requested == "uninstall" ? ToolInstallationRunner.UnsupportedReason(tool) : ToolInstallationRunner.PublicReason(tool, catalog);
            if (reason != null) { ShowOperationFailure(tool.Name + "：" + reason, tool.Id); return; }
            if (requested == "uninstall")
            {
                if (!ToolInstallationRunner.CanUninstall(tool, actual))
                { ShowOperationFailure(tool.Name + (tool.Msix != null ? "：署名付きMSIXの正式登録を確認できません。" : "：正式導入と削除プログラムを確認できません。"), tool.Id); return; }
            }
            else if (requested != "repair" && (requested == "install" ? actual.State != "未導入" : !ToolVersionView.Create(actual, releases[index]).CanUpdate))
            { ShowOperationFailure(tool.Name + "：導入状態または公開版が変わりました。状態を更新して確認してください。", tool.Id); return; }
            operationErrors.Remove(tool.Id);
            showToolInstaller(tool.Id, requested, catalog);
            await RefreshStates(true);
        }
        catch (Exception ex) { ShowOperationFailure("導入画面を開けません：" + ex.Message); }
        finally { EndOperation(); }
    }
    bool TryBeginOperation()
    {
        if (busy || refreshingCatalog || IsDisposed) return false;
        busy = true;
        actions.Enabled = hostActions.Enabled = false;
        UpdateActions();
        return true;
    }

    void EndOperation()
    {
        busy = false;
        if (IsDisposed) return;
        actions.Enabled = hostActions.Enabled = true;
        UpdateActions();
    }

    enum DashboardCommand { Start, Stop, Manual }
}
