using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Xml.Linq;

namespace KotorinToys;

internal sealed record TaskPlan(string Name, string Executable, string Arguments, string WorkingDirectory, string Sid, string Marker);
internal sealed record ScheduledTaskState(string Name, string Xml, string SecurityDescriptor = "")
{
    public string Fingerprint
    {
        get
        {
            var xml = XDocument.Parse(Xml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            // APIによる復元はSDDLをRegistrationInfoにも埋める。実権限は別途照合する。
            xml.Root?.Element(ns + "RegistrationInfo")?.Element(ns + "SecurityDescriptor")?.Remove();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting) + "\n" + NormalizeSecurity(SecurityDescriptor))));
        }
    }

    static string NormalizeSecurity(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var descriptor = new RawSecurityDescriptor(value);
        var acl = descriptor.DiscretionaryAcl;
        var entries = new List<string>();
        bool allAllow = true;
        if (acl != null)
            foreach (GenericAce ace in acl)
            {
                var bytes = new byte[ace.BinaryLength];
                ace.GetBinaryForm(bytes, 0);
                entries.Add(Convert.ToHexString(bytes));
                if (ace is not QualifiedAce qualified || qualified.AceQualifier != AceQualifier.AccessAllowed)
                    allAllow = false;
            }

        // 許可ACEだけなら順序でアクセス結果は変わらない。拒否・不明ACEは順序を保持する。
        if (allAllow)
            entries.Sort(StringComparer.Ordinal);
        return descriptor.Owner?.Value + "|" + descriptor.Group?.Value + "|" + (descriptor.ControlFlags & (ControlFlags.DiscretionaryAclProtected | ControlFlags.DiscretionaryAclPresent)) + "|" + string.Join(",", entries);
    }
}

internal interface IStartupGateway
{
    ScheduledTaskState? Read(string name);
    ScheduledTaskState Create(TaskPlan plan);
    ScheduledTaskState Restore(string name, string xml, string securityDescriptor = "");
    void Delete(string name, string expectedFingerprint);
}
