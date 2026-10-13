using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace KotorinToys;
internal sealed class InstallationQueue(InstallerAdapter adapter)
{
    public async Task<InstallResult[]> Run(IReadOnlyList<ToolDefinition> targets, string operation, string? catalogPath, IProgress<InstallResult>? progress = null, CancellationToken cancellation = default)
    {
        if (operation is not ("install" or "uninstall"))
            throw new IOException("未対応の導入操作です。");
        if (targets.Any(t => t.Id == "host"))
            throw new IOException("本体はツール一括操作に含めません。");
        var results = new List<InstallResult>();
        foreach (var tool in targets.DistinctBy(t => t.Id))
        {
            if (tool.Id == "host")
                throw new IOException("本体はツール一括操作に含めません。");
            InstallResult result;
            if (cancellation.IsCancellationRequested)
                result = new(tool.Id, operation, "未実行", "残りの操作を取り消しました。進行中の処理は強制終了していません。");
            else
            {
                progress?.Report(new(tool.Id, operation, "処理中", "一つずつ実行しています。"));
                result = operation == "install" ? await adapter.Install(tool, catalogPath ?? throw new IOException("配布台帳が未指定です。")) : await adapter.Uninstall(tool);
            }

            results.Add(result);
            progress?.Report(result);
        }

        return results.ToArray();
    }
}
