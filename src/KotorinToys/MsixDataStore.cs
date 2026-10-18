using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace KotorinToys;

internal sealed record MsixDataFile(string Path, long Length, string Sha256);
internal sealed record MsixDataSnapshot(string Id, string Family, string Owner, MsixDataFile[] Files);

// 通常の署名付きMSIXは削除時にLocalStateも失うため、Package領域の外で保持する。
// DBだけでなくDPAPI整合性キーも含め、現在ユーザーの同じFamilyだけへ復元する。
internal sealed class MsixDataStore(string packagesRoot, string backupRoot)
{
    readonly string owner = WindowsIdentity.GetCurrent().User!.Value;
    static readonly string[] states = ["LocalState", "RoamingState"];
    string PackageRoot => Safe(packagesRoot, MsixPolicy.Family);
    string BackupRoot => Path.GetFullPath(backupRoot);
    public static MsixDataStore Default()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new(Path.Combine(local, "Packages"), Path.Combine(local, "KotorinToys", "msix-data", MsixPolicy.Family));
    }
    public IDisposable AcquireLease()
    {
        NoLinkChain(BackupRoot);
        Directory.CreateDirectory(BackupRoot);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { new SecurityIdentifier(owner), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(BackupRoot).SetAccessControl(acl);
        return new FileStream(Safe(BackupRoot, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public MsixDataSnapshot Capture()
    {
        string id = Guid.NewGuid().ToString("N"); string directory = Safe(BackupRoot, id);
        Directory.CreateDirectory(directory);
        var files = List(PackageRoot);
        foreach (var file in files)
        {
            string source = Safe(PackageRoot, file.Path), target = Safe(directory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output); output.Flush(true);
        }
        var snapshot = new MsixDataSnapshot(id, MsixPolicy.Family, owner, files);
        VerifyAt(directory, snapshot);
        File.WriteAllText(Safe(directory, "snapshot.json"), JsonSerializer.Serialize(snapshot, ToolCatalog.Json));
        // 退避中の追加・削除・更新も検出する。失敗した退避を削除許可に使わない。
        var after = List(PackageRoot);
        if (!files.SequenceEqual(after)) throw new IOException("MSIXのデータが退避中に変更されました。削除せず正常終了を確認してください。");
        return snapshot;
    }
    public void Retain(MsixDataSnapshot snapshot)
    {
        Validate(snapshot); VerifyAt(Safe(BackupRoot, snapshot.Id), snapshot);
        string temporary = Safe(BackupRoot, "retained.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, ToolCatalog.Json));
        File.Move(temporary, Safe(BackupRoot, "retained.json"), true);
    }
    public MsixDataSnapshot? Pending()
    {
        string path = Safe(BackupRoot, "retained.json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("MSIXデータの保持記録が大きすぎます。");
        var snapshot = JsonSerializer.Deserialize<MsixDataSnapshot>(File.ReadAllText(path), ToolCatalog.Json) ?? throw new IOException("MSIXデータの保持記録が空です。");
        Validate(snapshot); VerifyAt(Safe(BackupRoot, snapshot.Id), snapshot);
        return snapshot;
    }
    public void PreflightRestore(MsixDataSnapshot snapshot)
    {
        Validate(snapshot); VerifyAt(Safe(BackupRoot, snapshot.Id), snapshot);
        foreach (var file in snapshot.Files)
        {
            string destination = Safe(PackageRoot, file.Path);
            if (File.Exists(destination) && Describe(PackageRoot, destination) != file)
                throw new IOException("復元先に異なる設定・履歴があります。上書きせず退避データを保持しました。");
        }
    }
    public void Restore(MsixDataSnapshot snapshot)
    {
        PreflightRestore(snapshot);
        foreach (var file in snapshot.Files)
        {
            string destination = Safe(PackageRoot, file.Path);
            if (File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Safe(Safe(BackupRoot, snapshot.Id), file.Path), destination, false);
        }
        VerifyPreserved(snapshot);
        File.Move(Safe(BackupRoot, "retained.json"), Safe(BackupRoot, "last-restored.json"), true);
    }
    public void VerifyPreserved(MsixDataSnapshot snapshot) { Validate(snapshot); VerifyAt(PackageRoot, snapshot); }
    void Validate(MsixDataSnapshot snapshot)
    {
        if (!Guid.TryParseExact(snapshot.Id, "N", out _) || snapshot.Family != MsixPolicy.Family || snapshot.Owner != owner ||
            snapshot.Files == null || snapshot.Files.Length > 10000 || snapshot.Files.DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Count() != snapshot.Files.Length ||
            snapshot.Files.Any(f => f.Length < 0 || !System.Text.RegularExpressions.Regex.IsMatch(f.Sha256, "^[a-f0-9]{64}$") ||
                !states.Any(state => f.Path.StartsWith(state + "/", StringComparison.Ordinal)) || f.Path.Contains('\\') || f.Path.Split('/').Any(p => p is "" or "." or "..")))
            throw new IOException("MSIXデータのFamily・所有者・保持記録が不正です。");
    }
    void VerifyAt(string root, MsixDataSnapshot snapshot)
    {
        foreach (var file in snapshot.Files)
        {
            string path = Safe(root, file.Path);
            if (!File.Exists(path) || Describe(root, path) != file) throw new IOException("MSIXの設定・履歴の保持照合に失敗しました。退避データは保持しています。");
        }
    }
    MsixDataFile[] List(string root)
    {
        var files = new List<MsixDataFile>(); long total = 0;
        void Visit(string directory, int depth)
        {
            NoLink(directory);
            if (depth > 64) throw new IOException("MSIXデータの階層が深すぎます。");
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                NoLink(path);
                if (Directory.Exists(path)) Visit(path, depth + 1);
                else
                {
                    if (files.Count >= 10000 || (total += new FileInfo(path).Length) > 4L * 1024 * 1024 * 1024)
                        throw new IOException("MSIXデータの退避上限（10,000ファイル・4GB）を超えています。手動退避後に操作してください。");
                    files.Add(Describe(root, path));
                }
            }
        }
        foreach (string state in states) { string directory = Safe(root, state); if (Directory.Exists(directory)) Visit(directory, 0); }
        return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
    }
    static MsixDataFile Describe(string root, string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new(Path.GetRelativePath(root, path).Replace('\\', '/'), input.Length, Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant());
    }
    static void NoLink(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("MSIXデータのリンクは扱いません。");
    }
    static string Safe(string root, string relative)
    {
        NoLinkChain(root); string full = PathSafety.Resolve(root, relative);
        string cursor = Path.GetFullPath(root);
        foreach (string part in Path.GetRelativePath(cursor, full).Split(Path.DirectorySeparatorChar)) { cursor = Path.Combine(cursor, part); NoLink(cursor); }
        return full;
    }
    static void NoLinkChain(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent) NoLink(current.FullName);
    }
}
