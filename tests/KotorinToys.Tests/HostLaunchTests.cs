using KotorinToys;

internal static class HostLaunchTests
{
    public static void Run(string root, string fixtures, Action<bool,string> check)
    {
        foreach (bool startup in new[] { false, true })
        {
            int calls = 0; bool canceled = false;
            using var context = new HostApplicationContext(root, startup, Path.Combine(fixtures,"launch-"+startup), hidden:true, localCatalog:true,
                ensureTargets: async token =>
                {
                    calls++;
                    try { await Task.Delay(Timeout.Infinite, token); }
                    catch(OperationCanceledException) { canceled = true; throw; }
                });
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while(calls == 0 && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
            check(calls == 1 && !context.Dashboard.Visible, "通常・サインインの非表示本体起動でも対象確認を一度開始する："+startup);
            context.ExitThread();
            deadline = DateTime.UtcNow.AddSeconds(5);
            while(!canceled && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
            check(canceled, "本体終了は起動確認へ取消を通知する："+startup);
        }
    }
}
