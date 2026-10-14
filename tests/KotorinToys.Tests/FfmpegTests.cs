using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using KotorinToys;

internal static class FfmpegTests
{
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> get) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => get(request, token); }
    public static async Task Run(string fixtures, Action<bool, string> check)
    {
        const string prefix = "ffmpeg-9.0.2-essentials_build/";
        byte[] Zip(string? extra = null, bool missing = false)
        {
            using var data = new MemoryStream();
            using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
            {
                foreach (var file in new[] { "bin/ffmpeg.exe", "bin/ffprobe.exe", "LICENSE", "README.txt" }.Where(f => !missing || f != "bin/ffprobe.exe"))
                { using var writer = new StreamWriter(zip.CreateEntry(prefix + file).Open()); writer.Write("テスト:" + file); }
                if (extra != null) { using var writer = new StreamWriter(zip.CreateEntry(extra).Open()); writer.Write("未採用"); }
            }
            return data.ToArray();
        }
        var root = Path.Combine(fixtures, "ffmpeg"); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "SSCapture.exe"), "テスト用識別子");
        Directory.CreateDirectory(Path.Combine(root, "data"));
        var history = Path.Combine(root, "data", "history.json"); File.WriteAllText(history, "保存履歴");
        var installed = new InstallationState("導入済み", "0.1.0", root, Path.Combine(root, "SSCapture.exe"));
        var bytes = Zip();
        FfmpegArchive Pin(byte[] content) => new(content.Length, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        FfmpegInstaller Make(byte[] content, FfmpegArchive? expected = null) => new(new Handler((request, _) =>
        {
            check(request.RequestUri == FfmpegInstaller.Source, "FFmpeg取得先は固定HTTPS URL");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }), expected ?? Pin(content));
        Task<ToolState> Stopped() => Task.FromResult(new ToolState("停止中"));
        check(FfmpegInstaller.State(installed) == "未導入", "FFmpeg2ファイルなしを検出");
        using (var installer = Make(bytes))
            check((await installer.Install(installed, Stopped)).Contains("配置が完了"), "検証ZIPから専用配置へ導入");
        check(File.ReadAllText(history) == "保存履歴" && File.Exists(Path.Combine(root, "tools", "ffprobe.exe")), "履歴保持とFFprobeの配置");
        check(File.Exists(Path.Combine(root, "tools", "ffmpeg-distribution", "LICENSE")), "ライセンスを同時配置");
        check(!Directory.GetDirectories(root, ".ffmpeg-install-*").Any(), "導入完了後の作業領域を整理");
        check(FfmpegInstaller.State(installed).StartsWith("配置済み"), "2ファイル配置と録画未確認を区別");
        var original = File.ReadAllBytes(Path.Combine(root, "tools", "ffmpeg.exe"));
        async Task Reject(byte[] content, string name, FfmpegArchive? expected = null, Func<Task<ToolState>>? state = null)
        {
            using var installer = Make(content, expected); bool rejected = false;
            try { await installer.Install(installed, state ?? Stopped); }
            catch (IOException) { rejected = true; }
            check(rejected, name);
            check(File.ReadAllBytes(Path.Combine(root, "tools", "ffmpeg.exe")).SequenceEqual(original), name + "：元ファイル保持");
        }
        await Reject(bytes, "起動中は取得前に拒否", state: () => Task.FromResult(new ToolState("実行中")));
        await Reject(bytes, "未確認状態は停止と扱わない", state: () => Task.FromResult(new ToolState("確認できない")));
        await Reject(bytes, "固定ハッシュ不一致を拒否", Pin(bytes) with { Sha256 = new string('0', 64) });
        await Reject(bytes, "容量不一致を拒否", Pin(bytes) with { Length = bytes.Length + 1 });
        await Reject(Zip(prefix + "../outside.exe"), "範囲外ZIPパスを拒否");
        await Reject(Zip(prefix + "bin/FFMPEG.exe"), "大文字小文字を含む重複ZIPを拒否");
        await Reject(Zip(missing: true), "FFprobe不足を拒否");
        int requests = 0;
        await Reject(bytes, "取得後の起動を再確認して拒否", state: () => Task.FromResult(new ToolState(++requests == 1 ? "停止中" : "実行中")));
        using (var locked = new FileStream(Path.Combine(root, "tools", "ffprobe.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
            await Reject(bytes, "配置途中の失敗は巻戻す");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); using var installer = Make(bytes); bool rejected = false;
            try { await installer.Install(installed, Stopped, cancellation: cancelled.Token); }
            catch (OperationCanceledException) { rejected = true; }
            check(rejected, "開始前キャンセルは配置しない");
        }
        check(File.ReadAllText(history) == "保存履歴" && !Directory.GetDirectories(root, ".ffmpeg-install-*").Any(), "全異常系で履歴と既存配置を保持・作業領域整理");
        check(FfmpegInstaller.State(installed with { State = "未導入" }).Contains("先に導入"), "SSCapture未導入は録画依存を導入できない");
    }
}
