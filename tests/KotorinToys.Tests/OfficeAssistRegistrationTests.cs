using System.Text.Json;
using KotorinToys;

internal static class OfficeAssistRegistrationTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        // 他の未取得サブモジュールに依存せず、今回追加する定義を製品と同じ検証器へ渡す。
        var child = Path.Combine(root, "tools", "OfficeAssist");
        var tool = JsonSerializer.Deserialize<ToolDefinition>(File.ReadAllText(Path.Combine(child, "tool.json")), ToolCatalog.Json)!
            with { ToolRoot = "tools/OfficeAssist" };
        ToolCatalog.Validate(tool, root);
        check(tool is { Id: "office-assist", Name: "OfficeAssist", Version: "0.1.0", DistributionStatus: "unpublished" }, "OfficeAssistを独立した未公開製品として検証");
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "catalog", "tools.json")));
        var references = catalog.RootElement.GetProperty("tools").EnumerateArray().Where(x => x.GetProperty("id").GetString() == tool.Id).ToArray();
        check(references.Length == 1 && references[0].GetProperty("definition").GetString() == "tools/OfficeAssist/tool.json", "ローカルカタログの参照とIDが一致");
        check(Directory.Exists(Path.Combine(child, ".git")) || File.Exists(Path.Combine(child, ".git")), "独立Gitを保持");
        check(File.Exists(PathSafety.Resolve(child, tool.Executable)), "管理用EXEの実在");
        check(new[] { tool.Readme, tool.Manual }.All(path => File.Exists(PathSafety.Resolve(child, path))), "利用資料の実在");
        check(tool.Capabilities!.SequenceEqual(["status", "show", "stop", "silent-start"]), "管理能力の宣言");
        check(tool.Runtimes!.Any(r => r.Kind == "dotnet-desktop" && r.Major == 10) && tool.Runtimes!.Any(r => r.Id == "excel"), "実行環境とExcel依存の宣言");
        check(ToolRepository.Find(tool) == null && ProductIdentity.FromTool(tool) == null, "公開URLや導入済み製品を推測しない");
        check(!ToolActionPolicy.Evaluate("install", tool, new("未導入", null, null, null), new("停止中")).Enabled, "未公開のインストールを拒否");
        check(File.ReadAllText(Path.Combine(root, "contracts", "ManagementEndpoint.Resilient.cs"))
            == File.ReadAllText(Path.Combine(child, "src", "OfficeAssist", "Integration", "ManagementEndpoint.cs")), "管理契約の承認済みコピーと一致");
    }
}
