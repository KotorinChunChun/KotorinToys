using System.Text.Json;

namespace KotorinToys;

internal sealed partial class PublicReleaseClient
{
    async Task<PublicRelease> CheckPortable(ToolDefinition tool, CancellationToken cancellation)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var token = deadline.Token;
            var repository = ProductIdentity.FromId(tool.Id)?.Repository ?? throw new IOException("公開製品の固定配布先がありません。");
            var listText = await Read(PublicReleaseIndex.Source, PublicReleaseIndex.MaximumBytes, token);
            var list = PublicReleaseIndex.Releases(listText.Value, repository, cache.UtcNow);
            var latest = PublicReleaseMetadata.Select(list);
            var sums = latest.Asset(tool, "SHA256SUMS.txt");
            if (sums.GetProperty("size").GetInt64() is <= 0 or > 32768)
                throw new IOException("公開ハッシュ台帳の容量が不正です。");
            var text = await Read(new(sums.GetProperty("browser_download_url").GetString()!), 32768, token);
            var publication = latest.Portable(tool, text.Value);
            // 公開情報だけを返し、インストーラー用のReleasePackageへ変換しない。
            return Confirmed(null, latest.Prerelease,
                "ポータブル版の公開情報・SHA256SUMS・GitHub asset情報を照合済みです。本体からの導入・更新は未対応です。「GitHubを開く」からZIPを取得してください。",
                publication, listText, text);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OperationCanceledException or ArgumentException)
        {
            cancellation.ThrowIfCancellationRequested();
            return Failed(null, "ポータブル版の公開確認失敗：", ex);
        }
    }
}
