using System.Diagnostics;
using System.Security.Cryptography;

namespace KotorinToys;

internal interface IHostSelfUpdateService
{
    string CurrentVersion { get; }
    string? BlockReason();
    Task<HostUpdateChannel> DetectChannel(CancellationToken cancellation);
    Task<PublicRelease> Check(HostUpdateChannel channel, CancellationToken cancellation);
    Task Handoff(ReleasePackage release, HostUpdateChannel from, HostUpdateChannel to, bool switchConfirmed, IProgress<string> progress, CancellationToken cancellation);
    SelfUpdateResult? LastResult();
}

internal sealed class HostSelfUpdateService(string root, string settingsDirectory) : IHostSelfUpdateService
{
    readonly SemaphoreSlim gate = new(1);
    public string CurrentVersion => HostProduct.CurrentVersion;
    public string? BlockReason()
    {
        try
        {
            var actual = new InstallationDetector().Detect(HostProduct.Definition);
            return HostSelfUpdateEligibility.Explain(actual, root, Environment.ProcessPath, CurrentVersion, File.Exists);
        }
        catch (Exception ex) { return "正式導入を照合できません：" + ex.Message; }
    }

    public async Task<HostUpdateChannel> DetectChannel(CancellationToken cancellation)
    {
        using var client = new PublicReleaseClient();
        return await client.DetectHostChannel(CurrentVersion, cancellation);
    }

    public async Task<PublicRelease> Check(HostUpdateChannel channel, CancellationToken cancellation)
    {
        using var client = new PublicReleaseClient();
        return await client.CheckHostChannel(channel, cancellation);
    }

    public async Task Handoff(ReleasePackage release, HostUpdateChannel from, HostUpdateChannel to, bool switchConfirmed, IProgress<string> progress, CancellationToken cancellation)
    {
        if (!await gate.WaitAsync(0, cancellation)) throw new IOException("本体の更新処理が既に実行中です。");
        try
        {
            if (BlockReason() is { } blocked) throw new IOException(blocked);
            if (to is not (HostUpdateChannel.Stable or HostUpdateChannel.Preview) ||
                from is not (HostUpdateChannel.Stable or HostUpdateChannel.Preview or HostUpdateChannel.Unknown) ||
                (from != to) != switchConfirmed) throw new IOException("更新対象の種類または切替確認が不正です。");
            var transition = switchConfirmed ? new HostChannelTransition(from, to, "backup/manifest.json") : null;
            if (!HostProduct.CanInstall(release, CurrentVersion, transition)) throw new IOException("新しい公開版ではありません。");
            progress.Report("公開配布情報を再照合しています…");
            var actualChannel = await DetectChannel(cancellation);
            if (actualChannel != from) throw new IOException("現在の版の種類が変わりました。公開版を再確認してください。");
            var latest = await Check(to, cancellation);
            if (!latest.Confirmed || latest.Package != release || latest.Prerelease != (to == HostUpdateChannel.Preview))
                throw new IOException("公開配布情報が変わったか確認できません。もう一度公開版を確認してください。");
            var id = Guid.NewGuid().ToString("N");
            var job = PathSafety.Resolve(settingsDirectory, "updates/" + id);
            using var current = Process.GetCurrentProcess();
            var provisional = new SelfUpdatePlan(1, id, Path.GetFullPath(root), Path.GetFullPath(settingsDirectory),
                current.Id, current.StartTime.ToUniversalTime().Ticks, CurrentVersion, release, "download-" + new string('0', 32) + "/packages.json", transition);
            SelfUpdateFiles.Validate(provisional, job);
            Directory.CreateDirectory(job);
            using var downloader = new ReleaseDownloader();
            var catalog = await downloader.PrepareHost(release, CurrentVersion, job, progress, cancellation, transition);
            cancellation.ThrowIfCancellationRequested();
            if (transition != null)
            {
                progress.Report("設定・履歴を退避しています…");
                await SelfUpdateBackup.Create(settingsDirectory, job, cancellation);
            }
            progress.Report("本体の外に更新プロセスを準備しています…");
            await CopyWorker(root, job, cancellation);
            var plan = provisional with { Catalog = Path.GetRelativePath(job, catalog).Replace('\\', '/') };
            SelfUpdateFiles.Validate(plan, job);
            var request = PathSafety.Resolve(job, "request.json");
            SelfUpdateFiles.Write(request, plan);
            using var lockedPlan = new FileStream(request, FileMode.Open, FileAccess.Read, FileShare.Read);
            var worker = PathSafety.Resolve(job, "worker/KotorinToys.Maintenance.exe");
            var start = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(worker) };
            start.ArgumentList.Add("--self-update"); start.ArgumentList.Add(request);
            using var launched = Process.Start(start) ?? throw new IOException("更新プロセスを開始できません。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            var readyFile = PathSafety.Resolve(job, "ready.json");
            try
            {
                while (!File.Exists(readyFile))
                {
                    if (launched.HasExited) throw new IOException("更新プロセスが準備完了前に終了しました。更新記録：" + job);
                    await Task.Delay(100, deadline.Token);
                }
                var ready = SelfUpdateFiles.Read<SelfUpdateReady>(readyFile);
                if (ready.Id != id || ready.WorkerPid != launched.Id || !SelfUpdateFiles.SamePath(launched.MainModule?.FileName, worker))
                    throw new IOException("更新プロセスの準備完了応答が一致しません。");
                cancellation.ThrowIfCancellationRequested();
                if (BlockReason() is { } changed) throw new IOException(changed);
                progress.Report("準備完了。本体だけを正常終了して更新し、再起動します。子ツールは停止しません。");
                // このファイル確定後は更新プロセスが責任を持つ。本体終了前の失敗・取消では確定しない。
                SelfUpdateFiles.Write(PathSafety.Resolve(job, "continue.json"), ready);
            }
            catch
            {
                SelfUpdateFiles.Write(PathSafety.Resolve(job, "cancel.json"), new SelfUpdateReady(id, launched.Id));
                throw;
            }
        }
        finally { gate.Release(); }
    }

    internal static async Task CopyWorker(string root, string job, CancellationToken cancellation)
    {
        var source = PathSafety.Resolve(root, "maintenance");
        var target = PathSafety.Resolve(job, "worker");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellation.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var input = PathSafety.Resolve(source, relative);
            var output = PathSafety.Resolve(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var original = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
            using (var copy = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await original.CopyToAsync(copy, cancellation); copy.Flush(true); }
            original.Position = 0;
            using var copied = File.OpenRead(output);
            var originalHash = await SHA256.HashDataAsync(original, cancellation);
            var copiedHash = await SHA256.HashDataAsync(copied, cancellation);
            if (!originalHash.SequenceEqual(copiedHash))
                throw new IOException("更新プロセスのコピーを照合できません。");
        }
    }

    public SelfUpdateResult? LastResult()
    {
        try
        {
            var path = PathSafety.Resolve(settingsDirectory, "updates/latest.json");
            if (!File.Exists(path)) return null;
            var result = SelfUpdateFiles.Read<SelfUpdateResult>(path);
            return result.SchemaVersion == 1 && Guid.TryParseExact(result.Id, "N", out _) && SelfUpdateFiles.SamePath(result.Root, root) &&
                result.State is "成功" or "失敗" or "要確認" or "再起動待ち" or "取消" ? result : null;
        }
        catch (Exception) { return null; }
    }
}
