namespace KotorinToys;

internal sealed record ToolVersionView(string Installed, string Published, string Update, bool CanUpdate)
{
    public static ToolVersionView Create(InstallationState installed, PublicRelease release)
    {
        string actual = installed.State == "導入済み" ? installed.Version ?? "未確認" : installed.State;
        string available = release.Package?.Package.Version ?? "不明";
        if (!release.Confirmed) return new(actual, release.Package == null ? "不明" : available + "（同梱台帳）", "公開版を確認できません", false);
        if (release.Prerelease) available += "（開発版）";
        if (installed.State == "未導入") return new(actual, available, "未導入", false);
        if (installed.State != "導入済み" || !Version.TryParse(installed.Version, out var current) ||
            !Version.TryParse(release.Package?.Package.Version, out var published)) return new(actual, available, "導入版を確認できません", false);
        return new(actual, available, current < published ? "更新あり" : current == published ? "公開版と同じ" : "公開版より新しい", current < published);
    }
}

internal static class DashboardStatus
{
    public static string Summary(IReadOnlyList<ToolState> states)
    {
        if (states.Count == 0) return "全体：状態確認中";
        var counts = states.GroupBy(s => s.State).Select(g => $"{g.Key} {g.Count()}件");
        return "全体：" + string.Join(" / ", counts);
    }
}
