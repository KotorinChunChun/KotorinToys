using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace KotorinToys;

internal sealed class InstallerAdapter(ToolManager manager, Func<string, IReadOnlyList<string>, Task<int>>? execute = null)
{
    readonly SemaphoreSlim gate = new(1);
    // 既存の呼び出し元を保持しつつ、配布定義の検証を純粋な読取責務へ委譲する。
    public static PackageCatalog Load(string path, IReadOnlyCollection<ToolDefinition>? definitions = null) => PackageCatalogReader.Load(path, definitions);
    internal static void Validate(PackageCatalog value) => PackageCatalogReader.Validate(value);
    public async Task<InstallResult> Install(ToolDefinition tool, string catalogPath, string? directory = null)
    {
        await gate.WaitAsync();
        try
        {
            if (tool.Msix != null) return await MsixInstaller.Create(manager).Install(tool, catalogPath);
            var package = Load(catalogPath, manager.KnownTools()).Packages.SingleOrDefault(p => p.Id == tool.Id) ?? throw new IOException("対象のローカル配布物がありません。");
            if (package.Kind != "inno") throw new IOException("EXEとMSIXの配布方式が一致しません。");
            if (package.AppId != tool.AppId || package.Name != tool.Name)
                throw new IOException("登録情報と配布物が一致しません。");
            var file = PathSafety.Resolve(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!, package.File);
            // 書換・削除を許可しないハンドルを実行終了まで保持する。
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            if (hash != package.Sha256)
                throw new IOException("配布物のSHA-256が一致しません。実行しません。");
            var arguments = new List<string>
            {
                "/VERYSILENT",
                "/SUPPRESSMSGBOXES",
                "/SP-",
                "/NORESTART",
                "/RESTARTEXITCODE=3010",
                "/NOCLOSEAPPLICATIONS",
                "/NOFORCECLOSEAPPLICATIONS",
                "/NORESTARTAPPLICATIONS"
            };
            if (directory != null)
                arguments.Add("/DIR=" + Path.GetFullPath(directory));
            var code = await (execute ?? Run)(file, arguments);
            var result = Classify(tool.Id, "install", code);
            if (result.State == "成功")
            {
                var actual = manager.Installation(tool);
                if (actual.State != "導入済み" || actual.Version != package.Version)
                    return result with
                    {
                        State = "要確認",
                        Detail = "導入処理は終了しましたが、登録・製品識別・実EXEの版を照合できません。"
                    };
            }

            return result.State == "成功" ? result with { Detail = $"導入済み（{package.Version}）。正式登録・製品識別・実行ファイルの版を確認しました。" } : result;
        }
        catch (Exception ex)
        {
            return new(tool.Id, "install", "失敗", ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<InstallResult> Uninstall(ToolDefinition tool)
    {
        await gate.WaitAsync();
        try
        {
            if (tool.Msix != null) return await MsixInstaller.Create(manager).Uninstall(tool);
            var installed = manager.Installation(tool);
            if (installed.State != "導入済み" || installed.Uninstaller == null || !File.Exists(installed.Uninstaller))
                throw new IOException("正式導入と削除プログラムを確認できません。");
            var stopped = await manager.Stop(tool);
            if (stopped.State != "停止中")
                throw new IOException("正常停止が未確認のため削除しません：" + stopped.Detail);
            var code = await (execute ?? Run)(installed.Uninstaller, new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" });
            var result = Classify(tool.Id, "uninstall", code);
            if (result.State == "成功" && manager.Installation(tool).State != "未導入")
                return result with
                {
                    State = "要確認",
                    Detail = "削除処理は終了しましたが正式登録が残っています。"
                };
            if (result.State == "失敗" && File.Exists(installed.Executable))
            {
                var recovery = new RemovalReservations(new(HostSettingsStore.DefaultDirectory), new TaskSchedulerGateway()).Recover(true, tool.Id);
                return result with
                {
                    Detail = result.Detail + " / " + recovery.Detail
                };
            }

            return result.State == "成功" ? result with { Detail = "削除済み。正式登録がないことを確認しました。設定・履歴は保持します。" } : result;
        }
        catch (Exception ex)
        {
            return new(tool.Id, "uninstall", "失敗", ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    internal static InstallResult Classify(string id, string operation, int code) => new(id, operation, code switch
    {
        0 => "成功",
        3010 => "再起動待ち",
        _ => "失敗"
    }, code switch
    {
        0 => "正式登録を再確認します。",
        3010 => "自動再起動はしません。",
        _ => "インストーラーが失敗しました。"
    }, code);
    static async Task<int> Run(string path, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path)
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("インストーラーを起動できません。");
        // 進行中のインストーラーは強制中断しない。
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
