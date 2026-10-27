namespace KotorinToys;

internal sealed class OperationDetailsForm : BrandedForm
{
    readonly ToolTip tooltips = new();
    public OperationDetailsForm(string name, string explanation, Action? openInstalled = null, ActionAvailability? installedLocation = null)
    {
        Text = WindowTitle.Create("詳細：" + name); Size = new(700, 440); MinimumSize = new(480, 300);
        Font = new("Yu Gothic UI", 11); StartPosition = FormStartPosition.CenterParent;
        var text = new TextBox { Name = "operationExplanation", Text = explanation.Replace("\r\n","\n").Replace("\n","\r\n"), Multiline = true, ReadOnly = true,
            Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, WordWrap = true, TabIndex = 0 };
        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(close);
        if (openInstalled != null)
        {
            var location = new Button { Name = "openInstalledLocation", Text = "正式導入先を開く", AutoSize = true };
            var rule = installedLocation ?? ActionAvailability.Deny("正式導入先を確認できません。");
            ActionHints.Apply(location, rule, tooltips);
            location.Click += (_, _) => { if (location.Enabled) openInstalled(); };
            buttons.Controls.Add(location);
        }
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new(10) };
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(text, 0, 0); layout.Controls.Add(buttons, 0, 1); Controls.Add(layout); CancelButton = close;
        ActionHints.Attach(buttons, tooltips);
        Shown += (_,_) => text.Select(0,0);
    }
    protected override void Dispose(bool disposing) { if (disposing) tooltips.Dispose(); base.Dispose(disposing); }
}
