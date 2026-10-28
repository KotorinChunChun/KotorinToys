using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace KotorinToys;

internal sealed record RegisteredMsix(string FullName, string Directory, int Origin);

// OSの現在ユーザー登録を正本とし、WindowsAppsの走査や裸EXE起動に依存しない。
internal static class MsixInstallation
{
    internal static InstallationState Detect(ToolDefinition tool, Func<string, RegisteredMsix[]>? read = null)
    {
        var identity = tool.Msix ?? throw new ArgumentException("MSIX定義がありません。");
        try
        {
            var packages = (read ?? ReadPackages)(identity.FamilyName);
            if (packages.Length == 0)
                return new("未導入", null, null, null, Detail: "署名付きMSIXをインストールしてください。");
            var candidates = packages.Select(package => Inspect(tool, package)).OrderByDescending(item => Version.Parse(item.Version!)).ToArray();
            var latest = candidates[0];
            if (Version.Parse(latest.Version!) < Version.Parse(tool.Version))
                return latest with { State = "更新が必要", Detail = $"MSIX {latest.Version} が導入済みです。署名付きMSIX {tool.Version} 以降へ更新してください。" };
            return latest;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or ArgumentException or InvalidOperationException or Win32Exception or FormatException)
        {
            return new("確認できない", null, null, null, Detail: "MSIX登録を確認できません: " + error.Message);
        }
    }

    static InstallationState Inspect(ToolDefinition tool, RegisteredMsix registered)
    {
        var expected = tool.Msix!;
        // PackageOrigin: Store / DeveloperSigned / LineOfBusinessだけを受け入れる。
        if (registered.Origin is not (3 or 5 or 6)) throw new IOException("署名付きパッケージの登録ではありません。");
        using var reader = XmlReader.Create(PathSafety.Resolve(registered.Directory, "AppxManifest.xml"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var manifest = XDocument.Load(reader); var ns = manifest.Root!.Name.Namespace;
        var id = manifest.Root.Element(ns + "Identity") ?? throw new IOException("パッケージIdentityがありません。");
        var name = (string?)id.Attribute("Name"); var version = (string?)id.Attribute("Version") ?? "";
        var expectedName = expected.FamilyName[..expected.FamilyName.LastIndexOf('_')];
        var publisherId = expected.FamilyName[(expected.FamilyName.LastIndexOf('_') + 1)..];
        if (name != expectedName || (string?)id.Attribute("Publisher") != expected.Publisher || (string?)id.Attribute("ProcessorArchitecture") != "x64" ||
            !Version.TryParse(version, out var numeric) || numeric.Revision < 0 ||
            registered.FullName != $"{name}_{version}_x64__{publisherId}")
            throw new IOException("MSIXのName・Publisher・版・アーキテクチャが登録情報と一致しません。");
        var app = manifest.Root.Element(ns + "Applications")?.Elements(ns + "Application").SingleOrDefault(a => (string?)a.Attribute("Id") == expected.ApplicationId);
        if ((string?)app?.Attribute("Executable") != tool.InstalledExecutable || (string?)app?.Attribute("EntryPoint") != "Windows.FullTrustApplication")
            throw new IOException("MSIXの起動設定が一致しません。");
        var executable = PathSafety.Resolve(registered.Directory, tool.InstalledExecutable);
        if (!File.Exists(executable)) throw new IOException("MSIXの実行ファイルがありません。");
        return new("導入済み", version, registered.Directory, executable, Detail: "署名付きMSIXの現在ユーザー登録とManifestを照合しました。");
    }

    internal static Process Activate(MsixDefinition identity, string origin, bool silent = true)
    {
        if (origin is not ("host_manual" or "direct_task" or "via_kotorintoys")) throw new IOException("未対応の起動経路です。");
        var type = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"), true)!;
        var activator = Activator.CreateInstance(type)!;
        try
        {
            var result = ((IApplicationActivationManager)activator).ActivateApplication(identity.FamilyName + "!" + identity.ApplicationId, ActivationArguments(origin, silent), 2, out var pid);
            Marshal.ThrowExceptionForHR(result);
            return Process.GetProcessById(checked((int)pid));
        }
        finally { Marshal.FinalReleaseComObject(activator); }
    }

    internal static string ActivationArguments(string origin, bool silent)
    {
        if (origin is not ("host_manual" or "direct_task" or "via_kotorintoys") || !silent && origin != "host_manual")
            throw new IOException("非常駐アプリは手動起動だけに対応しています。");
        return (silent ? "--startup " : "") + "--launch-origin " + origin;
    }

    internal static bool MatchesProcess(Process process, MsixDefinition identity)
    {
        uint length = 0;
        if (GetPackageFamilyName(process.Handle, ref length, null) != 122 || length is 0 or > 256) return false;
        var value = new StringBuilder((int)length);
        return GetPackageFamilyName(process.Handle, ref length, value) == 0 && value.ToString() == identity.FamilyName;
    }

    static RegisteredMsix[] ReadPackages(string family)
    {
        uint count = 0, length = 0;
        var code = GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref length, IntPtr.Zero);
        if (code == 0 && count == 0) return [];
        if (code != 122 || count is 0 or > 64 || length is 0 or > 131072) throw new Win32Exception(code);
        var names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        var buffer = Marshal.AllocHGlobal(checked((int)length * 2));
        try
        {
            code = GetPackagesByPackageFamily(family, ref count, names, ref length, buffer);
            if (code != 0) throw new Win32Exception(code);
            var result = new List<RegisteredMsix>();
            for (var index = 0; index < count; index++)
            {
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, index * IntPtr.Size)) ?? throw new IOException("MSIX登録名が空です。");
                uint pathLength = 0;
                code = GetPackagePathByFullName(name, ref pathLength, null);
                if (code != 122 || pathLength is 0 or > 32768) throw new Win32Exception(code);
                var path = new StringBuilder((int)pathLength);
                code = GetPackagePathByFullName(name, ref pathLength, path);
                if (code != 0) throw new Win32Exception(code);
                code = GetStagedPackageOrigin(name, out var origin);
                if (code != 0) throw new Win32Exception(code);
                result.Add(new(name, path.ToString(), origin));
            }
            return result.ToArray();
        }
        finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(names); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder? path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder? family);
    [DllImport("kernelbase.dll", CharSet = CharSet.Unicode)]
    static extern int GetStagedPackageOrigin(string fullName, out int origin);
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }
}
