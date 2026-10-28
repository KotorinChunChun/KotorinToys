using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace KotorinToys;

internal sealed class InstallerAdapter(ToolManager manager, Func<string, IReadOnlyList<string>, Task<int>>? execute = null,
    Func<ToolDefinition,InstallationState>? detectInstallation = null, string? diagnosticDirectory = null)
{
    readonly SemaphoreSlim gate = new(1);
    InstallerDiagnostics Diagnostics(ToolDefinition tool, string operation, string? target) => new(
        diagnosticDirectory ?? (execute == null ? Path.Combine(HostSettingsStore.DefaultDirectory, "logs", "installers") :
            Path.Combine(manager.Root, "dev", "history", "v" + HostProduct.CurrentVersion, "logs", "installer-fixtures")), tool.Id, operation, target);
    // 既存の呼び出し元を保持しつつ、配布定義の検証を純粋な読取責務へ委譲する。
    public static PackageCatalog Load(string path, IReadOnlyCollection<ToolDefinition>? definitions = null) => PackageCatalogReader.Load(path, definitions);
    internal static void Validate(PackageCatalog value) => PackageCatalogReader.Validate(value);
    public async Task<InstallResult> Install(ToolDefinition tool, string catalogPath, string? directory = null)
    {
        await gate.WaitAsync();
        InstallerDiagnostics? diagnostics = null;
        int? executionCode = null;
        InstallResult Done(InstallResult result) => diagnostics?.Complete(result) ?? result with { Detail = result.Detail + " 終了コード：未取得。" };
        try
        {
            if (tool.Msix != null) return await MsixInstaller.Create(manager, tool).Install(tool, catalogPath);
            diagnostics = Diagnostics(tool, "install", directory ?? manager.Installation(tool).Directory);
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
                "/NORESTARTAPPLICATIONS",
                "/LOG=" + diagnostics.NativeLog
            };
            if (directory != null)
                arguments.Add("/DIR=" + Path.GetFullPath(directory));
            var code = await (execute ?? Run)(file, arguments);
            executionCode = code;
            var result = Classify(tool.Id, "install", code);
            if (result.State == "成功")
            {
                var actual = (detectInstallation ?? manager.Installation)(tool);
                if (actual.State != "導入済み" || actual.Version != package.Version)
                    return Done(result with
                    {
                        State = "要確認",
                        Detail = "導入後の確認が必要です。" +
                            (actual.State == "導入済み" ? "実EXEの版が対象版と一致しません。" :
                                string.IsNullOrWhiteSpace(actual.Detail) ? "正式導入を確認できません（" + actual.State + "）。" : actual.Detail) +
                            $" 対象版：{package.Version} / 検出版：{actual.Version ?? "不明"}。"
                    });
            }

            return Done(result.State == "成功" ? result with { Detail = $"導入済み（{package.Version}）。正式登録・製品識別・実行ファイルの版を確認しました。" } : result);
        }
        catch (Exception ex)
        {
            return Done(new(tool.Id, "install", "失敗", ex.Message, executionCode));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<InstallResult> Uninstall(ToolDefinition tool)
    {
        await gate.WaitAsync();
        InstallerDiagnostics? diagnostics = null;
        int? executionCode = null;
        InstallResult Done(InstallResult result) => diagnostics?.Complete(result) ?? result with { Detail = result.Detail + " 終了コード：未取得。" };
        try
        {
            if (tool.Msix != null) return await MsixInstaller.Create(manager, tool).Uninstall(tool);
            var installed = manager.Installation(tool);
            diagnostics = Diagnostics(tool, "uninstall", installed.Directory);
            if (installed.State != "導入済み" || installed.Uninstaller == null || !File.Exists(installed.Uninstaller))
                throw new IOException("正式導入と削除プログラムを確認できません。");
            var stopped = await manager.Stop(tool);
            if (stopped.State != "停止中")
                throw new IOException("正常停止が未確認のため削除しません：" + stopped.Detail);
            var code = await (execute ?? Run)(installed.Uninstaller, new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=" + diagnostics.NativeLog });
            executionCode = code;
            var result = Classify(tool.Id, "uninstall", code);
            if (result.State == "成功" && manager.Installation(tool).State != "未導入")
                return Done(result with
                {
                    State = "要確認",
                    Detail = "削除処理は終了しましたが正式登録が残っています。"
                });
            if (result.State == "失敗" && File.Exists(installed.Executable))
            {
                var recovery = new RemovalReservations(new(HostSettingsStore.DefaultDirectory), new TaskSchedulerGateway()).Recover(true, tool.Id);
                return Done(result with
                {
                    Detail = result.Detail + " / " + recovery.Detail
                });
            }

            return Done(result.State == "成功" ? result with { Detail = "削除済み。正式登録がないことを確認しました。設定・履歴は保持します。" } : result);
        }
        catch (Exception ex)
        {
            return Done(new(tool.Id, "uninstall", "失敗", ex.Message, executionCode));
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
    }, operation == "uninstall" && code is not (0 or 3010) ? "削除プログラムが異常終了しました。詳細ログを確認してください。" : code switch
    {
        0 => "正式登録を再確認します。",
        3010 => "自動再起動はしません。",
        1 => "インストーラーを初期化できませんでした。",
        2 => "インストール開始前にキャンセルされました。",
        3 => "インストールの段階切替時に重大なエラーが発生しました。",
        4 => "インストール処理中に重大なエラーが発生しました。",
        6 => "インストーラーがデバッガーから強制終了されました。",
        5 => "処理中にキャンセルされました。",
        7 => "導入準備で中止されました。導入先・正常終了・保守補助のログを確認してください。",
        8 => "準備を完了できず、再起動が必要と報告されました。自動再起動はしません。",
        _ => "インストーラーが異常終了しました。"
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
