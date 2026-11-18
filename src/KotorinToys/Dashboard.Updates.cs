namespace KotorinToys;

internal sealed partial class Dashboard
{
    ActionAvailability UpdateAvailability(int[] selected, bool all)
    {
        if (busy || refreshingCatalog || BackgroundUpdateBusy) return ActionAvailability.Deny("処理中です。完了するまでお待ちください。");
        if (checkingReleases) return ActionAvailability.Deny("公開版を確認しています。");
        if (all)
            return Enumerable.Range(0, tools.Length).Any(i => MsixPolicy.Installed(installations[i]) && ToolActionPolicy.InstallEntry(tools[i]).Enabled)
                ? ActionAvailability.Allow("導入済みアプリの公開版を確認し、更新があるアプリを順にアップデートします。")
                : ActionAvailability.Deny("本体から更新できる導入済みアプリがありません。");
        if (selected.Length <= 1) return Availability(selected.Length == 1 ? selected[0] : -1, "update");
        return selected.Any(i => Availability(i, "update").Enabled)
            ? ActionAvailability.Allow("選択したアプリのうち、更新があるアプリを順にアップデートします。")
            : ActionAvailability.Deny("選択したアプリにアップデートできる公開版がありません。");
    }

    internal async Task UpdateTools(bool all)
    {
        if (UiClosed) return;
        var selected = all ? Enumerable.Range(0, tools.Length).ToArray() : SelectedRows();
        if (!UpdateAvailability(selected, all).Enabled || !TryBeginOperation()) return;
        preparingToolUpdates = true;
        SetUpdateStatus("更新を確認しています…");
        try
        {
            await RefreshReleases();
            // 処理開始前の定期確認は結果を反映しないため、完了を待って取得し直す。
            if (stateRefresh is { IsCompleted: false } pending) await pending;
            await RefreshStates(true);
            if (UiClosed) return;
            if (stateError != null) { SetUpdateStatus(stateError); return; }

            var targets = new List<ToolDefinition>();
            var results = new List<InstallResult>();
            bool uncheckedRelease = false;
            foreach (int i in selected)
            {
                var tool = tools[i];
                if (!MsixPolicy.Installed(installations[i]) || !ToolActionPolicy.InstallEntry(tool).Enabled) continue;
                if (!releases[i].Confirmed || releases[i].Package == null) uncheckedRelease = true;
                if (!ToolVersionView.Create(installations[i], releases[i]).CanUpdate) continue;
                var rule = Availability(i, "update", duringOperation: true);
                operationErrors.Remove(tool.Id);
                updateResults.Remove(tool.Id);
                if (rule.Enabled) targets.Add(tool);
                else results.Add(new(tool.Id, "update", "未実行", rule.Reason));
            }
            if (targets.Count == 0 && results.Count == 0)
            {
                SetUpdateStatus(uncheckedRelease ? "一部の公開版を確認できません。詳細欄を確認してください。" : "アップデートはありません。");
                return;
            }

            if (targets.Count > 0)
            {
                var catalog = new ReleaseCatalog(1, ConfirmedCatalog()!.Packages
                    .Where(item => targets.Any(tool => tool.Id == item.Package.Id)).ToArray());
                if (all || selected.Length > 1)
                {
                    var lines = targets.Select(tool =>
                    {
                        var index = Array.FindIndex(tools, candidate => candidate.Id == tool.Id);
                        var item = catalog.Packages.Single(package => package.Package.Id == tool.Id);
                        return $"{tool.Name}　{installations[index].Version} → {ReleaseDisplay.Version(item)}";
                    });
                    if (!confirmUpdates(string.Join("\n", lines) + "\n\nまとめてアップデートしますか？\nKotorinToysを終了しても処理は続きます。"))
                    {
                        SetUpdateStatus("アップデートを取り消しました。");
                        return;
                    }
                    if (UiClosed) return;
                }
                SetUpdateStatus($"バックグラウンドで更新中　{targets.Count}件");
                bool running = true;
                var progress = new Progress<InstallResult>(result =>
                {
                    if (UiClosed || !running || result.State != "処理中") return;
                    var tool = targets.First(t => t.Id == result.Id);
                    SetUpdateStatus($"更新中　{tool.Name}（{targets.IndexOf(tool) + 1}/{targets.Count}）");
                });
                // 取得・検証からインストーラー実行まで、既存の処理を1アプリずつ使う。
                try { results.AddRange(await runUpdates(targets, catalog, progress, lifetime.Token)); }
                finally { running = false; }
            }
            if (UiClosed) return;
            ShowUpdateResults(results, uncheckedRelease);
            await RefreshStates(true);
        }
        catch (Exception ex) { if (!UiClosed) SetUpdateStatus("アップデートを完了できませんでした。" + ex.Message); }
        finally
        {
            preparingToolUpdates = false;
            if (!UiClosed) UpdateDetails();
            EndOperation();
        }
    }

    void RestoreBackgroundUpdate()
    {
        if (updateSettings == null) return;
        var report = ToolUpdateJob.Latest(updateSettings);
        if (report == null) return;
        bool interrupted = !report.Finished && !BackgroundUpdateBusy;
        if (!interrupted && (report.Id, report.Finished) == (restoredUpdate?.Id, restoredUpdate?.Finished)) return;
        if (interrupted)
        {
            SetUpdateStatus("バックグラウンド更新が中断されました。導入状態を確認してください。");
            return;
        }
        restoredUpdate = report;
        if (report.Finished) ShowUpdateResults(report.Results);
        else SetUpdateStatus("バックグラウンドで更新中");
    }

    void ShowUpdateResults(IReadOnlyList<InstallResult> results, bool uncheckedRelease = false)
    {
        foreach (var result in results) updateResults[result.Id] = result;
        var counts = string.Join(" / ", results.GroupBy(r => r.State).Select(group => $"{group.Key} {group.Count()}件"));
        var guidance = results.Any(r => r.State is not ("成功" or "スキップ")) || uncheckedRelease ? "　詳細欄を確認してください。" : "";
        SetUpdateStatus("アップデート終了　" + counts + guidance);
    }

    void SetUpdateStatus(string text)
    {
        updateStatus.Text = text;
        updateStatus.Visible = true;
    }
}
