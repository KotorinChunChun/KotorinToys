namespace KotorinToys;

internal sealed record InstallationResultRow(string Id, string Name, string State, string Detail);
internal sealed record InstallationSummary(string Title, string Detail, bool AllSucceeded, InstallationResultRow[] Rows)
{
    public static InstallationSummary Create(IReadOnlyList<ToolDefinition> targets, string operation,
        IReadOnlyList<InstallResult> results, string? interruption = null)
    {
        var rows = targets.DistinctBy(t => t.Id).Select(tool =>
        {
            var result = results.LastOrDefault(r => r.Id == tool.Id && r.Operation == operation);
            var state = result?.State ?? "未実行";
            if (state is not ("成功" or "失敗" or "要確認" or "再起動待ち" or "未実行")) state = "要確認";
            return new InstallationResultRow(tool.Id, operation == "ffmpeg" ? tool.Name + "（FFmpeg / FFprobe）" : tool.Name, state, result?.Detail ?? interruption ?? "処理結果を取得できませんでした。");
        }).ToArray();
        int Count(string state) => rows.Count(row => row.State == state);
        var succeeded = rows.Length > 0 && Count("成功") == rows.Length;
        var verb = operation == "uninstall" ? "アンインストール" : operation == "ffmpeg" ? "FFmpeg導入" : "インストール";
        var title = succeeded ? $"{verb}が完了しました（{rows.Length}件すべて成功）" : $"{verb}処理が終了しました（要対応あり）";
        var detail = $"成功 {Count("成功")}件 / 失敗 {Count("失敗")}件 / 要確認 {Count("要確認")}件 / 再起動待ち {Count("再起動待ち")}件 / 未実行 {Count("未実行")}件";
        detail += Count("再起動待ち") > 0 ? "\nPCの再起動が必要です。自動再起動は行いません。"
            : succeeded ? "\nこの画面を閉じて、ツールを起動できます。" : "\n一覧の理由を確認してください。成功した対象は再実行不要です。";
        return new(title, detail, succeeded, rows);
    }
}
