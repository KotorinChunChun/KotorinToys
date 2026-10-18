using Windows.Management.Deployment;

namespace KotorinToys;

internal interface IMsixDeployment
{
    Task Install(string file);
    Task Remove(string fullName);
}

internal sealed class MsixDeployment : IMsixDeployment
{
    public async Task Install(string file)
    {
        var result = await new PackageManager().AddPackageAsync(new Uri(Path.GetFullPath(file)), null, DeploymentOptions.None);
        Check(result);
    }
    public async Task Remove(string fullName)
    {
        var result = await new PackageManager().RemovePackageAsync(fullName, RemovalOptions.None);
        Check(result);
    }
    static void Check(DeploymentResult result)
    {
        if (result.ExtendedErrorCode is { HResult: not 0 } error)
            throw new IOException(result.ErrorText, error);
    }
    internal static string Failure(Exception error)
    {
        int code = error.InnerException?.HResult ?? error.HResult;
        string hint = unchecked((uint)code) switch
        {
            0x800B0109 or 0x800B010A or 0x800B0100 => "発行元の署名・証明書の導入手順を確認してください。自動信頼登録はしません。",
            0x80073D02 => "別プロセスが使用中です。対象アプリを正常終了してから再試行してください。",
            0x80073CF3 => "依存パッケージまたは競合する登録を確認してください。",
            0x80073D06 => "導入済みより古い版へは変更しません。公開版を再確認してください。",
            0x80070005 => "現在ユーザーのアクセス権・組織ポリシーを確認してください。",
            _ => "詳細を確認してください。強制終了・証明書登録・自動再起動は行っていません。"
        };
        return $"MSIX処理失敗（0x{code:X8}）：{error.Message} / {hint}";
    }
}
