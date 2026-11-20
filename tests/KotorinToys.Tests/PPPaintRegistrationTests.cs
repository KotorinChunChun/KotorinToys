using System.Text.Json;
using KotorinToys;

internal static class PPPaintRegistrationTests
{
    internal static ToolDefinition Load(string root) => JsonSerializer.Deserialize<ToolDefinition>(
        File.ReadAllText(Path.Combine(root, "tools", "PPPaint", "tool.json")), ToolCatalog.Json)!
        with { ToolRoot = "tools/PPPaint" };

    public static void Run(string root, Action<bool, string> check)
    {
        var tool = Load(root);
        ToolCatalog.Validate(tool, root);
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "catalog", "tools.json")));
        var references = catalog.RootElement.GetProperty("tools").EnumerateArray().Where(t => t.GetProperty("id").GetString() == tool.Id).ToArray();
        check(references.Length == 1 && references[0].GetProperty("definition").GetString() == "tools/PPPaint/tool.json", "PPPaintのローカルカタログ参照が一意");
        var online = OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root, OnlineToolCatalog.RelativePath)), root).Single(t => t.Id == tool.Id);
        check(tool is { Id: "pppaint", Name: "PPPaint", Version: "0.2.0", AppId: "KotorinToys.PPPaint", DistributionStatus: "unpublished" }, "PPPaintを独立した未公開の画像編集ツールとして登録");
        check(JsonSerializer.Serialize(tool, ToolCatalog.Json) == JsonSerializer.Serialize(online, ToolCatalog.Json), "PPPaintのローカル・配信用定義が一致");
        var manager = new ToolManager(root);
        var child = manager.DevelopmentRoot(tool);
        check(child == Path.Combine(root, "tools", "PPPaint") &&
            (Directory.Exists(Path.Combine(child, ".git")) || File.Exists(Path.Combine(child, ".git"))) &&
            tool.Executable == "artifacts/management/PPPaint.exe", "独立子Gitと専用の管理実行先へ解決");
        check(new[] { tool.Manual, tool.Readme, tool.Shortcuts, tool.Icon! }.All(p => File.Exists(PathSafety.Resolve(child, p))), "利用説明・ショートカット・固有アイコンの参照が実在");
        check(tool.Capabilities!.SequenceEqual(["status", "show", "stop", "start"]) && tool.Runtimes!.Length == 0, "非常駐の手動起動と自己完結した実行環境を宣言");
        var installed = new InstallationState("未導入", null, null, null);
        check(ToolActionPolicy.Evaluate("start", tool, installed, new("停止中"), executableExists: true).Enabled &&
            ToolActionPolicy.Evaluate("stop", tool, installed, new("実行中", 123), executableExists: true).Enabled, "開発EXEの手動起動と実行中の正常停止が有効");
        check(!ToolActionPolicy.Evaluate("install", tool, installed, new("停止中")).Enabled && ProductIdentity.FromTool(tool) == null, "未公開の導入やInno形式を推測しない");
        var directory = Path.Combine(root, "dev", "history", "v0.1.16", "logs", "pppaint-policy-" + Guid.NewGuid().ToString("N"));
        var startup = new StartupCoordinator(new HostSettingsStore(directory), new FakeStartupGateway(), manager, Environment.ProcessPath!, allowDevelopment: true);
        check(startup.ModeAvailability(tool, "none").Enabled && !startup.ModeAvailability(tool, "direct_task").Enabled &&
            !startup.ModeAvailability(tool, "via_kotorintoys").Enabled, "自動起動を新設せず2方式とも無効");
        check(ToolRepository.Find(tool)?.AbsoluteUri == "https://github.com/KotorinChunChun/PPPaint", "確認済みのPPPaintリポジトリを使用");
    }

    sealed class FakeStartupGateway : IStartupGateway
    {
        public ScheduledTaskState? Read(string name) => null;
        public ScheduledTaskState Create(TaskPlan plan) => throw new IOException("試験でOS予約は変更しません。");
        public ScheduledTaskState Restore(string name, string xml, string securityDescriptor = "") => throw new IOException("試験でOS予約は変更しません。");
        public void Delete(string name, string fingerprint) => throw new IOException("試験でOS予約は変更しません。");
    }
}
