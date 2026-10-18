using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace KotorinToys;

internal sealed record MsixSigner(string Subject, string Thumbprint);

// PowerShellや外部signtoolを実行せず、Windows SIPでMSIX署名を検証する。
internal static class MsixSignature
{
    public static MsixSigner Verify(string path)
    {
        if (!File.Exists(path)) throw new IOException("MSIXファイルが見つかりません。");
        var file = new FileInfoNative { Size = (uint)Marshal.SizeOf<FileInfoNative>(), Path = Path.GetFullPath(path) };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoNative>());
        Marshal.StructureToPtr(file, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = pointer,
            Action = 1, Flags = 0x80 /* チェーンの失効確認（ルートを除く） */ };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            int code = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (code != 0) throw new IOException($"MSIX署名を信頼できません（0x{code:X8}）。発行元の導入手順で証明書・有効期限を確認してください。証明書は自動登録しません。");
            IntPtr provider = WTHelperProvDataFromStateData(data.State);
            IntPtr signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            IntPtr certificate = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
            if (certificate == IntPtr.Zero) throw new IOException("MSIX署名者を読み取れません。");
            var context = Marshal.PtrToStructure<ProviderCertificate>(certificate).Certificate;
            if (context == IntPtr.Zero) throw new IOException("MSIX署名者証明書がありません。");
            using var cert = new X509Certificate2(context);
            return new(cert.Subject, cert.Thumbprint);
        }
        finally
        {
            data.Action = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<FileInfoNative>(pointer); Marshal.FreeHGlobal(pointer);
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileInfoNative { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)]
    struct TrustData
    {
        public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Choice; public IntPtr File;
        public uint Action; public IntPtr State, Url; public uint Flags, Context; public IntPtr SignatureSettings;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProviderCertificate { public uint Size; public IntPtr Certificate; }
    [DllImport("wintrust.dll", ExactSpelling = true)] static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)] static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)] static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)] static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificate);
}
