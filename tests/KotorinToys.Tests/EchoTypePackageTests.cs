using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using KotorinToys;

internal static class EchoTypePackageTests
{
    public static async Task Run(string root, string fixtures, string catalogPath, string payloadPath, Action<bool, string> check)
    {
        var manager = new ToolManager(root);
        var tool = manager.Load().Single(candidate => candidate.Id == "echo-type");
        var catalog = InstallerAdapter.Load(catalogPath, manager.KnownTools());
        var package = catalog.Packages.Single();
        check(catalog.DistributionStatus == "unpublished" && package.Id == tool.Id,
            "EchoTypeの未公開ローカル台帳を正規の製品定義で照合");
        check(ToolInstallationRunner.PublicReason(tool, null) != null,
            "ローカル台帳があっても通常の配布経路は未公開");

        var payload = Path.GetFullPath(payloadPath);
        var declaration = ManagedProductDeclaration.Read(tool.Name, payload);
        var detector = new InstallationDetector(_ => new(payload));
        var installed = detector.Detect(tool);
        check(declaration.Id == tool.Id && installed.State == "導入済み" && installed.Version == package.Version,
            "配布候補の製品宣言・実行ファイル名・製品名・版を照合");
        var required = new[] { "CaptionDll/CaptionDllHost.exe", "Captions/EchoType.Captions.exe",
            "WindowsSpeech/EchoType.WindowsSpeech.exe", "setup-asr.ps1", "setup-haas.ps1",
            "Asr/worker.py", "Asr/requirements.txt", "Asr/requirements-qwen.txt", "Asr/requirements-fast.txt",
            "Asr/requirements-cuda.txt", "Asr/silero_vad_v6.onnx", "Asr/whisper_setup.py", "Asr/whisper-models.json",
            "msvcp140.dll", "msvcp140_1.dll", "vcruntime140.dll", "vcruntime140_1.dll" };
        check(required.All(file => File.Exists(Path.Combine(payload, file))),
            "配布候補は認識の補助実行ファイル・導入手順・依存定義・x64 CRTを収録");

        var destination = Path.Combine(fixtures, "echo-type-install");
        var dispatched = 0;
        var adapter = new InstallerAdapter(manager, (file, arguments) =>
        {
            dispatched++;
            check(Path.GetFileName(file) == package.File && arguments.Contains("/DIR=" + destination) &&
                arguments.Contains("/NORESTART") && arguments.Contains("/NOFORCECLOSEAPPLICATIONS"),
                "実インストーラーのSHA-256検証を通り、隔離先と停止・再起動保護を指定");
            return Task.FromResult(0);
        }, detector.Detect, Path.Combine(fixtures, "diagnostics"));
        var runner = new ToolInstallationRunner(manager,
            execute: (target, operation, catalog, _) => adapter.Install(target, catalog!, destination));
        var result = (await runner.Run([tool], "install", null, catalogPath)).Single();
        check(result.State == "成功" && dispatched == 1,
            "KotorinToysのローカル導入経路を実行直前の差し替えで確認");

        var profile = Path.Combine(fixtures, "echo-type-managed-profile");
        var profileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.ToUpperInvariant())))[..24];
        var isolated = tool with { Id = tool.Id + "." + profileHash, AppId = "", ToolRoot = "",
            Executable = tool.InstalledExecutable };
        var launcher = new ToolManager(payload, (_, start) =>
        {
            start.ArgumentList.Add("--smoke-management");
            start.ArgumentList.Add("--data-root");
            start.ArgumentList.Add(profile);
        });
        ToolState? running = null;
        ToolState? stopped = null;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            running = await launcher.Start(isolated, showWindow: false, cancellation: deadline.Token);
            check(running is { State: "実行中", ProtocolVersion: 2, LaunchOrigin: "host_manual" } &&
                string.Equals(running.RunningExecutable, installed.Executable, StringComparison.OrdinalIgnoreCase),
                $"KotorinToysの管理起動は配布候補EXE・準備状態・起動元を照合（{running.State} {running.Detail}）");
            using var process = Process.GetProcessById(running.Pid!.Value);
            check(process.MainWindowHandle == IntPtr.Zero, "本体からの無表示起動は利用者画面を開かない");
        }
        finally
        {
            stopped = await launcher.Stop(isolated);
        }
        check(stopped.State == "停止中", $"KotorinToysは試験プロセスへ正常停止を行い、終了を確認（{stopped.State} {stopped.Detail}）");
    }
}
