using System.Text.Json;
using KotorinToys;

internal static class ScriptSyncRegistrationTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var local = ToolCatalog.Load(root).Single(t => t.Id == "script-sync");
        var online = OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root, OnlineToolCatalog.RelativePath)), root)
            .Single(t => t.Id == local.Id);
        check(local is { Name: "ScriptSync", AppId: "KotorinToys.ScriptSync", Version: "0.0.6.3", DistributionStatus: "unpublished" },
            "ScriptSyncを単体MSIXと区別した収録定義として読む");
        check(JsonSerializer.Serialize(local with { ToolRoot = local.ToolRoot.Replace('\\', '/') }, ToolCatalog.Json) == JsonSerializer.Serialize(online, ToolCatalog.Json),
            "ローカル一覧と自己完結した配信用一覧のScriptSync定義が一致");
        var manager = new ToolManager(root);
        var child = manager.DevelopmentRoot(local);
        check(child == Path.Combine(root, "tools", "ScriptSync") &&
            PathSafety.Resolve(child, local.Executable) == Path.Combine(child, "target", "kotorintoys", "scriptsync.exe"),
            "移動先を基準に開発ソースと既存のRelease実行先を解決");
        check(new[] { local.Readme, local.Manual, local.Shortcuts, local.Icon! }.All(path => File.Exists(PathSafety.Resolve(child, path))),
            "ScriptSyncの説明・キー一覧・アイコン参照が実在");
        check(ToolRepository.Find(local)?.AbsoluteUri == "https://github.com/KotorinChunChun/ScriptSync",
            "子Gitで確認したScriptSyncリポジトリを案内");
        check(local.Capabilities!.SequenceEqual(["status", "show", "stop", "silent-start"]) && ProductIdentity.FromTool(local) == null &&
            ToolInstallationRunner.UnsupportedReason(local) != null,
            "管理IPC対応後も単体MSIXを汎用Inno管理製品として扱わない");
        var uninstalled = new InstallationState("未導入", null, null, null);
        var stopped = new ToolState("停止中");
        check(ToolActionPolicy.Evaluate("start", local, uninstalled, stopped, executableExists: true).Enabled &&
            ToolActionPolicy.Evaluate("stop", local, uninstalled, new("実行中", 123), executableExists: true).Enabled &&
            !ToolActionPolicy.Evaluate("install", local, uninstalled, stopped).Enabled,
            "開発EXEの管理操作を有効化し本体からの導入は未対応のまま保つ");
        check(ToolActionPolicy.Evaluate("manual", local, uninstalled, stopped, documentExists: true).Enabled,
            "未対応操作とは独立して使い方を開ける");
        using var tauri = JsonDocument.Parse(File.ReadAllText(Path.Combine(child, "src-tauri", "tauri.conf.json")));
        using var version = JsonDocument.Parse(File.ReadAllText(Path.Combine(child, "dev", "msix-version.json")));
        check(tauri.RootElement.GetProperty("identifier").GetString() == "jp.kotorinchunchun.scriptsync" &&
            local.Version == version.RootElement.GetProperty("applicationVersion").GetString() + "." + version.RootElement.GetProperty("revision").GetInt32(),
            "既存MSIX識別子と4部版を維持");
        var before = SynchronizationContext.Current;
        try
        {
            var tools = manager.Load();
            foreach (bool development in new[] { false, true })
            {
                using var form = new Dashboard(root, toolManager: manager, showDevelopmentState: development,
                    detectInstallation: _ => uninstalled, fileExists: _ => true);
                form.CreateControl();
                form.ApplySnapshot(tools.Select(_ => uninstalled).ToArray(), tools.Select(_ => new ToolState("実行中", 123)).ToArray(),
                    tools.Select(_ => new PublicRelease(null,false,false,"隔離試験")).ToArray());
                var grid = (DataGridView)form.Controls.Find("toolTable",true).Single();
                var row = grid.Rows.Cast<DataGridViewRow>().Single(r=>Equals(r.Tag,local.Id));
                check(Equals(row.Cells["installed"].Value,"未導入") && Equals(row.Cells["state"].Value,development ? "実行中（開発）" : "-"),
                    development ? "ローカル開発一覧で実行状態を明示し導入済みとは表示しない" : "通常一覧の未導入表示は従来どおり維持");
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(before); }
    }
}
