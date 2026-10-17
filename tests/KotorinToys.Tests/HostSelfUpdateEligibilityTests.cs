using KotorinToys;

internal static class HostSelfUpdateEligibilityTests
{
    public static void Run(string fixtures, Action<bool, string> check)
    {
        var root = Path.Combine(fixtures, "registered-host");
        var exe = Path.Combine(root, "KotorinToys.exe");
        var devExe = Path.Combine(fixtures, "debug", "KotorinToys.exe");
        var valid = new InstallationState("導入済み", "0.1.2", root, exe);
        string? Reason(InstallationState actual, string? running = null, string version = "0.1.2", string? runningRoot = null, Func<string, bool>? exists = null)
            => HostSelfUpdateEligibility.Explain(actual, runningRoot ?? root, running ?? exe, version, exists ?? (_ => true));
        var reason = Reason(new("未導入", null, null, null));
        check(reason?.Contains("正式インストール登録がありません") == true && reason.Contains(exe) && reason.Contains("setup.exe"), "未登録は原因・実行先・手動導入の対処を示す");
        reason = Reason(valid with { State = "修復が必要", Detail = "登録された実行ファイルがありません。" });
        check(reason?.Contains("修復が必要") == true && reason.Contains("登録された実行ファイルがありません"), "正式導入の検出異常は元の具体的理由を保持");
        reason = Reason(valid, running: devExe, runningRoot: Path.Combine(fixtures, "source"));
        check(reason?.Contains("参照先と正式導入先が異なります") == true && reason.Contains(devExe) && reason.Contains(root) && reason.Contains(exe) && reason.Contains("起動し直し"), "開発版と正式導入先の不一致は比較した実パスと対処を示す");
        reason = Reason(valid, running: devExe);
        check(reason?.Contains("別のEXE") == true && reason.Contains(devExe) && reason.Contains(exe), "参照ルートが同じでも実行EXEが違う原因を区別");
        reason = Reason(valid, version: "0.1.1");
        check(reason?.Contains("実行中の版：0.1.1") == true && reason.Contains("導入済みの版：0.1.2"), "版の不一致は比較した両方の版を示す");
        foreach (var file in HostSelfUpdateEligibility.RequiredFiles)
        {
            reason = Reason(valid, exists: path => Path.GetFileName(path) != file);
            check(reason?.Contains(Path.Combine(root, "maintenance", file)) == true && reason.Contains("setup.exe"), "更新依存ファイルの不足と修復方法を示す：" + file);
        }
        check(Reason(valid) == null && Reason(valid with { Directory = root.ToUpperInvariant() + Path.DirectorySeparatorChar, Executable = exe.ToUpperInvariant() }) == null,
            "正常導入は許可しパスの大小文字・末尾区切りだけの違いを誤検出しない");
        check(HostSelfUpdateEligibility.Explain(valid, root, null, "0.1.2", _ => true)?.Contains("パスを取得できません") == true,
            "実行ファイルのパス取得失敗を具体的に表示");
    }
}
