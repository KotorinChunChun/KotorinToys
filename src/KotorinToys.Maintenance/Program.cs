using System.Diagnostics;
using System.Text.Json;
using KotorinToys;

static string Argument(string[] args,string name)
{
    var index=Array.IndexOf(args,name);
    if(index<0 || index+1>=args.Length)throw new IOException("必須引数がありません："+name);
    return args[index+1];
}
try
{
    var product=Argument(args,"--product");
    var id=product switch{"KotorinToys"=>"host","LiveTextMarker"=>"live-text-marker","SmartPaste"=>"smart-paste","SSCapture"=>"ss-capture",_=>throw new IOException("未知の製品です。")};
    var root=Path.GetFullPath(Argument(args,"--root"));
    var operation=Argument(args,"--operation");
    if(operation is not ("update" or "remove" or "recover"))throw new IOException("未知の操作です。");
    var appId=product=="KotorinToys"?product:"KotorinToys."+product;
    var identity=new ToolDefinition(id,product,product+".exe","README.md",AppId:appId,InstalledExecutable:product+".exe");
    var detector=new InstallationDetector();
    var installed=operation=="update"?detector.UpdateTarget(identity,root):detector.Detect(identity);
    if(installed.State!="導入済み" || installed.Directory==null || !string.Equals(Path.TrimEndingDirectorySeparator(installed.Directory),Path.TrimEndingDirectorySeparator(root),StringComparison.OrdinalIgnoreCase))throw new IOException("正式導入の製品識別・配置を照合できません。");
    var store=new HostSettingsStore(HostSettingsStore.DefaultDirectory);
    var reservations=new RemovalReservations(store,new TaskSchedulerGateway());
    if(operation=="recover")
    {
        var recovered=reservations.Recover(true,id);Console.WriteLine(recovered.Detail);
        return recovered.Success?0:1;
    }
    // 本体のIPC IDだけは製品識別のhostと異なる。
    var managed=identity with{Id=id=="host"?"dashboard":id,AppId=""};
    var manager=new ToolManager(root);
    var stopped=await manager.Stop(managed);
    if(stopped.State!="停止中")throw new IOException("正常終了を確認できません："+stopped.State+" / "+stopped.Detail);
    if(operation=="remove")
    {
        var removed=reservations.Remove(id);Console.WriteLine(removed.Detail);
        if(!removed.Success)return 1;
    }
    Console.WriteLine("正常停止・対象照合完了。利用者データは保持します。");return 0;
}
catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
