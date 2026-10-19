namespace KotorinToys;

internal static class ReleaseDisplay
{
    // OSの表示言語やタイムゾーンに依存せず、日本時間のリリース公開日を表示する。
    public static string Version(ReleasePackage item) => item.Package.Version + Date(item.PublishedAt);
    public static string Date(DateTimeOffset? date) => date is { } value
        ? "（" + value.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture) + " 公開）"
        : "（公開日不明）";

    public static string Confirmation(IReadOnlyList<ToolDefinition> tools, string operation,
        Func<ToolDefinition, InstallationState> installed, ReleaseCatalog? catalog, string? localCatalog,
        IReadOnlyCollection<ToolDefinition>? definitions = null)
    {
        var local = localCatalog == null || operation != "install" ? null : InstallerAdapter.Load(localCatalog, definitions ?? tools);
        var lines = tools.Select(tool =>
        {
            var actual = installed(tool);
            string current = actual.Version ?? actual.State;
            if (operation != "install") return tool.Name + "：" + current + " → アンインストール";
            var item = catalog?.Packages.SingleOrDefault(p => p.Package.Id == tool.Id);
            string target = local != null
                ? (local.Packages.SingleOrDefault(p => p.Id == tool.Id)?.Version ?? "台帳に対象なし") + "（ローカル・公開日不明）"
                : item != null ? Version(item) : "取得可能な公開版なし";
            var reason = local == null ? ToolInstallationRunner.PublicReason(tool, catalog) : ToolInstallationRunner.UnsupportedReason(tool);
            return tool.Name + "：" + current + " → " + target + (reason == null ? "" : "\n  実行不可：" + reason);
        });
        return string.Join("\n", lines) + "\n\n" +
            (operation == "install" ? "インストール / アップデート" : "アンインストール") +
            "してもよろしいですか？";
    }
}
