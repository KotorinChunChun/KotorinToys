using KotorinToys;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class InstallerDiagnosticsTests
{
    public static async Task Run(string root, string fixtures, Action<bool,string> check)
    {
        var folder = Path.Combine(fixtures, "diagnostic-tests");
        Directory.CreateDirectory(folder);
        var tool = ToolCatalog.Load(root).Single(t => t.Id == "smart-paste");
        var file = Path.Combine(folder, "SmartPaste-0.1.5-setup.exe");
        File.WriteAllText(file, "実行しないfixture");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        var catalog = Path.Combine(folder, "packages.json");
        File.WriteAllText(catalog, JsonSerializer.Serialize(new PackageCatalog(1,"unpublished",
            [new(tool.Id,tool.Name,"0.1.5",tool.AppId,Path.GetFileName(file),hash,"unsigned-local-build")]), ToolCatalog.Json));
        var logs = Path.Combine(folder,"logs");
        int calls = 0;
        var adapter = new InstallerAdapter(new(root), (_, args) =>
        {
            calls++;
            var path = args.Single(a => a.StartsWith("/LOG="))[5..];
            File.WriteAllText(path,"Error: 導入先の照合を確認できません。",Encoding.Unicode);
            return Task.FromResult(7);
        }, diagnosticDirectory: logs);
        var result = await adapter.Install(tool,catalog);
        check(result.State=="失敗" && result.ExitCode==7 && result.Detail.Contains("終了コード：7") && result.Detail.Contains("導入先の照合"),"異常終了のコード・Inno UTF16ログの理由を表示");
        check(Directory.GetFiles(logs,"operation.txt",SearchOption.AllDirectories).Length==1 && File.ReadAllText(Directory.GetFiles(logs,"operation.txt",SearchOption.AllDirectories)[0]).Contains("終了コード：7"),"結果・終了コードをPC内へ永続保存");
        await adapter.Install(tool,catalog);
        check(calls==2 && Directory.GetDirectories(logs).Length==2,"再試行はログを上書きせず別操作として保存");
        File.WriteAllText(file,"改変");
        result = await adapter.Install(tool,catalog);
        check(calls==2 && result.ExitCode==null && result.Detail.Contains("未取得") && result.Detail.Contains("SHA-256"),"ハッシュ不一致は開始前として記録し実行しない");
        File.WriteAllText(file,"実行しないfixture");
        var throwing = new InstallerAdapter(new(root), (_,_)=>throw new IOException("起動拒否"), diagnosticDirectory:logs);
        result = await throwing.Install(tool,catalog);
        check(result.ExitCode==null && result.Detail.Contains("起動拒否") && result.Detail.Contains("未取得"),"開始例外は終了コードを捏造しない");
        string blocked = Path.Combine(folder,"blocked"); File.WriteAllText(blocked,"fixture");
        var blockedAdapter = new InstallerAdapter(new(root),(_,_)=>{calls++;return Task.FromResult(0);},diagnosticDirectory:blocked);
        result = await blockedAdapter.Install(tool,catalog);
        check(result.State=="失敗" && calls==2,"ログ作成不能ならインストーラーを開始しない");
        var expected = Path.Combine(folder,"正式 導入先");
        var maintenance = Path.Combine(folder,"maintenance");
        var diagnostic = new InstallerDiagnostics(logs,"ss-capture","install",expected,maintenance);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expected.ToUpperInvariant())));
        var errorFolder = Path.Combine(maintenance,key); Directory.CreateDirectory(errorFolder);
        var error = Path.Combine(errorFolder,"update-error.json");
        void Error(string target,DateTimeOffset utc) => File.WriteAllText(error,JsonSerializer.Serialize(new {root=target,operation="update",utc,message="正式登録と実EXEの版が一致しません。"}));
        Error(expected,DateTimeOffset.UtcNow.AddDays(-1));
        check(!diagnostic.Complete(new("ss-capture","install","失敗","準備中止",7)).Detail.Contains("保守補助："),"過去のSSCaptureエラーを今回の理由にしない");
        Error(folder,DateTimeOffset.UtcNow);
        check(!diagnostic.Complete(new("ss-capture","install","失敗","準備中止",7)).Detail.Contains("保守補助："),"別導入先の診断を取り込まない");
        Error(expected,DateTimeOffset.UtcNow);
        check(diagnostic.Complete(new("ss-capture","install","失敗","準備中止",7)).Detail.Contains("正式登録と実EXE"),"同じ導入先・今回の時刻の補助診断を取り込む");
        File.WriteAllText(error,"{");
        check(diagnostic.Complete(new("ss-capture","install","失敗","準備中止",7)).Detail.Contains("読取に失敗"),"壊れた診断JSONでも終了コードを失わず結果を返す");
        File.WriteAllText(diagnostic.NativeLog,new string('x',100000)+"\nError: 最後の失敗",Encoding.Unicode);
        check(InstallerDiagnostics.Tail(diagnostic.NativeLog).Length<=32768 && InstallerDiagnostics.Tail(diagnostic.NativeLog).Contains("最後の失敗"),"大きなInnoログの読込を末尾64KiBに制限");
        var transcript=Path.Combine(diagnostic.DirectoryPath,"operation.txt"); File.Delete(transcript);Directory.CreateDirectory(transcript);
        result=diagnostic.Complete(new("ss-capture","install","失敗","準備中止",7));
        check(result.ExitCode==7 && result.State=="失敗" && result.Detail.Contains("保存できません"),"結果保存不能でも終了コードと失敗を保持し再実行しない");
        foreach (int code in new[]{1,2,3,4,5,6,7,8,10,12345})
            check(InstallerAdapter.Classify(tool.Id,"install",code).State=="失敗","未知を含む非0コードを成功にしない："+code);
        var previous = SynchronizationContext.Current;
        try
        {
            using var form = new InstallationForm(new(root)); form.CreateControl();
            var button = form.Controls.Find("saveInstallationLog",true).Single();
            check(!button.Enabled,"空の詳細ログは保存ボタンを無効化");
            form.ShowSummary(InstallationSummary.Create([tool],"install",[new(tool.Id,"install","失敗","終了コード：7 / 原因：対象不一致")]));
            var exported = Path.Combine(folder,"画面 詳細ログ.txt");form.SaveLog(exported);
            check(button.Enabled && File.ReadAllText(exported).Contains("終了コード：7 / 原因：対象不一致"),"実フォームの詳細ログをUTF8テキストへ保存");
            var setRunning=typeof(InstallationForm).GetMethod("SetRunning",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
            setRunning.Invoke(form,[true]);bool denied=false;
            try { form.SaveLog(exported); } catch(IOException) { denied=true; }
            check(!button.Enabled && denied,"処理中は保存と直接呼出しの双方を拒否");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
