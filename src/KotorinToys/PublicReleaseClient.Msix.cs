using System.Text.Json;

namespace KotorinToys;

internal sealed partial class PublicReleaseClient
{
    async Task<PublicRelease> CheckMsix(ToolDefinition tool, ReleasePackage? fallback, CancellationToken cancellation)
    {
        try
        {
            if (!MsixPolicy.Supports(tool)) throw new IOException("このMSIXの固定製品識別には未対応です。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var listText = await Read(new("https://api.github.com/repos/KotorinChunChun/HttpSelector/releases?per_page=100"), 2 * 1024 * 1024, deadline.Token);
            using var list = JsonDocument.Parse(listText.Value);
            var latest = PublicReleaseMetadata.Select(list.RootElement, msix: true);
            var checksums = latest.Asset(tool, "SHA256SUMS.txt");
            if (checksums.GetProperty("size").GetInt64() is <= 0 or > 32768) throw new IOException("SHA256SUMSの容量が不正です。");
            var text = await Read(new(checksums.GetProperty("browser_download_url").GetString()!), 32768, deadline.Token);
            return Confirmed(latest.Msix(tool, text.Value, fallback), latest.Prerelease,
                "MSIX公開版・SHA-256照合済み。導入前にWindowsの署名検証と固定Publisher照合を行います。", null, listText, text);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OperationCanceledException or ArgumentException)
        {
            cancellation.ThrowIfCancellationRequested();
            return Failed(fallback, "MSIX公開確認失敗：", ex);
        }
    }
}
