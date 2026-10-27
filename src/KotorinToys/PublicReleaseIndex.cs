using System.Text.Json;
using System.Text.RegularExpressions;

namespace KotorinToys;

// 所有者が生成する公開一覧。更新停止や一部失敗を最新確認済みとして扱わない。
internal static class PublicReleaseIndex
{
    internal static readonly Uri Source = new("https://raw.githubusercontent.com/KotorinChunChun/KotorinToys/release-index/config/release-index.json");
    internal static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);
    internal const int MaximumBytes = 2 * 1024 * 1024;

    internal static JsonElement Releases(string text, string repository, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        RejectDuplicateProperties(root);
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            !root.GetProperty("generatedAt").TryGetDateTimeOffset(out var generated) ||
            generated > now.AddMinutes(5) || now - generated > MaximumAge)
            throw new IOException("公開情報一覧が古いか、更新日時を確認できません。時間をおいて再確認してください。");

        var repositories = root.GetProperty("repositories").EnumerateArray().ToArray();
        if (repositories.Length > OnlineToolCatalog.MaximumTools + 1 || repositories.Any(item =>
                !Regex.IsMatch(item.GetProperty("name").GetString() ?? "", "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")) ||
            repositories.Select(item => item.GetProperty("name").GetString()).Distinct(StringComparer.Ordinal).Count() != repositories.Length)
            throw new IOException("公開情報一覧の製品名または件数が不正です。");
        var matches = repositories.Where(item => item.GetProperty("name").GetString() == repository).ToArray();
        if (matches.Length != 1 || !matches[0].GetProperty("available").GetBoolean())
            throw new ReleaseReadException(ReleaseProblem.Connection, "この製品の公開情報を更新できませんでした。時間をおいて再確認してください。");
        var releases = matches[0].GetProperty("releases");
        if (releases.ValueKind != JsonValueKind.Array || releases.GetArrayLength() >= 100)
            throw new IOException("公開リリースを全件確認できません。");
        return releases.Clone();
    }

    static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new IOException("公開情報一覧の項目が重複しています。");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
