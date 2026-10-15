using System.Xml.Linq;
using KotorinToys;

internal static class MsixInstallationTests
{
    public static void Run(string fixtures, Action<bool, string> check)
    {
        var directory = Path.Combine(fixtures, "signed-msix"); Directory.CreateDirectory(directory);
        var identity = new MsixDefinition("HttpSelector.Development_xtxrnffqrdk4w", "CN=KotorinChunChun");
        var tool = new ToolDefinition("http-selector", "HttpSelector", "debug.exe", "README.md", Version: "0.0.3.2", InstalledExecutable: "HttpSelector.exe", Msix: identity);
        var registration = new RegisteredMsix("HttpSelector.Development_0.0.3.2_x64__xtxrnffqrdk4w", directory, 5);
        File.WriteAllText(Path.Combine(directory, "HttpSelector.exe"), "fixture only");
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        void Manifest(string publisher = "CN=KotorinChunChun", string version = "0.0.3.2", string executable = "HttpSelector.exe") =>
            new XDocument(new XElement(ns + "Package", new XElement(ns + "Identity", new XAttribute("Name", "HttpSelector.Development"), new XAttribute("Publisher", publisher), new XAttribute("Version", version), new XAttribute("ProcessorArchitecture", "x64")),
                new XElement(ns + "Applications", new XElement(ns + "Application", new XAttribute("Id", "App"), new XAttribute("Executable", executable), new XAttribute("EntryPoint", "Windows.FullTrustApplication")))))
                .Save(Path.Combine(directory, "AppxManifest.xml"));
        Manifest();
        var missing = MsixInstallation.Detect(tool, _ => []);
        check(missing.State == "未導入" && missing.Detail.Contains("署名付きMSIX"), "MSIX未導入は署名付き導入へ案内");
        var installed = MsixInstallation.Detect(tool, family => { check(family == identity.FamilyName, "固定Package Familyだけを照会"); return [registration]; });
        check(installed.State == "導入済み" && installed.Version == "0.0.3.2", "MSIX登録・Publisher・4部版を照合");
        check(MsixInstallation.Detect(tool, _ => [registration with { Origin = 4 }]).State == "確認できない", "署名なし開発登録を正規MSIXにしない");
        Manifest("CN=Other");
        check(MsixInstallation.Detect(tool, _ => [registration]).State == "確認できない", "異なるPublisherを拒否");
        Manifest(executable: "../outside.exe");
        check(MsixInstallation.Detect(tool, _ => [registration]).State == "確認できない", "MSIX起動EXE不一致を拒否");
        Manifest(version: "0.0.3.1");
        var old = MsixInstallation.Detect(tool, _ => [registration with { FullName = "HttpSelector.Development_0.0.3.1_x64__xtxrnffqrdk4w" }]);
        check(old.State == "更新が必要" && old.Detail.Contains("0.0.3.2"), "旧MSIXを開発EXEへ切り替えず更新を案内");
    }
}
