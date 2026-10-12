namespace KotorinToys;

internal sealed class StartupSettingsForm : Form
{
    readonly HostSettingsStore store;
    readonly StartupCoordinator coordinator;
    readonly ToolDefinition[] tools;
    readonly CheckBox hostStartup=new(){Text="本体をサインイン時に開始する",AutoSize=true};
    readonly CheckBox showDashboard=new(){Text="サインイン時に管理画面も表示する",AutoSize=true};
    readonly DataGridView grid=new(){Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
    readonly Label message=new(){Dock=DockStyle.Bottom,Height=85,Padding=new Padding(10),AutoEllipsis=true};
    readonly FlowLayoutPanel actions=new(){Dock=DockStyle.Bottom,Height=52,Padding=new Padding(8)};
    public StartupSettingsForm(HostSettingsStore settings,StartupCoordinator startup,ToolDefinition[] definitions)
    {
        store=settings;coordinator=startup;tools=definitions;
        Text=WindowTitle.Create("本体設定・自動起動");Size=new(850,540);MinimumSize=new(720,450);Font=new("Yu Gothic UI",11);
        StartPosition=FormStartPosition.CenterParent;
        var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=96,Padding=new Padding(10),FlowDirection=FlowDirection.TopDown};
        top.Controls.Add(hostStartup);top.Controls.Add(showDashboard);
        top.Controls.Add(new Label{AutoSize=true,Text="経由起動は共有の本体予約を連動登録します。停止と予約解除は別操作です。"});
        grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;grid.ColumnHeadersDefaultCellStyle.Padding=new(4,8,4,8);
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="tool",HeaderText="ツール",ReadOnly=true});
        var mode=new DataGridViewComboBoxColumn{Name="mode",HeaderText="次回サインインの希望方式",DisplayMember="Value",ValueMember="Key",
            DataSource=new[]{new KeyValuePair<string,string>("none","自動起動なし"),new("via_kotorintoys","KotorinToys経由"),new("direct_task","直接タスク")}};
        grid.Columns.Add(mode);grid.Columns.Add(new DataGridViewTextBoxColumn{Name="actual",HeaderText="実予約",ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="external",HeaderText="外部登録（読取のみ）",ReadOnly=true});
        foreach(var tool in tools)grid.Rows.Add(tool.Name,"none","確認中","確認中");
        grid.DataError+=(_,args)=>{args.ThrowException=false;message.Text="設定値を読み取れません。原本を保持して再確認してください。";};
        Add("本体設定を適用",async()=>await coordinator.SetHost(hostStartup.Checked,showDashboard.Checked));
        Add("選択ツールの方式を適用",async()=>
        {
            grid.EndEdit();var index=grid.CurrentRow?.Index ?? 0;
            return await coordinator.SetMode(tools[index],grid.Rows[index].Cells["mode"].Value?.ToString()??"none");
        });
        Add("未完了操作を復旧",coordinator.Recover);
        var refresh=new Button{Text="状態を再取得",AutoSize=true};refresh.Click+=async(_,_)=>await RefreshSettings();actions.Controls.Add(refresh);
        Controls.Add(grid);Controls.Add(top);Controls.Add(actions);Controls.Add(message);
        Shown+=async(_,_)=>await RefreshSettings();
    }
    void Add(string text,Func<Task<StartupResult>> action)
    {
        var button=new Button{Text=text,AutoSize=true};actions.Controls.Add(button);
        button.Click+=async(_,_)=>
        {
            actions.Enabled=false;grid.Enabled=false;
            try{var result=await action();message.Text=(result.Success?"完了：":"未完了：")+result.Detail;await RefreshSettings();}
            catch(Exception ex){message.Text="設定操作失敗："+ex.Message;}
            finally{if(!IsDisposed){actions.Enabled=true;grid.Enabled=true;}}
        };
    }
    async Task RefreshSettings()
    {
        try
        {
            var settings=store.Load();hostStartup.Checked=settings.HostStartup;showDashboard.Checked=settings.ShowDashboardAtLogon;
            var attempt=store.LastAttempt;
            if(attempt!=null)message.Text="前回の経由開始（"+attempt.AttemptedAt.ToLocalTime().ToString("g")+"）："+string.Join(" / ",attempt.Items.Select(i=>i.ToolId+"："+i.Result+" "+i.Detail));
            var states=await Task.WhenAll(tools.Select(tool=>coordinator.State(tool)));if(IsDisposed)return;
            var external=await Task.WhenAll(tools.Select(tool=>coordinator.ExternalReservations(tool)));if(IsDisposed)return;
            for(int i=0;i<tools.Length;i++)
            {
                grid.Rows[i].Cells["mode"].Value=settings.StartupModes.GetValueOrDefault(tools[i].Id,"none");
                grid.Rows[i].Cells["actual"].Value=states[i].ActualState+"："+states[i].Detail;
                grid.Rows[i].Cells["external"].Value=external[i].Length==0?"対象なし（タスク／Run／lnk）":string.Join(" / ",external[i].Select(e=>e.Source+"："+e.Name));
                grid.Rows[i].Cells["external"].ToolTipText=string.Join(Environment.NewLine,external[i].Select(e=>e.Detail));
            }
        }
        catch(Exception ex){if(!IsDisposed)message.Text="設定を確認できません："+ex.Message;}
    }
}
