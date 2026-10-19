namespace KotorinToys;

internal sealed partial class Dashboard
{
    internal void ApplySnapshot(InstallationState[] actual, ToolState[] execution, PublicRelease[] published)
    {
        if (actual.Length != tools.Length || execution.Length != tools.Length || published.Length != tools.Length) throw new ArgumentException("状態件数が一致しません。");
        installations = actual; states = execution; releases = published;
        for (int i = 0; i < tools.Length; i++)
        {
            var version = ToolVersionView.Create(actual[i], published[i]); var row = grid.Rows[i];
            row.Cells["installed"].Value = version.Installed; row.Cells["published"].Value = version.Published;
            row.Cells["published"].ToolTipText = published[i].Detail;
            row.Cells["update"].Value = version.Update; row.Cells["state"].Value = execution[i].State;
        }
        UpdateDetails(); UpdateActions();
    }
    void UpdateDetails()
    {
        var issue = string.Join("\n", new[] { catalogError, stateError }.Where(text => !string.IsNullOrWhiteSpace(text)));
        grid.AccessibleDescription = string.IsNullOrEmpty(issue) && tools.Length == 0 ? "登録ツールなし（configで一覧が空に設定されています）" : issue;
        if (hostActions.Controls.Find("refresh", false).SingleOrDefault() is Button refresh)
            tooltips.SetToolTip(refresh, string.IsNullOrEmpty(issue) ? "実行状態・GitHubの一覧・公開版を更新します。" : issue);
        for (int i = 0; i < tools.Length; i++)
        {
            grid.Rows[i].Cells["startup"].Value = startupSnapshots[i].Text;
            grid.Rows[i].Cells["startup"].ToolTipText = startupSnapshots[i].Detail;
            var texts = new[] { states[i].Detail, installations[i].State != "導入済み" ? installations[i].Detail : null,
                !releases[i].Confirmed || releases[i].Package == null ? releases[i].Detail : null,
                startupSnapshots[i].NeedsAttention ? startupSnapshots[i].Detail : null,
                operationErrors.GetValueOrDefault(tools[i].Id), issue };
            var detail = string.Join("\n", texts.Where(text => !string.IsNullOrWhiteSpace(text)).Distinct());
            grid.Rows[i].Cells["detail"].Value = detail;
            grid.Rows[i].Cells["detail"].ToolTipText = detail;
        }
    }
    void ShowOperationFailure(string message, string? toolId = null)
    {
        if (IsDisposed) return;
        if (toolId != null) operationErrors[toolId] = message;
        UpdateDetails(); notify(message);
    }
    void UpdateActions()
    {
        int i = grid.CurrentRow?.Index ?? -1;
        var repository = i >= 0 && i < tools.Length ? ToolRepository.Find(tools[i]) : null;
        ActionHints.Apply(github, busy || refreshingCatalog ? ActionAvailability.Deny("処理中です。完了するまでお待ちください。") :
            repository != null ? ActionAvailability.Allow(repository.AbsoluteUri) : ActionAvailability.Deny("GitHub公開先が未設定、またはツールが未選択です。"), tooltips);
        foreach (var name in new[] { "start", "stop", "manual" })
            if (actions.Controls.Find(name, false).SingleOrDefault() is Button button)
                ActionHints.Apply(button, Availability(i, name), tooltips);
        ActionHints.Apply(install, Availability(i, "install"), tooltips);
        ActionHints.Apply(update, Availability(i, "update"), tooltips);
        ActionHints.Apply(uninstall, Availability(i, "uninstall"), tooltips);
    }

    ActionAvailability Availability(int index, string operation, bool duringOperation = false)
    {
        if (index < 0 || index >= tools.Length) return ActionAvailability.Deny("先に対象アプリを選択してください。");
        var tool = tools[index]; var installed = installations[index];
        bool Exists(Func<string?> path) { try { var value = path(); return !string.IsNullOrEmpty(value) && fileExists(value); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; } }
        var exe = Exists(() => installed.State is "導入済み" or "更新が必要" ? installed.Executable : installed.State == "未導入" && tool.Msix == null ? PathSafety.Resolve(manager.DevelopmentRoot(tool), tool.Executable) : null);
        var manual = Exists(() => PathSafety.Resolve(tool.Msix != null ? manager.DevelopmentRoot(tool) : installed.Directory ?? manager.DevelopmentRoot(tool), tool.Manual));
        return ToolActionPolicy.Evaluate(operation, tool, installed, states[index], releases[index], exe, manual,
            Exists(() => installed.Uninstaller), (!duringOperation && busy) || refreshingCatalog, checkingReleases);
    }
}
