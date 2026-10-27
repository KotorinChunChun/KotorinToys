using System.Text.Json;
using KotorinToys;

internal static class ReleaseIndexFixture
{
    internal static object Index(string repository, object releases, DateTimeOffset? generated = null, bool available = true) =>
        new { schemaVersion = 1, generatedAt = generated ?? DateTimeOffset.UtcNow,
            repositories = new[] { new { name = repository, available, releases } } };

    internal static string Json(string repository, object releases, DateTimeOffset? generated = null, bool available = true) =>
        JsonSerializer.Serialize(Index(repository, releases, generated, available), ToolCatalog.Json);

    internal static bool IsIndex(HttpRequestMessage request) => request.RequestUri == PublicReleaseIndex.Source;
}
