namespace KotorinToys;
internal sealed class Dashboard : Form
{
    readonly ToolManager manager;
    readonly ToolDefinition[] tools;
    readonly DataGridView grid = new() { Dock=DockStyle.Fill, ReadOnly=true, AllowUserToAddRows=false, AllowUserToDeleteRows=false,
        SelectionMode=DataGridViewSelectionMode.FullRowSelect, MultiSelect=false, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible=false };
    readonly Label result = new() { Dock=DockStyle.Bottom, Height=65, Padding=new Padding(12), AutoEllipsis=true };
    readonly System.Windows.Forms.Timer timer = new() { Interval=4000 };
    readonly FlowLayoutPanel actions = new() { Dock=DockStyle.Bottom, Height=55, Padding=new Padding(8) };
    bool busy;
    public Dashboard(string root)
    {
        manager = new(root); tools=manager.Load();
        Text=WindowTitle.Create(); Size=new Size(880,480); MinimumSize=new Size(650,350);
        Font=new Font("Yu Gothic UI",11); StartPosition=FormStartPosition.CenterScreen;
        grid.Columns.Add("tool","ツール"); grid.Columns.Add("state","現在の状態"); grid.Columns.Add("detail","詳細");
        foreach(var tool in tools) grid.Rows.Add(tool.Name,"確認中","");
        foreach(var (label, command) in new[] { ("起動 / 画面を開く","start"),("正常停止","stop"),("状態を更新","refresh"),("使い方","manual") })
        {
            var button = new Button { Text=label, AutoSize=true, Height=35, Name=command };
            button.Click += async (_,_) => await Execute(command);
            actions.Controls.Add(button);
        }
        Controls.Add(grid); Controls.Add(actions); Controls.Add(result);
        result.Text="ツールの通知領域アイコンを維持します。本体を閉じても各ツールは動作します。";
        Shown += async (_,_) => await Execute("refresh");
        timer.Tick += async (_,_) => { if(!busy) await Execute("refresh"); }; timer.Start();
        FormClosed += (_,_) => timer.Dispose();
    }
    async Task Execute(string command)
    {
        if(busy) return;
        busy=true; actions.Enabled=false;
        try
        {
            var index=grid.CurrentRow?.Index ?? 0;
            var tool=tools[index];
            if(command=="manual")
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(manager.Resolve(tool.Manual)) { UseShellExecute=true });
            else if(command!="refresh")
            {
                grid.Rows[index].Cells[1].Value=command=="start" ? "起動処理中" : "停止処理中";
                var state=command=="start" ? await manager.Start(tool) : await manager.Stop(tool);
                result.Text=tool.Name+"："+state.State+" "+state.Detail;
            }
            var states=await Task.WhenAll(tools.Select(manager.Status));
            if(IsDisposed) return;
            for(int i=0;i<tools.Length;i++) { grid.Rows[i].Cells[1].Value=states[i].State; grid.Rows[i].Cells[2].Value=states[i].Detail; }
        }
        catch(Exception ex) { if(!IsDisposed) result.Text="操作失敗："+ex.Message; }
        finally { busy=false; if(!IsDisposed) actions.Enabled=true; }
    }
}
