using KotorinToys;
using System.Reflection;

internal static class DashboardSearchTests
{
    static T Find<T>(Control form,string name) where T:Control => (T)form.Controls.Find(name,true).Single();
    // 非表示fixtureのPerformClickはCanSelect=falseで処理しないため、入力イベントを直接配送する。
    static void Click(Button button)
    {
        if(!button.Enabled)throw new InvalidOperationException("無効なボタンを試験から操作しません。");
        typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(button,[EventArgs.Empty]);
    }
    static void Pump(Task task)
    {
        var until=DateTime.UtcNow.AddSeconds(15);
        while(!task.IsCompleted && DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(1);}
        if(!task.IsCompleted)throw new TimeoutException("絞り込み試験が完了しません。");
        task.GetAwaiter().GetResult();
    }
    public static void Run(string root,string fixtures,Action<bool,string> check)
    {
        var sample=new ToolDefinition("sample","SampleApp","sample.exe","README.md",Summary:"画像の記録と検索",Category:"撮影, AI用記録、撮影");
        check(ToolSearch.Tags(sample).SequenceEqual(["撮影","AI用記録"]),"カテゴリは複数タグの重複を除去する");
        check(ToolSearch.Tags(sample with{Category=""}).SequenceEqual(["未分類"]),"カテゴリが空でも未分類から探せる");
        check(ToolSearch.Matches(sample,"sampleapp 画像\tAI用記録",null),"名前・解説・カテゴリを横断して大文字小文字を無視したAND検索");
        check(!ToolSearch.Matches(sample,"Sample 存在しない",null) && !ToolSearch.Matches(sample,"Sample","DB"),"検索語とカテゴリの両条件を満たす場合だけ表示する");
        check(ToolSearch.Matches(sample,"  \t ","撮影") && !ToolSearch.Matches(sample,".*",null),"空白検索は制限せず正規表現記号も文字として扱う");
        var previous=SynchronizationContext.Current;
        try
        {
            var definitions=OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root,OnlineToolCatalog.RelativePath)),root).Take(3).ToArray();
            definitions[0]=definitions[0] with{Category="音声、配信",Summary="Alpha sample 音声を記録"};
            definitions[1]=definitions[1] with{Category="DB、検索",Summary="Beta sample データを検索"};
            definitions[2]=definitions[2] with{Category="音声、AI用記録",Summary="Gamma sample 画面を撮影"};
            byte[] response=OnlineCatalogTests.Encode(definitions);int requests=0;
            var home=Path.Combine(fixtures,"search-view");Directory.CreateDirectory(home);
            var settingsPath=Path.Combine(home,"settings.json");File.WriteAllText(settingsPath,"架空の起動設定・履歴");
            var store=new DashboardViewStore(home);var source=new ToolCatalogSource(root,home,_=>{requests++;return Task.FromResult(response);});
            var manager=new ToolManager(root,catalogSource:source);var catalog=ReleaseDownloader.Load(root);
            InstallationState Installed(ToolDefinition t)=>t.Id!=definitions[0].Id && t.Id!=definitions[1].Id?new("未導入",null,null,null):new("導入済み",t.Id==definitions[0].Id?"0.0.1":catalog.Packages.Single(p=>p.Package.Id==t.Id).Package.Version,root,"fixture.exe");
            using var form=new Dashboard(root,toolManager:manager,viewStore:store,detectInstallation:Installed,fileExists:_=>false,
                executionCheck:(t,_)=>Task.FromResult(new ToolState(t.Id==definitions[0].Id?"実行中":"停止中")),
                releaseCheck:(t,f,_)=>Task.FromResult(new PublicRelease(f,true,true,"fixture")),notify:_=>{});
            form.CreateControl();Pump(form.RefreshAll());form.PerformLayout();
            var grid=Find<DataGridView>(form,"toolTable");var search=Find<TextBox>(form,"viewSearch");var filter=Find<ComboBox>(form,"viewFilter");
            string[] Visible()=>grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible).Select(r=>(string)r.Tag!).ToArray();
            Button Tag(string name)=>Find<Button>(form,"categoryTag:"+name);
            check(Visible().Length==3 && Find<FlowLayoutPanel>(form,"categoryTags").Controls.OfType<Button>().Count()==6,"config内のカテゴリだけをタグにして初期一覧を表示");
            Click(Tag("音声"));check(Visible().SequenceEqual([definitions[0].Id,definitions[2].Id]),"1ツールの複数タグを個別の絞り込みに使える");
            search.Text="Alpha 音声";check(Visible().SequenceEqual([definitions[0].Id]),"検索とカテゴリを組み合わせて絞り込む");
            filter.SelectedIndex=3;check(Visible().Length==0 && form.SelectedIndex==-1 && !Find<Button>(form,"start").Enabled,"表示条件も積にして0件の非表示行を操作させない");
            Click(Find<Button>(form,"clearFilters"));check(Visible().Length==3 && search.Text=="" && filter.SelectedIndex==0,"解除ボタンで検索・カテゴリ・表示条件をすべて戻す");
            filter.SelectedIndex=2;check(Visible().SequenceEqual([definitions[0].Id]),"要更新フィルターを検索追加後も維持する");
            filter.SelectedIndex=0;Click(Tag("DB"));search.Text="sample";grid.CurrentCell=grid.Rows[1].Cells["tool"];
            form.SaveView();var saved=store.Load();check(saved.Query=="sample" && saved.Category=="DB" && File.ReadAllText(settingsPath)=="架空の起動設定・履歴","検索条件は表示専用ファイルへ保存し起動設定・履歴を変更しない");
            response=OnlineCatalogTests.Encode(definitions[1] with{Category="DB、追加カテゴリ"},definitions[2],definitions[0]);Pump(form.RefreshAll());
            check(search.Text=="sample" && Visible().SequenceEqual([definitions[1].Id]) && (string?)grid.CurrentRow?.Tag==definitions[1].Id && Tag("追加カテゴリ")!=null,
                "config更新でカテゴリを再生成し検索・選択ID・カテゴリを保持する");
            response=OnlineCatalogTests.Encode(definitions[1] with{Category="別カテゴリ"});Pump(form.RefreshAll());
            check(Visible().Length==0 && Tag("DB").Text=="DB (0)","選択カテゴリが消えたら0件と明示し勝手に条件を解除しない");
            Click(Find<Button>(form,"clearFilters"));check(Visible().Length==1 && !Find<Button>(form,"clearFilters").Enabled,"消えたカテゴリも解除でき全条件なしでは解除ボタンを無効にする");
            check(requests==3,"検索・タグ・解除だけではconfig通信を追加しない");
            using var restored=new Dashboard(root,viewStore:store,fileExists:_=>false);
            check(Find<TextBox>(restored,"viewSearch").Text=="sample" && Find<Button>(restored,"categoryTag:DB")!=null,"次回起動で保存した検索・カテゴリを復元する");
            form.Size=new(980,540);form.PerformLayout();
            check(grid.Height>=100 && Find<FlowLayoutPanel>(form,"categoryTags").MaximumSize.Width<=form.ClientSize.Width,"最小画面でタグを折り返し一覧の高さを確保する");
            check(Find<FlowLayoutPanel>(form,"categoryTags") is { AutoScroll:true } tags && tags.Height<=100 && tags.Controls.Cast<Control>().All(c=>c.Right<=tags.ClientSize.Width),
                "タグが増えても領域の高さを制限し横にはみ出さない");
            var legacy=Path.Combine(home,"legacy");Directory.CreateDirectory(legacy);File.WriteAllText(Path.Combine(legacy,"dashboard-view.json"),"{\"schemaVersion\":1,\"filter\":\"updates\",\"width\":1200,\"height\":700}");
            check(new DashboardViewStore(legacy).Load() is { Filter:"updates",Width:1200,Query:"",Category:null },"旧表示設定を条件なしの検索・カテゴリとして互換読込する");
            store.Save(new(Query:new string('a',1025)));check(store.Load()==new DashboardView(),"過大な検索設定を拒否し起動を妨げない");
        }
        finally{SynchronizationContext.SetSynchronizationContext(previous);}
    }
}
