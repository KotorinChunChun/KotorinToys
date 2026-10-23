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
                return MissingExecutable(tool, directory, executable);
            var definition = PathSafety.Resolve(directory, "tool.json");
            if (!File.Exists(definition))
                return new("確認できない", null, directory, null, Detail: "製品定義 tool.json がありません。配布元の修正版setupで設定・履歴を保持して修復してください。");
            if (new FileInfo(definition).Length > OnlineToolCatalog.MaximumBytes)
                return new("確認できない", null, directory, null, Detail: "製品定義 tool.json の容量が上限を超えています。配布元へ確認してください。");
            using var identity = ReadIdentity(definition);
            if (identity.RootElement.GetProperty("id").GetString() != tool.Id || identity.RootElement.GetProperty("appId").GetString() != tool.AppId)
                return new("確認できない", null, directory, null, Detail: "製品定義 tool.json のIDまたはAppIdが一致しません。別製品として起動・上書きしません。");
            var version = FileVersionInfo.GetVersionInfo(executable);
            if (!string.Equals(version.ProductName, tool.Name, StringComparison.Ordinal))
                return new("確認できない", null, directory, null, Detail: $"実行ファイルの製品名が一致しません。対象：{tool.Name} / 検出：{version.ProductName ?? "不明"}。");
            var installedVersion = $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}";
            var uninstaller = PathSafety.Resolve(directory, "unins000.exe");
            return new("導入済み", installedVersion, directory, executable, File.Exists(uninstaller) ? uninstaller : null, File.Exists(uninstaller) ? "" : "削除プログラムがありません。");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return new("確認できない", null, null, null, Detail: "製品定義 tool.json の必須項目または値の形式が不正です。配布元へ確認してください。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or System.Text.Json.JsonException)
        {
            return new("確認できない", null, null, null, Detail: ex.Message);
        }
    }

    static System.Text.Json.JsonDocument ReadIdentity(string path)
    {
        // 既存の導入定義はBOM付きUTF-8／UTF-16も読めていたため、文字コード検出を維持する。
        try { return System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)); }
        catch (System.Text.Json.JsonException error) { throw new IOException("製品定義 tool.json のJSON形式が不正です。配布元へ確認してください。",error); }
    }

    static InstallationState MissingExecutable(ToolDefinition tool, string directory, string executable)
    {
        var path = PathSafety.Resolve(directory, "tool.json");
        if (!File.Exists(path) || new FileInfo(path).Length > OnlineToolCatalog.MaximumBytes)
            return new("修復が必要", null, directory, executable, Detail: "実行ファイルがありません。製品定義を照合できないため、配布元の手順で修復してください。");
        using var identity = ReadIdentity(path);
        var value = identity.RootElement;
        if (value.GetProperty("id").GetString() != tool.Id || value.GetProperty("appId").GetString() != tool.AppId ||
            value.GetProperty("name").GetString() != tool.Name || value.GetProperty("installedExecutable").GetString() != tool.InstalledExecutable ||
            !Version.TryParse(value.GetProperty("version").GetString(), out var version) || version.Build < 0 || version.Revision >= 0)
            throw new IOException("欠落した実行ファイルの製品定義・版が一致しません。自動修復しません。");
        return new("修復が必要", version.ToString(), directory, executable,
            Detail: "登録された実行ファイルがありません。製品定義と登録先を照合済みです。「修復」で設定・履歴を保持して再導入できます。", Repairable: true);
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
