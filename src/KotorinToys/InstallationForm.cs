namespace KotorinToys;

internal sealed class InstallationForm:Form
{
    readonly ToolManager manager;
    readonly ToolDefinition[] tools;
    readonly CheckedListBox targets=new(){Dock=DockStyle.Left,Width=240,CheckOnClick=true};
    readonly TextBox results=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
    readonly FlowLayoutPanel buttons=new(){Dock=DockStyle.Bottom,Height=110,Padding=new(8)};
    const string PublicSource="GitHub Releasesの固定版を取得します。製品・版・容量・SHA-256を照合しますが、配布物は未署名の開発版です。SSCaptureの録画には別途FFmpegの配置が必要です。";
    readonly Label source=new(){Dock=DockStyle.Top,Height=85,Padding=new(8),Text=PublicSource};
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
        var published=new Button{Text="GitHub Releasesを使用",AutoSize=true};
        published.Click+=(_,_)=>{catalogPath=null;source.Text=PublicSource;};buttons.Controls.Add(published);buttons.Controls.Add(choose);
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
            if(selected.Length==0){results.AppendText("対象を選択してください。\r\n");return;}
            var confirmation=string.Join("、",selected.Select(t=>t.Name))+"\n"+label+"します。設定・履歴を保持し、正常終了の拒否時は中止します。\n"+(operation=="install"?(catalogPath==null?"GitHubから固定版を取得し、SHA-256を照合した未署名の開発版を実行します。":"未署名のローカルビルドを実行します。") :"対象の管理予約を整理します。外部予約は変更しません。");
            if(MessageBox.Show(this,confirmation,WindowTitle.Create("対象の確認"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            running=true;targets.Enabled=false;cancellation=new();foreach(Control control in buttons.Controls)if(control is Button b && b.Text!="残りをキャンセル")b.Enabled=false;
            try
            {
                var progress=new Progress<InstallResult>(result=>
                {
                    if(!IsDisposed)results.AppendText(result.Id+"："+result.State+" / "+result.Detail+"\r\n");
                });
                var prepared=catalogPath;
                if(operation=="install" && prepared==null)
                {
                    using var downloader=new ReleaseDownloader();
                    prepared=await downloader.Prepare(ReleaseDownloader.Load(manager.Root),selected,
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KotorinToys","packages"),
                        new Progress<string>(message=>{if(!IsDisposed)results.AppendText(message+"\r\n");}),cancellation.Token);
                }
                await new InstallationQueue(new InstallerAdapter(manager)).Run(selected,operation,prepared,progress,cancellation:cancellation.Token);
            }
            catch(OperationCanceledException){results.AppendText("取得または残りの処理をキャンセルしました。進行中のインストーラーは強制終了しません。\r\n");}
            catch(Exception ex){results.AppendText("操作失敗："+ex.Message+"\r\n");}
            finally
            {
                running=false;targets.Enabled=true;cancellation.Dispose();cancellation=null;foreach(Control control in buttons.Controls)control.Enabled=true;
                for(int i=0;i<tools.Length;i++){var selectedState=targets.GetItemChecked(i);targets.Items[i]=tools[i].Name+"："+manager.Installation(tools[i]).State;targets.SetItemChecked(i,selectedState);}
            }
        };
    }
}
