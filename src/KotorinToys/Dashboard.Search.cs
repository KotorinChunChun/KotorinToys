namespace KotorinToys;

internal sealed partial class Dashboard
{
    readonly TextBox search = new() { Name = "viewSearch", Width = 240, MaxLength = 1024, PlaceholderText = "名前・解説・カテゴリ" };
    readonly FlowLayoutPanel categories = new() { Name = "categoryTags", AutoSize = true, AutoScroll = true, WrapContents = true, Margin = Padding.Empty };
    readonly Button clearFilters = new() { Name = "clearFilters", Text = "絞り込みを解除", AutoSize = true };
    string? selectedCategory;
    bool rebuildingCategories;

    void InitializeSearch()
    {
        viewBar.FlowDirection = FlowDirection.TopDown; viewBar.WrapContents = false;
        var line = new FlowLayoutPanel { Name = "searchLine", AutoSize = true, Margin = Padding.Empty, WrapContents = true };
        line.Controls.Add(new Label { Text = "表示：", AutoSize = true, Margin = new(3,7,3,0) });
        line.Controls.Add(viewFilter); line.Controls.Add(viewCount);
        line.Controls.Add(new Label { Text = "検索：", AutoSize = true, Margin = new(12,7,3,0) });
        line.Controls.Add(search); line.Controls.Add(clearFilters);
        viewBar.Controls.Add(line); viewBar.Controls.Add(categories);
        viewBar.SizeChanged += (_,_) =>
        {
            int width = Math.Max(100,viewBar.ClientSize.Width-viewBar.Padding.Horizontal);
            line.MaximumSize = new(width,0); categories.MaximumSize = new(width,100);
        };
        search.AccessibleName = "ツールを検索";
        tooltips.SetToolTip(search, "名前・ひとこと解説・カテゴリを空白区切りのすべての語で検索します。Ctrl+Fで移動、Escで検索だけ解除。");
        search.TextChanged += (_,_) => ApplyView();
        search.KeyDown += (_,e) => { if(e.KeyCode==Keys.Escape){search.Clear(); e.Handled=e.SuppressKeyPress=true;} };
        clearFilters.Click += (_,_) => { search.Clear(); viewFilter.SelectedIndex=0; selectedCategory=null; RebuildCategories(); ApplyView(); };
        tooltips.SetToolTip(clearFilters,"検索・カテゴリ・表示条件を解除して、すべてのツールを表示します。");
        KeyPreview=true;
        KeyDown += (_,e) => { if(e.Control && e.KeyCode==Keys.F){search.Focus(); search.SelectAll(); e.Handled=e.SuppressKeyPress=true;} };
    }

    void RebuildCategories()
    {
        if(UiClosed || rebuildingCategories)return;
        rebuildingCategories=true;
        categories.SuspendLayout();
        try
        {
            foreach(var control in categories.Controls.Cast<Control>().ToArray())control.Dispose();
            categories.Controls.Clear();
            categories.Controls.Add(new Label { Text="カテゴリ：",AutoSize=true,Margin=new(3,7,3,0) });
            var tags=tools.SelectMany(ToolSearch.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
            Add(null,"すべて");
            foreach(var tag in tags)Add(tag,tag);
            if(selectedCategory!=null && !tags.Contains(selectedCategory,StringComparer.OrdinalIgnoreCase))Add(selectedCategory,selectedCategory+" (0)");
            void Add(string? value,string text)
            {
                bool selected=string.Equals(value,selectedCategory,StringComparison.OrdinalIgnoreCase);
                var button=new Button { Name=value==null?"categoryAll":"categoryTag:"+value,Text=text,Tag=value,AutoSize=true,FlatStyle=FlatStyle.Flat,
                    Padding=new(5,0,5,0),Margin=new(3),MaximumSize=new(280,60),BackColor=selected?SystemColors.Highlight:SystemColors.Control,
                    ForeColor=selected?SystemColors.HighlightText:SystemColors.ControlText,AccessibleName="カテゴリ："+text };
                button.Click += (_,_) => {selectedCategory=value;RebuildCategories();ApplyView();};
                tooltips.SetToolTip(button,value==null?"カテゴリで絞り込まずに表示します。":"「"+value+"」に属するツールだけを表示します。検索・表示条件と併用できます。");
                categories.Controls.Add(button);
            }
        }
        finally {categories.ResumeLayout(true);rebuildingCategories=false;}
    }
}
