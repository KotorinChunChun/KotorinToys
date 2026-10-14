namespace KotorinToys.Integration;

// 画面非表示でも管理要求を受け、各製品本来のFormClosingで保存・後始末を行う。
internal sealed class ManagedToolContext : ApplicationContext
{
    readonly Form window;
    readonly ManagementEndpoint endpoint;

    public ManagedToolContext(string id, Form window, bool silent, Func<string?> stopBlockReason)
    {
        this.window = window;
        _ = window.Handle;
        window.FormClosed += (_, _) => ExitThread();
        endpoint = new ManagementEndpoint(id, window, ShowWindow, window.Close, () =>
            stopBlockReason() ?? (Application.OpenForms.Cast<Form>().Any(form => form.Visible && form.Modal)
                ? "ダイアログを閉じてから停止してください。" : null));
        if (!silent) ShowWindow();
    }

    void ShowWindow()
    {
        window.Show();
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.BringToFront();
        window.Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { endpoint.Dispose(); window.Dispose(); }
        base.Dispose(disposing);
    }
}
