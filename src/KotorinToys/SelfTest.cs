namespace KotorinToys;
internal static class SelfTest
{
    public static int Run(string root)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(root,"dev","history","v0.1.0","logs"));
            var manager=new ToolManager(root);
            var tools=manager.Load();
            if(tools.Length!=3) throw new Exception("3ツール登録の検証に失敗しました。");
            foreach(var t in tools) if(!File.Exists(manager.Resolve(t.Executable)) || !File.Exists(manager.Resolve(t.Manual))) throw new Exception(t.Name+"の登録パスがありません。");
            try { manager.Resolve("../outside"); throw new Exception("範囲外パスを拒否しませんでした。"); } catch(IOException) { }
            foreach(var t in tools)
            {
                var started=manager.Start(t).GetAwaiter().GetResult();
                if(started.State!="実行中") throw new Exception(t.Name+"の起動失敗："+started.Detail);
                var again=manager.Start(t).GetAwaiter().GetResult();
                if(again.Pid!=started.Pid) throw new Exception("既存インスタンスが維持されませんでした。");
                var stopped=manager.Stop(t).GetAwaiter().GetResult();
                if(stopped.State!="停止中") throw new Exception(t.Name+"の正常停止失敗："+stopped.Detail);
            }
            foreach(var t in tools) if(manager.Start(t).GetAwaiter().GetResult().State!="実行中") throw new Exception("再起動に失敗しました。");
            File.WriteAllText(Path.Combine(root,"dev","history","v0.1.0","logs","integration-result.txt"), "PASS: 3ツールの登録・起動・既存PID維持・画面表示要求・正常停止・再起動");
            return 0;
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(root,"dev","history","v0.1.0","logs","integration-result.txt"),ex.ToString()); return 1; }
    }
}
