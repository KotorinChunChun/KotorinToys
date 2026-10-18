using System.Text.Json;
using KotorinToys;

internal static class MicRouterIntegrationTests
{
    public static async Task<int> Run()
    {
        var root = KotorinToys.Program.FindRoot();
        var folder = Path.Combine(root, "dev/history/v0.1.6/logs/mic-router-ipc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var config = Path.Combine(folder, "mic-settings.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new {
            microphone_id = "default://console", microphone_name = "既定のデバイス",
            cable_id = "default://console", cable_name = "既定のデバイス",
            cable_enabled = false, voice_input_enabled = false, monitoring = false,
            plugin_directory = Path.Combine(root, "tools/VoiceMixerMicRouter/plugins"),
            rvc_parameters = new { }, last_mode = "muted", rvc_enabled = false,
            hotkey_defaults_revision = 1, overlay_mode = "off"
        }));
        var manager = new ToolManager(root, (_, start) => {
            start.ArgumentList.Add("--config"); start.ArgumentList.Add(config);
        });
        var tool = manager.Load().Single(t => t.Id == "voice-mixer-mic-router");
        void Require(bool value, string label) { if (!value) throw new IOException(label); Console.WriteLine("PASS: " + label); }
        Require((await manager.Status(tool)).State == "停止中", "既存MicRouterがない環境だけで実接続試験");
        bool owned = false;
        try
        {
            var started = await manager.Start(tool, showWindow: false);
            owned = started.State == "実行中";
            Require(owned && started.Pid != null, "Toysの起動経路で管理窓口が準備完了");
            var reply = await manager.Send(tool, "status");
            Require(reply.ProtocolVersion == 2 && reply.Version == "1.0.18", "最新版の管理契約2に実接続");
            var same = await manager.Start(tool, showWindow: false);
            Require(same.Pid == started.Pid, "再起動要求は既存PIDを保持");
            Require((await manager.Send(tool, "stop", started.Pid!.Value + 1)).Result == "refused", "別PIDへの停止を拒否");
            Require((await manager.Send(tool, "shortcuts", started.Pid)).Result == "unsupported", "未対応操作を成功としない");
            Require((await manager.Stop(tool)).State == "停止中", "Toysが正常終了後の実停止を確認");
            owned = false;
            using var client = new PublicReleaseClient();
            var release = await client.Check(tool, ReleaseDownloader.Load(root).Packages.Single(p => p.Package.Id == tool.Id));
            Require(release.Confirmed && release.Prerelease && release.Package?.Package.Version == "1.0.18", "Toys自身の公開版・台帳照合に成功");
            return 0;
        }
        finally { if (owned) await manager.Stop(tool); }
    }
}
