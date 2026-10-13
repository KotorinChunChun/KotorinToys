using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Xml.Linq;

namespace KotorinToys;

internal sealed class TaskSchedulerGateway : IStartupGateway
{
    static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new IOException("Task Schedulerを利用できません。");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    public ScheduledTaskState? Read(string name)
    {
        object? service = null, folder = null, task = null;
        try
        {
            dynamic connected = Connect();
            service = connected;
            dynamic root = connected.GetFolder(@"\");
            folder = root;
            dynamic found = root.GetTask(name);
            task = found;
            return new(name, (string)found.Xml, (string)found.GetSecurityDescriptor(7));
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002))
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        finally
        {
            Release(task);
            Release(folder);
            Release(service);
        }
    }

    public ScheduledTaskState Create(TaskPlan plan)
    {
        ValidateName(plan.Name);
        if (Read(plan.Name) != null)
            throw new IOException("同名の予約が存在します。外部予約を上書きしません。");
        object? service = null, folder = null, definition = null, registered = null;
        try
        {
            dynamic connected = Connect();
            service = connected;
            dynamic root = connected.GetFolder(@"\");
            folder = root;
            dynamic task = connected.NewTask(0);
            definition = task;
            task.RegistrationInfo.Description = plan.Marker;
            task.Principal.UserId = plan.Sid;
            task.Principal.LogonType = 3;
            task.Principal.RunLevel = 0;
            task.Settings.Enabled = true;
            task.Settings.ExecutionTimeLimit = "PT0S";
            task.Settings.DisallowStartIfOnBatteries = false;
            task.Settings.StopIfGoingOnBatteries = false;
            task.Settings.MultipleInstances = 2;
            task.Settings.AllowDemandStart = true;
            task.Settings.AllowHardTerminate = false;
            task.Settings.StartWhenAvailable = true;
            task.Settings.RestartCount = 0;
            dynamic trigger = task.Triggers.Create(9);
            trigger.UserId = plan.Sid;
            trigger.Delay = "PT5S";
            trigger.Enabled = true;
            dynamic action = task.Actions.Create(0);
            action.Path = plan.Executable;
            action.Arguments = plan.Arguments;
            action.WorkingDirectory = plan.WorkingDirectory;
            dynamic saved = root.RegisterTaskDefinition(plan.Name, task, 2, plan.Sid, null, 3, null);
            registered = saved;
            var state = new ScheduledTaskState(plan.Name, (string)saved.Xml, (string)saved.GetSecurityDescriptor(7));
            if (!Matches(state, plan))
                throw new IOException("登録後の予約条件が一致しません。操作記録から再確認してください。");
            return state;
        }
        finally
        {
            Release(registered);
            Release(definition);
            Release(folder);
            Release(service);
        }
    }

    public ScheduledTaskState Restore(string name, string xml, string securityDescriptor = "")
    {
        ValidateName(name);
        if (Read(name) != null)
            throw new IOException("復元先に予約が存在します。上書きしません。");
        object? service = null, folder = null, registered = null;
        try
        {
            dynamic connected = Connect();
            service = connected;
            dynamic root = connected.GetFolder(@"\");
            folder = root;
            // 復元時は既存DACLへ利用者ACEを追加し直さない。
            var flags = string.IsNullOrEmpty(securityDescriptor) ? 2 : 18;
            dynamic saved = root.RegisterTask(name, xml, flags, WindowsIdentity.GetCurrent().User!.Value, null, 3, string.IsNullOrEmpty(securityDescriptor) ? null : securityDescriptor);
            registered = saved;
            return new(name, (string)saved.Xml, (string)saved.GetSecurityDescriptor(7));
        }
        finally
        {
            Release(registered);
            Release(folder);
            Release(service);
        }
    }

    public void Delete(string name, string expectedFingerprint)
    {
        ValidateName(name);
        var before = Read(name);
        if (before == null)
            return;
        if (before.Fingerprint != expectedFingerprint)
            throw new IOException("予約が外部で変更されています。削除しません。");
        object? service = null, folder = null;
        try
        {
            dynamic connected = Connect();
            service = connected;
            dynamic root = connected.GetFolder(@"\");
            folder = root;
            root.DeleteTask(name, 0);
        }
        finally
        {
            Release(folder);
            Release(service);
        }

        if (Read(name) != null)
            throw new IOException("予約解除を確認できません。");
    }

    public void RunOwned(string name, string expectedFingerprint)
    {
        ValidateName(name);
        object? service = null, folder = null, task = null, running = null;
        try
        {
            dynamic connected = Connect();
            service = connected;
            dynamic root = connected.GetFolder(@"\");
            folder = root;
            dynamic found = root.GetTask(name);
            task = found;
            var actual = new ScheduledTaskState(name, (string)found.Xml, (string)found.GetSecurityDescriptor(7));
            if (actual.Fingerprint != expectedFingerprint)
                throw new IOException("予約の外部変更を検出しました。開始しません。");
            running = found.Run(null);
        }
        finally
        {
            Release(running);
            Release(task);
            Release(folder);
            Release(service);
        }
    }

    public (int State, int Result) Outcome(string name)
    {
        object? service = null, folder = null, task = null;
        try
        {
            dynamic connected = Connect();
            service = connected;
            dynamic root = connected.GetFolder(@"\");
            folder = root;
            dynamic found = root.GetTask(name);
            task = found;
            return ((int)found.State, unchecked((int)found.LastTaskResult));
        }
        finally
        {
            Release(task);
            Release(folder);
            Release(service);
        }
    }

    static void ValidateName(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^KotorinToys(?:\.Test\.[a-f0-9]{32})?\.S-1-[0-9-]+\.[a-z][a-z0-9-]{0,63}$"))
            throw new IOException("管理予約名の範囲外です。");
    }

    public ExternalStartupState[] FindExternal(string executable, IEnumerable<string> ownedNames)
    {
        var own = ownedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<ExternalStartupState>();
        object? service = null, root = null;
        int inspected = 0;
        void Visit(dynamic folder, int depth)
        {
            if (depth > 8 || inspected >= 2000)
            {
                found.Add(new("タスク", "未確認", "読取上限に達しました。"));
                return;
            }

            object? tasks = null, folders = null;
            try
            {
                dynamic list = folder.GetTasks(1);
                tasks = list;
                foreach (dynamic task in list)
                {
                    try
                    {
                        if (++inspected > 2000)
                        {
                            found.Add(new("タスク", "未確認", "読取上限に達しました。"));
                            break;
                        }

                        var name = (string)task.Path;
                        if (own.Contains(name.TrimStart('\\')))
                            continue;
                        var xml = XDocument.Parse((string)task.Xml);
                        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
                        if (xml.Descendants(ns + "Exec").Any(action => string.Equals(Path.GetFullPath(action.Element(ns + "Command")?.Value ?? ""), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase)))
                            found.Add(new("外部タスク", name, "読取のみ。定義・所有者を自動変更しません。"));
                    }
                    catch (Exception ex) when (ex is COMException or IOException or ArgumentException)
                    {
                        found.Add(new("タスク", "未確認", "一部の予約を読めません。"));
                    }
                    finally
                    {
                        Release(task);
                    }
                }

                dynamic children = folder.GetFolders(0);
                folders = children;
                foreach (dynamic child in children)
                {
                    try
                    {
                        Visit(child, depth + 1);
                    }
                    finally
                    {
                        Release(child);
                    }
                }
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
            {
                found.Add(new("タスク", "未確認", "一部のフォルダーに読取権限がありません。"));
            }
            finally
            {
                Release(folders);
                Release(tasks);
            }
        }

        try
        {
            dynamic connected = Connect();
            service = connected;
            dynamic folder = connected.GetFolder(@"\");
            root = folder;
            Visit(folder, 0);
        }
        finally
        {
            Release(root);
            Release(service);
        }

        return found.Distinct().ToArray();
    }

    internal static bool Matches(ScheduledTaskState state, TaskPlan plan)
    {
        var xml = XDocument.Parse(state.Xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        string Value(string parent, string key) => xml.Root!.Element(ns + parent)?.Element(ns + key)?.Value ?? "";
        var principal = xml.Descendants(ns + "Principal").SingleOrDefault();
        var trigger = xml.Descendants(ns + "LogonTrigger").SingleOrDefault();
        var action = xml.Descendants(ns + "Exec").SingleOrDefault();
        if (xml.Root == null || principal == null || trigger == null || action == null)
            return false;
        static string? Sid(string? identity)
        {
            if (identity == null)
                return null;
            try
            {
                return identity.StartsWith("S-1-", StringComparison.Ordinal) ? new SecurityIdentifier(identity).Value : ((SecurityIdentifier)new NTAccount(identity).Translate(typeof(SecurityIdentifier))).Value;
            }
            catch (IdentityNotMappedException)
            {
                return null;
            }
        }

        return state.Name == plan.Name && Value("RegistrationInfo", "Description") == plan.Marker && principal.Element(ns + "UserId")?.Value == plan.Sid && principal.Element(ns + "LogonType")?.Value == "InteractiveToken" && (principal.Element(ns + "RunLevel")?.Value ?? "LeastPrivilege") == "LeastPrivilege" && xml.Root!.Element(ns + "Triggers")?.Elements().Count() == 1 && Sid(trigger.Element(ns + "UserId")?.Value) == plan.Sid && trigger.Element(ns + "Delay")?.Value == "PT5S" && (trigger.Element(ns + "Enabled")?.Value ?? "true") == "true" && xml.Root.Element(ns + "Actions")?.Elements().Count() == 1 && action.Element(ns + "Command")?.Value == plan.Executable && action.Element(ns + "Arguments")?.Value == plan.Arguments && action.Element(ns + "WorkingDirectory")?.Value == plan.WorkingDirectory && Value("Settings", "ExecutionTimeLimit") == "PT0S" && Value("Settings", "DisallowStartIfOnBatteries") == "false" && Value("Settings", "StopIfGoingOnBatteries") == "false" && Value("Settings", "MultipleInstancesPolicy") == "IgnoreNew" && Value("Settings", "AllowHardTerminate") == "false" && Value("Settings", "StartWhenAvailable") == "true" && (xml.Root.Element(ns + "Settings")?.Element(ns + "Enabled")?.Value ?? "true") == "true";
    }
}
