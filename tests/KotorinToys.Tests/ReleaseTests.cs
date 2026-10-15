using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using KotorinToys;

internal static class ReleaseTests
{
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>action(request,token);}
    public static async Task Run(string root,string fixtures,Action<bool,string> check)
    {
        var tool=ToolCatalog.Load(root).Single(t=>t.Id=="smart-paste");
        var bytes="実行しない公開取得テスト"u8.ToArray();
        var package=new LocalPackage(tool.Id,tool.Name,tool.Version,tool.AppId,$"{tool.Name}-{tool.Version}-setup.exe",Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),"unsigned-public-build");
        var item=new ReleasePackage(package,bytes.Length);var catalog=new ReleaseCatalog(1,[item]);
        var cache=Path.Combine(fixtures,"release-cache");Directory.CreateDirectory(cache);
        var pinned=ReleaseDownloader.Load(root);
        check(ToolCatalog.Load(root).Where(t=>t.DistributionStatus=="published").All(t=>pinned.Packages.Any(p=>p.Package.Id==t.Id && Version.Parse(p.Package.Version)<=Version.Parse(t.Version) && p.Package.AppId==t.AppId)),"公開固定台帳の製品識別と版を照合し、開発ソースの先行を許容");
        check(!pinned.Packages.Any(p=>p.Package.Id=="shortcut-key-counter"),"ポータブルZIPへ架空のInno公開情報を追加しない");
        HttpResponseMessage Ok(byte[] data)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(data)};
        using(var downloader=new ReleaseDownloader(new Handler((request,_)=>
        {
            check(request.RequestUri==ReleaseDownloader.Asset(item),"公開取得は固定所有者・製品・版・ファイル名を使用");
            return Task.FromResult(Ok(bytes));
        })))
        {
            var path=await downloader.Prepare(catalog,[tool],cache);
            check(InstallerAdapter.Load(path).DistributionStatus=="published","公開取得後の台帳は既存の導入アダプターで検証可能");
            check(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path)!,package.File)).SequenceEqual(bytes),"公開取得は検証済みのファイルだけ確定保存");
            check(!Directory.GetFiles(cache,"*.partial",SearchOption.AllDirectories).Any(),"正常取得で一時ファイルを残さない");
        }
        async Task Reject(ReleaseCatalog value,Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action,string message,CancellationToken token=default,ToolDefinition? definition=null)
        {
            using var downloader=new ReleaseDownloader(new Handler(action));bool rejected=false;
            try{await downloader.Prepare(value,[definition??tool],cache,cancellation:token);}
            catch(Exception ex) when(ex is IOException or HttpRequestException or OperationCanceledException){rejected=true;}
            check(rejected,message);
            check(!Directory.GetFiles(cache,"*.partial",SearchOption.AllDirectories).Any(),message+"：未検証の一時ファイルなし");
        }
        await Reject(catalog with{Packages=[item with{Package=package with{Sha256=new string('0',64)}}]},(_,_)=>Task.FromResult(Ok(bytes)),"公開取得の改ざんを拒否");
        await Reject(catalog,(_,_)=>Task.FromResult(Ok([1])),"公開取得の容量不一致を拒否");
        await Reject(catalog,(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=new("https://example.com/evil.exe")}}),"外部ホストへのリダイレクトを拒否");
        await Reject(catalog,(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),"未公開または欠落assetを成功にしない");
        await Reject(catalog,(_,_)=>throw new HttpRequestException("テストのオフライン"),"オフラインでは導入用台帳を生成しない");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();await Reject(catalog,(_,token)=>{token.ThrowIfCancellationRequested();return Task.FromResult(Ok(bytes));},"公開取得キャンセルで実行用ファイルを残さない",cancellation.Token);
        }
        await Reject(catalog,(_,_)=>throw new Exception("取得してはいけない"),"公開版と登録版の不一致は取得前に拒否",definition:tool with{Version="9.9.9"});
        int hops=0;
        using(var downloader=new ReleaseDownloader(new Handler((_,_)=>Task.FromResult(++hops==1
            ?new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=new("https://release-assets.githubusercontent.com/test")}}:Ok(bytes)))))
            check(File.Exists(await downloader.Prepare(catalog,[tool],cache)),"GitHubの許可されたassetホストへの移動は検証付きで許可");
        check(!ReleaseDownloader.Allowed(new("http://github.com/test")) && !ReleaseDownloader.Allowed(new("https://github.com:8443/test")) && !ReleaseDownloader.Allowed(new("https://user@github.com/test")),"非HTTPS・特殊ポート・ユーザー情報付き配布先を拒否");
        int attempts=0;
        using(var downloader=new ReleaseDownloader(new Handler((_,_)=>Task.FromResult(++attempts<3?new HttpResponseMessage(HttpStatusCode.InternalServerError):Ok(bytes)))))
            check(File.Exists(await downloader.Prepare(catalog,[tool],cache)) && attempts==3,"一時的な5xxは回数制限付きで再取得しハッシュを検証");
        await Reject(catalog,(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),"継続する5xxは上限後に失敗し無限再試行しない");
        await Reject(catalog,(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)),"リダイレクト先欠落を拒否");
        int redirectCount=0;
        await Reject(catalog,(_,_)=>{redirectCount++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=new("https://github.com/loop")}});},"リダイレクト連鎖を上限後に拒否");
        check(redirectCount==5,"リダイレクトのHTTP要求は最大5回");
    }
}
