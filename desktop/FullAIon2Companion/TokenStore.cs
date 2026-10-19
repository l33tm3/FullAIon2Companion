using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FullAIon2Companion;

/// <summary>Where the device token lives. Never logged, never in a URL.</summary>
public interface ITokenStore
{
    string? Read();
    void Write(string token);
    void Delete();
}

/// <summary>
/// The device token in Windows Credential Manager (a generic credential of the current user, "FullAIon2Companion/device"),
/// as the API asks (<c>storeIn: Windows Credential Manager</c>). A token kept by older builds in device.dpapi is moved here
/// once and the file deleted.
/// </summary>
public sealed class CredentialTokenStore(string legacyDpapiPath, string target = "FullAIon2Companion/device") : ITokenStore
{
    const int Generic = 1, LocalMachinePersist = 2;

    public string? Read()
    {
        if (CredReadW(target, Generic, 0, out var ptr))
        {
            try
            {
                var c = Marshal.PtrToStructure<Credential>(ptr);
                string? token = c.CredentialBlobSize == 0 ? null : Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2);
                return Valid(token) ? token : null;
            }
            finally { CredFree(ptr); }
        }
        if (Marshal.GetLastWin32Error() != 1168) throw new InvalidOperationException("No se pudo leer el Administrador de credenciales de Windows");
        // Migration from the DPAPI file of earlier builds.
        try
        {
            if (!File.Exists(legacyDpapiPath)) return null;
            string token = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(legacyDpapiPath), null, DataProtectionScope.CurrentUser));
            Write(token);
            File.Delete(legacyDpapiPath);
            return token;
        }
        catch { return null; }
    }
    public void Write(string token)
    {
        if (!Valid(token)) throw new FormatException("Token de dispositivo no válido");
        byte[] blob = Encoding.Unicode.GetBytes(token);
        var handle = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            var c = new Credential
            {
                Type = Generic, TargetName = target, UserName = "FullAIon2Companion", Persist = LocalMachinePersist,
                CredentialBlobSize = (uint)blob.Length, CredentialBlob = handle.AddrOfPinnedObject(),
            };
            if (!CredWriteW(ref c, 0)) throw new InvalidOperationException("No se pudo guardar el token en el Administrador de credenciales de Windows");
        }
        finally { Array.Clear(blob); handle.Free(); }
    }
    public void Delete()
    {
        if (!CredDeleteW(target, Generic, 0) && Marshal.GetLastWin32Error() != 1168) throw new InvalidOperationException("No se pudo quitar la credencial de este dispositivo");
        try { if (File.Exists(legacyDpapiPath)) File.Delete(legacyDpapiPath); } catch { }
    }
    static bool Valid(string? token) => token is not null && System.Text.RegularExpressions.Regex.IsMatch(token, "^[a-f0-9]{64}$");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Credential
    {
        public uint Flags; public int Type; public string TargetName; public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public int Persist;
        public uint AttributeCount; public IntPtr Attributes; public string? TargetAlias; public string UserName;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredWriteW(ref Credential credential, int flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredDeleteW(string target, int type, int flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr buffer);
}

/// <summary>The offline tests' store: a DPAPI file in a throwaway folder (never the user's credential).</summary>
public sealed class FileTokenStore(string path) : ITokenStore
{
    public string? Read() => File.Exists(path) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) : null;
    public void Write(string token) => File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
    public void Delete() { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
