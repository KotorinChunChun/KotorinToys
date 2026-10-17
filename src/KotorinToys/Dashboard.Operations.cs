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
            var index = grid.CurrentRow?.Index ?? -1;
            if (index < 0 || index >= tools.Length) return;
            var tool = tools[index];
            if (command == DashboardCommand.Stop && states[index].State == "停止中") return;
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
        if (updateOnly) { await OpenToolInstallation("update"); return; }
        if (!TryBeginOperation()) return;
        try
        {
            await RefreshReleases();
            if (!IsDisposed) showInstaller(null, ConfirmedCatalog());
            await RefreshStates(true);
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "詳細設定を開けません：" + ex.Message; }
        finally { EndOperation(); }
    }

    ReleaseCatalog? ConfirmedCatalog()
    {
        var packages = releases.Where(r => r.Confirmed && r.Package != null).Select(r => r.Package!).ToArray();
        return packages.Length > 0 ? new(1, packages) : null;
    }

    internal async Task OpenToolInstallation(string requested)
    {
        if (requested is not ("install" or "update" or "uninstall")) throw new ArgumentException("未対応の操作です。", nameof(requested));
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
            var catalog = ConfirmedCatalog();
            string? reason = requested == "uninstall" ? ToolInstallationRunner.UnsupportedReason(tool) : ToolInstallationRunner.PublicReason(tool, catalog);
            if (reason != null) { operation.Text = tool.Name + "：" + reason; return; }
            if (requested == "uninstall")
            {
                if (actual.State != "導入済み" || actual.Uninstaller == null)
                { operation.Text = tool.Name + "：正式導入と削除プログラムを確認できません。"; return; }
            }
            else if (requested == "install" ? actual.State != "未導入" : !ToolVersionView.Create(actual, releases[index]).CanUpdate)
            { operation.Text = tool.Name + "：導入状態または公開版が変わりました。状態を更新して確認してください。"; return; }
            showToolInstaller(tool.Id, requested, catalog);
            await RefreshStates(true);
        }
        catch (Exception ex) { if (!IsDisposed) operation.Text = "導入画面を開けません：" + ex.Message; }
        finally { EndOperation(); }
    }
    bool TryBeginOperation()
    {
        if (busy || refreshingCatalog || IsDisposed) return false;
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
