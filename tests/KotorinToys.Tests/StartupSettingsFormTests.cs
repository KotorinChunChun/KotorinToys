using KotorinToys;

internal static class StartupSettingsFormTests
{
    static void Pump(Task task)
    {
        var until=DateTime.UtcNow.AddSeconds(15);
        while(!task.IsCompleted && DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(1);}
        if(!task.IsCompleted)throw new TimeoutException("自動起動フォームの応答がありません。");
        task.GetAwaiter().GetResult();Application.DoEvents();
    }
    public static void Run(string root,string fixtures,Action<bool,string> check)
    {
        var context=SynchronizationContext.Current;
        try
        {
            var store=new HostSettingsStore(Path.Combine(fixtures,"startup-form"));store.Save(store.Load());
            var manager=new ToolManager(root);var tools=manager.Load().Take(3).ToArray();
            bool exists=true;
            InstallationState Detect(ToolDefinition tool)=>tool.Id==tools[0].Id?new("未導入",null,null,null):
                new("導入済み","1.0.0",root,tool.Id=="host"?Environment.ProcessPath!:Path.Combine(root,"fixture.exe"));
            var gateway=new StartupTests.FakeGateway();
            var coordinator=new StartupCoordinator(store,gateway,manager,Environment.ProcessPath!,installationDetector:Detect,fileExists:_=>exists);
            using var form=new StartupSettingsForm(store,coordinator,tools);form.CreateControl();Pump(form.Reload());form.PerformLayout();
            var grid=(DataGridView)form.Controls.Find("startupTools",true).Single();
            var apply=(Button)form.Controls.Find("applyToolStartup",true).Single();
            var recover=(Button)form.Controls.Find("recoverStartup",true).Single();
            check(grid.Columns.Cast<DataGridViewColumn>().All(c=>!string.IsNullOrWhiteSpace(c.HeaderCell.ToolTipText)) &&
                grid.Rows[1].Cells["mode"].ToolTipText.Contains("Toys経由") && grid.Rows[1].Cells["external"].ToolTipText.Contains("読取専用"),"全列とセルに自動起動の意味を説明するツールチップ");
            check(apply.AccessibleDescription!.Contains("ほかの行") && recover.AccessibleDescription!.Contains("再起動ボタンではありません"),"無効なボタンでも操作の目的と現在の可否を説明");
            check(grid.Rows[0].Cells["mode"].ReadOnly && grid.Rows[0].Cells["availability"].Value!.ToString()!.Contains("正式インストール"),"未導入行は方式をグレーアウトし理由を表示");
            check(!apply.Enabled && !recover.Enabled && recover.AccessibleDescription!.Contains("ありません"),"未変更の適用と不要な復旧を無効化");
            var before=File.ReadAllBytes(Path.Combine(store.DirectoryPath,"settings.json"));
            var rejected=coordinator.SetMode(tools[0],"via_kotorintoys").GetAwaiter().GetResult();
            check(!rejected.Success && rejected.Detail.Contains(tools[0].Name) && before.SequenceEqual(File.ReadAllBytes(Path.Combine(store.DirectoryPath,"settings.json"))) && gateway.Tasks.Count==0,"直接操作でも未導入対象を名前付き理由で拒否し設定・予約を保持");
            grid.Rows[1].Cells["mode"].Value="via_kotorintoys";
            grid.Rows[2].Cells["mode"].Value="direct_task";
            grid.CurrentCell=grid.Rows[1].Cells["mode"];
            check(apply.Enabled,"選択行の有効な変更だけを適用可能："+apply.AccessibleDescription);
            Pump(form.ApplySelectedSettings());
            check(store.Load().StartupModes.GetValueOrDefault(tools[1].Id)=="via_kotorintoys" && !store.Load().StartupModes.ContainsKey(tools[2].Id),"適用は選択行の1件だけを保存");
            check(grid.Rows[2].Cells["mode"].Value?.ToString()=="direct_task","別行の未適用値を勝手に自動起動なしへ戻さない");
            Pump(form.Reload());
            check(grid.Rows[2].Cells["mode"].Value?.ToString()=="direct_task" && !apply.Enabled,"再取得でも未保存の選択を保持し保存済み行の適用を無効にする");
            grid.CurrentCell=grid.Rows[2].Cells["mode"];Pump(form.ApplySelectedSettings());
            check(store.Load().StartupModes.GetValueOrDefault(tools[2].Id)=="direct_task","次の行も個別に保存できる");
            foreach (var msix in manager.Load().Where(t=>t.Msix!=null))
                check(!coordinator.ModeAvailability(msix,"direct_task").Enabled,msix.Name+"のMSIX直接タスクを設定前に拒否");
            exists=false;
            check(!coordinator.ModeAvailability(tools[1],"via_kotorintoys").Enabled && coordinator.ModeAvailability(tools[1],"none").Enabled,"EXE消失時は新規自動起動を拒否し解除の選択は残す");
            check(coordinator.SetMode(tools[1],"none").GetAwaiter().GetResult().Success && store.Load().StartupModes[tools[2].Id]=="direct_task","複数のEXEが消失しても選択対象だけを解除でき他の予約は保持");
            check(coordinator.SetMode(tools[2],"none").GetAwaiter().GetResult().Success && gateway.Tasks.Count==0,"最後の消失対象も所有条件を確認して解除できる");
            var help=form.Controls.Find("startupButtonHelp",true).Single();
            check(new[]{"本体設定を適用：","選択ツールの方式を適用：","未完了操作を復旧：","状態を再取得："}.All(help.Text.Contains),"画面内で4ボタンの意味を説明");
            form.Size=form.MinimumSize;form.PerformLayout();
            check(grid.Height>=180 && help.Height>=100 && help.Bottom<=form.Controls.Find("startupMessage",true).Single().Top,"最小サイズでも説明と一覧が重ならない");
        }
        finally{SynchronizationContext.SetSynchronizationContext(context);}
    }
}
