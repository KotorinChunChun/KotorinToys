using KotorinToys;
using KotorinToys.Integration;

internal static class HostWindowPresentationTests
{
    public static void Run(Action<bool, string> check)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            using var window = new Form { Text = WindowTitle.Create("再表示試験"), ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Bounds = new(100, 100, 600, 400) };
            using var other = new Form { Text = WindowTitle.Create("前面確認用"), ShowInTaskbar = false };
            HostApplicationContext.PresentWindow(window);
            Application.DoEvents();
            var normalBounds = window.Bounds;
            check(window.Visible && window.WindowState == FormWindowState.Normal, "通常表示を再表示できる");
            window.WindowState = FormWindowState.Maximized;
            Application.DoEvents();
            var maximizedBounds = window.Bounds;
            other.Show();
            HostApplicationContext.PresentWindow(window);
            Application.DoEvents();
            check(window.WindowState == FormWindowState.Maximized && window.Bounds == maximizedBounds, "再表示で最大化と画面サイズを維持する");
            check(window.Visible && !window.TopMost, "再表示で常時最前面の設定には変えない");
            Console.WriteLine("前面表示の観測：" + (Form.ActiveForm == window ? "同一fixtureのアクティブ画面を確認" :
                "この実行環境ではアクティブ画面を確認できません。実デスクトップの前面表示は未検証です。"));
            window.Hide();
            HostApplicationContext.PresentWindow(window);
            Application.DoEvents();
            check(window.Visible && window.WindowState == FormWindowState.Maximized, "トレイへ隠した最大化画面をそのまま表示する");
            window.Resize += (_, _) => HostApplicationContext.HideMinimized(window);
            window.WindowState = FormWindowState.Minimized;
            Application.DoEvents();
            check(!window.Visible, "最小化で画面がトレイに格納される");
            HostApplicationContext.PresentWindow(window);
            Application.DoEvents();
            check(window.Visible && window.WindowState == FormWindowState.Maximized, "最大化から最小化した画面は最大化で復元する");
            window.WindowState = FormWindowState.Normal;
            window.WindowState = FormWindowState.Minimized;
            Application.DoEvents();
            HostApplicationContext.PresentWindow(window);
            Application.DoEvents();
            check(window.WindowState == FormWindowState.Normal && window.Bounds == normalBounds, "通常から最小化した画面は元のサイズで復元する");

            using var dispatcher = new Control();
            string id = "window-presentation-test-" + Guid.NewGuid().ToString("N");
            int shown = 0;
            using var endpoint = new ManagementEndpoint(id, dispatcher, () => { shown++; HostApplicationContext.PresentWindow(window); }, () => { });
            window.WindowState = FormWindowState.Maximized;
            other.Activate();
            var request = Task.Run(() => ManagementEndpoint.ShowExisting(id));
            var until = DateTime.UtcNow.AddSeconds(8);
            while (!request.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(1); }
            if (!request.IsCompleted) throw new TimeoutException("再表示IPC試験が完了しません。");
            request.GetAwaiter().GetResult();
            Application.DoEvents();
            check(shown == 1 && window.Visible && window.WindowState == FormWindowState.Maximized,
                "二重起動と同じShowExisting管理IPCで既存画面を最大化のまま再表示する");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
