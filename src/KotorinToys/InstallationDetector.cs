using Microsoft.Win32;
using System.Diagnostics;
using System.Security;

namespace KotorinToys;

internal sealed class InstallationDetector(Func<string, InstallationRegistration?>? registryReader = null)
{
    public InstallationState Detect(ToolDefinition tool)
    {
        if (tool.Msix != null)
            return MsixInstallation.Detect(tool);
        if (string.IsNullOrEmpty(tool.AppId))
            return new("未導入", null, null, null, Detail: "正式導入の検出方法は未登録です。");
        try
        {
            var registration = (registryReader ?? ReadRegistration)(tool.AppId);
            if (registration == null)
                return new("未導入", null, null, null);
            var directory = registration.Directory;
            if (string.IsNullOrWhiteSpace(directory))
                return new("確認できない", null, null, null, Detail: "導入先の登録がありません。");
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            var executable = PathSafety.Resolve(directory, tool.InstalledExecutable);
            if (!File.Exists(executable))
                return new("修復が必要", null, directory, executable, Detail: "登録された実行ファイルがありません。");
            using var identity = System.Text.Json.JsonDocument.Parse(File.ReadAllText(PathSafety.Resolve(directory, "tool.json")));
            if (identity.RootElement.GetProperty("id").GetString() != tool.Id || identity.RootElement.GetProperty("appId").GetString() != tool.AppId)
                return new("確認できない", null, directory, null, Detail: "製品識別が一致しません。");
            var version = FileVersionInfo.GetVersionInfo(executable);
            if (!string.Equals(version.ProductName, tool.Name, StringComparison.Ordinal))
                return new("確認できない", null, directory, null, Detail: "実行ファイルの製品名が一致しません。");
            var installedVersion = $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}";
            var uninstaller = PathSafety.Resolve(directory, "unins000.exe");
            return new("導入済み", installedVersion, directory, executable, File.Exists(uninstaller) ? uninstaller : null, File.Exists(uninstaller) ? "" : "削除プログラムがありません。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            return new("確認できない", null, null, null, Detail: ex.Message);
        }
    }

    private static InstallationRegistration? ReadRegistration(string appId)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + appId + "_is1");
        return key == null ? null : new(key.GetValue("InstallLocation") as string);
    }

    // 未登録・途中導入の更新修復に限定。通常の導入検出は登録を必須とする。
    public InstallationState UpdateTarget(ToolDefinition tool, string root)
    {
        var actual = Detect(tool);
        if (actual.State == "未導入")
            actual = new InstallationDetector(_ => new(root)).Detect(tool);
        return actual.State == "導入済み" && string.Equals(actual.Directory, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase) ? actual : new("確認できない", null, null, null, Detail: "更新対象の製品識別・配置を照合できません。");
    }
}
