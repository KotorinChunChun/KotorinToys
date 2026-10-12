using Microsoft.Win32;
using System.Diagnostics;
using System.Security;

namespace KotorinToys;

internal sealed record InstallationRegistration(string? Directory);
internal sealed class InstallationDetector(Func<string, InstallationRegistration?>? registryReader = null)
{
    public InstallationState Detect(ToolDefinition tool)
    {
        if (string.IsNullOrEmpty(tool.AppId)) return new("未導入", null, null, null, Detail: "正式導入の検出方法は未登録です。");
        try
        {
            var registration = (registryReader ?? ReadRegistration)(tool.AppId);
            if (registration == null) return new("未導入", null, null, null);
            var directory = registration.Directory;
            if (string.IsNullOrWhiteSpace(directory)) return new("確認できない", null, null, null, Detail: "導入先の登録がありません。");
            directory = Path.GetFullPath(directory);
            var executable = PathSafety.Resolve(directory, tool.InstalledExecutable);
            if (!File.Exists(executable)) return new("修復が必要", null, directory, executable, Detail: "登録された実行ファイルがありません。");
            using var identity = System.Text.Json.JsonDocument.Parse(File.ReadAllText(PathSafety.Resolve(directory, "tool.json")));
            if (identity.RootElement.GetProperty("id").GetString() != tool.Id || identity.RootElement.GetProperty("appId").GetString() != tool.AppId)
                return new("確認できない", null, directory, null, Detail: "製品識別が一致しません。");
            var version = FileVersionInfo.GetVersionInfo(executable);
            if (!string.Equals(version.ProductName, tool.Name, StringComparison.Ordinal))
                return new("確認できない", null, directory, null, Detail: "実行ファイルの製品名が一致しません。");
            var installedVersion = $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}";
            var uninstaller = PathSafety.Resolve(directory, "unins000.exe");
            return new("導入済み", installedVersion, directory, executable,
                File.Exists(uninstaller) ? uninstaller : null, File.Exists(uninstaller) ? "" : "削除プログラムがありません。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException)
        { return new("確認できない", null, null, null, Detail: ex.Message); }
    }
    private static InstallationRegistration? ReadRegistration(string appId)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + appId + "_is1");
        return key == null ? null : new(key.GetValue("InstallLocation") as string);
    }
}

internal static class RuntimeDetector
{
    public static RuntimeState[] Detect(ToolDefinition tool, string executable)
    {
        if (tool.Runtimes == null) return [new("unknown", "依存情報", "未確認", "基本機能", false, "依存物は未調査です。")];
        var root = Path.GetDirectoryName(executable)!;
        return tool.Runtimes.Select(requirement => DetectOne(requirement, root)).ToArray();
    }
    private static RuntimeState DetectOne(RuntimeRequirement requirement, string root)
    {
        try
        {
            bool? present = requirement.Kind switch
            {
                "file" => File.Exists(PathSafety.Resolve(root, requirement.Path!)),
                "dotnet-desktop" => File.Exists(PathSafety.Resolve(root, "hostpolicy.dll")) || DesktopRuntimeInstalled(requirement.Major!.Value),
                "vc-x64" => VcRuntimeInstalled(),
                // 外部OneOCRなどの動的検出は未確認として機能側の診断へ案内する。
                _ => null
            };
            return new(requirement.Id, requirement.Name, present switch { true => "利用可能", false => "不足", null => "未確認" },
                requirement.Feature, requirement.Optional, requirement.Guide ?? "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or InvalidOperationException)
        { return new(requirement.Id, requirement.Name, "未確認", requirement.Feature, requirement.Optional, ex.Message); }
    }
    internal static bool DesktopRuntimeInstalled(int major)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");
        return key?.GetValueNames().Any(value => Version.TryParse(value, out var v) && v.Major == major) == true;
    }
    private static bool VcRuntimeInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
        return key?.GetValue("Installed") is int value && value == 1;
    }
}
