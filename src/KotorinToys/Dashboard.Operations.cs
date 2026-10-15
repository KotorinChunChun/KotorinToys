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
        if (repository == null) { operation.Text = tool.Name + "：GitHub公開先が未設定です。"; return; }
        if (!TryBeginOperation()) return;
        try
        {
            openRepository(repository);
            operation.Text = "最終操作：" + tool.Name + " / GitHubを開く要求を送りました。";
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "GitHubを開けません：" + ex.Message; }
        finally { EndOperation(); }
    }

    async Task Execute(DashboardCommand command)
    {
        if (!TryBeginOperation()) return;
        try
        {
            var index = grid.CurrentRow?.Index ?? 0; var tool = tools[index];
            if (command == DashboardCommand.Manual)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PathSafety.Resolve(manager.DocumentRoot(tool), tool.Manual)) { UseShellExecute = true });
            else
            {
                grid.Rows[index].Cells["state"].Value = command == DashboardCommand.Start ? "起動処理中" : "停止処理中";
                var state = command == DashboardCommand.Start ? await manager.Start(tool) : await manager.Stop(tool);
                operation.Text = $"最終操作：{tool.Name} / {state.State} {state.Detail}";
            }
            await RefreshStates(true);
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "操作失敗：" + ex.Message; }
        finally { EndOperation(); }
    }
    internal async Task OpenInstallation(bool updateOnly)
    {
        if (busy || IsDisposed) return;
        var index = grid.CurrentRow?.Index ?? 0;
        if (!TryBeginOperation()) return;
        try
        {
            if (updateOnly)
            {
                await RefreshReleases();
                await RefreshStates(true);
                if (IsDisposed) return;
                if (!ToolVersionView.Create(installations[index], releases[index]).CanUpdate)
                {
                    operation.Text = tools[index].Name + "：" + ToolVersionView.Create(installations[index], releases[index]).Update;
                    return;
                }
            }
            var packages = releases.Where(r => r.Package != null).Select(r => r.Package!).ToArray();
            var catalog = packages.Length > 0 ? new ReleaseCatalog(1, packages) : null;
            showInstaller(updateOnly ? tools[index].Id : null, catalog);
            await RefreshStates(true);
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "導入画面を開けません：" + ex.Message; }
        finally { EndOperation(); }
    }
    bool TryBeginOperation()
    {
        if (busy) return false;
        busy = true;
        actions.Enabled = hostActions.Enabled = false;
        return true;
    }

    void EndOperation()
    {
        busy = false;
        if (IsDisposed) return;
        actions.Enabled = hostActions.Enabled = true;
        UpdateFooter();
    }

    enum DashboardCommand { Start, Stop, Manual }
}
