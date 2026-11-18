using System.Diagnostics;

namespace KotorinToys;

internal sealed record ToolUpdateRequest(string Root, ToolDefinition[] Tools, ReleaseCatalog Catalog);
internal sealed record ToolUpdateReport(string Id, bool Finished, InstallResult[] Results);

internal static class ToolUpdateJob
{
    const int MaxReportBytes = 2 * 1024 * 1024;
    static string DirectoryFor(string settings) => PathSafety.Resolve(settings, "tool-updates");
    static FileStream Lease(string directory) => new(PathSafety.Resolve(directory, "updates.lock"),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    internal static bool IsRunning(string settings)
    {
        var directory = DirectoryFor(settings);
        if (!File.Exists(PathSafety.Resolve(directory, "updates.lock"))) return false;
        try { using var lease = Lease(directory); return false; }
        catch (IOException) { return true; }
    }

    internal static ToolUpdateReport? Latest(string settings)
    {
        try
        {
            var directory = DirectoryFor(settings);
            var pointer = PathSafety.Resolve(directory, "latest.json");
            if (!File.Exists(pointer)) return null;
            var id = SelfUpdateFiles.Read<string>(pointer);
            if (!Guid.TryParseExact(id, "N", out _)) return null;
            return SelfUpdateFiles.Read<ToolUpdateReport>(PathSafety.Resolve(directory, id + "/result.json"), MaxReportBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { return null; }
    }

    internal static async Task<InstallResult[]> Start(string root, string settings, IReadOnlyList<ToolDefinition> tools,
        ReleaseCatalog catalog, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var directory = DirectoryFor(settings);
        var job = PathSafety.Resolve(directory, Guid.NewGuid().ToString("N"));
        var id = Path.GetFileName(job);
        var request = PathSafety.Resolve(job, "request.json");
        var definitions = tools.Select(tool => tool with { ToolRoot = tool.ToolRoot.Replace('\\', '/') }).ToArray();
        SelfUpdateFiles.Write(request, new ToolUpdateRequest(root, definitions, catalog));
        var executable = Environment.ProcessPath ?? throw new IOException("更新プロセスの実行ファイルが見つかりません。");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add("--tool-update-job");
        start.ArgumentList.Add(settings);
        start.ArgumentList.Add(id);
        using var worker = Process.Start(start) ?? throw new IOException("バックグラウンド更新を開始できません。");
        await worker.WaitForExitAsync(cancellation);
        return SelfUpdateFiles.Read<ToolUpdateReport>(PathSafety.Resolve(job, "result.json"), MaxReportBytes).Results;
    }

    internal static async Task<int> Run(string settings, string id)
    {
        if (!Path.IsPathFullyQualified(settings) || !Guid.TryParseExact(id, "N", out _)) return 1;
        var directory = DirectoryFor(settings);
        var job = PathSafety.Resolve(directory, id);
        var resultPath = PathSafety.Resolve(job, "result.json");
        FileStream claim;
        try { claim = new(PathSafety.Resolve(job, "claimed"), FileMode.CreateNew, FileAccess.Write); }
        catch (IOException) { return 1; }
        using var ownedClaim = claim;
        ToolUpdateRequest? request = null;
        InstallResult[] results = [];
        FileStream? lease = null;
        try
        {
            request = SelfUpdateFiles.Read<ToolUpdateRequest>(PathSafety.Resolve(job, "request.json"), MaxReportBytes);
            // 同じ操作の再実行や、このユーザーの並列更新を受け付けない。
            lease = Lease(directory);
            if (!Path.IsPathFullyQualified(request.Root)) throw new IOException("アプリ一覧の参照先が不正です。");
            OnlineToolCatalog.Validate(request.Tools, request.Root, OnlineToolCatalog.MaximumTools);
            ReleaseDownloader.Validate(request.Catalog, request.Tools);
            if (request.Tools.Length != request.Catalog.Packages.Length ||
                request.Tools.Any(tool => ToolInstallationRunner.PublicReason(tool, request.Catalog) != null))
                throw new IOException("更新対象と公開配布情報が一致しません。");

            SelfUpdateFiles.Write(resultPath, new ToolUpdateReport(id, false, []));
            SelfUpdateFiles.Write(PathSafety.Resolve(directory, "latest.json"), id);
            var manager = new ToolManager(request.Root, fixedTools: request.Tools);
            results = await new ToolInstallationRunner(manager).Run(request.Tools, "update", request.Catalog, null);
        }
        catch (Exception ex)
        {
            results = request?.Tools?.Select(t => new InstallResult(t.Id, "update", "失敗", ex.Message)).ToArray()
                ?? [new("", "update", "失敗", ex.Message)];
        }
        try { SelfUpdateFiles.Write(resultPath, new ToolUpdateReport(id, true, results)); }
        finally { lease?.Dispose(); }
        return results.All(result => result.State is "成功" or "スキップ") ? 0 : 1;
    }
}
