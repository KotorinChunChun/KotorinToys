using KotorinToys;

internal static class InstallationModeTests
{
    public static void Run(string root,Action<bool,string> check)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        try
        {
            int executions=0,confirmations=0;var wait=new TaskCompletionSource<ToolState>();
            using var form=new InstallationForm(new(root),releases:ReleaseDownloader.Load(root),confirm:_=>{confirmations++;return false;},
                execute:(_,_,_,_,_,_,_)=>{executions++;throw new Exception("実導入しません。");},
                detectInstallation:_=>new("導入済み","0.0.1",root,"test.exe","unins000.exe"),
                executionState:_=>wait.Task,fileExists:_=>true);
            form.CreateControl();form.PerformLayout();
            var tabs=(TabControl)form.Controls.Find("installationModes",true).Single();
            _=tabs.Handle;
            check(tabs.TabPages.Cast<TabPage>().Select(p=>p.Text).SequenceEqual(["インストール・更新","アンインストール","FFmpeg","開発者向け"]),"一括画面を4つの用途別タブへ分離");
            var refreshing=form.RefreshAvailability();check(!tabs.Enabled,"状態確認中は操作タブを切替不可");
            wait.SetResult(new("停止中"));Pump(refreshing);
            var targets=(CheckedListBox)form.Controls.Find("installationTargets",true).Single();
            var install=(Button)form.Controls.Find("batchInstall",true).Single();var uninstall=(Button)form.Controls.Find("batchUninstall",true).Single();
            targets.SetItemChecked(0,true);check(install.Enabled && !uninstall.Enabled,"導入ページでは削除操作を有効にしない");
            tabs.SelectedIndex=1;
            check(targets.CheckedItems.Count==0 && !install.Enabled && !uninstall.Enabled,"タブ切替時はチェックをリセットし操作の取り違えを防ぐ");
            targets.SetItemChecked(0,true);check(uninstall.Enabled && !install.Enabled,"削除ページでは削除だけ実行可能");
            typeof(Button).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(uninstall,[EventArgs.Empty]);
            Application.DoEvents();check(confirmations==1 && executions==0,"一括削除も確認取消で実行しない");
            tabs.SelectedIndex=2;check(!targets.Visible && targets.CheckedItems.Count==0,"FFmpegページはアプリの対象チェックを隠す");
            check(form.Controls.Find("installFfmpeg",true).Single().Parent!.Parent!.Name=="ffmpegMode","FFmpegは専用ページだけへ配置");
            tabs.SelectedIndex=3;check(form.Controls.Find("chooseLocalCatalog",true).Single().Parent!.Parent!.Name=="developerMode","ローカル台帳は開発者向けページへ分離");
            tabs.SelectedIndex=0;check(targets.CheckedItems.Count==0 && tabs.Enabled,"戻った後も選び直すまで一括操作しない");
            form.Size=form.MinimumSize;form.PerformLayout();check(form.Controls.Find("installationResults",true).Single().Height>=100,"タブ追加後も最小サイズで結果を読める");
        }
        finally{SynchronizationContext.SetSynchronizationContext(previous);}
    }
    static void Pump(Task task)
    {
        var until=DateTime.UtcNow.AddSeconds(15);
        while(!task.IsCompleted && DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(1);}
        if(!task.IsCompleted)throw new TimeoutException("タブ状態の試験が完了しません。");task.GetAwaiter().GetResult();
    }
}
