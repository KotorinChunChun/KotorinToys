namespace KotorinToys;

internal sealed class InstallationForm:Form
{
    readonly ToolManager manager;
    readonly ToolDefinition[] tools;
    readonly CheckedListBox targets=new(){Dock=DockStyle.Left,Width=240,CheckOnClick=true};
    readonly TextBox results=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
    readonly FlowLayoutPanel buttons=new(){Dock=DockStyle.Bottom,Height=85,Padding=new(8)};
    readonly Label source=new(){Dock=DockStyle.Top,Height=75,Padding=new(8),Text="未公開のローカル配布物を使用します。台帳とSHA-256を照合しますが、ローカルビルドは署名されていません。信頼できるビルドの台帳を選択してください。"};
    string? catalogPath;
    CancellationTokenSource? cancellation;
    bool running;
    public InstallationForm(ToolManager toolManager)
    {
        manager=toolManager;tools=manager.Load();
        Text=WindowTitle.Create("導入・削除");Size=new(820,480);MinimumSize=new(720,420);Font=new("Yu Gothic UI",11);StartPosition=FormStartPosition.CenterParent;
        foreach(var tool in tools)targets.Items.Add(tool.Name+"："+manager.Installation(tool).State);
        var choose=new Button{Text="配布台帳を選択",AutoSize=true};
        choose.Click+=(_,_)=>
        {
            using var picker=new OpenFileDialog{Title="信頼できるローカルビルドのpackages.json",Filter="配布台帳|packages.json",CheckFileExists=true};
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            try{InstallerAdapter.Load(picker.FileName);catalogPath=picker.FileName;source.Text="ローカル配布台帳："+catalogPath+"\n未署名です。設定・履歴は保持します。";}
            catch(Exception ex){MessageBox.Show(this,ex.Message,WindowTitle.Create("配布台帳の確認"));}
        };
        buttons.Controls.Add(choose);
        Add("選択をインストール","install");Add("選択をアンインストール","uninstall");
        var cancel=new Button{Text="残りをキャンセル",AutoSize=true};cancel.Click+=(_,_)=>cancellation?.Cancel();buttons.Controls.Add(cancel);
        Controls.Add(results);Controls.Add(targets);Controls.Add(source);Controls.Add(buttons);
        FormClosing+=(_,e)=>{if(running){e.Cancel=true;cancellation?.Cancel();results.AppendText("進行中の処理が終わるまで閉じません。残りの対象は取り消します。\r\n");}};
    }
    void Add(string label,string operation)
    {
        var button=new Button{Text=label,AutoSize=true};buttons.Controls.Add(button);
        button.Click+=async(_,_)=>
        {
            if(running)return;
            var selected=targets.CheckedIndices.Cast<int>().Select(i=>tools[i]).ToArray();
            if(selected.Length==0 || operation=="install" && catalogPath==null){results.AppendText("対象と必要な配布台帳を選択してください。\r\n");return;}
            var confirmation=string.Join("、",selected.Select(t=>t.Name))+"\n"+label+"します。設定・履歴を保持し、正常終了の拒否時は中止します。\n"+(operation=="install"?"未署名のローカルビルドを実行します。":"対象の管理予約を整理します。外部予約は変更しません。");
            if(MessageBox.Show(this,confirmation,WindowTitle.Create("対象の確認"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            running=true;targets.Enabled=false;cancellation=new();
            try
            {
                var progress=new Progress<InstallResult>(result=>
                {
                    if(!IsDisposed)results.AppendText(result.Id+"："+result.State+" / "+result.Detail+"\r\n");
                });
                await new InstallationQueue(new InstallerAdapter(manager)).Run(selected,operation,catalogPath,progress,cancellation:cancellation.Token);
            }
            catch(Exception ex){results.AppendText("操作失敗："+ex.Message+"\r\n");}
            finally{running=false;targets.Enabled=true;cancellation.Dispose();cancellation=null;}
        };
    }
}
