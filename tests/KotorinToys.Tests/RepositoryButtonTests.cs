using KotorinToys;

internal static class RepositoryButtonTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var tools = ToolCatalog.Load(root);
        var expected = new Dictionary<string, string>
        {
            ["live-text-marker"] = "LiveTextMarker", ["smart-paste"] = "SmartPaste",
            ["ss-capture"] = "SSCapture", ["shortcut-key-counter"] = "ShortcutKeyCounter",
            ["voice-mixer-spk-router"] = "VoiceMixerSpkRouter", ["voice-mixer-mic-router"] = "VoiceMixerMicRouter", ["every-db"] = "EveryDB", ["capchun-screen"] = "CapchunWindow",
            ["chulip-video"] = "ChulipVideo-CSharp", ["chunap-tool"] = "ChunapTool",
            ["operation-recorder"] = "OperationRecorder", ["script-sync"] = "ScriptSync", ["md-visualizer"] = "MdVisualizer",
            ["pppaint"] = "PPPaint"
        };
        int opened = 0; Uri? requested = null;
        using var form = new Dashboard(root, openRepository: uri => { opened++; requested = uri; }, notify: _ => { });
        form.CreateControl(); form.PerformLayout();
        var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
        var github = (Button)form.Controls.Find("github", true).Single();
        var manual = form.Controls.Find("manual", true).Single();
        var row = form.Controls.Find("selectedActions", true).Single();
        check(github.Text == "GitHubを開く" && github.Parent == row &&
            row.Controls.GetChildIndex(github) == row.Controls.GetChildIndex(manual) + 1,
            "GitHubを開くは使い方の直後・選択アプリの操作行に配置");
        form.Size = form.MinimumSize; form.PerformLayout();
        check(github.Top == manual.Top && github.Left >= manual.Right && row.ClientRectangle.Contains(github.Bounds),
            "最小サイズでも使い方の隣にGitHubボタンを配置");
        for (int index = 0; index < tools.Length; index++)
        {
            var tool = tools[index]; grid.CurrentCell = grid.Rows[index].Cells[0];
            bool available = expected.TryGetValue(tool.Id, out var name);
            check(github.Enabled == available, tool.Name + " 公開先の有無でGitHubボタンを切替");
            int before = opened; form.OpenRepository();
            check(available ? opened == before + 1 && requested?.AbsoluteUri == "https://github.com/KotorinChunChun/" + name :
                opened == before && github.AccessibleDescription?.Contains("未設定") == true,
                tool.Name + " はアプリのトップページのみを開き未設定URLを創作しない");
        }
        var published = tools.First(tool => expected.ContainsKey(tool.Id));
        check(ToolRepository.Find(published with { Name = "Other" }) == null &&
            ToolRepository.Find(published with { AppId = "KotorinToys.Other" }) == null &&
            ToolRepository.Find(published with { Id = "unknown" }) == null, "別製品・未知IDのリンクを補完しない");
        grid.CurrentCell = null; int previous = opened; form.OpenRepository();
        check(!github.Enabled && opened == previous, "未選択ではGitHubを開かない");
        string? notice = null;
        using var failing = new Dashboard(root, openRepository: _ => throw new IOException("ブラウザー起動失敗の注入"), notify: text => notice = text);
        failing.CreateControl(); failing.OpenRepository();
        var failedGrid = (DataGridView)failing.Controls.Find("toolTable", true).Single();
        check(notice?.Contains("GitHubを開けません") == true && failedGrid.Rows[0].Cells["detail"].ToolTipText.Contains("ブラウザー起動失敗の注入") == true &&
            failing.Controls.Find("selectedActions", true).Single().Enabled, "ブラウザー起動失敗を通知し操作行を復旧");
    }
}
