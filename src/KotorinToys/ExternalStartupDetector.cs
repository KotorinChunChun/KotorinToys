using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace KotorinToys;

internal sealed record ExternalStartupState(string Source,string Name,string Detail,string State = "有効");
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
                        found.Add(new("Run登録",name,hive+" / "+view+"：外部管理。自動変更しません。", ReadApproval(hive, view == RegistryView.Registry32 && hive == RegistryHive.LocalMachine ? "Run32" : "Run", name)));
            }
            catch(Exception ex) when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {found.Add(new("Run登録","未確認",hive+"：取得できません。" + ex.Message,"未確認"));}
        }
        foreach(var (folder,hive) in new[]{(Environment.GetFolderPath(Environment.SpecialFolder.Startup),RegistryHive.CurrentUser),(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),RegistryHive.LocalMachine)})
        {
            object? shell=null;
            try
            {
                if(string.IsNullOrEmpty(folder)){found.Add(new("スタートアップ","未確認","スタートアップの場所を取得できません。","未確認"));continue;}
                var type=Type.GetTypeFromProgID("WScript.Shell");if(type==null){found.Add(new("スタートアップ","未確認","ショートカット読取機構がありません。","未確認"));continue;}
                dynamic scripting=Activator.CreateInstance(type)!;shell=scripting;
                foreach(var path in Directory.EnumerateFiles(folder,"*.lnk"))
                {
                    object? shortcut=null;
                    try
                    {
                        dynamic link=scripting.CreateShortcut(path);shortcut=link;
                        if(string.Equals(Path.GetFullPath((string)link.TargetPath),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase))
                            found.Add(new("スタートアップ",Path.GetFileName(path),folder+"：外部のショートカットです。移行・削除しません。", ReadApproval(hive,"StartupFolder",Path.GetFileName(path))));
                    }
                    catch(Exception ex) when(ex is COMException or ArgumentException or IOException){found.Add(new("スタートアップ",Path.GetFileName(path),"読取未確認："+ex.Message,"未確認"));}
                    finally{if(shortcut!=null && Marshal.IsComObject(shortcut))Marshal.FinalReleaseComObject(shortcut);}
                }
            }
            catch(DirectoryNotFoundException) { }
            catch(Exception ex) when(ex is COMException or UnauthorizedAccessException or IOException){found.Add(new("スタートアップ","未確認","取得できません。"+ex.Message,"未確認"));}
            finally{if(shell!=null && Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
        }
        return found.Distinct().ToArray();
    }
    static string ReadApproval(RegistryHive source, string branch, string name)
    {
        var states = new List<string>();
        foreach (var hive in source == RegistryHive.CurrentUser ? new[] { source } : new[] { RegistryHive.CurrentUser, source })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + branch);
                states.Add(ApprovalState(key?.GetValue(name)));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { states.Add("未確認"); }
        }
        return states.Contains("無効") ? "無効" : states.Contains("未確認") ? "未確認" : "有効";
    }
    // 非公開形式なので、既知の12バイト形式・状態値以外は推定しない。読取専用。
    internal static string ApprovalState(object? value) => value == null ? "有効" :
        value is byte[] bytes && bytes.Length == 12 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0
            ? bytes[0] switch { 2 or 6 => "有効", 3 or 7 => "無効", _ => "未確認" } : "未確認";
    internal static bool MatchesCommand(string command,string executable)
    {
        command=Environment.ExpandEnvironmentVariables(command.Trim());
        var end=command.StartsWith('"')?command.IndexOf('"',1):command.IndexOf(' ');
        var first=command.StartsWith('"')?(end>1?command[1..end]:""):(end>=0?command[..end]:command);
        try{return string.Equals(Path.GetFullPath(first),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase);}
        catch(ArgumentException){return false;}
    }
}
