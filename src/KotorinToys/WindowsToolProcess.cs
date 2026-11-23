using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KotorinToys;

internal static class WindowsToolProcess
{
    const uint QueryLimitedInformation = 0x1000;
    const uint TokenQuery = 0x0008;
    const int TokenElevation = 20;
    internal static readonly Lazy<bool> CurrentElevation = new(() => Read(Environment.ProcessId).Elevated);

    // モジュールの読取権限がなくても、照合に必要な実行パスと昇格状態を取得する。
    public static (string Path, bool Elevated) Read(int pid)
    {
        using var process = OpenProcess(QueryLimitedInformation, false, pid);
        if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var path = new StringBuilder(32768);
        int length = path.Capacity;
        if (!QueryFullProcessImageName(process, 0, path, ref length))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!OpenProcessToken(process, TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            if (!GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return (path.ToString(), elevated != 0);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int pid);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int length, out int bytesReturned);
}
