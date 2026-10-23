using System.Security.Cryptography;
using System.Text.Json;
using KotorinToys;

internal static class InstallationDiagnosisTests
{
    public static async Task Run(string root,string fixtures,Action<bool,string> check)
    {
        var directory=Path.Combine(fixtures,"installation-diagnosis");Directory.CreateDirectory(directory);
        var tool=new ToolDefinition("diagnosis-tool","KotorinToys.Tests","dummy.exe","README.md",
            AppId:"KotorinToys.DiagnosisTool",InstalledExecutable:"dummy.exe");
        File.Copy(Environment.ProcessPath!,Path.Combine(directory,"dummy.exe"));
        var detector=new InstallationDetector(_=>new(directory));
        var actual=detector.Detect(tool);
        check(actual.State=="確認できない" && actual.Detail.Contains("tool.json がありません") && actual.Directory==directory,
            "EXEだけある旧配布の欠落ファイル名と導入先を保持して起動を拒否");
        var definition=Path.Combine(directory,"tool.json");
        File.WriteAllText(definition,"{");
        check(detector.Detect(tool).Detail.Contains("JSON形式"),"製品定義のJSON破損を欠落と区別");
        File.WriteAllText(definition,new string(' ',OnlineToolCatalog.MaximumBytes+1));
        check(detector.Detect(tool).Detail.Contains("容量"),"通常EXEがあっても巨大な製品定義を読込前に拒否");
        File.WriteAllText(definition,"{}");
        check(detector.Detect(tool).Detail.Contains("必須項目"),"製品定義の必須項目欠落を具体化");
        File.WriteAllText(definition,JsonSerializer.Serialize(new{id=tool.Id,appId=tool.AppId}));
        check(detector.Detect(tool).State=="導入済み","旧2項目の製品定義は正常EXEとの照合を維持");
        File.WriteAllText(definition,JsonSerializer.Serialize(new{id=tool.Id,appId=tool.AppId}),new System.Text.UTF8Encoding(true));
        check(detector.Detect(tool).State=="導入済み","既存のBOM付きUTF-8製品定義を正常に検出");
        File.WriteAllText(definition,JsonSerializer.Serialize(new{id=tool.Id,appId=tool.AppId}),System.Text.Encoding.Unicode);
        check(detector.Detect(tool).State=="導入済み","既存のBOM付きUTF-16製品定義を正常に検出");
        check(detector.Detect(tool with{Name="OtherProduct"}).Detail.Contains("対象：OtherProduct / 検出：KotorinToys.Tests"),
            "実EXEの別製品名を期待値と実測値付きで拒否");

        var supported=ToolCatalog.Load(root).Single(t=>t.Id=="smart-paste");
        var file=Path.Combine(directory,"SmartPaste-0.1.5-setup.exe");File.WriteAllText(file,"実行しないfixture");
        var entry=new LocalPackage(supported.Id,supported.Name,"0.1.5",supported.AppId,Path.GetFileName(file),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant(),"unsigned-local-build");
        var catalog=Path.Combine(directory,"packages.json");File.WriteAllText(catalog,JsonSerializer.Serialize(new PackageCatalog(1,"unpublished",[entry]),ToolCatalog.Json));
        int executions=0;
        async Task<InstallResult> Result(InstallationState state)=>await new InstallerAdapter(new(root),(_,_)=>{executions++;return Task.FromResult(0);},_=>state).Install(supported,catalog);
        var missing=await Result(actual);
        check(missing.State=="要確認" && missing.Detail.Contains("tool.json がありません") && missing.Detail.Contains("対象版：0.1.5 / 検出版：不明"),
            "導入後の確認失敗で検出器の原因と期待版を消さない");
        var mismatch=await Result(new("導入済み","0.1.4",directory,file));
        check(mismatch.State=="要確認" && mismatch.Detail.Contains("実EXEの版") && mismatch.Detail.Contains("対象版：0.1.5 / 検出版：0.1.4"),
            "成功終了コードでも旧EXEが残れば対象版と検出版を区別");
        var success=await Result(new("導入済み","0.1.5",directory,file));
        check(success.State=="成功" && executions==3,"診断追加後も一致した模擬導入だけ成功");
    }
}
