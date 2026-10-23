using System.Net;
using System.Reflection;
using KotorinToys;

internal static class OperationDetailsTests
{
    public static void Run(string root, Action<bool,string> check)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            var tools = ToolCatalog.Load(root); var packages = ReleaseDownloader.Load(root);
            int shown = 0; string text = "";
            using var form = new Dashboard(root, showDetails: (_, value) => { shown++; text = value; }, fileExists: _ => false);
            var actual = tools.Select(_ => new InstallationState("未導入", null, null, null)).ToArray();
            var states = tools.Select(_ => new ToolState("停止中")).ToArray();
            var releases = tools.Select(t => new PublicRelease(packages.Packages.FirstOrDefault(p => p.Package.Id == t.Id), false, false,
                "接続が失敗しました。", Problem: ReleaseProblem.Connection)).ToArray();
            form.ApplySnapshot(actual, states, releases);
            var grid = (DataGridView)form.Controls.Find("toolTable", true).Single();
            grid.CurrentCell = grid.Rows[0].Cells["detail"];
            form.OpenSelectedDetails();
            check(shown == 1 && text.Contains("接続が失敗") && text.Contains("対処：") && text.Contains("起動不可"), "クリックで読む詳細に原因・対処・無効理由を残す");
            check(grid.Rows[0].Cells["detail"].Value?.ToString() == "-" && grid.Rows[0].Cells["detail"].ToolTipText.Contains("接続が失敗"),
                "未導入はハイフンで簡素化し、通信失敗の根拠はツールチップに残す");
            check(DashboardText.Detail(tools[0], new("導入済み", "0.1.0", root, "missing.exe"), states[0], releases[0], new("なし", ""), null, null, null) == "通信エラー",
                "導入済みでは通信失敗と配布形式未対応を区別する");
            var key = new KeyEventArgs(Keys.Enter);
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, [key]);
            check(shown == 2 && key.SuppressKeyPress, "詳細セルのEnterでも説明を読める");
            using var dialog = new OperationDetailsForm("Sample", text);
            check(dialog.Controls.Find("operationExplanation", true).Single() is TextBox { ReadOnly: true, Multiline: true } && dialog.Icon != null,
                "説明は読取専用で共通アイコンを持つ");
            using var lines = new OperationDetailsForm("Sample", "原因\n対処\r\n案内");
            check(((TextBox)lines.Controls.Find("operationExplanation",true).Single()).Text=="原因\r\n対処\r\n案内", "詳細の改行をWindowsの複数行表示へ統一");
            foreach (var (problem, summary) in new[] { (ReleaseProblem.RateLimit,"取得制限"), (ReleaseProblem.Unavailable,"公開物なし"),
                (ReleaseProblem.Unpublished,"未公開"), (ReleaseProblem.Unsupported,"形式未対応"), (ReleaseProblem.Validation,"公開情報を確認") })
            {
                var release = releases[0] with { Problem = problem };
                check(DashboardText.Detail(tools[0], actual[0], states[0], release, new("なし", ""), null, null, null) == summary,
                    "公開確認の理由を短く区別：" + summary);
                check(DashboardText.Update(tools[0], actual[0], release) != "なし", "未照合を更新なしと誤表示しない：" + summary);
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    public static async Task Transport(string root, Action<bool,string> check)
    {
        var tool = ToolCatalog.Load(root).First(t=>t.Id=="smart-paste");
        foreach (var (status, problem) in new[] { (HttpStatusCode.TooManyRequests, ReleaseProblem.RateLimit), (HttpStatusCode.NotFound, ReleaseProblem.Unavailable),
            (HttpStatusCode.ServiceUnavailable, ReleaseProblem.Connection) })
        {
            using var client = new PublicReleaseClient(new Handler(status));
            var release = await client.Check(tool, null);
            check(!release.Confirmed && release.Problem == problem, "HTTPの原因を型として保持：" + status);
        }
    }
    sealed class Handler(HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent("") });
    }
}
