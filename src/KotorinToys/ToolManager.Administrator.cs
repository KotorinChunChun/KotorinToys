using System.ComponentModel;
using System.Diagnostics;

namespace KotorinToys;

internal sealed partial class ToolManager
{
    internal const string AdministratorRunningNotice = "管理者として実行中です。画面表示・終了はアプリ側で操作してください。";

    public async Task<ToolState> StartAsAdministrator(ToolDefinition tool, CancellationToken cancellation = default)
    {
        var gate = operations.GetOrAdd(tool.Id, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(cancellation);
        try
        {
            var state = await Status(tool, cancellation);
            var installed = Installation(tool);
            var restriction = AdministratorLaunchRestriction(tool, installed, state, File.Exists(installed.Executable));
            if (restriction != null) return new("確認できない", Detail: restriction);
            cancellation.ThrowIfCancellationRequested();
            return RequestAdministratorLaunch(installed.Executable!, info =>
            {
                using var process = Process.Start(info) ?? throw new IOException("Windowsが起動プロセスを返しませんでした。");
                return process.Id;
            });
        }
        finally { gate.Release(); }
    }

    internal static string? AdministratorLaunchRestriction(ToolDefinition tool, InstallationState installed, ToolState state, bool executableExists)
    {
        if (tool.Msix != null) return "MSIX版は通常の「起動 / 設定を開く」から起動してください。";
        if (installed.State != "導入済み") return "管理者として起動するにはアプリを正式にインストールしてください。";
        if (!executableExists) return "実行ファイルが見つかりません。アプリを修復してください。";
        if (state.IsAdministrator) return AdministratorRunningNotice;
        if (state.State != "停止中") return state.State == "実行中"
            ? "先にアプリを終了してください。" : "状態確認が完了し、アプリが停止中になってから操作してください。";
        return null;
    }

    internal static ToolState RequestAdministratorLaunch(string executable, Func<ProcessStartInfo, int> launch)
    {
        var path = PathSafety.LocalExecutable(executable);
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(path)
        };
        // 通常の画面起動を使い、別の権限の管理窓口に表示依頼を送らない。
        try { return new("起動処理中", launch(start), "管理者としての起動をWindowsへ依頼しました。"); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { return new("取消", Detail: "管理者としての起動を取り消しました。"); }
        catch (Win32Exception ex)
        { return new("確認できない", Detail: $"管理者として起動できませんでした（Windowsエラー {ex.NativeErrorCode}）。{ex.Message}"); }
    }
}
