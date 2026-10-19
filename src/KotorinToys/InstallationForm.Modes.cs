namespace KotorinToys;

internal sealed partial class InstallationForm
{
    FlowLayoutPanel AddMode(string title,string name)
    {
        var page=new TabPage(title){Name=name};
        var panel=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new(6)};
        page.Controls.Add(panel);operationTabs.TabPages.Add(page);return panel;
    }
    void ModeChanged()
    {
        if(running)return;
        for(int i=0;i<targets.Items.Count;i++)targets.SetItemChecked(i,false);
        bool select=operationTabs.SelectedIndex is 0 or 1;
        targets.Enabled=targets.Visible=select;content.ColumnStyles[0].Width=select?300:0;
        source.Text=operationTabs.SelectedIndex switch
        {
            1=>"設定・履歴を保持してアンインストールします。",
            2=>"SSCaptureの録画用FFmpegを導入・修復します。",
            3=>"開発・検証用の取得元設定です。通常は変更不要です。",
            _=>catalogPath==null?PublicSource:"ローカル配布台帳："+catalogPath
        };
        results.Rows.Clear();completion.Text=select?"対象をチェックして実行してください。":"操作内容を確認してください。";
        completion.ForeColor=SystemColors.ControlText;UpdateAdvancedButtons();
    }
}
