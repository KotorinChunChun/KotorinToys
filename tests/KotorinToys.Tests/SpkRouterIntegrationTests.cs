using System.Text.Json;
using KotorinToys;

internal static class SpkRouterIntegrationTests
{
    public static async Task<int> Run()
    {
        var root = KotorinToys.Program.FindRoot();
        // 認証トークンを含む設定を版別ログへ書かない。
        var config = Path.Combine(Environment.GetEnvironmentVariable("TEMP")!, "KotorinToys-Spk-" + Guid.NewGuid().ToString("N"), "spk-settings.json");
        var manager = new ToolManager(root, (_, start) => {
            start.ArgumentList.Add("--config"); start.ArgumentList.Add(config);
        });
        var tool = manager.Load().Single(t => t.Id == "voice-mixer-spk-router");
        void Require(bool value, string label) { if (!value) throw new IOException(label); Console.WriteLine("PASS: " + label); }
        Require((await manager.Status(tool)).State == "停止中", "既存SpkRouterのない環境で試験");
        bool owned = false;
        try
        {
            var started = await manager.Start(tool, showWindow: false);
            owned = started.State == "実行中";
            Require(owned && started.Pid != null, "Toysから無表示起動し管理窓口を確認");
            var reply = await manager.Send(tool, "status");
            Require(reply.ProtocolVersion == 2 && reply.Version == "1.0.10", "SpkRouter 1.0.10の管理契約2へ実接続");
            Require((await manager.Start(tool, showWindow: false)).Pid == started.Pid, "既存PIDを保持");
            Require((await manager.Send(tool, "stop", started.Pid!.Value + 1)).Result == "refused", "別PIDへの停止を拒否");
            Require((await manager.Stop(tool)).State == "停止中", "Toysから正常停止し終了を確認");
            owned = false;
            var restarted = await manager.Start(tool, showWindow: false);
            owned = restarted.State == "実行中";
            Require(owned && restarted.Pid != started.Pid, "停止後に再起動");
            Require((await manager.Stop(tool)).State == "停止中", "再起動後も正常停止");
            owned = false;
            using var client = new PublicReleaseClient();
            var release = await client.Check(tool, ReleaseDownloader.Load(root).Packages.Single(p => p.Package.Id == tool.Id));
            Require(release.Confirmed && release.Prerelease && release.Package?.Package.Version == "1.0.10", "公開版と製品台帳の実照合");
            return 0;
        }
        finally { if (owned) await manager.Stop(tool); }
    }
}
