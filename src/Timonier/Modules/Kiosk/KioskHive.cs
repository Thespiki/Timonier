using System.ComponentModel;
using Microsoft.Win32;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Kiosk;

/// <summary>
/// Accès en écriture à la ruche (HKCU) d'un autre compte, depuis le broker élevé : HKU\&lt;SID&gt; si la session est
/// ouverte, sinon chargement temporaire de NTUSER.DAT (RegLoadKey avec SeBackup/SeRestore) puis déchargement garanti.
/// </summary>
internal sealed class UserHive : IDisposable
{
    private readonly RegistryKey _users;
    private readonly string? _mountName;
    public RegistryKey Root { get; }

    private UserHive(RegistryKey users, RegistryKey root, string? mountName)
    {
        _users = users;
        Root = root;
        _mountName = mountName;
    }

    /// <summary>
    /// Ouvre la ruche du compte. Si le compte ne s'est jamais connecté et que <paramref name="createProfile"/> est vrai,
    /// son profil est créé (API CreateProfile) ; sinon, renvoie null quand le profil n'existe pas.
    /// </summary>
    public static UserHive? Open(KioskAccount account, bool createProfile)
    {
        Validate.Sid(account.Sid);
        var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        try
        {
            var live = users.OpenSubKey(account.Sid, writable: true);
            if (live is not null) return new UserHive(users, live, null);

            var profile = KioskAccounts.ProfilePath(account.Sid);
            if (profile is null)
            {
                if (!createProfile) { users.Dispose(); return null; }
                profile = KioskNative.CreateUserProfile(account.Sid, account.Name, out var hr);
                if (profile is null && hr == KioskNative.HRESULT_ALREADY_EXISTS) profile = KioskAccounts.ProfilePath(account.Sid);
                if (profile is null)
                    throw new InvalidOperationException(L("Couldn't create the profile for the account “{0}” (0x{1:X8}). Sign in once with this account, then try again.", account.Name, hr));
                Log.Info("Kiosk", "profil créé pour le compte kiosque");
                profile = KioskAccounts.ProfilePath(account.Sid) ?? profile;
            }

            var file = Path.Combine(profile, "NTUSER.DAT");
            if (!File.Exists(file)) throw new FileNotFoundException(L("Account registry hive not found."), file);

            try
            {
                KioskNative.SetPrivilege("SeBackupPrivilege", true);
                KioskNative.SetPrivilege("SeRestorePrivilege", true);
            }
            catch
            {
                DropPrivileges();
                throw;
            }
            var mount = "Timonier_Kiosk_" + Guid.NewGuid().ToString("N")[..10];
            var rc = KioskNative.RegLoadKey(KioskNative.HKEY_USERS, mount, file);
            if (rc != 0)
            {
                DropPrivileges();
                throw new Win32Exception(rc, L("Couldn't load the kiosk account's registry hive: {0}", new Win32Exception(rc).Message));
            }
            var root = users.OpenSubKey(mount, writable: true);
            if (root is null)
            {
                KioskNative.RegUnLoadKey(KioskNative.HKEY_USERS, mount);
                DropPrivileges();
                throw new InvalidOperationException(L("Registry hive loaded but not accessible."));
            }
            return new UserHive(users, root, mount);
        }
        catch
        {
            users.Dispose();
            throw;
        }
    }

    private static void DropPrivileges()
    {
        try
        {
            KioskNative.SetPrivilege("SeBackupPrivilege", false);
            KioskNative.SetPrivilege("SeRestorePrivilege", false);
        }
        catch { /* sans conséquence */ }
    }

    public void Dispose()
    {
        Root.Dispose();
        _users.Dispose();
        if (_mountName is null) return;
        try
        {
            // Le déchargement exige SeRestorePrivilege : on le réactive au cas où une autre requête du broker l'aurait retiré entre-temps.
            try { KioskNative.SetPrivilege("SeRestorePrivilege", true); }
            catch (Exception ex) { Log.Warn("Kiosk", "privilège de restauration : " + ex.Message); }
            for (var attempt = 0; attempt < 6; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var rc = KioskNative.RegUnLoadKey(KioskNative.HKEY_USERS, _mountName);
                if (rc == 0) return;
                Thread.Sleep(250);
                if (attempt == 5) Log.Warn("Kiosk", $"déchargement de la ruche : code {rc}");
            }
        }
        finally { DropPrivileges(); }
    }

    // ------------------------------------------------------------------ Valeurs avec mémorisation de l'état précédent

    /// <summary>Valeur actuelle encodée (« d:1 », « s:texte ») ou chaîne vide si absente.</summary>
    public string ReadEncoded(string key, string name)
    {
        using var k = Root.OpenSubKey(key, false);
        return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) switch
        {
            int i => "d:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string s => "s:" + s,
            _ => "",
        };
    }

    public void SetDword(string key, string name, int value)
    {
        using var k = Root.CreateSubKey(key, writable: true);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void SetString(string key, string name, string value)
    {
        using var k = Root.CreateSubKey(key, writable: true);
        k.SetValue(name, value, RegistryValueKind.String);
    }

    /// <summary>Remet une valeur dans l'état mémorisé (supprime si elle était absente).</summary>
    public void Restore(string key, string name, string encoded)
    {
        if (encoded.Length == 0)
        {
            using var k = Root.OpenSubKey(key, writable: true);
            k?.DeleteValue(name, throwOnMissingValue: false);
            return;
        }
        if (encoded.StartsWith("d:", StringComparison.Ordinal) && int.TryParse(encoded[2..], out var d)) SetDword(key, name, d);
        else if (encoded.StartsWith("s:", StringComparison.Ordinal)) SetString(key, name, encoded[2..]);
    }
}
