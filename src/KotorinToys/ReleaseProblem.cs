namespace KotorinToys;

internal enum ReleaseProblem { None, Connection, RateLimit, Unavailable, Unpublished, Unsupported, Validation }
internal sealed class ReleaseReadException(ReleaseProblem problem, string message) : IOException(message)
{ public ReleaseProblem Problem { get; } = problem; }
internal static class ReleaseProblems
{
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
