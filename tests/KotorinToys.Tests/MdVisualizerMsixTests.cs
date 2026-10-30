using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using KotorinToys;

internal static class MdVisualizerMsixTests
{
    static ToolDefinition Tool => new("md-visualizer", "MdVisualizer", "mdvisualizer.exe", "README.md", Version:"0.0.19.3",
        InstalledExecutable:"mdvisualizer.exe", AppId:"KotorinToys.MdVisualizer", RepositoryUrl:"https://github.com/KotorinChunChun/MdVisualizer",
        Msix:new(MsixPolicy.MdFamily,MsixPolicy.Publisher,"MdVisualizer"), Capabilities:["status","show","stop","start"], DistributionStatus:"published");
    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(respond(request)); }
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static async Task Run(string root,string fixtures,Action<bool,string> check)
    {
        var tool=Tool; var local=ToolCatalog.Load(root).Single(t=>t.Id==tool.Id);
        check(MsixPolicy.Supports(local)&&Version.TryParse(local.Version,out var version)&&version.Revision>=0&&local.Capabilities!.Contains("start")&&!local.Capabilities.Contains("silent-start"),"MdVisualizerは固定MSIX・4部版番号・非常駐の通常起動能力で登録");
        check(MsixPolicy.Supports(MsixReleaseTests.Tool),"HttpSelectorの固定MSIX経路を保持");
        foreach(var bad in new[]{tool with{Msix=new("Other_family",MsixPolicy.Publisher)},tool with{Msix=new(MsixPolicy.MdFamily,"CN=Other")},tool with{InstalledExecutable="other.exe"},tool with{RepositoryUrl="https://github.com/KotorinChunChun/Other"}})
            check(!MsixPolicy.Supports(bad),"MdVisualizerの別Identity・署名者・EXE・配布先を拒否");
        check(MsixInstallation.ActivationArguments("host_manual",false)=="--launch-origin host_manual"&&MsixInstallation.ActivationArguments("host_manual",true)=="--startup --launch-origin host_manual","MSIX通常起動と無表示起動の引数を分離");
        foreach(var origin in new[]{"via_kotorintoys","direct_task"}){
            bool denied=false;try{MsixInstallation.ActivationArguments(origin,false);}catch(IOException){denied=true;}check(denied,"非常駐MSIXの自動起動を実行入口で拒否");
        }
        var directory=Path.Combine(fixtures,"mdvisualizer-msix");Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,MsixPolicy.FileName(tool.Version,tool.Id));
        XNamespace ns="http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var manifest=new XDocument(new XElement(ns+"Package",new XElement(ns+"Identity",new XAttribute("Name",MsixPolicy.MdName),new XAttribute("Publisher",MsixPolicy.Publisher),new XAttribute("Version",tool.Version),new XAttribute("ProcessorArchitecture","x64")),new XElement(ns+"Applications",new XElement(ns+"Application",new XAttribute("Id","MdVisualizer"),new XAttribute("Executable","mdvisualizer.exe"),new XAttribute("EntryPoint","Windows.FullTrustApplication")))));
        var entries=new Dictionary<string,byte[]>{["AppxManifest.xml"]=Encoding.UTF8.GetBytes(manifest.ToString()),["Assets/128x128@2x.png"]=[1,2,3], ["mdvisualizer.exe"]=Encoding.UTF8.GetBytes("無害な試験用ペイロード")};
        XNamespace bm="http://schemas.microsoft.com/appx/2010/blockmap";
        var map=new XElement(bm+"BlockMap",new XAttribute("HashMethod","http://www.w3.org/2001/04/xmlenc#sha256"));
        foreach(var entry in entries)map.Add(new XElement(bm+"File",new XAttribute("Name",entry.Key),new XAttribute("Size",entry.Value.Length),new XElement(bm+"Block",new XAttribute("Hash",Convert.ToBase64String(SHA256.HashData(entry.Value))))));
        entries["AppxBlockMap.xml"]=Encoding.UTF8.GetBytes(map.ToString());entries["AppxSignature.p7x"]=[1];
        using(var zip=ZipFile.Open(file,ZipArchiveMode.Create))foreach(var entry in entries){using var output=zip.CreateEntry(entry.Key.Replace("@","%40")).Open();output.Write(entry.Value);}
        var hash=Hash(File.ReadAllBytes(file));var package=new LocalPackage(tool.Id,tool.Name,tool.Version,tool.AppId,Path.GetFileName(file),hash,"signed-msix","msix");
        using(var locked=await MsixPackageVerifier.Open(tool,package,file,_=>new(MsixPolicy.Publisher,MsixPolicy.Thumbprint)))check(locked.Length>0,"MdVisualizerのManifest・EXE・署名方針・BlockMapを照合");
        bool rejected=false;try{using var invalid=await MsixPackageVerifier.Open(MsixReleaseTests.Tool,package,file,_=>new(MsixPolicy.Publisher,MsixPolicy.Thumbprint));}catch(IOException){rejected=true;}check(rejected,"MdVisualizer配布物を別製品として導入しない");
        string Url(string name)=>$"https://github.com/KotorinChunChun/MdVisualizer/releases/download/v{tool.Version}/{name}";
        var releases=new[]{new{tag_name="v"+tool.Version,draft=false,prerelease=true,published_at="2026-10-28T00:00:00Z",assets=new[]{new{name=package.File,state="uploaded",size=new FileInfo(file).Length,browser_download_url=Url(package.File),digest="sha256:"+hash},new{name="SHA256SUMS.txt",state="uploaded",size=200L,browser_download_url=Url("SHA256SUMS.txt"),digest=""}}}};
        using(var client=new PublicReleaseClient(new Handler(request=>new(HttpStatusCode.OK){Content=new StringContent(ReleaseIndexFixture.IsIndex(request)?ReleaseIndexFixture.Json("MdVisualizer",releases):hash+" *"+package.File+"\n")}))){
            var published=await client.Check(tool,null);check(published.Confirmed&&published.Package?.Package==package,"MdVisualizer公開情報一覧・4部タグ・MSIXハッシュを照合");
        }
        var packages=Path.Combine(directory,"packages");var backup=Path.Combine(directory,"backup");
        var state=Path.Combine(packages,MsixPolicy.MdFamily,"LocalState");Directory.CreateDirectory(state);File.WriteAllText(Path.Combine(state,"settings.json"),"Md専用設定");
        var cache=Path.Combine(packages,MsixPolicy.MdFamily,"LocalCache","Roaming","com.mdvisualizer.viewer");Directory.CreateDirectory(cache);File.WriteAllText(Path.Combine(cache,"settings.json"),"Tauri専用設定");
        var store=new MsixDataStore(packages,backup,MsixPolicy.MdFamily);using(var lease=store.AcquireLease()){
            var snapshot=store.Capture();check(store.Matches(tool)&&!store.Matches(MsixReleaseTests.Tool)&&snapshot.Family==MsixPolicy.MdFamily&&snapshot.Files.Length==2,"製品別にMSIX設定とTauriの仮想Roamingを退避しHttpSelectorと分離");
            store.Retain(snapshot);store.VerifyPreserved(snapshot);
            var other=new MsixDataStore(packages,Path.Combine(directory,"other-backup"));bool mismatch=false;try{other.VerifyPreserved(snapshot);}catch(IOException){mismatch=true;}check(mismatch,"別Familyへのデータ復元を拒否");
        }
    }
    public static async Task<int> VerifyActual(string file)
    {
        var tool=ToolCatalog.Load(KotorinToys.Program.FindRoot()).Single(t=>t.Id=="md-visualizer");var hash=Hash(await File.ReadAllBytesAsync(file));var package=new LocalPackage(tool.Id,tool.Name,tool.Version,tool.AppId,Path.GetFileName(file),hash,"signed-msix","msix");
        using var locked=await MsixPackageVerifier.Open(tool,package,file);
        Console.WriteLine($"PASS: 実MSIX署名・固定Identity・BlockMap・SHA-256 {hash} ({locked.Length} bytes)");return 0;
    }
}
