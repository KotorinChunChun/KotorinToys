using KotorinToys;

internal static class CompactDashboardTests
{
    public static void Run(string root,string fixtures,Action<bool,string> check,bool saveImage=false)
    {
        var previous=SynchronizationContext.Current;
        try
        {
            var tools=ToolCatalog.Load(root);var catalog=ReleaseDownloader.Load(root);
            var actual=tools.Select(t=>new InstallationState("導入済み",t.Version,root,Path.Combine(root,t.InstalledExecutable),Path.Combine(root,"unins000.exe"))).ToArray();
            var releases=tools.Select(t=>new PublicRelease(catalog.Packages.SingleOrDefault(p=>p.Package.Id==t.Id),true,true,"照合の長い説明"+new string('説',300))).ToArray();
            releases[0]=releases[0] with{Package=releases[0].Package! with{PublishedAt=DateTimeOffset.Parse("2026-10-19T00:00:00Z")}};
            int portable=Array.FindIndex(tools,t=>t.Id=="shortcut-key-counter");
            actual[portable]=new("未導入",null,null,null);
            releases[portable]=new(null,true,false,"ZIP公開済み・本体導入未対応",new("0.1.5",DateTimeOffset.Parse("2026-10-17T00:00:00Z"),"https://github.com/KotorinChunChun/ShortcutKeyCounter/releases",1,new string('a',64)));
            var states=tools.Select(_=>new ToolState("停止中")).ToArray();
            var settings=new HostSettingsStore(Path.Combine(fixtures,"compact-settings"));
            var startup=new StartupCoordinator(settings,new StartupTests.FakeGateway(),new(root),Environment.ProcessPath!,allowDevelopment:true);
            using var form=new Dashboard(root,settings,startup,notify:_=>{},fileExists:path=>!path.EndsWith("ShortcutKeyCounter.exe",StringComparison.OrdinalIgnoreCase));form.CreateControl();
            form.ApplySnapshot(actual,states,releases);form.PerformLayout();
            var grid=(DataGridView)form.Controls.Find("toolTable",true).Single();
            check(grid.Columns["installed"]!.HeaderText=="導入Ver" && !grid.Columns.Contains("published") && grid.Columns.Contains("category"),"公開Ver列を除去しカテゴリ列を追加");
            check(grid.Rows[0].Cells["update"].ToolTipText.Contains("2026/10/19 公開"),"公開日は更新列のツールチップへ保持");
            check(grid.Rows.Cast<DataGridViewRow>().All(r=>new[]{"なし","-","未対応"}.Contains(r.Cells["update"].Value?.ToString()) || r.Cells["update"].Value?.ToString()?.StartsWith("要(")==true),"更新列は対象版または短い状態だけを表示");
            check(new[]{"update","state","startup","detail"}.All(name=>grid.Rows[portable].Cells[name].Value?.ToString()=="-"),"未導入行はZIP案内や停止中を表示せずハイフンに統一");
            form.ReportExecution(tools[0],false,new("実行中",Detail:"停止を拒否しました。"+new string('長',2500)));
            grid.AutoResizeRows();
            check(grid.Rows[0].Cells["detail"].Value?.ToString()=="操作できません" && grid.Rows[0].Cells["detail"].ToolTipText.Contains("停止を拒否"),"長い操作エラーも短い詳細とツールチップへ分離");
            check(grid.Rows[0].Height<100 && grid.Columns["detail"]!.DefaultCellStyle.WrapMode==DataGridViewTriState.False,"長文の詳細で一覧の行が巨大化しない");
            check(form.Controls.Find("advancedSettings",true).Single().Text=="一括インストール","詳細設定ボタンを一括インストールへ改名");
            var missing=actual[0] with{State="未導入",Version=null};
            check(DashboardText.Update(tools[0],missing,releases[0])=="-","未導入表示を更新なしと混同しない");
            check(DashboardText.Update(tools[0],actual[0],releases[0] with{Confirmed=false})=="未対応" && DashboardText.Published(releases[0] with{Confirmed=false})=="—","未確認の同梱版を公開Verや最新と誤表示しない");
            if(saveImage)
            {
                grid.CurrentCell=grid.Rows[portable].Cells["tool"];form.PerformLayout();grid.AutoResizeRows();
                // 非表示FormのVisible継承で子が描かれないため、レイアウトだけを親なしで描く。
                // Form.Showやデスクトップ撮影、Shownのオンライン更新は実行しない。
                var layout=form.Controls.OfType<TableLayoutPanel>().Single();layout.Font=form.Font;form.Controls.Remove(layout);
                layout.Bounds=new Rectangle(Point.Empty,form.ClientSize);layout.CreateControl();layout.PerformLayout();grid.AutoResizeRows();
                using var bitmap=new Bitmap(layout.Width,layout.Height);layout.DrawToBitmap(bitmap,new Rectangle(Point.Empty,layout.Size));
                var path=Path.Combine(fixtures,"compact-dashboard.png");bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
                form.Controls.Add(layout);
                Console.WriteLine("fixture画面描画（実デスクトップ撮影ではありません）: "+path);
            }
        }
        finally{SynchronizationContext.SetSynchronizationContext(previous);}
    }
}
