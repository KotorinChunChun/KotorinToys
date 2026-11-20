using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace KotorinToys;

internal sealed record PinnedAuthenticodeSigner(string Subject, string Thumbprint, bool HasTimestamp);

// 自己署名ルートをインポートせず、WinVerifyTrust が検証した一次署名者だけを取得する。
internal static class PinnedAuthenticodeSignature
{
    const int CertEUntrustedRoot = unchecked((int)0x800B0109);

    internal static PinnedAuthenticodeSigner Verify(string path)
    {
        if (!File.Exists(path)) throw new IOException("署名確認対象のインストーラーが見つかりません。");
        var file = new FileInfoNative { Size = (uint)Marshal.SizeOf<FileInfoNative>(), Path = Path.GetFullPath(path) };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoNative>());
        Marshal.StructureToPtr(file, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = pointer,
            Action = 1, Flags = 0x80 /* ルートを除く失効確認 */ };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            int code = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            // 自己署名ルート未登録だけは、provider が検証済みの署名・固定拇印・時刻印を返せる場合に限る。
            if (code is not 0 && code != CertEUntrustedRoot)
                throw new IOException($"インストーラーのAuthenticode署名を検証できません（0x{code:X8}）。");
            IntPtr provider = WTHelperProvDataFromStateData(data.State);
            IntPtr signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            IntPtr certificate = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
            if (certificate == IntPtr.Zero) throw new IOException("インストーラーの一次署名者を読み取れません。");
            IntPtr timestamp = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, true, 0);
            if (timestamp == IntPtr.Zero) throw new IOException("インストーラーのAuthenticode時刻印を確認できません。");
            if (Marshal.PtrToStructure<ProviderSigner>(timestamp).Error != 0)
                throw new IOException("インストーラーのAuthenticode時刻印を検証できません。");
            var context = Marshal.PtrToStructure<ProviderCertificate>(certificate).Certificate;
            if (context == IntPtr.Zero) throw new IOException("インストーラーの一次署名者証明書がありません。");
            using var cert = new X509Certificate2(context);
            return new(cert.Subject, cert.Thumbprint, true);
        }
        finally
        {
            if (data.State != IntPtr.Zero) { data.Action = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
            Marshal.DestroyStructure<FileInfoNative>(pointer); Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileInfoNative { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)]
    struct TrustData { public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Choice; public IntPtr File; public uint Action; public IntPtr State, Url; public uint Flags, Context; public IntPtr SignatureSettings; }
    [StructLayout(LayoutKind.Sequential)] struct ProviderCertificate { public uint Size; public IntPtr Certificate; }
    [StructLayout(LayoutKind.Sequential)]
    struct ProviderSigner
    {
        public uint Size; public System.Runtime.InteropServices.ComTypes.FILETIME VerifyAsOf;
        public uint CertificateChainCount; public IntPtr CertificateChain; public uint SignerType; public IntPtr Signer;
        public uint Error; public uint CounterSignerCount; public IntPtr CounterSigners; public IntPtr ChainContext;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)] static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)] static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)] static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificate);
}
