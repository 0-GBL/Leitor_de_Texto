using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Leitor.Services;
public static class AtomicFiles
{
    public static void EnsurePrivateDirectory(string directory)
    {
        if (Directory.Exists(directory)) return; // Do not replace established ACLs on existing user data.
        Directory.CreateDirectory(directory);
        var user = WindowsIdentity.GetCurrent().User ?? throw new IOException("Conta do Windows indisponível.");
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(security);
    }
    public static void Write(string path, byte[] data)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(data); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
