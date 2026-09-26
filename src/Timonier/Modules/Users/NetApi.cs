using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Timonier.Modules.Users;

/// <summary>
/// P/Invoke vers l'API réseau de Windows (netapi32 : base SAM locale) et quelques fonctions advapi32
/// (privilèges, chargement de ruche). Structures déclarées « blittables » (pointeurs en nint) : les chaînes sont
/// allouées et libérées explicitement, et les mots de passe effacés de la mémoire non managée après usage.
/// </summary>
internal static unsafe partial class NetApi
{
    public const int NERR_Success = 0;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_MORE_DATA = 234;
    public const int ERROR_NO_SUCH_MEMBER = 1387;
    public const int ERROR_MEMBER_IN_ALIAS = 1378;
    public const int ERROR_MEMBER_NOT_IN_ALIAS = 1377;
    public const int NERR_UserNotFound = 2221;
    public const int NERR_UserExists = 2224;
    public const int NERR_GroupExists = 2223;
    public const int NERR_BadPassword = 2203;
    public const int NERR_PasswordTooShort = 2245;
    public const int NERR_LastAdmin = 2452;
    private const int MAX_PREFERRED_LENGTH = -1;
    private const int FILTER_NORMAL_ACCOUNT = 0x0002;

    public const uint UF_SCRIPT = 0x0001;
    public const uint UF_ACCOUNTDISABLE = 0x0002;
    public const uint UF_LOCKOUT = 0x0010;
    public const uint UF_PASSWD_NOTREQD = 0x0020;
    public const uint UF_NORMAL_ACCOUNT = 0x0200;
    public const uint UF_DONT_EXPIRE_PASSWD = 0x10000;
    public const uint USER_PRIV_USER = 1;
    public const uint TIMEQ_FOREVER = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct USER_INFO_1
    {
        public nint name, password;
        public uint password_age, priv;
        public nint home_dir, comment;
        public uint flags;
        public nint script_path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USER_INFO_3
    {
        public nint name, password;
        public uint password_age, priv;
        public nint home_dir, comment;
        public uint flags;
        public nint script_path;
        public uint auth_flags;
        public nint full_name, usr_comment, parms, workstations;
        public uint last_logon, last_logoff, acct_expires, max_storage, units_per_week;
        public nint logon_hours;
        public uint bad_pw_count, num_logons;
        public nint logon_server;
        public uint country_code, code_page, user_id, primary_group_id;
        public nint profile, home_dir_drive;
        public uint password_expired;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USER_INFO_24
    {
        public int internet_identity;
        public uint flags;
        public nint internet_provider_name, internet_principal_name, user_sid;
    }

    [StructLayout(LayoutKind.Sequential)] private struct USER_INFO_1003 { public nint password; }
    [StructLayout(LayoutKind.Sequential)] private struct USER_INFO_1008 { public uint flags; }
    [StructLayout(LayoutKind.Sequential)] private struct USER_INFO_1011 { public nint full_name; }
    [StructLayout(LayoutKind.Sequential)] private struct USER_INFO_1020 { public uint units_per_week; public nint logon_hours; }
    [StructLayout(LayoutKind.Sequential)] private struct LOCALGROUP_MEMBERS_INFO_0 { public nint sid; }
    [StructLayout(LayoutKind.Sequential)] private struct USER_MODALS_INFO_0 { public uint min_passwd_len, max_passwd_age, min_passwd_age, force_logoff, password_hist_len; }
    [StructLayout(LayoutKind.Sequential)] private struct USER_MODALS_INFO_3 { public uint lockout_duration, lockout_observation_window, lockout_threshold; }

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetUserEnum(string? server, int level, int filter, out nint buf, int prefMaxLen, out int read, out int total, ref int resume);
    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetUserGetInfo(string? server, string user, int level, out nint buf);
    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetUserSetInfo(string? server, string user, int level, nint buf, out int parmErr);
    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetUserAdd(string? server, int level, nint buf, out int parmErr);
    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetUserDel(string? server, string user);
    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetLocalGroupGetMembers(string? server, string group, int level, out nint buf, int prefMaxLen, out int read, out int total, ref nint resume);
    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetLocalGroupAddMembers(string? server, string group, int level, nint buf, int count);
    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetLocalGroupDelMembers(string? server, string group, int level, nint buf, int count);
    [LibraryImport("netapi32.dll")]
    private static partial int NetUserModalsGet(nint server, int level, out nint buf);
    [LibraryImport("netapi32.dll")]
    private static partial int NetApiBufferFree(nint buf);

    // ------------------------------------------------------------------ Lecture

    /// <summary>Compte tel que lu dans la base SAM locale (lecture autorisée sans élévation).</summary>
    public sealed record RawUser(string Name, string FullName, string Comment, uint Flags, uint PasswordAgeSeconds, uint LastLogonUnix, byte[]? LogonHours, uint Rid);

    public static List<RawUser> EnumerateUsers()
    {
        var list = new List<RawUser>();
        int resume = 0, rc;
        do
        {
            rc = NetUserEnum(null, 3, FILTER_NORMAL_ACCOUNT, out var buf, MAX_PREFERRED_LENGTH, out var read, out _, ref resume);
            if (rc != NERR_Success && rc != ERROR_MORE_DATA) throw Error(rc, L("énumération des comptes"));
            try
            {
                var items = (USER_INFO_3*)buf;
                for (var i = 0; i < read; i++)
                {
                    var u = items[i];
                    byte[]? hours = null;
                    if (u.logon_hours != 0 && u.units_per_week == 168)
                    {
                        hours = new byte[21];
                        Marshal.Copy(u.logon_hours, hours, 0, 21);
                    }
                    list.Add(new RawUser(Str(u.name), Str(u.full_name), Str(u.comment), u.flags, u.password_age, u.last_logon, hours, u.user_id));
                }
            }
            finally
            {
                if (buf != 0) NetApiBufferFree(buf);
            }
        } while (rc == ERROR_MORE_DATA);
        return list;
    }

    /// <summary>SID et liaison à un compte Microsoft (niveau 24). Null si indisponible.</summary>
    public static (string? Sid, string? InternetName) GetIdentity(string user)
    {
        var rc = NetUserGetInfo(null, user, 24, out var buf);
        if (rc != NERR_Success) return (null, null);
        try
        {
            var info = *(USER_INFO_24*)buf;
            string? sid = null;
            if (info.user_sid != 0) sid = new System.Security.Principal.SecurityIdentifier(info.user_sid).Value;
            var internet = info.internet_identity != 0 ? Str(info.internet_principal_name) : null;
            return (sid, string.IsNullOrWhiteSpace(internet) ? null : internet);
        }
        finally { NetApiBufferFree(buf); }
    }

    public static uint GetFlags(string user)
    {
        var rc = NetUserGetInfo(null, user, 1, out var buf);
        if (rc != NERR_Success) throw Error(rc, L("lecture du compte"));
        try { return ((USER_INFO_1*)buf)->flags; }
        finally { NetApiBufferFree(buf); }
    }

    /// <summary>SID (chaînes) des membres d'un groupe local.</summary>
    public static HashSet<string> GetGroupMemberSids(string group)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        nint resume = 0;
        int rc;
        do
        {
            rc = NetLocalGroupGetMembers(null, group, 0, out var buf, MAX_PREFERRED_LENGTH, out var read, out _, ref resume);
            if (rc != NERR_Success && rc != ERROR_MORE_DATA) throw Error(rc, L("lecture du groupe {0}", group));
            try
            {
                var items = (LOCALGROUP_MEMBERS_INFO_0*)buf;
                for (var i = 0; i < read; i++)
                {
                    if (items[i].sid != 0) set.Add(new System.Security.Principal.SecurityIdentifier(items[i].sid).Value);
                }
            }
            finally
            {
                if (buf != 0) NetApiBufferFree(buf);
            }
        } while (rc == ERROR_MORE_DATA);
        return set;
    }

    public sealed record PasswordPolicy(int MinLength, int? MaxAgeDays);
    public sealed record LockoutPolicy(int Threshold, int DurationMinutes, int WindowMinutes);

    public static PasswordPolicy? GetPasswordPolicy()
    {
        if (NetUserModalsGet(0, 0, out var buf) != NERR_Success) return null;
        try
        {
            var m = *(USER_MODALS_INFO_0*)buf;
            return new PasswordPolicy((int)m.min_passwd_len, m.max_passwd_age == TIMEQ_FOREVER ? null : (int)(m.max_passwd_age / 86400));
        }
        finally { NetApiBufferFree(buf); }
    }

    public static LockoutPolicy? GetLockoutPolicy()
    {
        if (NetUserModalsGet(0, 3, out var buf) != NERR_Success) return null;
        try
        {
            var m = *(USER_MODALS_INFO_3*)buf;
            return new LockoutPolicy((int)m.lockout_threshold, (int)(m.lockout_duration / 60), (int)(m.lockout_observation_window / 60));
        }
        finally { NetApiBufferFree(buf); }
    }

    // ------------------------------------------------------------------ Écriture (processus élevé)

    public static void AddUser(string name, string password, bool passwordNotRequired)
    {
        var pName = Marshal.StringToHGlobalUni(name);
        var pPass = Marshal.StringToHGlobalUni(password);
        try
        {
            var info = new USER_INFO_1
            {
                name = pName,
                password = pPass,
                priv = USER_PRIV_USER,
                flags = UF_SCRIPT | UF_NORMAL_ACCOUNT | UF_DONT_EXPIRE_PASSWD | (passwordNotRequired ? UF_PASSWD_NOTREQD : 0),
            };
            var rc = NetUserAdd(null, 1, (nint)(&info), out _);
            if (rc != NERR_Success) throw Error(rc, L("création du compte"));
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
            Marshal.ZeroFreeGlobalAllocUnicode(pPass);
        }
    }

    public static void DeleteUser(string name)
    {
        var rc = NetUserDel(null, name);
        if (rc != NERR_Success) throw Error(rc, L("suppression du compte"));
    }

    public static void SetFullName(string name, string fullName)
    {
        var p = Marshal.StringToHGlobalUni(fullName);
        try
        {
            var info = new USER_INFO_1011 { full_name = p };
            var rc = NetUserSetInfo(null, name, 1011, (nint)(&info), out _);
            if (rc != NERR_Success) throw Error(rc, L("modification du nom complet"));
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    public static void SetFlags(string name, uint flags)
    {
        var info = new USER_INFO_1008 { flags = flags };
        var rc = NetUserSetInfo(null, name, 1008, (nint)(&info), out _);
        if (rc != NERR_Success) throw Error(rc, L("modification du compte"));
    }

    public static void SetPassword(string name, string password)
    {
        var p = Marshal.StringToHGlobalUni(password);
        try
        {
            var info = new USER_INFO_1003 { password = p };
            var rc = NetUserSetInfo(null, name, 1003, (nint)(&info), out _);
            if (rc != NERR_Success) throw Error(rc, L("changement du mot de passe"));
        }
        finally { Marshal.ZeroFreeGlobalAllocUnicode(p); }
    }

    /// <summary>Plages horaires (bitmap de 21 octets, heure UTC, bit 0 = dimanche 0 h–1 h UTC).</summary>
    public static void SetLogonHours(string name, byte[] bitmap)
    {
        if (bitmap.Length != 21) throw new ArgumentException("21 octets attendus.", nameof(bitmap));
        fixed (byte* p = bitmap)
        {
            var info = new USER_INFO_1020 { units_per_week = 168, logon_hours = (nint)p };
            var rc = NetUserSetInfo(null, name, 1020, (nint)(&info), out _);
            if (rc != NERR_Success) throw Error(rc, L("enregistrement des plages horaires"));
        }
    }

    /// <summary>Ajoute un compte (par SID) à un groupe local ; sans effet s'il en est déjà membre.</summary>
    public static void AddToGroup(string group, string sid) => ChangeMembership(group, sid, add: true);

    /// <summary>Retire un compte (par SID) d'un groupe local ; sans effet s'il n'en est pas membre.</summary>
    public static void RemoveFromGroup(string group, string sid) => ChangeMembership(group, sid, add: false);

    private static void ChangeMembership(string group, string sid, bool add)
    {
        var id = new System.Security.Principal.SecurityIdentifier(sid);
        var bytes = new byte[id.BinaryLength];
        id.GetBinaryForm(bytes, 0);
        fixed (byte* pSid = bytes)
        {
            var member = new LOCALGROUP_MEMBERS_INFO_0 { sid = (nint)pSid };
            var rc = add
                ? NetLocalGroupAddMembers(null, group, 0, (nint)(&member), 1)
                : NetLocalGroupDelMembers(null, group, 0, (nint)(&member), 1);
            if (rc == NERR_Success || (add && rc == ERROR_MEMBER_IN_ALIAS) || (!add && rc is ERROR_MEMBER_NOT_IN_ALIAS or ERROR_NO_SUCH_MEMBER)) return;
            throw Error(rc, add ? L("ajout au groupe {0}", group) : L("retrait du groupe {0}", group));
        }
    }

    private static string Str(nint p) => p == 0 ? "" : Marshal.PtrToStringUni(p) ?? "";

    /// <summary>Message clair en français pour les erreurs NetAPI courantes.</summary>
    public static Exception Error(int code, string context) => code switch
    {
        ERROR_ACCESS_DENIED => new UnauthorizedAccessException(L("Accès refusé ({0}) : droits administrateur requis.", context)),
        NERR_UserExists => new InvalidOperationException(L("Un compte portant ce nom existe déjà.")),
        NERR_GroupExists => new InvalidOperationException(L("Ce nom est déjà utilisé par un groupe local : choisissez-en un autre.")),
        NERR_UserNotFound => new InvalidOperationException(L("Compte introuvable (il a peut-être été supprimé entre-temps).")),
        NERR_PasswordTooShort or NERR_BadPassword => new InvalidOperationException(
            L("Le mot de passe ne respecte pas la stratégie de mots de passe de ce PC (longueur, complexité ou historique).")),
        NERR_LastAdmin => new InvalidOperationException(L("Windows refuse : c'est le dernier compte administrateur.")),
        _ => new Win32Exception(code, L("Échec de l'opération ({0}) : {1} (code {2}).", context, new Win32Exception(code).Message, code)),
    };

    // ------------------------------------------------------------------ Privilèges et ruches

    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attributes; }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8, SE_PRIVILEGE_ENABLED = 0x2;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;
    public static readonly nint HKEY_USERS = unchecked((nint)(int)0x80000003);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);
    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? system, string name, out LUID luid);
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TOKEN_PRIVILEGES state, uint len, nint prev, nint retLen);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint h);
    [LibraryImport("advapi32.dll", EntryPoint = "RegLoadKeyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegLoadKey(nint hKey, string subKey, string file);
    [LibraryImport("advapi32.dll", EntryPoint = "RegUnLoadKeyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegUnLoadKey(nint hKey, string subKey);

    /// <summary>Active (ou désactive) un privilège du jeton du processus courant (ex. SeRestorePrivilege).</summary>
    public static void SetPrivilege(string name, bool enable)
    {
        if (!OpenProcessToken(-1, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = enable ? SE_PRIVILEGE_ENABLED : 0 };
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, 0, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (enable && Marshal.GetLastPInvokeError() == ERROR_NOT_ALL_ASSIGNED)
                throw new UnauthorizedAccessException(L("Privilège {0} indisponible : exécution administrateur requise.", name));
        }
        finally { CloseHandle(token); }
    }
}
