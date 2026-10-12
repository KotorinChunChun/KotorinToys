using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace KotorinToys;

internal sealed record LocalPackage(string Id,string Name,string Version,string AppId,string File,string Sha256,string Signature);
internal sealed record PackageCatalog(int SchemaVersion,string DistributionStatus,LocalPackage[] Packages);
internal sealed record InstallResult(string Id,string Operation,string State,string Detail,int? ExitCode=null);
internal sealed class InstallerAdapter(ToolManager manager,Func<string,IReadOnlyList<string>,Task<int>>? execute=null)
{
    readonly SemaphoreSlim gate=new(1);
    public static PackageCatalog Load(string path)
    {
        var value=JsonSerializer.Deserialize<PackageCatalog>(File.ReadAllText(path),ToolCatalog.Json)??throw new IOException("配布台帳が空です。");
        Validate(value);
        foreach(var package in value.Packages)PathSafety.Resolve(Path.GetDirectoryName(Path.GetFullPath(path))!,package.File);
        return value;
    }
    internal static void Validate(PackageCatalog value)
    {
        if(value.SchemaVersion!=1 || value.DistributionStatus is not ("unpublished" or "published") || value.Packages==null || value.Packages.Length is <1 or >4 || value.Packages.Select(p=>p.Id).Distinct().Count()!=value.Packages.Length)
            throw new IOException("未対応または重複したローカル配布台帳です。");
        foreach(var package in value.Packages)
        {
            var expected=package.Id switch{"host"=>"KotorinToys","live-text-marker"=>"LiveTextMarker","smart-paste"=>"SmartPaste","ss-capture"=>"SSCapture",_=>throw new IOException("未登録の配布製品です。")};
            if(package.Name!=expected || package.AppId!=(package.Id=="host"?expected:"KotorinToys."+expected)
                || !System.Text.RegularExpressions.Regex.IsMatch(package.Version,"^[0-9]+\\.[0-9]+\\.[0-9]+$")
                || !System.Text.RegularExpressions.Regex.IsMatch(package.Sha256,"^[a-f0-9]{64}$") || package.Signature!=(value.DistributionStatus=="published"?"unsigned-public-build":"unsigned-local-build")
                || package.File!=$"{expected}-{package.Version}-setup.exe")throw new IOException("製品・版・ハッシュの配布定義が不正です。");
        }
    }
    public async Task<InstallResult> Install(ToolDefinition tool,string catalogPath,string? directory=null)
    {
        await gate.WaitAsync();
        try
        {
            var package=Load(catalogPath).Packages.SingleOrDefault(p=>p.Id==tool.Id)??throw new IOException("対象のローカル配布物がありません。");
            if(package.AppId!=tool.AppId || package.Name!=tool.Name)throw new IOException("登録情報と配布物が一致しません。");
            var file=PathSafety.Resolve(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!,package.File);
            // 書換・削除を許可しないハンドルを実行終了まで保持する。
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
            var hash=Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            if(hash!=package.Sha256)throw new IOException("配布物のSHA-256が一致しません。実行しません。");
            var arguments=new List<string>{"/VERYSILENT","/SUPPRESSMSGBOXES","/SP-","/NORESTART","/RESTARTEXITCODE=3010","/NOCLOSEAPPLICATIONS","/NOFORCECLOSEAPPLICATIONS","/NORESTARTAPPLICATIONS"};
            if(directory!=null)arguments.Add("/DIR="+Path.GetFullPath(directory));
            var code=await (execute??Run)(file,arguments);
            var result=Classify(tool.Id,"install",code);
            if(result.State=="成功")
            {
                var actual=manager.Installation(tool);
                if(actual.State!="導入済み" || actual.Version!=package.Version)return result with{State="要確認",Detail="導入処理は終了しましたが、登録・製品識別・実EXEの版を照合できません。"};
            }
            return result;
        }
        catch(Exception ex){return new(tool.Id,"install","失敗",ex.Message);}
        finally{gate.Release();}
    }
    public async Task<InstallResult> Uninstall(ToolDefinition tool)
    {
        await gate.WaitAsync();
        try
        {
            var installed=manager.Installation(tool);
            if(installed.State!="導入済み" || installed.Uninstaller==null)throw new IOException("正式導入と削除プログラムを確認できません。");
            var stopped=await manager.Stop(tool);
            if(stopped.State!="停止中")throw new IOException("正常停止が未確認のため削除しません："+stopped.Detail);
            var code=await (execute??Run)(installed.Uninstaller,new[]{"/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART"});
            var result=Classify(tool.Id,"uninstall",code);
            if(result.State=="成功" && manager.Installation(tool).State!="未導入")return result with{State="要確認",Detail="削除処理は終了しましたが正式登録が残っています。"};
            if(result.State=="失敗" && File.Exists(installed.Executable))
            {
                var recovery=new RemovalReservations(new(HostSettingsStore.DefaultDirectory),new TaskSchedulerGateway()).Recover(true,tool.Id);
                return result with{Detail=result.Detail+" / "+recovery.Detail};
            }
            return result;
        }
        catch(Exception ex){return new(tool.Id,"uninstall","失敗",ex.Message);}
        finally{gate.Release();}
    }
    internal static InstallResult Classify(string id,string operation,int code)=>new(id,operation,code switch{0=>"成功",3010=>"再起動待ち",_=>"失敗"},
        code switch{0=>"正式登録を再確認します。",3010=>"自動再起動はしません。",_=>"インストーラーが失敗しました。"},code);
    static async Task<int> Run(string path,IReadOnlyList<string> arguments)
    {
        var info=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(path)};
        foreach(var argument in arguments)info.ArgumentList.Add(argument);
        using var process=Process.Start(info)??throw new IOException("インストーラーを起動できません。");
        // 進行中のインストーラーは強制中断しない。
        await process.WaitForExitAsync();return process.ExitCode;
    }
}
internal sealed class InstallationQueue(InstallerAdapter adapter)
{
    public async Task<InstallResult[]> Run(IReadOnlyList<ToolDefinition> targets,string operation,string? catalogPath,
        IProgress<InstallResult>? progress=null,CancellationToken cancellation=default)
    {
        if(operation is not ("install" or "uninstall"))throw new IOException("未対応の導入操作です。");
        if(targets.Any(t=>t.Id=="host"))throw new IOException("本体はツール一括操作に含めません。");
        var results=new List<InstallResult>();
        foreach(var tool in targets.DistinctBy(t=>t.Id))
        {
            if(tool.Id=="host")throw new IOException("本体はツール一括操作に含めません。");
            InstallResult result;
            if(cancellation.IsCancellationRequested)result=new(tool.Id,operation,"未実行","残りの操作を取り消しました。進行中の処理は強制終了していません。");
            else
            {
                progress?.Report(new(tool.Id,operation,"処理中","一つずつ実行しています。"));
                result=operation=="install"?await adapter.Install(tool,catalogPath??throw new IOException("配布台帳が未指定です。")):await adapter.Uninstall(tool);
            }
            results.Add(result);progress?.Report(result);
        }
        return results.ToArray();
    }
}
