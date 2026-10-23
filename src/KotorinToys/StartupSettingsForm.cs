namespace KotorinToys;

internal sealed partial class StartupSettingsForm : BrandedForm
{
    readonly HostSettingsStore store;
    readonly StartupCoordinator coordinator;
    readonly ToolDefinition[] tools;
    readonly CheckBox hostStartup=new(){Text="本体をサインイン時に開始する",AutoSize=true};
    readonly CheckBox showDashboard=new(){Text="サインイン時に管理画面も表示する",AutoSize=true};
    readonly DataGridView grid=new(){Name="startupTools",Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
    readonly Label message=new(){Name="startupMessage",Dock=DockStyle.Bottom,Height=65,Padding=new Padding(10),AutoEllipsis=true};
    readonly FlowLayoutPanel actions=new(){Dock=DockStyle.Bottom,Height=52,Padding=new Padding(8)};
    public StartupSettingsForm(HostSettingsStore settings,StartupCoordinator startup,ToolDefinition[] definitions)
    {
        store=settings;coordinator=startup;tools=definitions;
        Text=WindowTitle.Create("本体設定・自動起動");Size=new(1080,720);MinimumSize=new(920,650);Font=new("Yu Gothic UI",11);
        StartPosition=FormStartPosition.CenterParent;
        var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=96,Padding=new Padding(10),FlowDirection=FlowDirection.TopDown};
        top.Controls.Add(hostStartup);top.Controls.Add(showDashboard);
        top.Controls.Add(new Label{AutoSize=true,Text="アプリの方式は1件ずつ保存します。保存しても、今動いているアプリは停止しません。"});
        grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;grid.ColumnHeadersDefaultCellStyle.Padding=new(4,8,4,8);
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="tool",HeaderText="ツール",ReadOnly=true});
        var mode=new DataGridViewComboBoxColumn{Name="mode",HeaderText="次回サインインの希望方式",DisplayMember="Value",ValueMember="Key",
            DataSource=new[]{new KeyValuePair<string,string>("none","自動起動なし"),new("via_kotorintoys","Toys経由"),new("direct_task","OSから直接")}};
        grid.Columns.Add(mode);grid.Columns.Add(new DataGridViewTextBoxColumn{Name="actual",HeaderText="実予約",ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="external",HeaderText="外部登録（読取のみ）",ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="availability",HeaderText="設定できない理由・案内",ReadOnly=true,FillWeight=170});
        foreach (DataGridViewColumn column in grid.Columns) column.HeaderCell.ToolTipText = ColumnHelp(column.Name);
        grid.ShowCellToolTips = true;
        grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True; grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode=DataGridViewColumnSortMode.NotSortable;
        foreach(var tool in tools)grid.Rows.Add(tool.Name,"none","確認中","確認中");
        grid.DataError+=(_,args)=>{args.ThrowException=false;message.Text="設定値を読み取れません。原本を保持して再確認してください。";};
        Add("applyHostStartup","本体設定を適用",async()=>await coordinator.SetHost(hostStartup.Checked,showDashboard.Checked));
        Add("applyToolStartup","選択ツールの方式を適用",ApplySelected);
        Add("recoverStartup","未完了操作を復旧",coordinator.Recover);
        var refresh=new Button{Name="refreshStartup",Text="状態を再取得",AutoSize=true};refresh.Click+=async(_,_)=>await Reload();actions.Controls.Add(refresh);
        var help=new Label{Name="startupButtonHelp",Dock=DockStyle.Bottom,Height=115,Padding=new Padding(10),Text=
            "本体設定を適用：上のチェックを保存\n"+
            "選択ツールの方式を適用：選択行だけ保存\n"+
            "未完了操作を復旧：中断した変更を元に戻す\n"+
            "状態を再取得：予約を再確認（未保存の編集は保持）"};
        Controls.Add(grid);Controls.Add(top);Controls.Add(help);Controls.Add(actions);Controls.Add(message);
        InitializeEditing();
        Shown+=async(_,_)=>await Reload();
    }
    internal static string ColumnHelp(string name) => name switch
    {
        "tool" => "自動起動を設定するアプリです。行を選んで方式を変更します。",
        "mode" => "次回Windowsサインイン時の希望です。Toys経由：KotorinToysから起動。OSから直接：Windowsのタスクから起動。自動起動なし：予約解除。方式の変更後、選択行を適用してください。",
        "actual" => "Windowsに実際に登録された予約との照合結果です。希望方式を選んだだけでは予約は変わりません。",
        "external" => "本体以外が登録したタスク・スタートアップです。読取専用で、この画面から削除しません。",
        _ => "設定できない理由と、次に行う操作です。保存・予約変更とアプリの起動停止は別です。"
    };
    void Add(string name,string text,Func<Task<StartupResult>> action)
    {
        var button=new Button{Name=name,Text=text,AutoSize=true};actions.Controls.Add(button);
        button.Click+=async(_,_)=>await RunAction(button,action);
    }

    async Task RunAction(Button button,Func<Task<StartupResult>> action)
    {
        if (working || refreshing || !button.Enabled) return;
        working=true;UpdateButtons();
        try{var result=await action();message.Text=(result.Success?"完了：":"未完了：")+result.Detail;await Reload();}
        catch(Exception ex){message.Text="設定操作失敗："+ex.Message;}
        finally{working=false;if(!IsDisposed)UpdateButtons();}
    }
    internal Task ApplySelectedSettings()=>RunAction((Button)actions.Controls.Find("applyToolStartup",false).Single(),ApplySelected);
}
