using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KotorinToys;

// 利用者PC内だけに保持する。子の設定・履歴を収集せず、今回の対象・時刻の診断だけを読む。
internal sealed class InstallerDiagnostics
{
    readonly string id, operation;
    readonly string? target;
    readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    readonly string maintenanceRoot;
    public string DirectoryPath { get; }
    public string NativeLog => Path.Combine(DirectoryPath, "installer.log");
    public InstallerDiagnostics(string logRoot, string id, string operation, string? target, string? maintenanceRoot = null)
    {
        this.id = id; this.operation = operation;
        this.target = target == null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        this.maintenanceRoot = maintenanceRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSCaptureMaintenance");
        DirectoryPath = Path.Combine(logRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, "operation.txt"), $"製品：{id}\n操作：{operation}\n開始UTC：{started:O}\n導入先：{this.target}\n");
    }
    internal static string Tail(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int first = stream.ReadByte(), second = stream.ReadByte();
        bool unicode = first == 255 && second == 254;
        long offset = Math.Max(0, stream.Length - 65536);
        if (unicode) offset -= offset % 2;
        stream.Position = offset;
        using var reader = new StreamReader(stream, unicode ? Encoding.Unicode : Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    string Reason()
    {
        if (id == "ss-capture" && target != null)
        {
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.ToUpperInvariant())));
            var folder = Path.Combine(maintenanceRoot, key);
            var messages = new List<string>();
            foreach (var name in operation == "install" ? new[] { "update", "observe-update", "commit-update", "recover-update" } : new[] { "remove", "commit-remove", "recover-remove" })
            {
                string path = Path.Combine(folder, name + "-error.json");
                if (!File.Exists(path) || new FileInfo(path).Length > 65536) continue;
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                var value = json.RootElement;
                if (value.GetProperty("utc").GetDateTimeOffset() < started || value.GetProperty("operation").GetString() != name ||
                    !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.GetProperty("root").GetString()!)), target, StringComparison.OrdinalIgnoreCase)) continue;
                string message = value.GetProperty("message").GetString() ?? "";
                if (message.Length > 1000) message = message[..1000];
                messages.Add(name + "：" + message);
            }
            if (messages.Count > 0) return "保守補助：" + string.Join(" / ", messages);
        }
        if (!File.Exists(NativeLog)) return "詳細原因の記録はありません。保存したログを配布元へ確認してください。";
        var lines = Tail(NativeLog).Split('\n').Where(line =>
            new[] { "Error:", "Exception", "failed", "エラー", "失敗", "中止" }.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .TakeLast(3).Select(line => line.Trim()).Select(line => line.Length > 400 ? line[..400] : line);
        var excerpt = string.Join(" / ", lines);
        return excerpt.Length == 0 ? "詳細原因はインストーラーログを確認してください。" : "ログ抜粋：" + excerpt;
    }
    public InstallResult Complete(InstallResult result)
    {
        string detail = result.Detail;
        string code = result.ExitCode?.ToString() ?? "未取得（実行開始前または実行待機中の失敗）";
        if (result.State != "成功")
        {
            detail += " 終了コード：" + code + "。";
            if (result.ExitCode is not (null or 0 or 3010))
            {
                try { detail += " " + Reason(); }
                catch (Exception ex) { detail += " 診断ログの読取に失敗しました：" + ex.Message; }
            }
        }
        try
        {
            File.AppendAllText(Path.Combine(DirectoryPath, "operation.txt"), $"終了UTC：{DateTimeOffset.UtcNow:O}\n結果：{result.State}\n終了コード：{code}\n詳細：{detail}\n");
        }
        catch (Exception ex) { detail += " 結果ログを保存できません：" + ex.Message; }
        return result with { Detail = detail + " ログ：" + DirectoryPath };
    }
}
