using System.Text.Json;
using KotorinToys;
using KotorinToys.Integration;

internal static class Program
{
    static int count;
    static string root = "";
    static string fixtures = "";
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        count++; Console.WriteLine("PASS: " + name);
    }
    static void Reject(Action action, string name)
    {
        try { action(); }
        catch (IOException) { Check(true, name); return; }
        throw new Exception("拒否しませんでした: " + name);
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(["--shortcut-ipc"])) return ShortcutKeyCounterIntegrationTests.Run().GetAwaiter().GetResult();
            if (args.SequenceEqual(["--http-selector-ipc"])) return HttpSelectorIntegrationTests.Run().GetAwaiter().GetResult();
            if (args.SequenceEqual(["--http-selector-msix"])) return HttpSelectorIntegrationTests.RunMsix().GetAwaiter().GetResult();
            if (args.SequenceEqual(["--new-tools-ipc"])) return AddedToolIntegrationTests.Run().GetAwaiter().GetResult();
            if (args.SequenceEqual(["--everydb-ipc"])) return AddedToolIntegrationTests.Run("every-db").GetAwaiter().GetResult();
            if (args.SequenceEqual(["--new-tools-panel"])) return AddedToolIntegrationTests.Panel();
            if (args.SequenceEqual(["--shortcut-panel"])) return ShortcutKeyCounterIntegrationTests.Panel();
            if (args.Length == 2 && args[0] == "--shortcut-owned-stop" && int.TryParse(args[1], out var ownedPid))
                return ShortcutKeyCounterIntegrationTests.StopOwned(ownedPid).GetAwaiter().GetResult();
            IconTests.Asset(Check);
            if (args.SequenceEqual(["--upgrade-stop-host"])) return UpgradeRebootValidation.StopHost().GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--upgrade-arm-boot") return UpgradeRebootValidation.Arm(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--post-upgrade-boot-check") return UpgradeRebootValidation.Check(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--upgrade-reboot-ready") return UpgradeRebootValidation.ReadyToReboot(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--validate-online-catalog")
            {
                var catalogRoot = KotorinToys.Program.FindRoot();
                var path = Path.GetFullPath(args[1]);
                if (new FileInfo(path).Length > OnlineToolCatalog.MaximumBytes) throw new IOException("一覧configの容量上限を超えています。");
                var validated = OnlineToolCatalog.Parse(File.ReadAllBytes(path), catalogRoot);
                Console.WriteLine($"一覧config検証成功：{validated.Length}件。通信・公開は行っていません。"); return 0;
            }
            if (args.SequenceEqual(["--repository-button-tests"])) { RepositoryButtonTests.Run(KotorinToys.Program.FindRoot(), Check); Console.WriteLine($"PASS: {count} 件"); return 0; }
            if (args.Length >= 2 && args[0] == "--verify-icon-exe")
            {
                var source = Path.Combine(KotorinToys.Program.FindRoot(), "src", "KotorinToys", "assets", "KotorinToys.ico");
                foreach (var path in args.Skip(1)) IconTests.Executable(path, source, Check);
                Console.WriteLine($"PASS: {count} 件"); return 0;
            }
            if(args.Length==2 && args[0]=="--run-native-release-host")return InstalledStartupTests.NativeTask("--release-host "+StartupCoordinator.Quote(Path.GetFullPath(args[1]))).GetAwaiter().GetResult();
            if(args.Length==1 && args[0]=="--public-release-check") return PublicReleaseTests.Live(KotorinToys.Program.FindRoot()).GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--run-native-tool-update")return InstalledStartupTests.NativeTask("--tool-update "+StartupCoordinator.Quote(Path.GetFullPath(args[1]))).GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--tool-update")return ReleaseAcceptance.UpdateTool(args[1]).GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--release-host")return ReleaseAcceptance.Host(args[1]).GetAwaiter().GetResult();
            if(args.Length==1 && args[0]=="--run-native-release-install")return InstalledStartupTests.NativeTask("--release-install").GetAwaiter().GetResult();
            if(args.Length==1 && args[0]=="--release-install")return ReleaseAcceptance.Run().GetAwaiter().GetResult();
            if(args.Length==1 && args[0]=="--run-native-release-ui")return InstalledStartupTests.NativeTask("--release-ui").GetAwaiter().GetResult();
            if(args.Length==1 && args[0]=="--release-ui") { Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new InstallationForm(new(KotorinToys.Program.FindRoot())));return 0; }
            if(args.Contains("--foreground-pid")) { GetWindowThreadProcessId(GetForegroundWindow(),out var pid); Console.WriteLine(pid); return 0; }
            if(args.Length==1 && args[0]=="--run-native-arm-reboot")return BootValidation.Prepare().GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--arm-reboot-test")return BootValidation.ArmWithReceipt(args[1]).GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--boot-check")return BootValidation.Check(args[1]).GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--native-install")return InstalledStartupTests.NativeInstall(args[1]).GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--run-native-install")return InstalledStartupTests.NativeTask("--native-install "+StartupCoordinator.Quote(Path.GetFullPath(args[1]))).GetAwaiter().GetResult();
            if(args.Length==1 && args[0]=="--run-native-startup")return InstalledStartupTests.NativeTask("--installed-startup").GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--run-native-cycle")return InstalledStartupTests.NativeTask("--installed-cycle "+StartupCoordinator.Quote(Path.GetFullPath(args[1]))).GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--installed-cycle")return InstalledStartupTests.Cycle(args[1]).GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--run-native-host-cycle")return InstalledStartupTests.NativeTask("--installed-host-cycle "+StartupCoordinator.Quote(Path.GetFullPath(args[1]))).GetAwaiter().GetResult();
            if(args.Length==2 && args[0]=="--installed-host-cycle")return InstalledStartupTests.HostCycle(args[1]).GetAwaiter().GetResult();
            if(args.Length==3 && args[0]=="--via-probe")
            {
                var manager=new ToolManager(args[1]);var store=new HostSettingsStore(HostSettingsStore.DefaultDirectory);
                var host=new InstallationDetector().Detect(new("host","KotorinToys","KotorinToys.exe","README.md",AppId:"KotorinToys",InstalledExecutable:"KotorinToys.exe"));
                var coordinator=new StartupCoordinator(store,new TaskSchedulerGateway(),manager,host.Executable??"unknown");
                var snapshots=manager.Load().Select(t=>manager.Snapshot(t,coordinator.State(t).GetAwaiter().GetResult()).GetAwaiter().GetResult()).ToArray();
                var settingsFile=Path.Combine(store.DirectoryPath,"settings.json");
                var registrations=new List<object>();
                foreach(var view in new[]{Microsoft.Win32.RegistryView.Registry64,Microsoft.Win32.RegistryView.Registry32})
                {
                    using var hive=Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser,view);
                    using var registration=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\KotorinToys_is1");
                    registrations.Add(new {View=view.ToString(),Exists=registration!=null,Path=registration?.GetValue("InstallLocation")});
                }
                File.WriteAllText(args[2],JsonSerializer.Serialize(new {Host=host,SettingsHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(settingsFile))),Snapshots=snapshots,Registrations=registrations,Is64Bit=Environment.Is64BitProcess,Sid=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value},ToolCatalog.Json));
                return 0;
            }
            if(args.Length==3 && args[0]=="--filesystem-probe")
            {
                string readable;
                try{using var stream=File.OpenRead(args[1]);readable="読取成功";}catch(Exception ex){readable=ex.GetType().Name+" / 0x"+ex.HResult.ToString("X8");}
                File.WriteAllText(args[2],JsonSerializer.Serialize(new {Target=args[1],LocalAppData=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),FileExists=File.Exists(args[1]),DirectoryExists=Directory.Exists(Path.GetDirectoryName(args[1])),Readable=readable,Session=System.Diagnostics.Process.GetCurrentProcess().SessionId,UserSid=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value},ToolCatalog.Json));
                return 0;
            }
            if(args.Length==3 && args[0]=="--cleanup-task") {StartupTests.Cleanup(args[1],args[2]);return 0;}
            if(args.Contains("--scheduler-probe")){InstalledStartupTests.Probe().GetAwaiter().GetResult();return 0;}
            root = KotorinToys.Program.FindRoot();
            fixtures = Path.Combine(root, "dev", "history", "v" + HostProduct.CurrentVersion, "logs", "host-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtures);
            if (args.SequenceEqual(["--msix-tests"]))
            {
                MsixInstallationTests.Run(fixtures, Check);
                MsixReleaseTests.Run(fixtures, Check).GetAwaiter().GetResult();
                Console.WriteLine($"PASS: {count} 件"); return 0;
            }
            if (args.Length == 3 && args[0] == "--startup-root-maintenance")
                return StartupRootMaintenance.Run(args[1], args[2], fixtures).GetAwaiter().GetResult();
            if (args.SequenceEqual(["--startup-root-tests"]))
            {
                StartupTests.Run(root, fixtures, Check);
                Console.WriteLine($"PASS: {count} 件"); return 0;
            }
            if (args.SequenceEqual(["--startup-overview-live"]))
                return StartupOverviewLive.Run(root, fixtures).GetAwaiter().GetResult();
            if (args.SequenceEqual(["--startup-overview-tests"]))
            {
                StartupOverviewTests.Run(root, Check).GetAwaiter().GetResult();
                StartupOverviewTests.VerifyMismatch(root, fixtures, Check).GetAwaiter().GetResult();
                StartupOverviewDashboardTests.Run(root, fixtures, Check);
                Console.WriteLine($"PASS: {count} 件"); return 0;
            }
            if (args.Length is 1 or 2 && args[0] == "--portable-release-check")
                return PortableReleaseTests.Live(root, args.Length == 2 ? args[1] : null).GetAwaiter().GetResult();
            if (args.Length is 1 or 2 && args[0] == "--portable-release-panel")
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                var area = Screen.PrimaryScreen!.WorkingArea;
                using var panel = new Dashboard(root, new HostSettingsStore(fixtures), releaseCheck: async (tool, fallback, token) =>
                {
                    using var client = PortableReleaseTests.LiveClient(args.Length == 2 && tool.Id == "shortcut-key-counter" ? args[1] : null);
                    return await client.Check(tool, fallback, token);
                })
                {
                    Text = WindowTitle.Create("公開版確認"), StartPosition = FormStartPosition.Manual,
                    Bounds = new(area.Left + area.Width / 2, area.Top, area.Width - area.Width / 2, area.Height)
                };
                Application.Run(panel);
                return 0;
            }
            if (args.SequenceEqual(["--public-catalog-acceptance"]))
            {
                PublishedDownloadTests.RunCatalog(root, Path.Combine(fixtures, "public-catalog")).GetAwaiter().GetResult(); return 0;
            }
            if (args.SequenceEqual(["--download-tool-release-tests"]) || args.SequenceEqual(["--download-host-release-test"]))
            {
                PublishedDownloadTests.Run(root, Path.Combine(fixtures, "public-download"), args[0] == "--download-host-release-test").GetAwaiter().GetResult();
                return 0;
            }
            if (args.SequenceEqual(["--distribution-tests"]))
            {
                ProductIdentityTests.Run(Check);
                DistributionMappingTests.Run(root, Check).GetAwaiter().GetResult();
                Console.WriteLine($"PASS: {count} 件"); return 0;
            }
            if (args.SequenceEqual(["--installation-tests"]))
            {
                InstallerTests.Run(root, fixtures, Check).GetAwaiter().GetResult();
                ToolInstallationRunnerTests.Run(root, Check).GetAwaiter().GetResult();
                DistributionMappingTests.Run(root, Check).GetAwaiter().GetResult();
                PublicReleaseTests.Run(root, fixtures, Check).GetAwaiter().GetResult();
                PublicReleaseMetadataTests.Run(root, Check);
                PortableReleaseTests.Run(root, Check).GetAwaiter().GetResult();
                PortableReleaseTests.Form(root, Check);
                ReleaseDisplayTests.Run(root, Check);
                var previousInstallationContext = SynchronizationContext.Current;
                try { InstallationSummaryTests.Run(root, Check); }
                finally { SynchronizationContext.SetSynchronizationContext(previousInstallationContext); }
                HostUpdateFormTests.Run(root, Check);
                InstallationFormWorkflowTests.Run(root, Check);
                DashboardTests.Run(root, Check);
                DashboardRefreshTests.Run(root, Check);
                DashboardInstallationTests.Run(root, Check);
                Console.WriteLine($"PASS: {count} 件"); return 0;
            }
            if (args.SequenceEqual(["--self-update-panel"]))
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                var area = Screen.PrimaryScreen!.WorkingArea;
                using var panel = new HostUpdateForm(new HostSelfUpdateService(root, fixtures))
                {
                    StartPosition = FormStartPosition.Manual,
                    Bounds = new(area.Left + area.Width / 2, area.Top, area.Width - area.Width / 2, area.Height)
                };
                Application.Run(panel);
                return 0;
            }
            if (args.SequenceEqual(["--host-release-check"]))
            {
                using var client = new PublicReleaseClient();
                var result = client.CheckHost(true).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(result, ToolCatalog.Json));
                return result.Confirmed ? 0 : 1;
            }
            if (args.SequenceEqual(["--self-update-tests"]))
            {
                HostReleaseTests.Run(fixtures, Check).GetAwaiter().GetResult();
                SelfUpdateWorkflowTests.Run(fixtures, Check).GetAwaiter().GetResult();
                HostUpdateFormTests.Run(root, Check);
                Console.WriteLine($"PASS: {count} 件"); return 0;
            }
            HostReleaseTests.Run(fixtures, Check).GetAwaiter().GetResult();
            SelfUpdateWorkflowTests.Run(fixtures, Check).GetAwaiter().GetResult();
            if (args.SequenceEqual(["--online-catalog-tests"]))
            {
                OnlineCatalogTests.Run(root, fixtures, Check).GetAwaiter().GetResult();
                OnlineCatalogDashboardTests.Run(root, fixtures, Check);
                Console.WriteLine($"PASS: {count} 件"); return 0;
            }
            OnlineCatalogTests.Run(root, fixtures, Check).GetAwaiter().GetResult();
            CatalogTests(); DetectionTests(); EndpointTests().GetAwaiter().GetResult();
            MsixInstallationTests.Run(fixtures, Check);
            MsixReleaseTests.Run(fixtures, Check).GetAwaiter().GetResult();
            ProductIdentityTests.Run(Check);
            StartupTests.Run(root,fixtures,Check);
            StartupOverviewTests.Run(root,Check).GetAwaiter().GetResult();
            StartupOverviewTests.VerifyMismatch(root,fixtures,Check).GetAwaiter().GetResult();
            RemovalTests.Run(fixtures,Check);
            InstallerTests.Run(root,fixtures,Check).GetAwaiter().GetResult();
            var previousContext = SynchronizationContext.Current;
            try { InstallationSummaryTests.Run(root, Check); InstallationFormWorkflowTests.Run(root, Check); DashboardTests.Run(root, Check); DashboardRefreshTests.Run(root, Check); DashboardInstallationTests.Run(root, Check); OnlineCatalogDashboardTests.Run(root, fixtures, Check); StartupOverviewDashboardTests.Run(root, fixtures, Check); HostUpdateFormTests.Run(root, Check); IconTests.FormsAndDefinitions(root, fixtures, Check); }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
            ReleaseTests.Run(root,fixtures,Check).GetAwaiter().GetResult();
            ToolInstallationRunnerTests.Run(root, Check).GetAwaiter().GetResult();
            DistributionMappingTests.Run(root, Check).GetAwaiter().GetResult();
            FfmpegTests.Run(fixtures, Check).GetAwaiter().GetResult();
            PublicReleaseTests.Run(root, fixtures, Check).GetAwaiter().GetResult();
            PublicReleaseMetadataTests.Run(root, Check);
            PortableReleaseTests.Run(root, Check).GetAwaiter().GetResult();
            PortableReleaseTests.Form(root, Check);
            ReleaseDisplayTests.Run(root, Check);
            ToolUpdateTests.Run(root, fixtures, Check).GetAwaiter().GetResult();
            BootValidation.Tests(Check);
            UpgradeRebootValidation.Tests(Check);
            Check(JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"catalog","host.json"))).RootElement.GetProperty("version").GetString() == HostProduct.CurrentVersion,
                "本体の製品定義とAssemblyの配布版を一致させる");
            if(args.Contains("--scheduler")) StartupTests.LiveScheduler(root,fixtures,Check);
            if(args.Contains("--installed-startup")) InstalledStartupTests.Run(fixtures,Check).GetAwaiter().GetResult();
            if (args.Contains("--live")) LiveTests().GetAwaiter().GetResult();
            if (args.Contains("--host-lifetime")) HostLifetimeTests().GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {count} 件");
            File.WriteAllText(Path.Combine(fixtures, "result.txt"), $"PASS: {count} 件");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void CatalogTests()
    {
        var tools = ToolCatalog.Load(root);
        Check(tools.Length == 9, "schema 2でEveryDBを含む9ツールを解決する");
        Check(tools.Count(t => t.DistributionStatus == "published") == 6 && tools.Single(t => t.Id == "shortcut-key-counter").DistributionStatus == "unpublished", "本体対応6ツールと今回公開しないShortcutKeyCounterを区別");
        Check(tools.Count(t => t.DistributionStatus == "unpublished") == 3, "ShortcutKeyCounter・HttpSelector・EveryDBは本体用Inno配布の対象外");
        Check(tools.All(t => t.Runtimes?.Length > 0), "9ツールの依存情報を持つ");
        Check(tools.All(t => t.Capabilities?.Contains("silent-start")==true && t.SupportedEnvironment=="windows11-x64"),"9ツールの確認済み能力と環境");
        var httpSelector = tools.Single(t => t.Id == "http-selector");
        Check(httpSelector is { AppId: "KotorinToys.HttpSelector", DistributionStatus: "unpublished" } &&
            Version.TryParse(httpSelector.Version, out var httpVersion) && httpVersion.Revision >= 0, "HttpSelectorの4成分版・固定製品識別・配布状態を保持");
        var everyDb = tools.Single(t => t.Id == "every-db");
        Check(everyDb is { Name: "EveryDB", Version: "0.1.0", AppId: "KotorinToys.EveryDB", DistributionStatus: "unpublished" } &&
            everyDb.Executable == "src-tauri/target/kotorintoys/debug/everydb.exe" && everyDb.Capabilities!.SequenceEqual(["status", "show", "stop", "silent-start"]),
            "EveryDBの独立リポジトリ・開発実行先・管理能力を登録");
        var contract=File.ReadAllText(Path.Combine(root,"contracts","ManagementEndpoint.cs")).Replace("\r\n", "\n");
        Check(new[] {"tools/LiveTextMarker/source/ManagementEndpoint.cs","tools/SmartPaste/SmartPaste/ManagementEndpoint.cs","tools/SSCapture/src/SSCapture/ManagementEndpoint.cs"}.All(path=>File.ReadAllText(Path.Combine(root,path)).Replace("\r\n", "\n")==contract),"改行形式を除く共通管理実装と3子コピーの一致");
        foreach (var name in new[] { "CapchunScreen", "ChulipVideo", "ChunapTool" })
        {
            string Normalize(string value) => value.Replace("\r\n", "\n").TrimEnd();
            var directory = Path.Combine(root, "tools", name, "src", name, "Integration");
            Check(Normalize(File.ReadAllText(Path.Combine(directory, "ManagementEndpoint.cs"))) == Normalize(contract), name + " 管理契約の共通コピーが一致");
            Check(Normalize(File.ReadAllText(Path.Combine(directory, "ManagedToolContext.cs"))) == Normalize(File.ReadAllText(Path.Combine(root, "contracts", "ManagedToolContext.cs"))), name + " 無表示起動・正常終了の共通コピーが一致");
        }
        Check(tools.All(t => File.Exists(PathSafety.Resolve(PathSafety.Resolve(root,t.ToolRoot), t.Manual))), "論理ルート基準の文書参照");
        Reject(() => PathSafety.Resolve(root, "../outside"), "親へのパストラバーサルを拒否");
        Reject(() => PathSafety.Resolve(root, "C:/outside"), "絶対パスを拒否");
        Reject(() => PathSafety.Resolve(root, ""), "空パスを拒否");
        var legacyRoot = Path.Combine(fixtures, "legacy");
        Directory.CreateDirectory(Path.Combine(legacyRoot,"catalog"));
        var legacy = new ToolDefinition("sample-tool", "Sample", "sample.exe", "README.md");
        void Save(object data) => File.WriteAllText(Path.Combine(legacyRoot,"catalog","tools.json"), JsonSerializer.Serialize(data, ToolCatalog.Json));
        Save(new Catalog(1,[legacy]));
        Check(ToolCatalog.Load(legacyRoot).Single().Id == "sample-tool", "schema 1の互換読込");
        Save(new Catalog(1,[legacy,legacy])); Reject(() => ToolCatalog.Load(legacyRoot), "重複IDを拒否");
        Save(new Catalog(99,[legacy])); Reject(() => ToolCatalog.Load(legacyRoot), "未知形式を拒否");
        Save(new Catalog(1,[legacy with { Executable="../bad.exe" }])); Reject(() => ToolCatalog.Load(legacyRoot), "登録EXEの範囲外参照を拒否");
        Save(new Catalog(1,[legacy with { Id="bad id" }])); Reject(() => ToolCatalog.Load(legacyRoot), "不正IDを拒否");
        Save(new Catalog(2,[legacy])); Reject(() => ToolCatalog.Load(legacyRoot), "新形式の定義参照欠落を拒否");
        Save(new { tools = Array.Empty<object>() }); Reject(() => ToolCatalog.Load(legacyRoot), "形式版欠落を拒否");
        Save(new Catalog(1,[legacy with { Readme="../outside.md" }])); Reject(() => ToolCatalog.Load(legacyRoot), "READMEの範囲外参照を拒否");
        Save(new Catalog(1,[legacy with { Shortcuts="../outside.json" }])); Reject(() => ToolCatalog.Load(legacyRoot), "宣言の範囲外参照を拒否");
        Save(new Catalog(1,[legacy with { InstalledExecutable="../outside.exe" }])); Reject(() => ToolCatalog.Load(legacyRoot), "導入EXEの範囲外参照を拒否");
        var versionRoot = Path.Combine(fixtures, "catalog-versions");
        Directory.CreateDirectory(Path.Combine(versionRoot, "catalog"));
        var versionDefinitionRoot = Path.Combine(versionRoot, "tools", "HttpSelector");
        Directory.CreateDirectory(versionDefinitionRoot);
        var definition = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tools", "HttpSelector", "tool.json")))!.AsObject();
        File.WriteAllText(Path.Combine(versionRoot, "catalog", "tools.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            tools = new[] { new { id = "http-selector", name = "HttpSelector", definition = "tools/HttpSelector/tool.json" } }
        }, ToolCatalog.Json));
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts", "tool.schema.json")));
        var versionPattern = schema.RootElement.GetProperty("properties").GetProperty("version").GetProperty("pattern").GetString()!;
        foreach (var version in new[] { "0.0.3", "0.0.3.2", "12.34.56.78" })
        {
            definition["version"] = version;
            File.WriteAllText(Path.Combine(versionDefinitionRoot, "tool.json"), definition.ToJsonString(ToolCatalog.Json));
            Check(ToolCatalog.Load(versionRoot).Single().Version == version, "数値3成分または4成分の製品版を保持：" + version);
            Check(System.Text.RegularExpressions.Regex.IsMatch(version, versionPattern), "ツールschemaも3成分または4成分の版を許容：" + version);
        }
        foreach (var version in new[] { "", "0.0", "0.0.3.2.1", "v0.0.3.2", "0.0.3.2-beta", "0.0.3+2" })
        {
            definition["version"] = version;
            File.WriteAllText(Path.Combine(versionDefinitionRoot, "tool.json"), definition.ToJsonString(ToolCatalog.Json));
            Reject(() => ToolCatalog.Load(versionRoot), "数値3～4成分以外の製品版を拒否：" + version);
            Check(!System.Text.RegularExpressions.Regex.IsMatch(version, versionPattern), "ツールschemaも不正な版を拒否：" + version);
        }
    }
    static void DetectionTests()
    {
        var installedRoot = Path.Combine(fixtures,"installed");
        Directory.CreateDirectory(installedRoot);
        var tool = new ToolDefinition("test-install", "KotorinToys.Tests", "dummy.exe", "README.md",
            AppId:"KotorinToys.TestDummy", InstalledExecutable:"dummy.exe");
        Check(new InstallationDetector(_ => null).Detect(tool).State=="未導入", "登録なしは未導入");
        Check(new InstallationDetector(_ => new(null)).Detect(tool).State=="確認できない", "登録先不明は未導入にしない");
        var detector = new InstallationDetector(_ => new(installedRoot));
        Check(detector.Detect(tool).State=="修復が必要", "登録済みEXE欠落を検出");
        File.Copy(Environment.ProcessPath!, Path.Combine(installedRoot,"dummy.exe"));
        File.WriteAllText(Path.Combine(installedRoot,"tool.json"), JsonSerializer.Serialize(new { id="other", appId=tool.AppId }));
        Check(detector.Detect(tool).State=="確認できない", "製品識別不一致を拒否");
        File.WriteAllText(Path.Combine(installedRoot,"tool.json"), JsonSerializer.Serialize(new { id=tool.Id, appId=tool.AppId }));
        Check(detector.Detect(tool).State=="導入済み", "本体経由記録に依存しない導入検出");
        Check(detector.Detect(tool).Version!=null, "EXEから版を取得");
        Check(detector.Detect(tool).Uninstaller==null, "削除プログラム欠落を偽装しない");
        Check(new InstallationDetector(_=>null).UpdateTarget(tool,installedRoot).State=="導入済み","未登録の更新修復でも実EXEと製品定義を照合");
        Check(new InstallationDetector(_=>new(Path.Combine(fixtures,"other-install"))).UpdateTarget(tool,installedRoot).State=="確認できない","登録された別配置を更新修復で迂回しない");
        Check(new InstallationDetector(_=>null).UpdateTarget(tool with{Id="other"},installedRoot).State=="確認できない","未登録の更新修復で別製品定義を拒否");
        Check(new InstallationDetector(_=>null).Detect(tool).State=="未導入","更新修復の照合を通常の正式導入検出へ流用しない");
        Check(RuntimeDetector.Detect(tool,Path.Combine(installedRoot,"dummy.exe")).Single().State=="未確認", "依存物未調査を空にしない");
        var runtimes = tool with { Runtimes=[new("test", "テスト依存", "file", "worker.exe")] };
        Check(RuntimeDetector.Detect(runtimes,Path.Combine(installedRoot,"dummy.exe")).Single().State=="不足", "ワーカー不足を検出");
        File.WriteAllText(Path.Combine(installedRoot,"worker.exe"),"fixture");
        Check(RuntimeDetector.Detect(runtimes,Path.Combine(installedRoot,"dummy.exe")).Single().State=="利用可能", "確認済み依存物あり");
        Check(RuntimeDetector.Detect(tool with { Runtimes=[new("external","外部依存","external")] },Path.Combine(installedRoot,"dummy.exe")).Single().State=="未確認", "未対応の外部依存は不明");
    }
    static async Task EndpointTests()
    {
        var id="test-endpoint-"+Environment.ProcessId;
        int shows=0, stops=0;
        string? refusal="処理中のため停止できません。";
        var ready = new TaskCompletionSource<(Control,ApplicationContext)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var control = new Control();
            using var context = new ApplicationContext();
            using var endpoint = new ManagementEndpoint(id,control,()=>Interlocked.Increment(ref shows),()=>Interlocked.Increment(ref stops),()=>Volatile.Read(ref refusal));
            ready.SetResult((control,context)); Application.Run(context);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        var (control,context) = await ready.Task;
        try
        {
            var manager = new ToolManager(root);
            var tool = new ToolDefinition(id,"Fixture",Path.GetRelativePath(root,Environment.ProcessPath!),"README.md");
            var status=await manager.Status(tool);
            Check(status.State=="実行中" && status.ProtocolVersion==2, "UI応答後の準備状態");
            Check(status.Pid==Environment.ProcessId, "セッションとEXEを確認したPID");
            var mismatch=await manager.Send(tool,"show",Environment.ProcessId+1);
            Check(mismatch.Result=="refused" && shows==0, "別PIDへの表示を拒否");
            var show=await manager.Send(tool,"show",Environment.ProcessId);
            Check(show.Result=="completed" && shows==1, "画面操作実行後に完了応答");
            var stopped=await manager.Stop(tool);
            Check(stopped.State=="実行中" && stops==0 && stopped.Detail.Contains("処理中"), "停止拒否を成功・停止待ちにしない");
            var keys=await manager.Send(tool,"shortcuts",Environment.ProcessId);
            Check(keys.Result=="unsupported", "実効キー未対応を明示");
            var reused=await manager.Start(tool,false);
            Check(reused.Pid==Environment.ProcessId && shows==1, "無表示の既存開始は画面を開かない");
            reused=await manager.Start(tool,true);
            Check(reused.Pid==Environment.ProcessId && shows==2, "手動の既存開始は同じPIDを表示");
            Volatile.Write(ref refusal,null);
            var accepted=await manager.Send(tool,"stop",Environment.ProcessId);
            Check(accepted.Result=="accepted", "正常終了の受理と完了を分離");
            await Task.Delay(100);
            Check(stops==1, "正常終了処理をUIへ渡す");
        }
        finally
        {
            control.BeginInvoke(new Action(context.ExitThread));
            if(!thread.Join(3000)) throw new Exception("テスト用UIを終了できません。");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool EnumWindows(WindowCallback callback, nint state);
    delegate bool WindowCallback(nint window, nint state);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindowVisible(nint window);
    static bool HasVisibleWindow(int processId)
    {
        bool visible=false;
        EnumWindows((window,_) => { GetWindowThreadProcessId(window,out var pid); if(pid==processId && IsWindowVisible(window)) visible=true; return true; },0);
        return visible;
    }
    static async Task LiveTests()
    {
        var manager=new ToolManager(root);
        foreach(var tool in manager.Load())
        {
            Check((await manager.Status(tool)).State=="停止中",tool.Name+" 実連携前は停止状態");
            try
            {
                var state=await manager.Start(tool,false);
                Check(state.State=="実行中" && state.Pid.HasValue,tool.Name+" 実窓口の準備完了");
                Check(!HasVisibleWindow(state.Pid!.Value),tool.Name+" startupのトップレベル画面は非表示");
                var snapshot=await manager.Snapshot(tool);
                Check(snapshot.Installation.State=="未導入" && snapshot.Startup.ActualState=="未確認",tool.Name+" 開発実行と正式導入・予約を混同しない");
                var shown=await manager.Start(tool,true);
                Check(shown.Pid==state.Pid && HasVisibleWindow(shown.Pid!.Value),tool.Name+" 同一PIDの実画面を表示");
                await Task.Delay(200);
                GetWindowThreadProcessId(GetForegroundWindow(),out var foreground);
                Check(foreground==shown.Pid,tool.Name+" 表示要求後に前面のPIDを確認");
            }
            finally { Check((await manager.Stop(tool)).State=="停止中",tool.Name+" 実プロセスを正常終了して復元"); }
        }
        await HostLifetimeTests();
    }
    static async Task HostLifetimeTests()
    {
        var ready=new TaskCompletionSource<(Control,HostApplicationContext)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            using var dispatch=new Control(); _=dispatch.Handle;
            using var context=new HostApplicationContext(root,true,Path.Combine(fixtures,"host-settings"));
            ready.SetResult((dispatch,context)); Application.Run(context);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        var (control,host)=await ready.Task;
        Task Ui(Action action)
        {
            var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            control.BeginInvoke(new Action(()=>{try {action();done.SetResult();} catch(Exception ex){done.SetException(ex);} })); return done.Task;
        }
        try
        {
            await Ui(()=>Check(!host.Dashboard.Visible,"本体startupは管理画面非表示"));
            await Ui(()=>{host.ShowDashboard();Check(host.Dashboard.Visible,"本体管理画面を表示");});
            await Ui(()=>{host.Dashboard.Close();Check(!host.Dashboard.Visible && !host.Dashboard.IsDisposed,"本体画面を閉じても管理部は継続");});
            await Ui(()=>{host.ShowDashboard();Check(host.Dashboard.Visible,"閉じた本体画面を再表示");});
        }
        finally { await Ui(host.ExitThread); Check(thread.Join(3000),"本体を明示的に正常終了"); }
    }
}
