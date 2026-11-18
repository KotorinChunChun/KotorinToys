using System.Text.Json;
using Microsoft.Win32;

namespace KotorinToys;

internal static class TranscriptionDistribution
{
    internal const string Id = "transcription-server";
    internal const string Name = "TranscriptionServer";
    internal const string AppId = "KotorinToys.TranscriptionServer";
    internal const string NativeAppId = "{6C0A7E36-24C5-4D67-8D18-0677E953741B}";
    internal const string Repository = "https://github.com/KotorinChunChun/TranscriptionServer";
    internal const string Kind = "transcription-inno";
    internal const string Signature = "github-checksum";
    internal static readonly Uri Releases = new("https://api.github.com/repos/KotorinChunChun/TranscriptionServer/releases?per_page=100");
    internal static string FileName(string version) => $"TranscriptionServer-v{version}-setup-x64.exe";
    internal static bool Supports(ToolDefinition tool) => tool.Id == Id && tool.Name == Name && tool.AppId == AppId &&
        tool.InstalledExecutable == "TranscriptionServer.exe" && tool.RepositoryUrl == Repository && tool.Msix == null;

    internal static InstallationRegistration? ReadRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + NativeAppId + "_is1");
        return key == null ? null : new(key.GetValue("InstallLocation") as string, key.GetValue("DisplayVersion") as string);
    }

    internal static InstallationState Detect(string directory, string executable, string? registeredVersion)
    {
        using var marker = Read(directory, "installation.json");
        using var build = Read(directory, "BUILD.json");
        var identity = marker.RootElement;
        var data = build.RootElement;
        if (identity.GetProperty("product").GetString() != Name || identity.GetProperty("appId").GetString() != NativeAppId ||
            identity.GetProperty("scope").GetString() != "user" || data.GetProperty("runtime").GetString() != "rust" ||
            !Version.TryParse(registeredVersion, out var version) || version.Build < 0 || version.Revision >= 0 ||
            data.GetProperty("version").GetString() != registeredVersion || !File.Exists(executable))
            throw new IOException("TranscriptionServerの実行ファイル、製品識別または導入版を照合できません。");
        return new("導入済み", registeredVersion, directory, executable);
    }

    static JsonDocument Read(string directory, string name)
    {
        var path = PathSafety.Resolve(directory, name);
        if (!File.Exists(path) || new FileInfo(path).Length > 32768)
            throw new IOException($"TranscriptionServerの導入情報 {name} を読めません。");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }
}
