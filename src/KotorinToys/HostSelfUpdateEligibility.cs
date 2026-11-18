namespace KotorinToys;

internal static class HostSelfUpdateEligibility
{
    internal static readonly string[] RequiredFiles = ["KotorinToys.Maintenance.exe", "KotorinToys.Maintenance.runtimeconfig.json", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll"];

    public static string? Explain(InstallationState actual, string? executable, string currentVersion,
        Func<string, bool> fileExists)
    {
        var running = Paths(("実行中のファイル", executable));
        const string manual = "\n対処：GitHub Releasesの本体setup.exeでインストールしてください。設定・履歴は保持されます。";
        if (actual.State == "未導入")
            return "更新できません：このユーザーの正式インストール登録がありません。" + running + manual;
        if (actual.State != "導入済み")
            return $"更新できません：正式導入を確認できません（{actual.State}）。\n理由：{actual.Detail}" + running + manual;
        var launch = "\n対処：本体を終了し、正式な実行ファイルから起動し直してください。";
        if (!SelfUpdateFiles.SamePath(actual.Executable, executable))
            return "更新できません：正式導入版とは別のEXEを起動しています。" +
                Paths(("実行中のファイル", executable), ("正式な実行ファイル", actual.Executable)) + launch;
        if (actual.Directory == null || !SelfUpdateFiles.SamePath(actual.Executable,
                PathSafety.Resolve(actual.Directory, HostProduct.Definition.Executable)))
            return "更新できません：正式導入先に登録された実行ファイルが一致しません。" +
                Paths(("正式導入先", actual.Directory), ("正式な実行ファイル", actual.Executable)) + manual;
        if (actual.Version != currentVersion)
            return $"更新できません：実行中の版と導入済みの版が異なります。\n実行中の版：{currentVersion}\n導入済みの版：{actual.Version}" +
                running + "\n対処：本体を終了して正式導入版を起動し直してください。解消しない場合はsetup.exeで更新してください。";
        foreach (var name in RequiredFiles)
        {
            var path = PathSafety.Resolve(actual.Directory, "maintenance/" + name);
            if (!fileExists(path))
                return "更新できません：更新用ファイルがないか、アクセスできません。\n対象：" + path + manual;
        }
        return null;
    }

    static string Paths(params (string Label, string? Value)[] rows)
    {
        var width = rows.Max(row => row.Label.Length);
        return string.Concat(rows.Select(row => "\n" + row.Label.PadRight(width, '　') + "：" +
            (row.Value ?? "パスを取得できません")));
    }
}
