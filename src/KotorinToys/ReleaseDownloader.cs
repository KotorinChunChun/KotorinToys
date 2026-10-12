using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace KotorinToys;

internal sealed record ReleasePackage(LocalPackage Package,long Length);
internal sealed record ReleaseCatalog(int SchemaVersion,ReleasePackage[] Packages);
internal sealed class ReleaseDownloader : IDisposable
{
    const long MaxBytes=512L*1024*1024;
    readonly HttpClient client;
    public ReleaseDownloader(HttpMessageHandler? handler=null)
    {
        client=new(handler??new HttpClientHandler{AllowAutoRedirect=false});
        client.Timeout=Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KotorinToys/0.1.0");
    }
    public static ReleaseCatalog Load(string root)
    {
        var path=PathSafety.Resolve(root,"catalog/releases.json");
        if(new FileInfo(path).Length>2*1024*1024)throw new IOException("公開台帳の容量が不正です。");
        var catalog=JsonSerializer.Deserialize<ReleaseCatalog>(File.ReadAllText(path),ToolCatalog.Json)??throw new IOException("公開台帳が空です。");
        Validate(catalog);return catalog;
    }
    internal static void Validate(ReleaseCatalog catalog)
    {
        if(catalog.SchemaVersion!=1 || catalog.Packages==null)throw new IOException("公開台帳の形式が不正です。");
        InstallerAdapter.Validate(new(1,"published",catalog.Packages.Select(p=>p.Package).ToArray()));
        if(catalog.Packages.Any(p=>p.Package.Id=="host" || p.Length is <=0 or >MaxBytes))throw new IOException("公開ツールのサイズまたは対象が不正です。");
    }
    internal static Uri Asset(ReleasePackage item)=>new($"https://github.com/KotorinChunChun/{item.Package.Name}/releases/download/v{item.Package.Version}/{item.Package.File}");
    internal static bool Allowed(Uri uri)=>uri.Scheme=="https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    public async Task<string> Prepare(ReleaseCatalog catalog,IReadOnlyList<ToolDefinition> tools,string cache,
        IProgress<string>? progress=null,CancellationToken cancellation=default)
    {
        Validate(catalog);
        var selected=tools.DistinctBy(t=>t.Id).Select(tool=>
        {
            var item=catalog.Packages.SingleOrDefault(p=>p.Package.Id==tool.Id)??throw new IOException("公開配布物がありません："+tool.Name);
            if(item.Package.Name!=tool.Name || item.Package.AppId!=tool.AppId || item.Package.Version!=tool.Version)throw new IOException("登録製品・版と公開台帳が一致しません。");
            return item;
        }).ToArray();
        if(selected.Length==0)throw new IOException("取得対象がありません。");
        var directory=PathSafety.Resolve(cache,"download-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        foreach(var item in selected)
        {
            progress?.Report(item.Package.Name+"：ダウンロード中（固定版 "+item.Package.Version+"）");
            await Download(item,directory,cancellation);
            progress?.Report(item.Package.Name+"：SHA-256・サイズ一致。未署名です。");
        }
        var path=PathSafety.Resolve(directory,"packages.json");
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new PackageCatalog(1,"published",selected.Select(p=>p.Package).ToArray()),ToolCatalog.Json),cancellation);
        return path;
    }
    async Task Download(ReleasePackage item,string directory,CancellationToken cancellation)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token=deadline.Token;var uri=Asset(item);HttpResponseMessage? response=null;
        var partial=PathSafety.Resolve(directory,item.Package.File+".partial");
        try
        {
            for(int hops=0;hops<=4;hops++)
            {
                if(!Allowed(uri))throw new IOException("許可外の配布先へ移動しません。");
                for(int attempt=0;;attempt++)
                {
                    response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
                    if((int)response.StatusCode is >=500 and <=599 && attempt<2)
                    {response.Dispose();response=null;await Task.Delay(TimeSpan.FromSeconds(attempt+1),token);continue;}
                    break;
                }
                if((int)response.StatusCode is >=300 and <400)
                {
                    var location=response.Headers.Location??throw new IOException("リダイレクト先がありません。");
                    uri=new Uri(uri,location);response.Dispose();response=null;continue;
                }
                response.EnsureSuccessStatusCode();break;
            }
            if(response==null)throw new IOException("リダイレクト回数が上限を超えました。");
            if(response.Content.Headers.ContentLength is long length && length!=item.Length)throw new IOException("配布物のサイズが公開台帳と一致しません。");
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using(var input=await response.Content.ReadAsStreamAsync(token))
            await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
            {
                var buffer=new byte[81920];long total=0;int read;
                while((read=await input.ReadAsync(buffer,token))>0)
                {
                    total+=read;if(total>item.Length || total>MaxBytes)throw new IOException("配布物の容量上限を超えました。");
                    hash.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read),token);
                }
                if(total!=item.Length || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()!=item.Package.Sha256)throw new IOException("配布物のSHA-256またはサイズが一致しません。実行しません。");
                await output.FlushAsync(token);output.Flush(true);
            }
            token.ThrowIfCancellationRequested();File.Move(partial,PathSafety.Resolve(directory,item.Package.File));
        }
        finally {response?.Dispose();if(File.Exists(partial))File.Delete(partial);}
    }
    public void Dispose()=>client.Dispose();
}
