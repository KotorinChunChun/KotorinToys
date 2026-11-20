using System.Security.Cryptography;
using System.Text.Json;
using KotorinToys;

internal static class InstallerTests
{
    public static async Task Run(string root,string fixtures,Action<bool,string> check)
    {
        var directory=Path.Combine(fixtures,"installer");Directory.CreateDirectory(directory);
        var tool=ToolCatalog.Load(root).Single(t=>t.Id=="smart-paste");
        var file=Path.Combine(directory,"SmartPaste-0.1.4-setup.exe");File.WriteAllText(file,"実行しないテスト用配布物");
        var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        var package=new LocalPackage(tool.Id,tool.Name,"0.1.4",tool.AppId,Path.GetFileName(file),hash,"unsigned-local-build");
        var catalogPath=Path.Combine(directory,"packages.json");
        void Save(LocalPackage value)=>File.WriteAllText(catalogPath,JsonSerializer.Serialize(new PackageCatalog(1,"unpublished",[value]),ToolCatalog.Json));
        Save(package);
        check(InstallerAdapter.Load(catalogPath).Packages.Single()==package,"ローカル配布台帳の製品・版・固定ハッシュを読込");
        var executions=0;
        var adapter=new InstallerAdapter(new(root),(path,arguments)=>
        {
            executions++;
            check(arguments.Contains("/NORESTART") && arguments.Contains("/NOFORCECLOSEAPPLICATIONS"),"導入アダプターは再起動と強制終了を禁止");
            var locked=false;
            try{using var rewrite=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);}
            catch(IOException){locked=true;}
            check(locked,"検証済み配布物は実行中の書換を許可しない");
            return Task.FromResult(3010);
        });
        check((await adapter.Install(tool,catalogPath)).State=="再起動待ち","インストーラー3010は成功や自動再起動に置換しない");
        Save(package with{Sha256=new string('0',64)});
        check((await adapter.Install(tool,catalogPath)).State=="失敗" && executions==1,"ハッシュ不一致の配布物は実行しない");
        Save(package with{File="../outside.exe"});
        try{InstallerAdapter.Load(catalogPath);check(false,"範囲外配布物を拒否");}catch(IOException){check(true,"範囲外配布物を拒否");}
        Save(package);using var cancellation=new CancellationTokenSource();
        adapter=new InstallerAdapter(new(root),(_,_)=>{cancellation.Cancel();return Task.FromResult(3010);});
        var next=ToolCatalog.Load(root).Single(t=>t.Id=="ss-capture");
        var results=await new InstallationQueue(adapter).Run([tool,next],"install",catalogPath,cancellation:cancellation.Token);
        check(results[0].State=="再起動待ち" && results[1].State=="未実行","キャンセルは進行中を強制中断せず残りだけ未実行にする");
        adapter=new InstallerAdapter(new(root),(_,_)=>Task.FromResult(7));
        var progressRows=new List<InstallResult>();
        results=await new InstallationQueue(adapter).Run([tool,next],"install",catalogPath,new ImmediateProgress<InstallResult>(progressRows.Add));
        check(results.Length==2 && results.All(r=>r.State=="失敗"),"一括の部分失敗・配布物欠落を対象別に返す");
        check(progressRows.Where(r=>r.State=="処理中").Count()==2 && progressRows.Where(r=>r.State=="処理中").All(r=>r.Detail=="実行中です"),"ローカル一括配布も実行中ですに統一");
        check(InstallerAdapter.Classify(tool.Id,"install",0).State=="成功" && InstallerAdapter.Classify(tool.Id,"install",7).State=="失敗","終了コード分類を区別する");
        try{await new InstallationQueue(adapter).Run([tool,new("host","KotorinToys","KotorinToys.exe","README.md")],"install",catalogPath);check(false,"一括に本体を含めない");}
        catch(IOException){check(true,"一括に本体を含めない");}

        var pppaint = tool with { Id = PppaintDistribution.Id, Name = PppaintDistribution.Name, AppId = PppaintDistribution.AppId,
            InstalledExecutable = "PPPaint.exe", RepositoryUrl = PppaintDistribution.Repository, Msix = null,
            Capabilities = ["status", "show", "stop", "start"], DistributionStatus = "published" };
        var pppaintDirectory = Path.Combine(fixtures, "pppaint-installer");
        Directory.CreateDirectory(pppaintDirectory);
        var pppaintCatalog = Path.Combine(pppaintDirectory, "packages.json");
        var pppaintFile = Path.Combine(pppaintDirectory, PppaintDistribution.FileName("0.2.36"));
        File.WriteAllText(pppaintFile, "署名確認を注入するPPPaint配布物");
        var pppaintHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pppaintFile))).ToLowerInvariant();
        var pppaintPackage = new LocalPackage(pppaint.Id, pppaint.Name, "0.2.36", pppaint.AppId, Path.GetFileName(pppaintFile),
            pppaintHash, PppaintDistribution.Signature, PppaintDistribution.Kind);
        File.WriteAllText(pppaintCatalog, JsonSerializer.Serialize(new PackageCatalog(1, "published", [pppaintPackage]), ToolCatalog.Json));
        executions = 0;
        adapter = new InstallerAdapter(new(root), (_, _) => { executions++; return Task.FromResult(0); },
            authenticodeSignature: _ => new("CN=Other", PppaintDistribution.Thumbprint, true));
        check((await adapter.Install(pppaint, pppaintCatalog)).State == "失敗" && executions == 0, "PPPaintは固定と異なるAuthenticode署名者を実行しない");
        adapter = new InstallerAdapter(new(root), (_, _) => { executions++; return Task.FromResult(0); },
            detectInstallation: _ => new("未導入", null, null, null),
            authenticodeSignature: _ => new(PppaintDistribution.Publisher, PppaintDistribution.Thumbprint, true));
        check((await adapter.Install(pppaint, pppaintCatalog, pppaintDirectory)).State == "要確認" && executions == 1,
            "PPPaintはSHA256後に固定署名者・時刻印を照合して実行する");
        foreach (var invalid in new[] { pppaintPackage with { Signature = "unsigned-public-build" }, pppaintPackage with { Kind = "inno" },
            pppaintPackage with { File = "PPPaint-0.2.35-setup.exe" } })
        {
            try { InstallerAdapter.Validate(new(1, "published", [invalid])); check(false, "PPPaintの不正署名ポリシーを拒否"); }
            catch (IOException) { check(true, "PPPaintの不正署名ポリシーを拒否"); }
        }
    }
}
