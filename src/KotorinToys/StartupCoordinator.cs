using System.Security.Principal;

namespace KotorinToys;

internal sealed class StartupCoordinator(HostSettingsStore store,IStartupGateway gateway,ToolManager manager,
    string hostExecutable,string taskPrefix="KotorinToys",bool allowDevelopment=false)
{
    readonly string sid=WindowsIdentity.GetCurrent().User!.Value;
    string Name(string id)=>taskPrefix+"."+sid+"."+id;
    string Marker(HostSettings settings,string id)=>"KotorinToys:"+settings.OwnerToken+":"+id;
    public Task<ExternalStartupState[]> ExternalReservations(ToolDefinition tool)=>Task.Run(()=>
    {
        try
        {
            var executable=manager.Executable(tool);var result=ExternalStartupDetector.Detect(executable);
            if(gateway is TaskSchedulerGateway real)
                result=result.Concat(real.FindExternal(executable,store.Load().OwnedTasks.Values.Select(t=>t.Name))).ToArray();
            return result;
        }
        catch(Exception){return new[]{new ExternalStartupState("外部予約","未確認","実行先または予約の読取に失敗しました。")};}
    });
    public Task<StartupState> State(ToolDefinition tool)=>Task.Run(()=>
    {
        try
        {
            var settings=store.Load();var mode=settings.StartupModes.GetValueOrDefault(tool.Id,"none");
            if(store.Journal!=null || store.RemovalPending)return new StartupState(mode,"要対応","前回の予約操作が未完了です。復旧が必要です。");
            var id=mode=="via_kotorintoys"?"host":tool.Id;
            var actual=gateway.Read(Name(id));
            if(actual!=null && (!settings.OwnedTasks.TryGetValue(id,out var own) || own.Name!=actual.Name || own.Fingerprint!=actual.Fingerprint))
                return new StartupState(mode,"要対応","同名の外部予約または定義変更を検出しました。変更しません。");
            if(mode=="none")return new StartupState(mode,actual==null?"なし":"要対応",actual==null?"":"希望なしですが予約が残っています。");
            if(actual==null)return new StartupState(mode,"要対応","希望する予約がありません。");
            var plans=Plans(settings);
            return plans.TryGetValue(id,out var plan) && TaskSchedulerGateway.Matches(actual,plan)
                ? new StartupState(mode,"登録済み",mode=="via_kotorintoys"?"本体の共有サインイン予約から起動します。":"本体を介さず起動します。")
                : new StartupState(mode,"要対応","実行先または予約条件が一致しません。");
        }
        catch(Exception ex){return new StartupState(null,"確認できない",ex.Message);}
    });
    public Task<StartupResult> SetMode(ToolDefinition tool,string mode)=>Change(tool.Id,before=>
    {
        if(mode is not ("none" or "via_kotorintoys" or "direct_task"))throw new IOException("未対応の自動起動方式です。");
        var modes=new Dictionary<string,string>(before.StartupModes){[tool.Id]=mode};return before with{StartupModes=modes};
    });
    public Task<StartupResult> SetHost(bool enabled,bool showDashboard)=>Change("host",before=>
    {
        if(!enabled && before.StartupModes.Values.Contains("via_kotorintoys"))
            throw new IOException("経由起動の対象があります。各ツールを別の方式へ変更してから本体予約を解除してください。");
        return before with{HostStartup=enabled,ShowDashboardAtLogon=showDashboard};
    });
    Task<StartupResult> Change(string id,Func<HostSettings,HostSettings> update)=>Task.Run(()=>
    {
        try
        {
            using var lease=store.AcquireLease();
            if(store.Journal!=null || store.RemovalPending)return new StartupResult(false,"未完了の操作記録があります。先に復旧してください。");
            var before=store.Load();var after=update(before);var plans=Plans(after);
            var originals=new Dictionary<string,TaskOwnership>();
            foreach(var pair in before.OwnedTasks)
            {
                if(pair.Value.Name!=Name(pair.Key))throw new IOException("所有記録の予約名が一致しません。");
                var actual=gateway.Read(pair.Value.Name);if(actual==null)continue;
                if(actual.Fingerprint!=pair.Value.Fingerprint)throw new IOException("予約の外部変更を検出しました。変更しません。");
                originals[pair.Key]=pair.Value with{Xml=actual.Xml};
            }
            foreach(var pair in plans)
            {
                var actual=gateway.Read(pair.Value.Name);
                if(actual!=null && !originals.ContainsKey(pair.Key))throw new IOException("同名の外部予約があります。上書きしません。");
            }
            before=before with{OwnedTasks=originals};store.Save(before);
            var journal=new StartupJournal(Guid.NewGuid().ToString("N"),id,before,"開始",new(),new());
            store.SaveJournal(journal);
            try
            {
                var owned=new Dictionary<string,TaskOwnership>();
                foreach(var pair in originals)
                {
                    var actual=gateway.Read(pair.Value.Name)!;
                    if(plans.TryGetValue(pair.Key,out var plan) && TaskSchedulerGateway.Matches(actual,plan)) {owned[pair.Key]=pair.Value;continue;}
                    store.SaveJournal(journal with{Phase="解除:"+pair.Key});gateway.Delete(pair.Value.Name,pair.Value.Fingerprint);
                }
                foreach(var pair in plans)
                {
                    if(owned.ContainsKey(pair.Key))continue;
                    journal.PlannedTasks[pair.Key]=pair.Value;store.SaveJournal(journal with{Phase="登録:"+pair.Key});
                    var actual=gateway.Create(pair.Value);
                    var ownership=new TaskOwnership(actual.Name,actual.Fingerprint,actual.Xml,actual.SecurityDescriptor);
                    journal.CreatedTasks[pair.Key]=ownership;store.SaveJournal(journal with{Phase="照合:"+pair.Key});
                    if(!TaskSchedulerGateway.Matches(actual,pair.Value))throw new IOException("予約の登録後照合に失敗しました。");
                    owned[pair.Key]=ownership;
                }
                foreach(var pair in owned)
                    if(gateway.Read(pair.Value.Name)?.Fingerprint!=pair.Value.Fingerprint)throw new IOException("確定前に予約が変更されました。");
                store.Save(after with{OwnedTasks=owned});store.CompleteJournal();return new StartupResult(true,"希望と実予約の照合・保存が完了しました。");
            }
            catch(Exception ex)
            {
                var restored=Rollback(journal);
                return new StartupResult(false,ex.Message+" / "+restored.Detail);
            }
        }
        catch(Exception ex){return new StartupResult(false,ex.Message);}
    });
    public Task<StartupResult> Recover()=>Task.Run(()=>
    {
        if(store.RemovalPending)return new RemovalReservations(store,gateway,taskPrefix).Recover();
        try{using var lease=store.AcquireLease();var journal=store.Journal;return journal==null?new StartupResult(true,"未完了の操作はありません。"):Rollback(journal);}
        catch(Exception ex){return new StartupResult(false,ex.Message);}
    });
    StartupResult Rollback(StartupJournal journal)
    {
        try
        {
            var beforePlans=Plans(journal.Before);
            foreach(var pair in journal.Before.OwnedTasks)
            {
                var recorded=new ScheduledTaskState(pair.Value.Name,pair.Value.Xml,pair.Value.SecurityDescriptor);
                if(!beforePlans.TryGetValue(pair.Key,out var expected) || recorded.Fingerprint!=pair.Value.Fingerprint || !TaskSchedulerGateway.Matches(recorded,expected))
                    throw new IOException("復元記録の所有条件または定義が一致しません。");
            }
            foreach(var pair in journal.PlannedTasks)
            {
                if(pair.Value.Name!=Name(pair.Key) || pair.Value.Sid!=sid || pair.Value.Marker!=Marker(journal.Before,pair.Key))
                    throw new IOException("新規予約の操作記録が不正です。");
                var actual=gateway.Read(pair.Value.Name);if(actual==null)continue;
                var expected=journal.CreatedTasks.GetValueOrDefault(pair.Key)?.Fingerprint;
                if(expected!=null ? actual.Fingerprint!=expected : !TaskSchedulerGateway.Matches(actual,pair.Value))
                    throw new IOException("新規予約に外部変更があり、自動復元しません。");
                gateway.Delete(actual.Name,actual.Fingerprint);
            }
            var restored=new Dictionary<string,TaskOwnership>();
            foreach(var pair in journal.Before.OwnedTasks)
            {
                var actual=gateway.Read(pair.Value.Name);
                if(actual!=null && actual.Fingerprint!=pair.Value.Fingerprint)throw new IOException("旧予約に外部変更があり復元できません。");
                actual??=gateway.Restore(pair.Value.Name,pair.Value.Xml,pair.Value.SecurityDescriptor);
                if(actual.Fingerprint!=pair.Value.Fingerprint)throw new IOException("旧予約の復元後照合に失敗しました。");
                restored[pair.Key]=pair.Value;
            }
            store.Save(journal.Before with{OwnedTasks=restored});store.CompleteJournal();return new(true,"変更前の設定と予約へ復元しました。");
        }
        catch(Exception ex){store.SaveJournal(journal with{Phase="要対応"});return new(false,"復元未確認："+ex.Message);}
    }
    Dictionary<string,TaskPlan> Plans(HostSettings settings)
    {
        var plans=new Dictionary<string,TaskPlan>();var tools=manager.Load().ToDictionary(t=>t.Id);
        foreach(var pair in settings.StartupModes)
        {
            if(!tools.TryGetValue(pair.Key,out var tool))throw new IOException("登録されていない自動起動対象です。");
            if(pair.Value!="none" && !allowDevelopment && manager.Installation(tool).State!="導入済み")
                throw new IOException("自動起動には正式導入済みの安定起動先が必要です。");
            if(pair.Value!="direct_task")continue;
            if(!allowDevelopment && manager.Installation(tool).State!="導入済み")throw new IOException("直接予約には正式導入済みの安定起動先が必要です。");
            var executable=manager.Executable(tool);if(!File.Exists(executable))throw new IOException("起動先がありません。");
            plans[pair.Key]=new(Name(pair.Key),executable,"--startup --launch-origin direct_task",Path.GetDirectoryName(executable)!,sid,Marker(settings,pair.Key));
        }
        if(settings.HostStartup || settings.StartupModes.Values.Contains("via_kotorintoys"))
        {
            if(!File.Exists(hostExecutable))throw new IOException("本体の起動先がありません。");
            var hostIdentity=new ToolDefinition("host","KotorinToys","KotorinToys.exe","README.md",AppId:"KotorinToys",InstalledExecutable:"KotorinToys.exe");
            var installation=new InstallationDetector().Detect(hostIdentity);
            if(!allowDevelopment && (installation.State!="導入済み" || !string.Equals(installation.Executable,hostExecutable,StringComparison.OrdinalIgnoreCase)))
                throw new IOException("本体予約には登録済みの正式導入先が必要です。開発用予約はテスト専用です。");
            var arguments="--startup --launch-origin via_kotorintoys --root "+Quote(manager.Root)+" --settings-dir "+Quote(store.DirectoryPath);
            plans["host"]=new(Name("host"),hostExecutable,arguments,Path.GetDirectoryName(hostExecutable)!,sid,Marker(settings,"host"));
        }
        return plans;
    }
    internal static string Quote(string value)=>"\""+value.Replace("\"","\\\"")+"\"";
    public async Task StartViaTargets(CancellationToken cancellation=default)
    {
        var settings=store.Load();var results=new List<StartupAttemptItem>();
        bool incomplete=store.Journal!=null || store.RemovalPending;
        foreach(var tool in manager.Load())
        {
            cancellation.ThrowIfCancellationRequested();
            if(settings.StartupModes.GetValueOrDefault(tool.Id)!="via_kotorintoys")continue;
            try
            {
                if(incomplete){results.Add(new(tool.Id,"見送り","未完了の予約操作があります。復旧が必要です。"));continue;}
                var state=await State(tool);
                if(state.ActualState!="登録済み"){results.Add(new(tool.Id,"見送り",state.ActualState+"："+state.Detail));continue;}
                // サインインの瞬間に方式切替が始まっていれば対象を再起動しない。
                using var lease=store.AcquireLease();
                var current=store.Load();
                if(store.Journal!=null || store.RemovalPending || current.StartupModes.GetValueOrDefault(tool.Id)!="via_kotorintoys")
                {results.Add(new(tool.Id,"見送り","開始直前に予約操作または希望方式が変更されました。"));continue;}
                var started=await manager.Start(tool,false,"via_kotorintoys",cancellation);
                results.Add(new(tool.Id,started.State=="実行中"?"準備完了":"未完了",started.Detail,started.Pid));
            }
            catch(Exception ex) when(ex is not OperationCanceledException){results.Add(new(tool.Id,"失敗",ex.Message));}
        }
        store.RecordAttempt(results.ToArray());
    }
}
