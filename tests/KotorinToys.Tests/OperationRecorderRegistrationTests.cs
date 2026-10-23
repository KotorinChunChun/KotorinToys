using KotorinToys;

internal static class OperationRecorderRegistrationTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var local = ToolCatalog.Load(root).Single(t => t.Id == "operation-recorder");
        var online = OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root, OnlineToolCatalog.RelativePath)), root)
            .Single(t => t.Id == local.Id);
        check(local is { Name: "OperationRecorder", Version: "1.3.2", AppId: "KotorinToys.OperationRecorder", InstalledExecutable: "OperationRecorder.exe", DistributionStatus: "unpublished" },
            "OperationRecorderの製品識別・版・未公開状態をローカル定義から取得");
        check(online.Name == local.Name && online.AppId == local.AppId && online.Version == local.Version &&
            online.Executable == local.Executable && online.InstalledExecutable == local.InstalledExecutable &&
            online.Manual == local.Manual && online.DistributionStatus == local.DistributionStatus &&
            online.Capabilities!.SequenceEqual(local.Capabilities!) && online.RepositoryUrl == local.RepositoryUrl,
            "OperationRecorderを子ファイルなしの配信用一覧でも同じ契約で取得");
        var manager = new ToolManager(root);
        check(manager.DevelopmentRoot(local) == Path.Combine(root, "tools", "OperationRecorder") &&
            PathSafety.Resolve(manager.DevelopmentRoot(local), local.Executable) == Path.Combine(root, "tools", "OperationRecorder", "artifacts", "management", "bin", "OperationRecorderApp", "debug", "OperationRecorder.exe"),
            "OperationRecorderの独立配置と管理専用ビルド出力を解決");
        check(string.IsNullOrEmpty(local.RepositoryUrl) && ToolRepository.Find(local) == null && ToolRepository.Find(online) == null,
            "OperationRecorderの未確認GitHub URLを補完しない");
        check(ToolInstallationRunner.PublicReason(local, ReleaseDownloader.Load(root)) != null && ProductIdentity.FromId(local.Id) == null,
            "OperationRecorderの未公開インストーラーを導入可能にせず固定配布表へ追加しない");
        var stopped = new ToolState("停止中");
        var uninstalled = new InstallationState("未導入", null, null, null);
        check(ToolActionPolicy.Evaluate("start", local, uninstalled, stopped, executableExists: true).Enabled &&
            !ToolActionPolicy.Evaluate("install", local, uninstalled, stopped).Enabled,
            "OperationRecorderは開発EXEがあれば起動でき未公開の導入操作は無効");
        check(ToolActionPolicy.Evaluate("stop", local, uninstalled, new("実行中", 1), executableExists: true).Enabled &&
            ToolActionPolicy.Evaluate("manual", local, uninstalled, stopped, documentExists: true).Enabled &&
            File.Exists(PathSafety.Resolve(manager.DevelopmentRoot(local), local.Manual)),
            "OperationRecorderの正常停止と同梱使い方に既存の汎用操作を適用");
    }
}
