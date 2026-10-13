using System.Text.Json;

namespace KotorinToys;

internal sealed class HostSettingsStore(string directory)
{
    public string DirectoryPath => Path.GetFullPath(directory);
    public bool RemovalPending => File.Exists(Path.Combine(DirectoryPath, "removal-operation.json"));
    public StartupAttempt? LastAttempt => File.Exists(Path.Combine(DirectoryPath, "last-startup-attempt.json")) ? JsonSerializer.Deserialize<StartupAttempt>(File.ReadAllText(Path.Combine(DirectoryPath, "last-startup-attempt.json")), ToolCatalog.Json) : null;

    public void RecordAttempt(StartupAttemptItem[] items) => AtomicWrite(Path.Combine(DirectoryPath, "last-startup-attempt.json"), new StartupAttempt(DateTimeOffset.UtcNow, items));
    string FilePath => Path.Combine(DirectoryPath, "settings.json");

    string JournalPath => Path.Combine(DirectoryPath, "startup-operation.json");

    public IDisposable AcquireLease()
    {
        Directory.CreateDirectory(DirectoryPath);
        return new FileStream(Path.Combine(DirectoryPath, "startup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public HostSettings Load()
    {
        if (!File.Exists(FilePath))
            return new();
        try
        {
            var value = JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(FilePath), ToolCatalog.Json) ?? throw new IOException("設定が空です。");
            if (value.SchemaVersion != 1 || !Guid.TryParseExact(value.OwnerToken, "N", out _) || value.StartupModes == null || value.OwnedTasks == null || value.StartupModes.Any(pair => !ValidId(pair.Key) || pair.Value is not ("none" or "via_kotorintoys" or "direct_task")) || value.OwnedTasks.Any(pair => !ValidId(pair.Key) || string.IsNullOrWhiteSpace(pair.Value.Name) || string.IsNullOrWhiteSpace(pair.Value.Fingerprint)))
                throw new IOException("本体設定の形式が不正です。原本を保持しています。");
            return value;
        }
        catch (JsonException ex)
        {
            throw new IOException("本体設定を読み込めません。原本を保持しています。", ex);
        }
    }

    static bool ValidId(string id) => System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][a-z0-9-]{0,63}$");
    public void Save(HostSettings settings) => AtomicWrite(FilePath, settings);
    public StartupJournal? Journal => File.Exists(JournalPath) ? JsonSerializer.Deserialize<StartupJournal>(File.ReadAllText(JournalPath), ToolCatalog.Json) ?? throw new IOException("操作記録が空です。") : null;

    public void SaveJournal(StartupJournal journal) => AtomicWrite(JournalPath, journal);
    public void CompleteJournal()
    {
        if (File.Exists(JournalPath))
            File.Move(JournalPath, Path.Combine(DirectoryPath, "last-startup-operation.json"), true);
    }

    void AtomicWrite<T>(string path, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, ToolCatalog.Json);
                stream.Flush(true);
            }

            if (File.Exists(path))
                File.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KotorinToys");
}
