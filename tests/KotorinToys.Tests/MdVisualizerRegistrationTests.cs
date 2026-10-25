using System.Text.Json;
using KotorinToys;

internal static class MdVisualizerRegistrationTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var tool = ToolCatalog.Load(root).Single(t => t.Id == "md-visualizer");
        var online = OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root, OnlineToolCatalog.RelativePath)), root).Single(t => t.Id == tool.Id);
        check(tool is { Name: "MdVisualizer", Version: "0.0.19", AppId: "KotorinToys.MdVisualizer", DistributionStatus: "unpublished" }, "MdVisualizerを独立した非常駐ツールとして登録");
        check(JsonSerializer.Serialize(tool with { ToolRoot = tool.ToolRoot.Replace('\\', '/') }, ToolCatalog.Json) == JsonSerializer.Serialize(online, ToolCatalog.Json), "MdVisualizerのローカル・配信用定義が一致");
        var manager = new ToolManager(root);
        var child = manager.DevelopmentRoot(tool);
        check(child == Path.Combine(root, "tools", "MdVisualizer") && tool.Executable == "src-tauri/target/kotorintoys/debug/mdvisualizer.exe", "独立子Git内の専用Debug実行先を解決");
        check(new[] { tool.Manual, tool.Readme, tool.Shortcuts, tool.Icon! }.All(p => File.Exists(PathSafety.Resolve(child, p))), "説明・ショートカット・固有アイコンの参照が実在");
        check(tool.Capabilities!.SequenceEqual(["status", "show", "stop", "start"]) && !tool.Capabilities.Contains("silent-start"), "通常起動能力を持ち常駐の無表示起動能力を持たない");
        var installed = new InstallationState("未導入", null, null, null);
        check(ToolActionPolicy.Evaluate("start", tool, installed, new("停止中"), executableExists: true).Enabled, "非常駐ツールも開発EXEから手動起動できる");
        check(ToolActionPolicy.Evaluate("start", tool, installed, new("実行中", 123), executableExists: true).Enabled &&
            ToolActionPolicy.Evaluate("stop", tool, installed, new("実行中", 123), executableExists: true).Enabled, "既存画面の表示・停止が有効");
        check(!ToolActionPolicy.Evaluate("install", tool, installed, new("停止中")).Enabled && ProductIdentity.FromTool(tool) == null, "単体MSIXを汎用Inno導入と混同しない");
        var directory = Path.Combine(root, "dev", "history", "v0.1.11", "logs", "mdvisualizer-policy-" + Guid.NewGuid().ToString("N"));
        var startup = new StartupCoordinator(new HostSettingsStore(directory), new FakeStartupGateway(), manager, Environment.ProcessPath!, allowDevelopment: true);
        check(startup.ModeAvailability(tool, "none").Enabled && !startup.ModeAvailability(tool, "direct_task").Enabled && !startup.ModeAvailability(tool, "via_kotorintoys").Enabled, "未導入判定と独立して自動起動2方式を拒否");
        check(ToolRepository.Find(tool)?.AbsoluteUri == "https://github.com/KotorinChunChun/MdVisualizer", "MdVisualizerのGitHub参照を維持");
    }
    sealed class FakeStartupGateway : IStartupGateway
    {
        public ScheduledTaskState? Read(string name) => null;
        public ScheduledTaskState Create(TaskPlan plan) => throw new IOException("試験でOS予約は変更しません。");
        public ScheduledTaskState Restore(string name, string xml, string securityDescriptor = "") => throw new IOException("試験でOS予約は変更しません。");
        public void Delete(string name, string fingerprint) => throw new IOException("試験でOS予約は変更しません。");
    }
}
