using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using KotorinToys;

internal static class FocusTranslatorIntegrationTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindow(string? cls, string? title);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);

    internal static ToolDefinition Load(string root)
    {
        var tool = JsonSerializer.Deserialize<ToolDefinition>(File.ReadAllText(Path.Combine(root, "tools/FocusTranslator/tool.json")), ToolCatalog.Json)!
            with { ToolRoot = "tools/FocusTranslator" };
        ToolCatalog.Validate(tool, root);
        return tool;
    }

    internal static void Registration(string root, Action<bool, string> check)
    {
        var tool = Load(root);
        check(tool is { Id: "focus-translator", Name: "FocusTranslator", Version: "0.5.6", DistributionStatus: "unpublished" }, "FocusTranslatorの製品定義と未公開版を確認");
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "catalog/tools.json")));
        check(catalog.RootElement.GetProperty("tools").EnumerateArray().Count(x => x.GetProperty("id").GetString() == tool.Id && x.GetProperty("definition").GetString() == "tools/FocusTranslator/tool.json") == 1, "ローカルカタログへ重複なく登録");
        check(tool.Capabilities!.SequenceEqual(["status", "show", "stop", "silent-start"]), "管理操作と無表示起動を宣言");
        check(ProductIdentity.FromTool(tool)?.AppId == "KotorinToys.FocusTranslator", "固定許可表を増やさず製品識別を検証");
        var child = Path.Combine(root, tool.ToolRoot);
        check(File.Exists(PathSafety.Resolve(child, tool.Manual)) && File.Exists(PathSafety.Resolve(child, tool.Readme)), "説明書と管理連携資料が存在");
        check(File.Exists(PathSafety.Resolve(child, tool.Executable)), "管理用EXEが存在");
        check(!ToolActionPolicy.Evaluate("install", tool, new("未導入", null, null, null), new("停止中")).Enabled, "未公開版のオンライン導入を許可しない");
    }

    internal static async Task<int> Run()
    {
        var root = KotorinToys.Program.FindRoot();
        var directory = Path.Combine(root, "dev/history/v0.1.11/logs/focus-translator-" + Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(directory, "settings");
        Directory.CreateDirectory(settings);
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings))).ToLowerInvariant()[..16];
        var tool = Load(root) with { Id = "focus-translator-" + suffix };
        var manager = new ToolManager(root, (candidate, start) =>
        {
            if (candidate.Id != tool.Id) throw new IOException("試験対象以外を起動しません。");
            start.ArgumentList.Add("--settings-dir"); start.ArgumentList.Add(settings); start.ArgumentList.Add("--passive");
        });
        var messages = new List<string>(); int? owned = null;
        void Check(bool ok, string message) { if (!ok) throw new IOException(message); messages.Add("PASS: " + message); Console.WriteLine(messages[^1]); }
        nint Window() => FindWindow("FocusTranslatorSettings", "設定 - FocusTranslator - ver " + tool.Version);
        async Task<string> Raw(string request)
        {
            using var pipe = new NamedPipeClientStream(".", "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + tool.Id, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await pipe.ConnectAsync(deadline.Token);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(request.AsMemory(), deadline.Token);
            return await reader.ReadLineAsync(deadline.Token) ?? "";
        }
        try
        {
            Registration(root, Check);
            Check((await manager.Status(tool)).State == "停止中", "専用の管理窓口は開始前に停止中");
            var started = await manager.Start(tool, false); owned = started.Pid;
            Check(started.State == "実行中" && owned.HasValue, "本体から無表示起動して準備完了: " + started.Detail);
            using var process = Process.GetProcessById(owned!.Value);
            Check(started.Version == tool.Version && started.ProtocolVersion == 2 && started.LaunchOrigin == "host_manual", "実版・管理契約・起動経路が一致");
            Check(Window() == 0 || !IsWindowVisible(Window()), "管理起動は初回案内と設定画面を表示しない");
            Check((await manager.Send(tool, "status")).Ready, "UIスレッドから準備完了を応答");
            Check((await manager.Send(tool, "stop")).Result == "refused", "対象PID欠落の停止を拒否");
            using (var wrong = JsonDocument.Parse(await Raw(JsonSerializer.Serialize(new { command = "stop", expectedPid = owned + 1 }))))
                Check(wrong.RootElement.GetProperty("result").GetString() == "refused", "子自身も誤PID停止を拒否");
            using (var unknown = JsonDocument.Parse(await Raw("{\"command\":\"unknown\",\"requestId\":\"echo\"}")))
                Check(unknown.RootElement.GetProperty("result").GetString() == "unsupported" && unknown.RootElement.GetProperty("requestId").GetString() == "echo", "未対応操作と要求IDを正しく応答");
            try { await Raw("{"); } catch (IOException) { }
            Check((await manager.Send(tool, "status")).Ready, "壊れたJSONの後も管理受付を継続");
            Check((await manager.Start(tool, true)).Pid == owned && IsWindowVisible(Window()), "同じPIDの設定画面を共通タイトルで表示");
            Check((await manager.Send(tool, "stop", owned)).Result == "refused" && !process.HasExited, "設定画面を開いた状態では終了を拒否");
            Check(PostMessage(Window(), 0x0010, 0, 0), "設定画面へ通常の閉じる操作を依頼");
            await Task.Delay(250);
            Check((await manager.Stop(tool)).State == "停止中" && process.HasExited, "設定画面を閉じてから正常停止し実プロセス終了を確認"); owned = null;
            var restarted = await manager.Start(tool, false, "via_kotorintoys"); owned = restarted.Pid;
            Check(restarted.State == "実行中" && restarted.LaunchOrigin == "via_kotorintoys" && owned != started.Pid, "本体経由の起動経路で再起動");
            Check((await manager.Stop(tool)).State == "停止中", "再起動後も正常停止"); owned = null;
            var config = Path.Combine(settings, "config.json");
            Check(File.Exists(config), "試験設定は専用ディレクトリへ保存");
            Console.WriteLine($"PASS: {messages.Count} 件 / {directory}"); return 0;
        }
        catch (Exception error) { messages.Add("FAIL: " + error); Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (owned.HasValue) try
            {
                if (Window() != 0) PostMessage(Window(), 0x0010, 0, 0);
                await Task.Delay(150);
                if ((await manager.Status(tool)).Pid == owned) messages.Add("後始末: " + (await manager.Stop(tool)).State);
            } catch (Exception error) { messages.Add("正常終了未確認: " + error.Message); }
            File.WriteAllLines(Path.Combine(directory, "result.txt"), messages);
        }
    }
}
