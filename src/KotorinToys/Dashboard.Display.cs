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
            var texts = new[] { states[i].Detail, installations[i].State != "導入済み" ? installations[i].Detail : null,
                !releases[i].Confirmed || releases[i].Package == null ? releases[i].Detail : null,
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
        github.Enabled = !busy && !refreshingCatalog && repository != null;
        github.AccessibleDescription = repository?.AbsoluteUri ?? "GitHub公開先が未設定です。";
        tooltips.SetToolTip(github, github.AccessibleDescription);
        foreach (var name in new[] { "start", "stop", "manual" })
            if (actions.Controls.Find(name, false).SingleOrDefault() is Button button)
                button.Enabled = !busy && !refreshingCatalog && i >= 0 && i < tools.Length && (name != "stop" || states[i].State != "停止中");
        install.Enabled = update.Enabled = uninstall.Enabled = false;
        if (i < 0 || i >= tools.Length) return;
        var version = ToolVersionView.Create(installations[i], releases[i]);
        bool idle = !busy && !refreshingCatalog && !checkingReleases;
        var candidate = releases[i].Confirmed && releases[i].Package != null
            ? new ReleaseCatalog(1, [releases[i].Package!]) : null;
        var reason = ToolInstallationRunner.PublicReason(tools[i], candidate);
        install.Enabled = idle && installations[i].State == "未導入" && reason == null;
        update.Enabled = idle && version.CanUpdate && reason == null;
        uninstall.Enabled = idle && installations[i].State == "導入済み" && installations[i].Uninstaller != null &&
            ToolInstallationRunner.UnsupportedReason(tools[i]) == null;
        tooltips.SetToolTip(install, reason ?? "未導入のツールをGitHub Releasesから導入します。");
        tooltips.SetToolTip(update, reason ?? (version.CanUpdate ? "新しい公開版へアップデートします。" : "導入済みで新しい公開版がある場合だけ利用できます。"));
        tooltips.SetToolTip(uninstall, "正式導入と削除プログラムを確認できる場合だけ利用できます。設定・履歴は保持します。");
    }
}
