using Microsoft.Win32;
using System.Diagnostics;
using System.Security;

namespace KotorinToys;

internal static class RuntimeDetector
{
    public static RuntimeState[] Detect(ToolDefinition tool, string executable)
    {
        if (tool.Runtimes == null)
            return [new("unknown", "依存情報", "未確認", "基本機能", false, "依存物は未調査です。")];
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
            return new(requirement.Id, requirement.Name, present switch
            {
                true => "利用可能",
                false => "不足",
                null => "未確認"
            }, requirement.Feature, requirement.Optional, requirement.Guide ?? "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or InvalidOperationException)
        {
            return new(requirement.Id, requirement.Name, "未確認", requirement.Feature, requirement.Optional, ex.Message);
        }
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
