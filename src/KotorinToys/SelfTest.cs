namespace KotorinToys;
internal static class SelfTest
{
    public static int Run(string root)
    {
        var logDirectory = Path.Combine(root,"dev","history","v0.1.0","logs","integration-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDirectory);
        var manager=new ToolManager(root);
        var owned=new List<ToolDefinition>();
        var results=new List<string>();
        var exitCode=0;
        try
        {
            var tools=manager.Load();
            if(tools.Length!=9) throw new Exception("9ツール登録の検証に失敗しました。");
            foreach(var t in tools) if(!File.Exists(manager.Executable(t)) || !File.Exists(PathSafety.Resolve(manager.DocumentRoot(t),t.Manual))) throw new Exception(t.Name+"の登録パスがありません。");
            try { manager.Resolve("../outside"); throw new Exception("範囲外パスを拒否しませんでした。"); } catch(IOException) { }
            foreach(var t in tools)
            {
                var before=manager.Status(t).GetAwaiter().GetResult();
                if(before.State!="停止中") { results.Add(t.Id+": SKIP 既存インスタンスは変更しない（"+before.State+"）"); continue; }
                owned.Add(t);
                var started=manager.Start(t,false).GetAwaiter().GetResult();
                if(started.State!="実行中") throw new Exception(t.Name+"の起動失敗："+started.Detail);
                var again=manager.Start(t,false).GetAwaiter().GetResult();
                if(again.Pid!=started.Pid) throw new Exception("既存インスタンスが維持されませんでした。");
                var stopped=manager.Stop(t).GetAwaiter().GetResult();
                if(stopped.State!="停止中") throw new Exception(t.Name+"の正常停止失敗："+stopped.Detail);
                if(manager.Start(t,false).GetAwaiter().GetResult().State!="実行中") throw new Exception(t.Name+"の再起動に失敗しました。");
                results.Add(t.Id+": PASS 登録・無表示起動・既存PID維持・正常停止・再起動");
            }
        }
        catch(Exception ex) { results.Add("FAIL: "+ex); exitCode=1; }
        finally
        {
            foreach(var tool in owned)
                try
                {
                    var restored=manager.Stop(tool).GetAwaiter().GetResult();
                    if(restored.State!="停止中") { results.Add(tool.Id+": 復元未確認: "+restored.State); exitCode=1; }
                }
                catch(Exception ex) { results.Add(tool.Id+": 復元失敗: "+ex.Message); exitCode=1; }
            File.WriteAllLines(Path.Combine(logDirectory,"result.txt"), results);
        }
        return exitCode;
    }
}
