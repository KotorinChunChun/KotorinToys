using KotorinToys;

internal static class ReleaseReadinessTests
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var tools=OnlineToolCatalog.Parse(File.ReadAllBytes(Path.Combine(root,OnlineToolCatalog.RelativePath)),root);
        var catalog=ReleaseDownloader.Load(root,tools);
        var report=await ReleaseReadiness.Check(root,tools,catalog);
        check(report.Passed && !report.Online && !report.ReadyToPublish,"静的検査を実公開の照合成功と混同しない");
        check(report.Items.Any(i=>i.Level=="案内のみ") && !report.Items.Any(i=>i.Id=="shortcut-key-counter" && i.Level=="公開照合済み"),"未公開・ZIP候補を導入可能として数えない");
        Task<PublicRelease> Good(ToolDefinition t,ReleasePackage? fallback)=>Task.FromResult(new PublicRelease(fallback,true,true,"fixture"));
        report=await ReleaseReadiness.Check(root,tools,catalog,tools,Good);
        check(!(await ReleaseReadiness.Check(root,tools,catalog,online:Good)).ReadyToPublish,"公開configを省略したオンライン検査を公開準備完了にしない");
        check(report.ReadyToPublish && report.Items.Count(i=>i.Level=="公開照合済み")==10,"公開configと全対応製品の配布物が一致した場合だけ公開準備完了");
        report=await ReleaseReadiness.Check(root,tools,catalog,tools.Skip(1).ToArray(),Good);
        check(!report.ReadyToPublish && report.Items.Any(i=>i.Level=="エラー" && i.Detail.Contains("公開configに登録")),"公開configへ未反映の候補を公開前に検出");
        var altered=tools.ToArray();altered[0]=altered[0] with{DistributionStatus="unpublished"};
        report=await ReleaseReadiness.Check(root,tools,catalog,altered,Good);
        check(!report.Passed && report.Items.Any(i=>i.Detail.Contains("配布方式")),"公開configの製品・配布方式差異を検出");
        var invalid=catalog.Packages.ToArray();invalid[0]=invalid[0] with{Length=0};
        check(!(await ReleaseReadiness.Check(root,tools,new(1,invalid))).Passed,"容量不正を公開前に拒否");
        invalid=catalog.Packages.ToArray();invalid[0]=invalid[0] with{Package=invalid[0].Package with{Sha256="invalid"}};
        check(!(await ReleaseReadiness.Check(root,tools,new(1,invalid))).Passed,"SHA-256形式不正を公開前に拒否");
        invalid=catalog.Packages.ToArray();invalid[0]=invalid[0] with{Package=invalid[0].Package with{File="Wrong-9.0.0-setup.exe"}};
        check(!(await ReleaseReadiness.Check(root,tools,new(1,invalid))).Passed,"製品版とファイル名の不一致を公開前に拒否");
        check(!(await ReleaseReadiness.Check(root,tools,new(1,[catalog.Packages[0],catalog.Packages[0]]))).Passed,"重複した配布IDを公開前に拒否");
        altered=tools.ToArray();altered[0]=altered[0] with{RepositoryUrl="https://github.com/OtherOwner/LiveTextMarker"};
        check(!(await ReleaseReadiness.Check(root,altered,catalog)).Passed,"別所有者への定義差替を公開前に拒否");
        report=await ReleaseReadiness.Check(root,tools,catalog,tools,(t,f)=>Task.FromResult(new PublicRelease(f,false,false,"通信失敗",Problem:ReleaseProblem.Connection)));
        check(!report.ReadyToPublish && report.Items.Count(i=>i.Level=="エラー")==10,"通信失敗や未照合を全件成功にしない");
        report=await ReleaseReadiness.Check(root,tools,catalog,tools,(t,f)=>Task.FromResult(new PublicRelease(catalog.Packages[0],true,true,"別製品fixture")));
        check(!report.Passed && report.Items.Any(i=>i.Detail.Contains("製品")),"公開確認が返した別製品も再照合して拒否");
        var selected=tools.Where(t=>t.Id=="smart-paste").ToArray();
        report=await ReleaseReadiness.Check(root,selected,new(1,[]),selected,(t,_)=>Good(t,catalog.Packages.Single(p=>p.Package.Id==t.Id)));
        check(report.ReadyToPublish,"新規標準製品に同梱fallback台帳の追加を強制せず定義とReleaseだけで確認可能");
        var publish=File.ReadAllText(Path.Combine(root,"dev/scripts/publish-releases.ps1"));
        check(publish.IndexOf("test-release-readiness.ps1",StringComparison.Ordinal)<publish.IndexOf("release create",StringComparison.Ordinal),"本体公開スクリプトはRelease作成前にオンライン整合検査を必須化");
    }
}
