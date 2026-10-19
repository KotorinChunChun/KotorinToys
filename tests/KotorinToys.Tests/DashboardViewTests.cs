using System.Reflection;
using KotorinToys;

internal static class DashboardViewTests
{
    public static void Run(string root,string fixtures,Action<bool,string> check)
    {
        var previous=SynchronizationContext.Current;
        try
        {
            var directory=Path.Combine(fixtures,"display-settings");Directory.CreateDirectory(directory);
            var settings=Path.Combine(directory,"settings.json");File.WriteAllText(settings,"起動予約の架空原本");
            var store=new DashboardViewStore(directory);var tools=ToolCatalog.Load(root);var catalog=ReleaseDownloader.Load(root);
            string? repository=null,details=null;
            using(var form=new Dashboard(root,viewStore:store,fileExists:_=>true,openRepository:uri=>repository=uri.AbsoluteUri,showDetails:(name,_)=>details=name))
            {
                var actual=tools.Select((_,i)=>i==0?new InstallationState("導入済み","0.0.1",root,"test.exe","unins000.exe"):new("未導入",null,null,null)).ToArray();
                var states=tools.Select(_=>new ToolState("停止中")).ToArray();
                var releases=tools.Select(t=>new PublicRelease(catalog.Packages.SingleOrDefault(p=>p.Package.Id==t.Id),true,true,"fixture")).ToArray();
                form.ApplySnapshot(actual,states,releases);form.Size=new(1400,800);form.CreateControl();form.PerformLayout();
                var grid=(DataGridView)form.Controls.Find("toolTable",true).Single();
                var filter=(ComboBox)form.Controls.Find("viewFilter",true).Single();
                filter.SelectedIndex=1;
                check(grid.Rows.Cast<DataGridViewRow>().Count(r=>r.Visible)==1 && (string?)grid.CurrentRow?.Tag==tools[0].Id,"導入済みで絞っても選択IDを維持");
                filter.SelectedIndex=2;
                check(grid.Rows.Cast<DataGridViewRow>().Count(r=>r.Visible)==1,"更新ありは照合済みの新しい版だけを表示");
                releases[0]=releases[0] with{Confirmed=false};form.ApplySnapshot(actual,states,releases);
                check(grid.Rows.Cast<DataGridViewRow>().All(r=>!r.Visible) && form.SelectedIndex==-1 && !form.Controls.Find("start",true).Single().Enabled,
                    "該当0件では選択を外し非表示行を操作させない");
                filter.SelectedIndex=0;releases[0]=releases[0] with{Confirmed=true};form.ApplySnapshot(actual,states,releases);
                typeof(DataGridView).GetMethod("OnColumnHeaderMouseClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(grid,[new DataGridViewCellMouseEventArgs(0,-1,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))]);
                var expected=tools.OrderBy(t=>t.Name,StringComparer.CurrentCultureIgnoreCase).First();
                check((string?)grid.Rows[0].Tag==expected.Id,"列見出しで一覧を並べ替える");
                grid.CurrentCell=grid.Rows[0].Cells["detail"];form.OpenSelectedDetails();
                check(details==expected.Name && tools[form.SelectedIndex].Id==expected.Id,"並べ替え後も表示行ではなく安定したIDで詳細を開く");
                form.OpenRepository();check(repository==ToolRepository.Find(expected)?.AbsoluteUri,"並べ替え後も選択したアプリの公開先を開く");
                actual[Array.FindIndex(tools,t=>t.Id==expected.Id)]=new("導入済み","7.6.5",root,"test.exe");form.ApplySnapshot(actual,states,releases);
                check(grid.Rows.Cast<DataGridViewRow>().Single(r=>Equals(r.Tag,expected.Id)).Cells["installed"].Value?.ToString()=="7.6.5","状態更新を並べ替え前の行番号へ誤配しない");
                grid.Columns["detail"]!.DisplayIndex=0;form.SaveView();
                check(File.ReadAllText(settings)=="起動予約の架空原本","表示の保存で起動予約・所有情報を書き換えない");
            }
            var saved=store.Load();check(saved.Width==1400 && saved.Height==800 && saved.Columns!.Single(c=>c.Name=="detail").Order==0 && saved.SortColumn=="tool","サイズ・列幅・列順・並べ替えを別ファイルへ保存");
            using(var restored=new Dashboard(root,viewStore:store))
            {
                check(restored.Size==new Size(1400,800) && ((DataGridView)restored.Controls.Find("toolTable",true).Single()).Columns["detail"]!.DisplayIndex==0,"次回の一覧でサイズと列順を復元");
            }
            File.WriteAllText(Path.Combine(directory,"dashboard-view.json"),"{broken");check(store.Load()==new DashboardView(),"破損した表示設定は起動を妨げず既定値へ戻す");
            check(new DashboardViewStore(directory).Save(new(Columns:[new("future-column",0,200)]))==null && store.Load().Columns!.Length==0,"未知の列設定を無害に無視する");
            File.WriteAllText(Path.Combine(directory,"dashboard-view.json"),"{\"schemaVersion\":1,\"width\":999999}");check(store.Load()==new DashboardView(),"極端なサイズで画面を壊さない");
        }
        finally{SynchronizationContext.SetSynchronizationContext(previous);}
    }
}
