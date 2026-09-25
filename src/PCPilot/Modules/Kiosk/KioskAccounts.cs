using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using System.Security.Principal;
using PcPilot.Core.Model;
using PcPilot.Core.Platform;
using PcPilot.Core.Security;

namespace PcPilot.Modules.Kiosk;

/// <summary>Compte local candidat pour la borne.</summary>
internal sealed record KioskAccount(string Name, string Sid, string? FullName, bool IsAdmin, bool Enabled, bool IsCurrent, string? ProfilePath)
{
    public bool HasProfile => ProfilePath is not null;
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) || FullName == Name ? Name : $"{FullName} ({Name})";
}

/// <summary>Énumération et contrôle des comptes locaux (lecture seule ; utilisable dans l'interface et dans le broker).</summary>
internal static partial class KioskAccounts
{
    private const string AdminsSid = "S-1-5-32-544";
    private const string UsersSid = "S-1-5-32-545";

    [StructLayout(LayoutKind.Sequential)]
    private struct LocalGroupMembersInfo0 { public nint Sid; }

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetLocalGroupGetMembers(string? server, string group, int level, out nint buffer, int prefMaxLen,
        out int entriesRead, out int totalEntries, nint resumeHandle);

    [LibraryImport("netapi32.dll")]
    private static partial int NetApiBufferFree(nint buffer);

    /// <summary>SID des membres directs d'un groupe local (robuste face aux SID orphelins ou de domaine).</summary>
    public static HashSet<string> GroupMemberSids(string groupSid)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = ((NTAccount)new SecurityIdentifier(groupSid).Translate(typeof(NTAccount))).Value;
        var group = name[(name.IndexOf('\\') + 1)..];
        var rc = NetLocalGroupGetMembers(null, group, 0, out var buffer, -1, out var read, out _, 0);
        try
        {
            if (rc != 0) throw new System.ComponentModel.Win32Exception(rc, "NetLocalGroupGetMembers");
            var size = Marshal.SizeOf<LocalGroupMembersInfo0>();
            for (var i = 0; i < read; i++)
            {
                var info = Marshal.PtrToStructure<LocalGroupMembersInfo0>(buffer + i * size);
                if (info.Sid != 0) result.Add(new SecurityIdentifier(info.Sid).Value);
            }
        }
        finally { if (buffer != 0) NetApiBufferFree(buffer); }
        return result;
    }

    public static bool IsAdmin(string sid)
    {
        try { return GroupMemberSids(AdminsSid).Contains(sid); }
        catch (Exception ex)
        {
            Log.Warn("Kiosk", "lecture du groupe Administrateurs : " + ex.Message);
            return true; // prudence : dans le doute, on considère le compte comme administrateur (refusé)
        }
    }

    /// <summary>Chemin du profil existant (dossier + NTUSER.DAT), ou null.</summary>
    public static string? ProfilePath(string sid)
    {
        var raw = RegistryAccess.Read(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid, "ProfileImagePath") as string;
        if (string.IsNullOrEmpty(raw)) return null;
        var path = Environment.ExpandEnvironmentVariables(raw);
        return File.Exists(Path.Combine(path, "NTUSER.DAT")) ? path : null;
    }

    private static bool IsBuiltIn(string sid) =>
        sid.EndsWith("-500", StringComparison.Ordinal) || sid.EndsWith("-501", StringComparison.Ordinal)
        || sid.EndsWith("-503", StringComparison.Ordinal) || sid.EndsWith("-504", StringComparison.Ordinal);

    /// <summary>Comptes locaux activés (hors comptes intégrés). Lent (SAM) : à appeler hors du thread UI.</summary>
    public static List<KioskAccount> List(string? currentSid)
    {
        var admins = GroupMemberSids(AdminsSid);
        var list = new List<KioskAccount>();
        using var ctx = new PrincipalContext(ContextType.Machine);
        using var searcher = new PrincipalSearcher(new UserPrincipal(ctx));
        foreach (var p in searcher.FindAll())
        {
            using (p)
            {
                if (p is not UserPrincipal u || u.Sid is null) continue;
                var sid = u.Sid.Value;
                if (IsBuiltIn(sid)) continue;
                var enabled = u.Enabled ?? true;
                list.Add(new KioskAccount(u.SamAccountName, sid, u.DisplayName, admins.Contains(sid), enabled,
                    string.Equals(sid, currentSid, StringComparison.OrdinalIgnoreCase), ProfilePath(sid)));
            }
        }
        return [.. list.OrderBy(a => a.IsAdmin).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Résout un compte local éligible pour la borne : il doit exister, être activé, ne pas être administrateur
    /// et ne pas être le compte qui utilise PC Pilot. Lève <see cref="ValidationException"/> sinon.
    /// </summary>
    public static KioskAccount ResolveEligible(string name, string? currentSid)
    {
        var user = Validate.LocalUserName(name);
        using var ctx = new PrincipalContext(ContextType.Machine);
        using var u = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, user)
                      ?? throw new ValidationException($"Le compte local « {user} » n'existe pas.");
        var sid = u.Sid?.Value ?? throw new ValidationException("SID du compte introuvable.");
        if (IsBuiltIn(sid)) throw new ValidationException("Les comptes intégrés de Windows ne peuvent pas servir de compte kiosque.");
        if (string.Equals(sid, currentSid, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Le compte kiosque doit être différent de celui que vous utilisez actuellement.");
        if (u.Enabled == false) throw new ValidationException($"Le compte « {user} » est désactivé.");
        if (IsAdmin(sid))
            throw new ValidationException($"« {user} » est administrateur : utilisez un compte standard pour la borne (un administrateur peut sortir du mode kiosque).");
        return new KioskAccount(u.SamAccountName, sid, u.DisplayName, false, true, false, ProfilePath(sid));
    }

    /// <summary>Crée un compte local standard (membre du groupe Utilisateurs uniquement). Appelé dans le broker.</summary>
    public static string Create(string name, string? password, string? fullName)
    {
        using var ctx = new PrincipalContext(ContextType.Machine);
        using (var existing = Principal.FindByIdentity(ctx, IdentityType.SamAccountName, name))
            if (existing is not null) throw new ValidationException($"Un compte ou un groupe nommé « {name} » existe déjà.");

        using var u = new UserPrincipal(ctx)
        {
            SamAccountName = name,
            DisplayName = string.IsNullOrWhiteSpace(fullName) ? name : fullName,
            Description = "Compte de borne (mode kiosque) créé par PC Pilot",
            Enabled = true,
            PasswordNeverExpires = true,
        };
        if (string.IsNullOrEmpty(password)) u.PasswordNotRequired = true;
        else u.SetPassword(password);
        try { u.Save(); }
        catch (PasswordException ex)
        {
            throw new ValidationException("Mot de passe refusé par la stratégie de mots de passe de Windows : " + ex.Message);
        }

        // Les comptes locaux rejoignent normalement « Utilisateurs » : on le garantit, sans jamais ajouter d'autre groupe.
        try
        {
            using var users = GroupPrincipal.FindByIdentity(ctx, IdentityType.Sid, UsersSid);
            if (users is not null && !users.Members.Contains(u))
            {
                users.Members.Add(u);
                users.Save();
            }
        }
        catch (Exception ex) { Log.Warn("Kiosk", "ajout au groupe Utilisateurs : " + ex.Message); }
        return u.Sid?.Value ?? "";
    }
}
