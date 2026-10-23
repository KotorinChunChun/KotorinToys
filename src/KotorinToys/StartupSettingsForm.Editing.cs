namespace KotorinToys;

internal sealed partial class StartupSettingsForm
{
    readonly ToolTip hints = new();
    readonly Dictionary<string, string> drafts = new();
    HostSettings saved = new();
    bool working, refreshing, loading = true, loaded;
    string? loadError;
    Task? pendingReload;
    bool Pending => store.RemovalPending || File.Exists(Path.Combine(store.DirectoryPath, "startup-operation.json"));

    void InitializeEditing()
    {
        hostStartup.Name="hostStartup"; showDashboard.Name="showDashboardAtLogon";
        grid.CurrentCellDirtyStateChanged += (_, _) => { if(grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, e) =>
        {
            if(loading || e.RowIndex < 0 || e.ColumnIndex != grid.Columns["mode"]!.Index) return;
            var id=tools[e.RowIndex].Id; var value=grid.Rows[e.RowIndex].Cells["mode"].Value?.ToString() ?? "none";
            if(value==saved.StartupModes.GetValueOrDefault(id,"none")) drafts.Remove(id); else drafts[id]=value;
            UpdateButtons();
        };
        grid.SelectionChanged += (_, _) => UpdateButtons();
        grid.CurrentCellChanged += (_, _) => UpdateButtons();
        hostStartup.CheckedChanged += (_, _) => UpdateButtons(); showDashboard.CheckedChanged += (_, _) => UpdateButtons();
        ActionHints.Attach(actions,hints); ActionHints.Attach(this,hints);
        FormClosing += (_, e) => { if(working || refreshing) { e.Cancel=true;message.Text="処理の完了までお待ちください。予約操作は強制中断しません。"; } };
        Disposed += (_, _) => hints.Dispose();
        UpdateButtons();
    }

    internal Task Reload()
    {
        if(pendingReload is { IsCompleted:false }) return pendingReload;
        return pendingReload=ReloadCore();
    }
    async Task ReloadCore()
    {
        refreshing=true;UpdateButtons();
        try
        {
            var next=store.Load();
            bool hostDraft=loaded && hostStartup.Checked!=saved.HostStartup;
            bool showDraft=loaded && showDashboard.Checked!=saved.ShowDashboardAtLogon;
            var states=await Task.WhenAll(tools.Select(coordinator.State));if(IsDisposed)return;
            var external=await Task.WhenAll(tools.Select(coordinator.ExternalReservations));if(IsDisposed)return;
            loading=true;
            if(!hostDraft)hostStartup.Checked=next.HostStartup;
            if(!showDraft)showDashboard.Checked=next.ShowDashboardAtLogon;
            saved=next;loadError=null;
            for(int i=0;i<tools.Length;i++)
            {
                var tool=tools[i];var current=next.StartupModes.GetValueOrDefault(tool.Id,"none");
                if(drafts.GetValueOrDefault(tool.Id)==current)drafts.Remove(tool.Id);
                var choices=new List<KeyValuePair<string,string>>{new("none","自動起動なし")};
                foreach(var (key,label) in new[]{("via_kotorintoys","Toys経由"),("direct_task","OSから直接")})
                    if(coordinator.ModeAvailability(tool,key).Enabled)choices.Add(new(key,label));
                var desired=drafts.GetValueOrDefault(tool.Id,current);
                if(choices.All(p=>p.Key!=desired))choices.Add(new(desired,desired=="direct_task"?"OSから直接（要確認）":"Toys経由（要確認）"));
                var cell=(DataGridViewComboBoxCell)grid.Rows[i].Cells["mode"];
                cell.DataSource=choices;cell.Value=desired;cell.ReadOnly=choices.Count==1;
                cell.Style.BackColor=cell.ReadOnly?SystemColors.Control:SystemColors.Window;
                cell.Style.ForeColor=cell.ReadOnly?SystemColors.GrayText:SystemColors.WindowText;
                var availability=coordinator.ModeAvailability(tool,"via_kotorintoys");
                var explanation=availability.Enabled ? drafts.ContainsKey(tool.Id)?"未保存です。選択行の方式を適用してください。":"方式を変更し、選択行の方式を適用してください。" : availability.Reason;
                grid.Rows[i].Cells["availability"].Value=explanation;cell.ToolTipText=ColumnHelp("mode")+"\n"+explanation;
                grid.Rows[i].Cells["actual"].Value=states[i].ActualState+"："+states[i].Detail;
                grid.Rows[i].Cells["actual"].ToolTipText=ColumnHelp("actual")+"\n"+states[i].Detail;
                grid.Rows[i].Cells["external"].Value=external[i].Length==0?"対象なし（タスク／Run／lnk）":string.Join(" / ",external[i].Select(e=>e.Source+"："+e.Name+"（"+e.State+"）"));
                grid.Rows[i].Cells["external"].ToolTipText=ColumnHelp("external")+"\n"+string.Join(Environment.NewLine,external[i].Select(e=>e.State+"："+e.Detail));
                grid.Rows[i].Cells["tool"].ToolTipText=ColumnHelp("tool");
                grid.Rows[i].Cells["availability"].ToolTipText=explanation;
            }
            loaded=true;
        }
        catch(Exception ex){loadError="設定を確認できません："+ex.Message;if(!IsDisposed)message.Text=loadError;}
        finally{loading=false;refreshing=false;if(!IsDisposed)UpdateButtons();}
    }

    async Task<StartupResult> ApplySelected()
    {
        grid.EndEdit();int index=grid.CurrentRow?.Index ?? -1;
        if(index<0 || index>=tools.Length)return new(false,"設定するアプリを選択してください。");
        var tool=tools[index];var mode=grid.Rows[index].Cells["mode"].Value?.ToString() ?? "none";
        var rule=coordinator.ModeAvailability(tool,mode);if(!rule.Enabled)return new(false,rule.Reason);
        var result=await coordinator.SetMode(tool,mode);
        if(result.Success)drafts.Remove(tool.Id);
        return result;
    }

    void UpdateButtons()
    {
        ActionAvailability Decide(ActionAvailability action) => working || refreshing ? ActionAvailability.Deny("処理中です。完了するまでお待ちください。") :
            !loaded ? ActionAvailability.Deny("先に設定の読取りが必要です。") : loadError!=null ? ActionAvailability.Deny(loadError) :
            Pending ? ActionAvailability.Deny("中断した予約変更があります。「未完了操作を復旧」を先に実行してください。") : action;
        void Set(string name,ActionAvailability rule)
        {
            string help = name switch { "applyHostStartup" => "上の2つのチェックを保存します。各アプリの方式は保存しません。",
                "applyToolStartup" => "選択行の方式だけを保存して予約へ反映します。ほかの行や実行中のアプリは変更しません。",
                "recoverStartup" => "中断した予約変更を記録に従って元へ戻します。アプリの再起動ボタンではありません。",
                _ => "保存済み設定とWindowsの予約を再確認します。未保存の編集は保持します。" };
            if(actions.Controls.Find(name,false).SingleOrDefault() is Button button)ActionHints.Apply(button,rule with { Reason = help + "\n" + rule.Reason },hints);
        }
        var host=coordinator.HostAvailability(hostStartup.Checked);
        if(host.Enabled && hostStartup.Checked==saved.HostStartup && showDashboard.Checked==saved.ShowDashboardAtLogon)host=ActionAvailability.Deny("上のチェック設定は保存済みです。変更すると保存できます。");
        Set("applyHostStartup",Decide(host));
        var index=grid.CurrentRow?.Index ?? -1;
        ActionAvailability selected=ActionAvailability.Deny("先に設定するアプリを選択してください。");
        if(index>=0 && index<tools.Length)
        {
            var tool=tools[index];var mode=grid.Rows[index].Cells["mode"].Value?.ToString() ?? "none";
            selected=coordinator.ModeAvailability(tool,mode);
            if(selected.Enabled && mode==saved.StartupModes.GetValueOrDefault(tool.Id,"none"))
                selected=ActionAvailability.Deny(grid.Rows[index].Cells["mode"].ReadOnly ? grid.Rows[index].Cells["availability"].Value?.ToString() ?? "自動起動は設定できません。" : "この行は保存済みです。方式を変更してください。");
        }
        Set("applyToolStartup",Decide(selected));
        Set("recoverStartup",working || refreshing ? ActionAvailability.Deny("処理中です。") : Pending ? ActionAvailability.Allow("中断した変更を、記録された変更前の状態へ復元します。") : ActionAvailability.Deny("復旧が必要な中断操作はありません。"));
        Set("refreshStartup",working || refreshing ? ActionAvailability.Deny("処理中です。") : ActionAvailability.Allow("保存済み設定と予約を読み直します。編集中の方式は保持します。"));
        bool editable=!working && !refreshing && loaded && loadError==null && !Pending;
        grid.Enabled=editable;
        var hostStart=coordinator.HostAvailability(true);
        hostStartup.Enabled=editable && (hostStart.Enabled || saved.HostStartup);
        showDashboard.Enabled=editable && hostStart.Enabled && (hostStartup.Checked || saved.StartupModes.Values.Contains("via_kotorintoys"));
        hostStartup.AccessibleDescription=hostStart.Reason;hints.SetToolTip(hostStartup,hostStart.Reason);
        showDashboard.AccessibleDescription=showDashboard.Enabled?"サインイン時に管理画面も開くかを選びます。":"本体または本体経由の自動起動を有効にしたときに選べます。";
        hints.SetToolTip(showDashboard,showDashboard.AccessibleDescription);
    }
}
