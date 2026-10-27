namespace KotorinToys;

internal sealed partial class Dashboard
{
    internal void ApplySnapshot(InstallationState[] actual, ToolState[] execution, PublicRelease[] published)
    {
        if (UiClosed) return;
        if (actual.Length != tools.Length || execution.Length != tools.Length || published.Length != tools.Length) throw new ArgumentException("状態件数が一致しません。");
        installations = actual; states = execution; releases = published;
        for (int i = 0; i < tools.Length; i++)
        {
            var version = ToolVersionView.Create(actual[i], published[i]); var row = RowFor(i);
            row.Cells["installed"].Value = version.Installed;
            row.Cells["update"].Value = DashboardText.Update(tools[i], actual[i], published[i]);
            row.Cells["update"].ToolTipText = version.Published + "\n" + version.Update + "\n" + ReleaseProblems.Explanation(published[i]);
            row.Cells["state"].Value = actual[i].State == "未導入" ? "-" : execution[i].State;
        }
        UpdateDetails(); ApplyView(); UpdateActions();
    }
    void UpdateDetails()
    {
        if (UiClosed) return;
        var issue = string.Join("\n", new[] { catalogError, stateError }.Where(text => !string.IsNullOrWhiteSpace(text)));
        grid.AccessibleDescription = string.IsNullOrEmpty(issue) && tools.Length == 0 ? "登録ツールなし（configで一覧が空に設定されています）" : issue;
        if (hostActions.Controls.Find("refresh", false).SingleOrDefault() is Button refresh)
            tooltips.SetToolTip(refresh, string.IsNullOrEmpty(issue) ? "実行状態・一覧・公開版を更新します。公開情報は5分のキャッシュを使い、期限後に再取得します。取得制限の解除待ち中は通信しません。" : issue);
        for (int i = 0; i < tools.Length; i++)
        {
            var row = RowFor(i);
            row.Cells["startup"].Value = installations[i].State == "未導入" ? "-" : startupSnapshots[i].Text;
            row.Cells["startup"].ToolTipText = startupSnapshots[i].Detail;
            var texts = new[] { states[i].Detail, installations[i].State != "導入済み" ? installations[i].Detail : null,
                !releases[i].Confirmed || releases[i].Package == null ? ReleaseProblems.Explanation(releases[i]) : null,
                startupSnapshots[i].NeedsAttention ? startupSnapshots[i].Detail : null,
                operationErrors.GetValueOrDefault(tools[i].Id), issue };
            var detail = string.Join("\n", texts.Append(DashboardText.Advice(tools[i], installations[i], states[i], releases[i])).Where(text => !string.IsNullOrWhiteSpace(text)).Distinct());
            row.Cells["detail"].Value = DashboardText.Detail(tools[i], installations[i], states[i], releases[i], startupSnapshots[i], operationErrors.GetValueOrDefault(tools[i].Id), catalogError, stateError);
            if (installations[i].State == "未導入")
            {
                if (row.Cells["detail"] is DataGridViewButtonCell) row.Cells["detail"] = new DataGridViewTextBoxCell();
                row.Cells["update"].Value = row.Cells["state"].Value = row.Cells["startup"].Value = row.Cells["detail"].Value = "-";
                row.Cells["detail"].ToolTipText = "未導入です。インストール可否はボタンにカーソルを合わせて確認できます。";
                row.Cells["startup"].ToolTipText = "導入後に設定できます。";
                row.Cells["detail"].ToolTipText += "\n" + detail;
                continue;
            }
            row.Cells["detail"].ToolTipText = detail;
            var locationRule = locations.Running(states[i]);
            bool location = locationRule.Enabled && row.Cells["detail"].Value?.ToString() == "別配置で実行中";
            if (location && row.Cells["detail"] is not DataGridViewButtonCell)
                row.Cells["detail"] = new DataGridViewButtonCell { FlatStyle = FlatStyle.Standard, Value = row.Cells["detail"].Value, ToolTipText = detail };
            else if (!location && row.Cells["detail"] is DataGridViewButtonCell)
                row.Cells["detail"] = new DataGridViewTextBoxCell { Value = row.Cells["detail"].Value, ToolTipText = detail };
            if (location) row.Cells["detail"].ToolTipText = locationRule.Reason + "\n詳細を読むには右クリックしてください。\n" + detail;
            else if (states[i].RunningExecutable != null) row.Cells["detail"].ToolTipText += "\n" + locationRule.Reason;
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
        if (UiClosed) return;
        grid.Enabled = !busy && !refreshingCatalog;
        actions.AccessibleDescription = hostActions.AccessibleDescription = busy || refreshingCatalog ? "処理中です。完了するまでお待ちください。" : null;
        int i = SelectedIndex;
        var repository = i >= 0 && i < tools.Length ? ToolRepository.Find(tools[i]) : null;
        ActionHints.Apply(github, busy || refreshingCatalog ? ActionAvailability.Deny("処理中です。完了するまでお待ちください。") :
            repository != null ? ActionAvailability.Allow(repository.AbsoluteUri) : ActionAvailability.Deny("GitHub公開先が未設定、またはツールが未選択です。"), tooltips);
        foreach (var name in new[] { "start", "stop", "manual" })
            if (actions.Controls.Find(name, false).SingleOrDefault() is Button button)
                ActionHints.Apply(button, Availability(i, name), tooltips);
        bool repair = i >= 0 && i < installations.Length && installations[i].State == "修復が必要";
        install.Text = repair ? "修復" : "インストール";
        var installRule = i < 0 || i >= tools.Length ? ActionAvailability.Deny("先に対象アプリを選択してください。") :
            repair ? Availability(i, "repair") : ToolActionPolicy.InstallEntry(tools[i], busy || refreshingCatalog || checkingReleases);
        ActionHints.Apply(install, installRule, tooltips);
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
