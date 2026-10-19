namespace KotorinToys;

internal sealed record ReadinessItem(string Id,string Name,string Level,string Detail);
internal sealed record ReadinessReport(bool Online,ReadinessItem[] Items,bool PublicConfigChecked=false)
{
    public bool Passed => Items.All(item=>item.Level!="エラー");
    public bool ReadyToPublish => Online && PublicConfigChecked && Passed;
}

// 表示用キャッシュの版を公開版とはみなさず、実行時と同じ検証器を公開前にも使う。
internal static class ReleaseReadiness
{
    public static async Task<ReadinessReport> Check(string root,ToolDefinition[] tools,ReleaseCatalog catalog,
        ToolDefinition[]? publishedTools=null,Func<ToolDefinition,ReleasePackage?,Task<PublicRelease>>? online=null)
    {
        var items=new List<ReadinessItem>();
        void Add(ToolDefinition? tool,string level,string detail)=>items.Add(new(tool?.Id??"catalog",tool?.Name??"一覧・台帳",level,detail));
        try
        {
            OnlineToolCatalog.Validate(tools,root,OnlineToolCatalog.MaximumTools);
            if(catalog.SchemaVersion!=1 || catalog.Packages==null)throw new IOException("公開台帳の形式が不正です。");
            if(catalog.Packages.Length>0)ReleaseDownloader.Validate(catalog,tools);
            if(publishedTools!=null)OnlineToolCatalog.Validate(publishedTools,root,OnlineToolCatalog.MaximumTools);
        }
        catch(Exception error) when(error is IOException or ArgumentException or InvalidOperationException or NullReferenceException)
        { Add(null,"エラー",error.Message);return new(online!=null,items.ToArray(),publishedTools!=null); }
        foreach(var published in publishedTools??[])
            if(!tools.Any(tool=>tool.Id==published.Id))Add(published,"エラー","公開configだけに存在する候補があります。採用定義との登録差異を確認してください。");
        foreach(var tool in tools)
        {
            if(!Version.TryParse(tool.Version,out var declared) || declared.Build<0)
            { Add(tool,"エラー","定義の製品版が不正です。");continue; }
            if(publishedTools!=null)
            {
                var published=publishedTools.SingleOrDefault(t=>t.Id==tool.Id);
                if(published==null)Add(tool,"エラー","公開configに登録がありません。ローカル候補と公開候補が一致しません。");
                else if(published.Name!=tool.Name || published.AppId!=tool.AppId || published.Msix!=tool.Msix ||
                    ProductIdentity.FromTool(published)?.Repository!=ProductIdentity.FromTool(tool)?.Repository ||
                    published.DistributionStatus!=tool.DistributionStatus)
                    Add(tool,"エラー","公開configの製品・配布先・配布方式が採用定義と一致しません。");
            }
            if(tool.Msix==null && tool.DistributionStatus!="published")
            { Add(tool,"案内のみ","未公開／ZIP候補。本体から導入できる製品としては数えません。");continue; }
            var fallback=catalog.Packages.SingleOrDefault(p=>p.Package.Id==tool.Id);
            if(ToolInstallationRunner.UnsupportedReason(tool) is { } unsupported)
            { Add(tool,"エラー",unsupported);continue; }
            if(fallback!=null && (fallback.Package.Name!=tool.Name || fallback.Package.AppId!=tool.AppId ||
                tool.Msix!=null && fallback.Package.Kind!="msix" || tool.Msix==null && fallback.Package.Kind!="inno"))
            { Add(tool,"エラー","定義と公開台帳の製品・配布方式が一致しません。");continue; }
            if(online==null)
            { Add(tool,fallback==null?"未照合":"静的確認",fallback==null?"同梱台帳なし。公開前にオンライン照合が必要です。":"製品・版・ファイル名・容量・SHA-256の形式を確認。実公開は未照合です。");continue; }
            try
            {
                var release=await online(tool,fallback);
                if(!release.Confirmed || release.Package==null || release.Portable!=null)throw new IOException("照合済みインストーラーがありません："+release.Detail);
                var item=release.Package;
                ReleaseDownloader.Validate(new(1,[item]),tools);
                if(item.Package.Id!=tool.Id || item.Package.Name!=tool.Name || item.Package.AppId!=tool.AppId ||
                    tool.Msix!=null && item.Package.Kind!="msix" || tool.Msix==null && item.Package.Kind!="inno")
                    throw new IOException("公開台帳の製品・配布方式が定義と一致しません。");
                if(tool.Msix!=null && Version.Parse(item.Package.Version)<Version.Parse(tool.Version))throw new IOException("公開MSIXが定義の必要版より古い状態です。");
                Add(tool,"公開照合済み",item.Package.Version+"：製品・tag・台帳・容量・SHA-256をGitHub情報と照合しました。");
            }
            catch(Exception error) when(error is IOException or HttpRequestException or OperationCanceledException or ArgumentException or InvalidOperationException or FormatException)
            { Add(tool,"エラー",error.Message); }
        }
        return new(online!=null,items.ToArray(),publishedTools!=null);
    }
}
