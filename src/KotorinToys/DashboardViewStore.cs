using System.Text.Json;

namespace KotorinToys;

internal sealed record DashboardColumnView(string Name, int Order, int Width);
internal sealed record DashboardView(int SchemaVersion = 1, int Width = 1180, int Height = 680,
    string Filter = "all", string? SortColumn = null, bool Descending = false, DashboardColumnView[]? Columns = null);

// 起動予約や所有記録とは別の、表示だけの設定。
internal sealed class DashboardViewStore(string directory)
{
    public static readonly string[] ColumnNames = ["tool","summary","installed","published","update","state","startup","detail"];
    string FilePath => Path.Combine(directory, "dashboard-view.json");
    public DashboardView Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 65536) return new();
            var view = JsonSerializer.Deserialize<DashboardView>(File.ReadAllBytes(FilePath), ToolCatalog.Json);
            if (view == null || view.SchemaVersion != 1 || view.Width is < 980 or > 6000 || view.Height is < 540 or > 4000 ||
                view.Filter is not ("all" or "installed" or "updates") || view.SortColumn != null && !ColumnNames.Contains(view.SortColumn) ||
                view.Columns?.Length > 32 || view.Columns?.Any(c => c == null || c.Width is < 40 or > 3000 || c.Order is < 0 or > 31) == true)
                return new();
            return view with { Columns = view.Columns?.Where(c => ColumnNames.Contains(c.Name)).DistinctBy(c=>c.Name).ToArray() };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }
    public string? Save(DashboardView view)
    {
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(view, ToolCatalog.Json));
            File.Move(temporary, FilePath, true); return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { return "表示設定を保存できませんでした。保存先の権限と空き容量を確認してください。"; }
        finally { try { if(File.Exists(temporary)) File.Delete(temporary); } catch(IOException) { } catch(UnauthorizedAccessException) { } }
    }
}
