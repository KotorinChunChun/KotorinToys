using System.Collections;

namespace KotorinToys;

internal sealed partial class Dashboard
{
    readonly ComboBox viewFilter = new() { Name = "viewFilter", DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly Label viewCount = new() { Name = "viewCount", AutoSize = true, Margin = new(8,7,0,0) };
    readonly FlowLayoutPanel viewBar = new() { Name = "viewBar", Dock = DockStyle.Fill, AutoSize = true, Padding = new(8,3,8,3) };
    readonly DashboardViewStore? viewStore;
    string? sortColumn;
    bool sortDescending, applyingView;
    internal int SelectedIndex => grid.CurrentRow is { Visible:true, Tag:string id } ? Array.FindIndex(tools,t=>t.Id==id) : -1;
    DataGridViewRow RowFor(int index) => grid.Rows.Cast<DataGridViewRow>().Single(row=>Equals(row.Tag,tools[index].Id));
    void AddToolRow(ToolDefinition tool) { int row = grid.Rows.Add(tool.Name, tool.Summary, DashboardText.Categories(tool.Category), "", "", "確認中", "未確認", ""); grid.Rows[row].Tag=tool.Id; }

    void InitializeView()
    {
        viewFilter.Items.AddRange(["すべて", "通常", "要更新", "未導入"]); viewFilter.SelectedIndex=0;
        viewBar.Controls.Add(new Label { Text="表示：", AutoSize=true, Margin=new(3,7,3,0) });
        viewBar.Controls.Add(viewFilter); viewBar.Controls.Add(viewCount);
        viewFilter.SelectedIndexChanged += (_,_)=>ApplyView();
        grid.AllowUserToOrderColumns=true;
        grid.ColumnHeaderMouseClick += (_,e)=>
        {
            if(e.ColumnIndex<0 || busy || refreshingCatalog)return;
            string name=grid.Columns[e.ColumnIndex].Name;
            sortDescending=sortColumn==name && !sortDescending;sortColumn=name;ApplyView();
        };
        var saved=viewStore?.Load();
        if(saved!=null)
        {
            Size=new(saved.Width,saved.Height);sortColumn=saved.SortColumn;sortDescending=saved.Descending;
            foreach(var column in saved.Columns??[])
                grid.Columns[column.Name]!.FillWeight=column.Width;
            int order=0;
            foreach(var column in (saved.Columns??[]).OrderBy(c=>c.Order)) grid.Columns[column.Name]!.DisplayIndex=order++;
            viewFilter.SelectedIndex=saved.Filter=="installed"?1:saved.Filter=="updates"?2:saved.Filter=="missing"?3:0;
        }
        FormClosing += (_,_)=>SaveView();
    }
    internal void SaveView()
    {
        if(viewStore==null)return;
        var size=WindowState==FormWindowState.Normal?Size:RestoreBounds.Size;
        var error=viewStore.Save(new(1,Math.Clamp(size.Width,980,6000),Math.Clamp(size.Height,540,4000),
            viewFilter.SelectedIndex==1?"installed":viewFilter.SelectedIndex==2?"updates":viewFilter.SelectedIndex==3?"missing":"all",sortColumn,sortDescending,
            grid.Columns.Cast<DataGridViewColumn>().Select(c=>new DashboardColumnView(c.Name,c.DisplayIndex,Math.Clamp(c.Width,40,3000))).ToArray()));
        if(error!=null){viewBar.AccessibleDescription=error;tooltips.SetToolTip(viewFilter,error);}
    }
    void ApplyView()
    {
        if(applyingView || grid.Rows.Count!=tools.Length)return;
        applyingView=true;
        try
        {
            string? selected=grid.CurrentRow?.Tag as string;
            string? selectedColumn=grid.CurrentCell?.OwningColumn?.Name;
            bool Matches(int i)=>viewFilter.SelectedIndex switch
            {
                1=>installations[i].State is not "未導入" && states[i].State is "実行中" or "停止中",
                2=>ToolVersionView.Create(installations[i],releases[i]).CanUpdate,
                3=>installations[i].State == "未導入",
                _=>true
            };
            if(grid.CurrentRow is {Tag:string id} && Array.FindIndex(tools,t=>t.Id==id) is var current && current>=0 && !Matches(current)) grid.CurrentCell=null;
            for(int i=0;i<tools.Length;i++) RowFor(i).Visible=Matches(i);
            if(sortColumn!=null)grid.Sort(new ViewComparer(sortColumn,sortDescending));
            foreach(DataGridViewColumn column in grid.Columns) column.HeaderCell.SortGlyphDirection=column.Name==sortColumn
                ?sortDescending?SortOrder.Descending:SortOrder.Ascending:SortOrder.None;
            var row=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>r.Visible && Equals(r.Tag,selected)) ?? grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>r.Visible);
            if(row!=null)grid.CurrentCell=row.Cells[selectedColumn??"tool"];else{grid.CurrentCell=null;grid.ClearSelection();}
            viewCount.Text=grid.Rows.Cast<DataGridViewRow>().Count(r=>r.Visible)+"件";
            UpdateActions();
        }
        finally{applyingView=false;}
    }
    sealed class ViewComparer(string column,bool descending):IComparer
    {
        public int Compare(object? a,object? b)
        {
            var left=(DataGridViewRow)a!;var right=(DataGridViewRow)b!;
            string x=left.Cells[column].Value?.ToString()??"",y=right.Cells[column].Value?.ToString()??"";
            int order=column is "installed" or "published" && Version.TryParse(x,out var vx) && Version.TryParse(y,out var vy)
                ?vx.CompareTo(vy):StringComparer.CurrentCultureIgnoreCase.Compare(x,y);
            if(order==0)order=StringComparer.Ordinal.Compare(left.Tag?.ToString(),right.Tag?.ToString());
            return descending?-order:order;
        }
    }
}
