namespace KotorinToys;

// 一覧は短い要約、詳しい根拠と対処はセルのツールチップに分ける。
internal static class DashboardText
{
    public static string Published(PublicRelease release) => release.Confirmed ? release.Package?.Package.Version ?? release.Portable?.Version ?? "—" : "—";
    public static string Update(ToolDefinition tool, InstallationState installed, PublicRelease release)
    {
        if (release.Portable != null || ToolInstallationRunner.UnsupportedReason(tool) != null || tool.Msix == null && tool.DistributionStatus != "published") return "未対応";
        if (installed.State == "未導入") return "未導入";
        if (!release.Confirmed || release.Package == null || !MsixPolicy.Installed(installed)) return "未対応";
        var view = ToolVersionView.Create(installed, release);
        return view.CanUpdate ? "あり" : Version.TryParse(installed.Version, out _) ? "なし" : "未対応";
    }
    public static string Detail(ToolDefinition tool, InstallationState installed, ToolState execution, PublicRelease release,
        StartupOverview startup, string? operationError, string? catalogError, string? stateError)
    {
        if (!string.IsNullOrEmpty(operationError)) return "操作できません";
        if (!string.IsNullOrEmpty(catalogError)) return "一覧を再確認";
        if (!string.IsNullOrEmpty(stateError)) return "状態を再確認";
        if (startup.NeedsAttention) return "自動起動を確認";
        if (installed.State is "修復が必要" or "確認できない") return "導入状態を確認";
        if (execution.State == "確認できない") return "状態を再確認";
        if (ReleaseProblems.Summary(release) is { } problem) return problem;
        if (release.Portable != null || tool.Id == "shortcut-key-counter" && tool.DistributionStatus != "published") return "ZIP版を利用";
        if (tool.Msix == null && tool.DistributionStatus != "published") return "導入は未対応";
        if (!release.Confirmed) return "公開版を再確認";
        if (installed.State == "未導入") return "インストール可";
        return string.IsNullOrWhiteSpace(execution.Detail) ? "" : "状態の説明あり";
    }
    public static string Advice(ToolDefinition tool, InstallationState installed, ToolState execution, PublicRelease release)
    {
        if (!release.Confirmed && release.Problem is ReleaseProblem.Connection or ReleaseProblem.RateLimit)
            return "対処：通信状態を確認し、時間をおいて「状態を更新」を押してください。配布形式の未対応とは別の状態です。";
        if (!release.Confirmed && release.Problem is ReleaseProblem.Unavailable or ReleaseProblem.Validation)
            return "対処：GitHubの公開ページと配布情報を確認してください。照合できない配布物は実行しません。";
        if (release.Portable != null || tool.Id == "shortcut-key-counter" && tool.DistributionStatus != "published")
            return "対処：GitHubからZIPを取得して展開してください。本体からのインストール・自動起動設定は未対応です。";
        if (installed.State is "修復が必要" or "確認できない") return "対処：導入先を確認し、必要なら配布元のsetupで修復してください。設定・履歴は削除しないでください。";
        if (!release.Confirmed) return "対処：通信状態を確認し、時間をおいて「状態を更新」で再確認してください。未確認の版を最新とは判定しません。";
        if (installed.State == "未導入") return "対処：このアプリを使うには「インストール」を押してください。開発用EXEがないことと、起動済みアプリの障害は別です。";
        if (execution.State == "確認できない") return "対処：「状態を更新」で再確認してください。別配置・旧版・アプリの処理中で応答できない場合があります。強制終了は行いません。";
        return "";
    }
}
