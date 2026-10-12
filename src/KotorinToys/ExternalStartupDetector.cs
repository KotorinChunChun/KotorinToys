using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace KotorinToys;

internal sealed record ExternalStartupState(string Source,string Name,string Detail);
internal static class ExternalStartupDetector
{
    public static ExternalStartupState[] Detect(string executable)
    {
        var found=new List<ExternalStartupState>();
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            try
            {
                using var root=RegistryKey.OpenBaseKey(hive,view);
                using var key=root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                foreach(var name in key?.GetValueNames() ?? [])
                    if(key!.GetValue(name) is string command && MatchesCommand(command,executable))
                        found.Add(new("Run登録",name,hive+" / "+view+"：外部管理。自動変更しません。"));
            }
            catch(Exception ex) when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {found.Add(new("Run登録","未確認",hive+"：取得できません。"));}
        }
        foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Startup),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)})
        {
            object? shell=null;
            try
            {
                if(!Directory.Exists(folder))continue;
                var type=Type.GetTypeFromProgID("WScript.Shell");if(type==null){found.Add(new("スタートアップ","未確認","ショートカット読取機構がありません。"));continue;}
                dynamic scripting=Activator.CreateInstance(type)!;shell=scripting;
                foreach(var path in Directory.EnumerateFiles(folder,"*.lnk"))
                {
                    object? shortcut=null;
                    try
                    {
                        dynamic link=scripting.CreateShortcut(path);shortcut=link;
                        if(string.Equals(Path.GetFullPath((string)link.TargetPath),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase))
                            found.Add(new("スタートアップ",Path.GetFileName(path),"外部のショートカットです。移行・削除しません。"));
                    }
                    catch(Exception ex) when(ex is COMException or ArgumentException or IOException){found.Add(new("スタートアップ",Path.GetFileName(path),"読取未確認"));}
                    finally{if(shortcut!=null && Marshal.IsComObject(shortcut))Marshal.FinalReleaseComObject(shortcut);}
                }
            }
            catch(Exception ex) when(ex is COMException or UnauthorizedAccessException or IOException){found.Add(new("スタートアップ","未確認","取得できません。"));}
            finally{if(shell!=null && Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
        }
        return found.Distinct().ToArray();
    }
    internal static bool MatchesCommand(string command,string executable)
    {
        command=Environment.ExpandEnvironmentVariables(command.Trim());
        var end=command.StartsWith('"')?command.IndexOf('"',1):command.IndexOf(' ');
        var first=command.StartsWith('"')?(end>1?command[1..end]:""):(end>=0?command[..end]:command);
        try{return string.Equals(Path.GetFullPath(first),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase);}
        catch(ArgumentException){return false;}
    }
}
