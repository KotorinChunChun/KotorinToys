using System.Diagnostics;

namespace KotorinToys;

// 状態の説明文ではなく、照合した構造化パスだけをExplorerへ渡す。
internal sealed class ToolLocations(Func<int, string?>? runningPath = null, Func<string, bool>? exists = null,
    Action<ProcessStartInfo>? open = null)
{
    readonly Func<int, string?> runningPath = runningPath ?? ReadRunningPath;
    readonly Func<string, bool> exists = exists ?? File.Exists;
    readonly Action<ProcessStartInfo> open = open ?? (info => Process.Start(info));

    static string? ReadRunningPath(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return process.SessionId == Process.GetCurrentProcess().SessionId ? process.MainModule?.FileName : null;
    }

    internal static string LocalExecutable(string? path) => PathSafety.LocalExecutable(path);

    public ActionAvailability Running(ToolState state)
    {
        try
        {
            if (state.State != "確認できない" || state.Pid is not > 0) return ActionAvailability.Deny("別配置の実行中アプリを確認できません。");
            var path = LocalExecutable(state.RunningExecutable); var expected = LocalExecutable(state.ExpectedExecutable);
            if (string.Equals(path, expected, StringComparison.OrdinalIgnoreCase)) return ActionAvailability.Deny("別配置ではありません。");
            if (!exists(path)) return ActionAvailability.Deny("実行中として確認したEXEが見つかりません。状態を更新してください。");
            return ActionAvailability.Allow("実行中のEXEを選択表示します：" + path);
        }
        catch (Exception error) when (LocationError(error)) { return ActionAvailability.Deny(error.Message); }
    }

    public void OpenRunning(ToolState state)
    {
        var rule = Running(state);
        if (!rule.Enabled) throw new IOException(rule.Reason);
        var path = LocalExecutable(state.RunningExecutable);
        if (!string.Equals(LocalExecutable(runningPath(state.Pid!.Value)), path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("実行中アプリの配置が変わったか、同じセッションで確認できません。状態を更新してください。");
        Select(path);
    }

    public ActionAvailability Installed(InstallationState installed)
    {
        try
        {
            if (installed.State is not ("導入済み" or "更新が必要")) return ActionAvailability.Deny("正式導入先を確認できません。");
            var path = LocalExecutable(installed.Executable);
            return exists(path) ? ActionAvailability.Allow("正式導入先のEXEを選択表示します：" + path)
                : ActionAvailability.Deny("正式導入先のEXEがありません。");
        }
        catch (Exception error) when (LocationError(error)) { return ActionAvailability.Deny(error.Message); }
    }

    public void OpenInstalled(InstallationState installed)
    {
        var rule = Installed(installed);
        if (!rule.Enabled) throw new IOException(rule.Reason);
        Select(LocalExecutable(installed.Executable));
    }

    void Select(string path)
    {
        if (!exists(path)) throw new IOException("EXEが見つかりません。状態を更新してください。");
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        info.ArgumentList.Add("/select," + path);
        open(info);
    }

    internal static bool LocationError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException;
}
