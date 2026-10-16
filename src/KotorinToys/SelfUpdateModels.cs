using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KotorinToys;

internal sealed record SelfUpdatePlan(int SchemaVersion, string Id, string Root, string SettingsDirectory,
    int ParentPid, long ParentStartUtcTicks, string FromVersion, ReleasePackage Release, string Catalog);
internal sealed record SelfUpdateReady(string Id, int WorkerPid);
internal sealed record SelfUpdateResult(int SchemaVersion, string Id, string Root, string FromVersion,
    string ToVersion, string State, string Detail, bool Restarted, int? ExitCode, DateTimeOffset CompletedAt);

internal static class SelfUpdateFiles
{
    public static readonly JsonSerializerOptions Json = new(ToolCatalog.Json)
    { MaxDepth = 16, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static T Read<T>(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new IOException("更新記録の容量が不正です。");
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json) ?? throw new IOException("更新記録が空です。");
    }

    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = PathSafety.Resolve(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value, Json); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool SamePath(string? a, string? b) => a != null && b != null &&
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    public static void Validate(SelfUpdatePlan plan, string job)
    {
        if (plan.SchemaVersion != 1 || !Regex.IsMatch(plan.Id ?? "", "^[a-f0-9]{32}$") ||
            plan.ParentPid <= 0 || plan.ParentStartUtcTicks <= 0 || plan.ParentStartUtcTicks > DateTime.MaxValue.Ticks ||
            string.IsNullOrWhiteSpace(plan.Root) || string.IsNullOrWhiteSpace(plan.SettingsDirectory) ||
            !Path.IsPathFullyQualified(plan.Root) || !Path.IsPathFullyQualified(plan.SettingsDirectory) || plan.Release?.Package == null ||
            SamePath(plan.Root, Path.GetPathRoot(plan.Root)) ||
            plan.Catalog == null || !Regex.IsMatch(plan.Catalog, "^download-[a-f0-9]{32}/packages\\.json$"))
            throw new IOException("自己更新の操作記録が不正です。");
        if (!SamePath(job, PathSafety.Resolve(plan.SettingsDirectory, "updates/" + plan.Id))) throw new IOException("更新jobの配置が一致しません。");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Root)) + Path.DirectorySeparatorChar;
        var canonicalJob = Path.TrimEndingDirectorySeparator(Path.GetFullPath(job)) + Path.DirectorySeparatorChar;
        if (canonicalJob.StartsWith(root, StringComparison.OrdinalIgnoreCase) || root.StartsWith(canonicalJob, StringComparison.OrdinalIgnoreCase))
            throw new IOException("更新プロセスは導入先と分離した専用領域で実行してください。");
        if (!HostProduct.IsNewer(plan.Release, plan.FromVersion)) throw new IOException("自己更新の対象が新しい版ではありません。");
        PathSafety.Resolve(job, plan.Catalog);
    }

    public static void ValidateInstallation(InstallationState installed, SelfUpdatePlan plan, bool afterUpdate = false)
    {
        if (installed.State != "導入済み" || !SamePath(installed.Directory, plan.Root) ||
            !SamePath(installed.Executable, PathSafety.Resolve(plan.Root, "KotorinToys.exe")) ||
            installed.Version != (afterUpdate ? plan.Release.Package.Version : plan.FromVersion))
            throw new IOException("本体の正式導入・製品・実行先・版を照合できません。");
    }
}
