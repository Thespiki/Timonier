using System.Security.Principal;
using System.Text.RegularExpressions;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Users;

/// <summary>Compte local Windows (base SAM de ce PC).</summary>
internal sealed record LocalAccount
{
    public required string Name { get; init; }
    public required string Sid { get; init; }
    public string FullName { get; init; } = "";
    public string Comment { get; init; } = "";
    public uint Rid { get; init; }
    public bool Enabled { get; init; }
    public bool IsAdmin { get; init; }
    public bool LockedOut { get; init; }
    public bool PasswordNotRequired { get; init; }
    public DateTime? PasswordLastSet { get; init; }
    public DateTime? LastLogon { get; init; }
    /// <summary>Adresse du compte Microsoft associé (compte local « connecté »), sinon null.</summary>
    public string? MicrosoftAccount { get; init; }
    /// <summary>Plages horaires brutes (UTC) ; null = aucune restriction.</summary>
    public byte[]? LogonHours { get; init; }
    public bool IsCurrent { get; init; }

    /// <summary>Comptes créés par Windows : Administrateur (500), Invité (501), DefaultAccount (503), WDAGUtilityAccount (504).</summary>
    public bool IsBuiltIn => Rid is 500 or 501 or 503 or 504;
    public bool IsBuiltInAdministrator => Rid == 500;
    public bool IsGuest => Rid == 501;
    /// <summary>Comptes techniques masqués par défaut : DefaultAccount, WDAGUtilityAccount, reliquats « defaultuserN » de l'installation.</summary>
    public bool IsSystemAccount => Rid is 503 or 504 || LocalAccounts.IsSetupLeftover(Name);
    public bool HasLogonRestriction => LogonHours is { } h && h.Any(b => b != 0xFF);
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Name : FullName;
    public string TypeLabel => IsAdmin ? "Administrateur" : "Standard";
}

/// <summary>
/// Lecture des comptes locaux (API NetUser, sans élévation) et garde-fous communs aux actions d'administration :
/// jamais d'action qui verrouillerait l'utilisateur courant ou supprimerait le dernier administrateur actif.
/// </summary>
internal static partial class LocalAccounts
{
    public const string AdministratorsSid = "S-1-5-32-544";
    public const string UsersSid = "S-1-5-32-545";

    [GeneratedRegex(@"^defaultuser\d+$", RegexOptions.IgnoreCase)] private static partial Regex LeftoverRx();
    [GeneratedRegex(@"^S-1-5-21-\d{1,10}-\d{1,10}-\d{1,10}-\d{3,10}$")] private static partial Regex LocalSidRx();

    public static bool IsSetupLeftover(string name) => LeftoverRx().IsMatch(name);

    /// <summary>Nom localisé d'un groupe intégré (« Administrateurs », « Utilisateurs »…) à partir de son SID.</summary>
    public static string GroupName(string sid)
    {
        var account = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
        var slash = account.IndexOf('\\');
        return slash >= 0 ? account[(slash + 1)..] : account;
    }

    /// <summary>Énumère les comptes locaux. Lent (quelques dizaines de ms) : à appeler hors du thread UI.</summary>
    public static List<LocalAccount> Enumerate(string? currentSid = null)
    {
        currentSid ??= WindowsIdentity.GetCurrent().User?.Value;
        HashSet<string> admins;
        try { admins = NetApi.GetGroupMemberSids(GroupName(AdministratorsSid)); }
        catch (Exception ex)
        {
            Log.Warn("Users", "membres Administrateurs illisibles : " + ex.Message);
            admins = [];
        }

        var now = DateTime.Now;
        var result = new List<LocalAccount>();
        foreach (var u in NetApi.EnumerateUsers())
        {
            var (sid, msa) = NetApi.GetIdentity(u.Name);
            if (sid is null)
            {
                try { sid = ((SecurityIdentifier)new NTAccount(Environment.MachineName, u.Name).Translate(typeof(SecurityIdentifier))).Value; }
                catch { continue; }
            }
            result.Add(new LocalAccount
            {
                Name = u.Name,
                Sid = sid,
                FullName = u.FullName,
                Comment = u.Comment,
                Rid = u.Rid,
                Enabled = (u.Flags & NetApi.UF_ACCOUNTDISABLE) == 0,
                LockedOut = (u.Flags & NetApi.UF_LOCKOUT) != 0,
                PasswordNotRequired = (u.Flags & NetApi.UF_PASSWD_NOTREQD) != 0,
                PasswordLastSet = u.PasswordAgeSeconds is > 0 and < uint.MaxValue ? now.AddSeconds(-u.PasswordAgeSeconds) : null,
                LastLogon = u.LastLogonUnix == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(u.LastLogonUnix).LocalDateTime,
                MicrosoftAccount = msa,
                LogonHours = u.LogonHours,
                IsAdmin = admins.Contains(sid),
                IsCurrent = string.Equals(sid, currentSid, StringComparison.OrdinalIgnoreCase),
            });
        }
        return [.. result.OrderByDescending(a => a.IsCurrent).ThenBy(a => a.IsSystemAccount).ThenBy(a => !a.Enabled)
                         .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    // ------------------------------------------------------------------ Garde-fous (processus élevé)

    /// <summary>SID d'un compte local de ce PC (S-1-5-21-…-RID).</summary>
    public static string ValidateLocalSid(string value)
    {
        var sid = Validate.Sid(value.Trim());
        if (!LocalSidRx().IsMatch(sid)) throw new ValidationException("Ce SID ne correspond pas à un compte local.");
        return sid;
    }

    /// <summary>Retrouve le compte dans l'énumération ACTUELLE (aucun identifiant arbitraire n'atteint l'API).</summary>
    public static (LocalAccount Target, List<LocalAccount> All) Resolve(string sid, string? clientSid)
    {
        var all = Enumerate(clientSid);
        var target = all.FirstOrDefault(a => string.Equals(a.Sid, sid, StringComparison.OrdinalIgnoreCase))
                     ?? throw new ValidationException("Compte local introuvable (il a peut-être été supprimé entre-temps).");
        return (target, all);
    }

    /// <summary>
    /// Vrai si le compte est l'utilisateur de l'interface ou le compte du processus élevé : une action sur lui
    /// pourrait fermer l'accès en cours.
    /// </summary>
    public static bool IsSessionAccount(LocalAccount account, string? clientSid)
    {
        var processSid = WindowsIdentity.GetCurrent().User?.Value;
        return string.Equals(account.Sid, clientSid, StringComparison.OrdinalIgnoreCase)
               || string.Equals(account.Sid, processSid, StringComparison.OrdinalIgnoreCase);
    }

    public static void RefuseSessionAccount(LocalAccount account, string? clientSid, string what)
    {
        if (account.IsCurrent || IsSessionAccount(account, clientSid))
            throw new ValidationException($"Par sécurité, Timonier refuse de {what} le compte avec lequel vous êtes connecté.");
    }

    /// <summary>Refuse de retirer le dernier administrateur actif (désactivation, suppression, rétrogradation).</summary>
    public static void RefuseLastAdmin(LocalAccount account, List<LocalAccount> all)
    {
        if (!account.IsAdmin || !account.Enabled) return;
        var enabledAdmins = all.Count(a => a.IsAdmin && a.Enabled);
        if (enabledAdmins <= 1)
            throw new ValidationException("C'est le dernier compte administrateur actif de ce PC : sans lui, plus personne ne pourrait administrer Windows.");
    }

    public static string AdministratorsGroup => GroupName(AdministratorsSid);
    public static string UsersGroup => GroupName(UsersSid);
}
