namespace KotorinToys;

// 対象ごとに取得と実行を完結させ、未対応製品や1件の失敗を他製品へ波及させない。
internal sealed class ToolInstallationRunner(ToolManager manager,
    Func<ReleaseCatalog, ToolDefinition, IProgress<string>?, CancellationToken, Task<string>>? prepare = null,
    Func<ToolDefinition, string, string?, CancellationToken, Task<InstallResult>>? execute = null)
{
    internal static string? UnsupportedReason(ToolDefinition tool)
    {
        var product = ProductIdentity.FromId(tool.Id);
        if (tool.Id == "host" || product == null || product.Name != tool.Name || product.AppId != tool.AppId || tool.Msix != null)
            return "本体の導入・削除管理に未対応です。「GitHubを開く」または「使い方」で配布方法を確認してください。";
        return null;
    }

    internal static string? PublicReason(ToolDefinition tool, ReleaseCatalog? catalog)
    {
        if (UnsupportedReason(tool) is { } reason) return reason;
        if (tool.DistributionStatus != "published")
            return "本体対応インストーラーは未公開です。ポータブル版などの利用方法は「GitHubを開く」または「使い方」を確認してください。";
        var package = catalog?.Packages.SingleOrDefault(p => p.Package.Id == tool.Id)?.Package;
        if (package == null) return "照合済みの公開インストーラーがありません。「状態を更新」で再確認してください。";
        if (package.Name != tool.Name || package.AppId != tool.AppId)
            return "公開配布情報と製品識別が一致しません。実行しません。";
        return null;
    }

    public async Task<InstallResult[]> Run(IReadOnlyList<ToolDefinition> targets, string operation,
        ReleaseCatalog? catalog, string? localCatalog, IProgress<InstallResult>? progress = null,
        IProgress<string>? downloadProgress = null, CancellationToken cancellation = default)
    {
        if (operation is not ("install" or "uninstall")) throw new IOException("未対応の導入操作です。");
        var results = new List<InstallResult>();
        foreach (var tool in targets.DistinctBy(t => t.Id))
        {
            InstallResult result;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (UnsupportedReason(tool) is { } unsupported) throw new IOException(unsupported);
                string? path = localCatalog;
                if (operation == "install" && path == null)
                {
                    if (PublicReason(tool, catalog) is { } reason) throw new IOException(reason);
                    ReleaseDownloader.Validate(catalog!);
                    var item = catalog!.Packages.Single(p => p.Package.Id == tool.Id);
                    progress?.Report(new(tool.Id, operation, "処理中", "公開配布物を取得・検証しています。"));
                    path = await (prepare ?? Prepare)(new(1, [item]), tool with { Version = item.Package.Version }, downloadProgress, cancellation);
                }
                cancellation.ThrowIfCancellationRequested();
                progress?.Report(new(tool.Id, operation, "処理中", "実行中です"));
                result = await (execute ?? Execute)(tool, operation, path, cancellation);
            }
            catch (OperationCanceledException)
            {
                result = new(tool.Id, operation, "未実行", cancellation.IsCancellationRequested
                    ? "残りの操作を取り消しました。進行中の処理は強制終了していません。"
                    : "取得が時間上限に達しました。インストーラーは実行していません。");
            }
            catch (Exception ex) { result = new(tool.Id, operation, "失敗", ex.Message); }
            results.Add(result); progress?.Report(result);
        }
        return results.ToArray();
    }

    async Task<string> Prepare(ReleaseCatalog catalog, ToolDefinition tool, IProgress<string>? progress, CancellationToken cancellation)
    {
        using var downloader = new ReleaseDownloader();
        return await downloader.Prepare(catalog, [tool],
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KotorinToys", "packages"), progress, cancellation);
    }

    async Task<InstallResult> Execute(ToolDefinition tool, string operation, string? path, CancellationToken cancellation)
    {
        var adapter = new InstallerAdapter(manager);
        return (await new InstallationQueue(adapter, new ToolUpdateCoordinator(manager, adapter))
            .Run([tool], operation, path, cancellation: cancellation)).Single();
    }
}
