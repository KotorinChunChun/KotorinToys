namespace KotorinToys;

internal enum ReleaseProblem { None, Connection, RateLimit, Unavailable, Unpublished, Unsupported, Validation }
internal sealed class ReleaseReadException(ReleaseProblem problem, string message, DateTimeOffset? retryAt = null) : IOException(message)
{
    public ReleaseProblem Problem { get; } = problem;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}
internal static class ReleaseProblems
{
    public static string JapanTime(DateTimeOffset time) => time.ToOffset(TimeSpan.FromHours(9))
        .ToString("yyyy/MM/dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    public static string RateLimitMessage(DateTimeOffset? retryAt) =>
        "GitHub公開情報の取得回数制限に達しました。" + (retryAt is { } value
            ? $"解除予定：{JapanTime(value)}（日本時間）。その時刻までは再取得を行いません。解除後に「状態を更新」で再確認してください。"
            : "解除予定時刻はGitHubから取得できません。少なくとも1分間は再取得を控え、時間をおいて「状態を更新」で再確認してください。");
    public static string Explanation(PublicRelease release) => !release.Confirmed && release.Problem == ReleaseProblem.RateLimit
        ? RateLimitMessage(release.RetryAt) : release.Detail;
    public static ReleaseProblem From(Exception error) => error switch
    {
        ReleaseReadException read => read.Problem,
        HttpRequestException or OperationCanceledException => ReleaseProblem.Connection,
        _ => ReleaseProblem.Validation
    };
    public static string? Summary(PublicRelease release) => release.Confirmed ? null : release.Problem switch
    {
        ReleaseProblem.Connection => "通信エラー", ReleaseProblem.RateLimit => "取得制限",
        ReleaseProblem.Unavailable => "公開物なし", ReleaseProblem.Unpublished => "未公開",
        ReleaseProblem.Unsupported => "形式未対応", ReleaseProblem.Validation => "公開情報を確認", _ => null
    };
}
