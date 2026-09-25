using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PcPilot.Modules.Kiosk;

/// <summary>
/// Appels Win32 propres au mode kiosque (exécutés uniquement dans le broker élevé) :
/// privilèges de sauvegarde/restauration, chargement de la ruche d'un autre compte, création de profil,
/// secret LSA « DefaultPassword » de l'ouverture de session automatique.
/// </summary>
internal static unsafe partial class KioskNative
{
    // ------------------------------------------------------------------ Privilèges du jeton

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020, TOKEN_QUERY = 0x0008, SE_PRIVILEGE_ENABLED = 0x2;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivileges newState, uint length, nint previous, nint returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    /// <summary>Active (ou désactive) un privilège déjà détenu par le jeton du processus élevé.</summary>
    public static void SetPrivilege(string name, bool enable)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupPrivilegeValue");
            var tp = new TokenPrivileges { Count = 1, Luid = luid, Attributes = enable ? SE_PRIVILEGE_ENABLED : 0 };
            if (!AdjustTokenPrivileges(token, false, ref tp, (uint)sizeof(TokenPrivileges), 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AdjustTokenPrivileges");
            if (enable && Marshal.GetLastWin32Error() == ERROR_NOT_ALL_ASSIGNED)
                throw new Win32Exception(ERROR_NOT_ALL_ASSIGNED, $"Privilège {name} non détenu par le processus administrateur.");
        }
        finally { CloseHandle(token); }
    }

    // ------------------------------------------------------------------ Ruches utilisateur

    public static readonly nint HKEY_USERS = unchecked((nint)(int)0x80000003);

    [LibraryImport("advapi32.dll", EntryPoint = "RegLoadKeyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegLoadKey(nint hKey, string subKey, string file);

    [LibraryImport("advapi32.dll", EntryPoint = "RegUnLoadKeyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegUnLoadKey(nint hKey, string subKey);

    // ------------------------------------------------------------------ Profil

    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateProfile(string sid, string userName, char* profilePath, uint cch);

    public const int HRESULT_ALREADY_EXISTS = unchecked((int)0x800700B7);

    /// <summary>Crée le profil (dossier + NTUSER.DAT) d'un compte qui ne s'est jamais connecté. Renvoie le chemin ou null.</summary>
    public static string? CreateUserProfile(string sid, string userName, out int hresult)
    {
        var buffer = stackalloc char[260];
        hresult = CreateProfile(sid, userName, buffer, 260);
        return hresult == 0 ? new string(buffer) : null;
    }

    // ------------------------------------------------------------------ Vérification du mot de passe

    [LibraryImport("advapi32.dll", EntryPoint = "LogonUserW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LogonUser(string user, string domain, string password, int logonType, int provider, out nint token);

    private const int LOGON32_LOGON_INTERACTIVE = 2, LOGON32_PROVIDER_DEFAULT = 0;
    public const int ERROR_LOGON_FAILURE = 1326;

    /// <summary>Vérifie un couple compte local / mot de passe (0 = valide, sinon code d'erreur Win32).</summary>
    public static int CheckCredentials(string user, string password)
    {
        if (LogonUser(user, ".", password, LOGON32_LOGON_INTERACTIVE, LOGON32_PROVIDER_DEFAULT, out var token))
        {
            CloseHandle(token);
            return 0;
        }
        return Marshal.GetLastWin32Error();
    }

    // ------------------------------------------------------------------ Secret LSA (DefaultPassword)

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaUnicodeString { public ushort Length; public ushort MaximumLength; public nint Buffer; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaObjectAttributes
    {
        public int Length;
        public nint RootDirectory;
        public nint ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor;
        public nint SecurityQualityOfService;
    }

    private const uint POLICY_CREATE_SECRET = 0x00000020;
    private const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaOpenPolicy(nint systemName, ref LsaObjectAttributes attributes, uint access, out nint policy);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaStorePrivateData(nint policy, LsaUnicodeString* keyName, LsaUnicodeString* privateData);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaClose(nint policy);

    [LibraryImport("advapi32.dll")]
    private static partial int LsaNtStatusToWinError(uint status);

    /// <summary>
    /// Enregistre (ou supprime si <paramref name="secret"/> est null) un secret privé LSA. C'est l'emplacement protégé
    /// que Winlogon lit pour « DefaultPassword » : le mot de passe n'est jamais écrit en clair dans le registre.
    /// </summary>
    public static void StorePrivateData(string keyName, string? secret)
    {
        var attributes = new LsaObjectAttributes { Length = sizeof(LsaObjectAttributes) };
        var status = LsaOpenPolicy(0, ref attributes, POLICY_CREATE_SECRET, out var policy);
        if (status != 0) throw new Win32Exception(LsaNtStatusToWinError(status), "LsaOpenPolicy");
        var keyPtr = Marshal.StringToHGlobalUni(keyName);
        var dataPtr = secret is null ? 0 : Marshal.StringToHGlobalUni(secret);
        try
        {
            var key = new LsaUnicodeString { Buffer = keyPtr, Length = (ushort)(keyName.Length * 2), MaximumLength = (ushort)((keyName.Length + 1) * 2) };
            if (secret is null)
            {
                status = LsaStorePrivateData(policy, &key, null);
                if (status == STATUS_OBJECT_NAME_NOT_FOUND) status = 0; // rien à supprimer
            }
            else
            {
                var data = new LsaUnicodeString { Buffer = dataPtr, Length = (ushort)(secret.Length * 2), MaximumLength = (ushort)((secret.Length + 1) * 2) };
                status = LsaStorePrivateData(policy, &key, &data);
            }
            if (status != 0) throw new Win32Exception(LsaNtStatusToWinError(status), "LsaStorePrivateData");
        }
        finally
        {
            Marshal.FreeHGlobal(keyPtr);
            if (dataPtr != 0) Marshal.ZeroFreeGlobalAllocUnicode(dataPtr);
            LsaClose(policy);
        }
    }
}
