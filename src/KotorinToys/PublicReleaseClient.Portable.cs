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
            using var list = JsonDocument.Parse(await Read(new($"https://api.github.com/repos/KotorinChunChun/{repository}/releases?per_page=100"), 2 * 1024 * 1024, token));
            var latest = PublicReleaseMetadata.Select(list.RootElement);
            var sums = latest.Asset(tool, "SHA256SUMS.txt");
            if (sums.GetProperty("size").GetInt64() is <= 0 or > 32768)
                throw new IOException("公開ハッシュ台帳の容量が不正です。");
            var text = await Read(new(sums.GetProperty("browser_download_url").GetString()!), 32768, token);
            var publication = latest.Portable(tool, text);
            // 公開情報だけを返し、インストーラー用のReleasePackageへ変換しない。
            return new(null, true, latest.Prerelease,
                "ポータブル版の公開情報・SHA256SUMS・GitHub asset情報を照合済みです。本体からの導入・更新は未対応です。「GitHubを開く」からZIPを取得してください。",
                publication);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OperationCanceledException or ArgumentException)
        {
            cancellation.ThrowIfCancellationRequested();
            return new(null, false, false, "ポータブル版の公開確認失敗：" + ex.Message, Problem: ReleaseProblems.From(ex));
        }
    }
}
