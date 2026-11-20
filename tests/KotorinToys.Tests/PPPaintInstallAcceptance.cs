using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using KotorinToys;

internal static class PPPaintInstallAcceptance
{
    public static async Task<int> Run(string root, string catalogPath, string output, string? previousInstaller)
    {
        output = Path.GetFullPath(output);
        var logsRoot = Path.Combine(root, "dev", "history", "v" + HostProduct.CurrentVersion, "logs");
        Require(output.StartsWith(logsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !Directory.Exists(output), "新規の版別ログフォルダーを指定してください。");
        Directory.CreateDirectory(output);
        var source = new ToolCatalogSource(root, Path.Combine(output, "catalog"));
        if (catalogPath == "--online")
            Require((await source.Refresh()).Accepted, "公開カタログを取得できませんでした。");
        var tool = source.Load().Single(tool => tool.Id == "pppaint");
        var manager = new ToolManager(root, catalogSource: source);
        var adapter = new InstallerAdapter(manager, diagnosticDirectory: output);
        var target = Path.Combine(output, "installed");
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PPPaint.lnk");
        Require(manager.Installation(tool).State == "未導入" && Process.GetProcessesByName("PPPaint").Length == 0 &&
            !File.Exists(shortcut), "既存のPPPaint導入・ショートカット・プロセスがあるため隔離試験を実行できません。");

        if (catalogPath == "--online")
        {
            using var client = new PublicReleaseClient(cache: new PublicReleaseCache());
            var bundled = ReleaseDownloader.Load(root, source.KnownTools()).Packages.Single(package => package.Package.Id == tool.Id);
            var release = await client.Check(tool, bundled);
            Require(release is { Confirmed: true, Package: not null }, release.Detail);
            using var downloader = new ReleaseDownloader();
            catalogPath = await downloader.Prepare(new(1, [release.Package!]), [tool], Path.Combine(output, "downloads"));
            Console.WriteLine("公開カタログ / Release / 配布台帳 / 実ダウンロードを照合");
        }

        var settings = Path.Combine(output, "settings");
        Directory.CreateDirectory(settings);
        var sentinel = Path.Combine(settings, "preserved.txt");
        await File.WriteAllTextAsync(sentinel, "settings preserved");
        var isolation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings.ToLowerInvariant())))
            .ToLowerInvariant()[..16];
        var isolatedTool = tool with
        {
            Id = tool.Id + "-" + isolation, AppId = "", Executable = "PPPaint.exe", ToolRoot = ""
        };
        var isolatedManager = new ToolManager(target, (_, process) =>
        {
            process.ArgumentList.Add("--settings-dir");
            process.ArgumentList.Add(settings);
        });
        try
        {
            InstallResult install;
            if (previousInstaller == null)
                install = await adapter.Install(tool, catalogPath, target);
            else
            {
                await InstallPrevious(previousInstaller, target);
                Require(manager.Installation(tool) is { State: "導入済み", Version: "0.2.35" },
                    "製品定義がない公式旧版の検出に失敗しました。");
                install = await new ToolUpdateCoordinator(manager, adapter).Run(tool, catalogPath, updateOnly: true);
                Console.WriteLine("単体版0.2.35の正式登録を認識し、既存の導入先へ更新");
            }
            Require(install.State == "成功", install.Detail);
            var registration = manager.Installation(tool);
            Require(registration is { State: "導入済み", Version: "0.2.36", Uninstaller: not null } &&
                registration.Directory == target, "正式導入の登録、実行ファイルの版、導入先を照合できません。");
            var signer = PinnedAuthenticodeSignature.Verify(registration.Uninstaller!);
            Require(signer.Subject == PppaintDistribution.Publisher && signer.Thumbprint == PppaintDistribution.Thumbprint &&
                signer.HasTimestamp, "削除プログラムの署名を照合できません。");
            Console.WriteLine("導入済み 0.2.36 / 正式登録・対象版・削除プログラムの署名を照合");

            var running = await isolatedManager.Start(isolatedTool);
            Require(running is { State: "実行中", ProtocolVersion: 2, LaunchOrigin: "host_manual", Pid: not null },
                "起動または管理窓口の確認に失敗しました：" + running.Detail);
            using var process = Process.GetProcessById(running.Pid!.Value);
            process.Refresh();
            Require(process.MainWindowHandle != IntPtr.Zero, "手動起動で編集ウィンドウが開きませんでした。");
            Require((await isolatedManager.Stop(isolatedTool)).State == "停止中", "正常停止を確認できませんでした。");
            Console.WriteLine("実ウィンドウ / 管理プロトコル2 / 正常停止");
        }
        finally
        {
            await isolatedManager.Stop(isolatedTool);
            if (manager.Installation(tool) is { Directory: var installed } && installed == target)
            {
                var remove = await adapter.Uninstall(tool);
                Require(remove.State == "成功", remove.Detail);
            }
        }
        Require(manager.Installation(tool).State == "未導入" && !File.Exists(shortcut) &&
            await File.ReadAllTextAsync(sentinel) == "settings preserved",
            "登録・ショートカットの削除または設定の保持を確認できませんでした。");
        Console.WriteLine("一時登録とショートカットの削除 / 設定保持を確認");
        return 0;
    }

    static async Task InstallPrevious(string installer, string target)
    {
        using var stream = File.OpenRead(installer);
        const string expected = "95B708CC6FC920D2C11522E978CE5364CDA2D092BA0F7CE40A1D4019A3FADF83";
        Require(Convert.ToHexString(await SHA256.HashDataAsync(stream)) == expected, "公式旧版とハッシュが一致しません。");
        var start = new ProcessStartInfo(Path.GetFullPath(installer)) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/SP-", "/NORESTART", "/DIR=" + target })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("公式旧版の導入を開始できません。");
        await process.WaitForExitAsync();
        Require(process.ExitCode == 0, "公式旧版の一時導入に失敗しました。");
    }

    static void Require(bool condition, string reason)
    {
        if (!condition) throw new IOException(reason);
    }
}
