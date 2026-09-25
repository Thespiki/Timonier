using System.Globalization;
using Microsoft.Win32;
using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Users;

/// <summary>Validation commune des paramètres des actions sur les comptes.</summary>
internal static class AccountParams
{
    public const int MaxPassword = 127;

    public static string Sid(IReadOnlyDictionary<string, string> p) => LocalAccounts.ValidateLocalSid(Validate.Required(p, "sid", 80));

    /// <summary>Mot de passe : jamais tronqué ni journalisé ; aucun caractère de contrôle.</summary>
    public static string Password(IReadOnlyDictionary<string, string> p, bool allowEmpty)
    {
        p.TryGetValue("password", out var pwd);
        pwd ??= "";
        if (pwd.Length == 0 && !allowEmpty) throw new ValidationException("Saisissez un mot de passe.");
        if (pwd.Length > MaxPassword) throw new ValidationException($"Mot de passe trop long (maximum {MaxPassword} caractères).");
        if (pwd.Any(char.IsControl)) throw new ValidationException("Le mot de passe contient des caractères non autorisés.");
        return pwd;
    }

    public static string? FullName(IReadOnlyDictionary<string, string> p)
    {
        var v = Validate.Optional(p, "fullName", 64);
        if (v is not null && v.Any(c => char.IsControl(c) || c is '<' or '>' or '"' or '\\' or '/' or '[' or ']' or ':' or '|' or '=' or '+' or '*' or '?'))
            throw new ValidationException("Le nom complet contient des caractères non autorisés.");
        return v;
    }

    public static string DescribeTarget(IReadOnlyDictionary<string, string> p)
    {
        try
        {
            var sid = Sid(p);
            var a = LocalAccounts.Enumerate().FirstOrDefault(x => string.Equals(x.Sid, sid, StringComparison.OrdinalIgnoreCase));
            return a is null ? "(compte introuvable)" : $"« {a.Name} »" + (a.FullName.Length > 0 && a.FullName != a.Name ? $" ({a.FullName})" : "");
        }
        catch { return "(compte inconnu)"; }
    }

    public static string? ProfilePath(string sid)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid, false);
            return k?.GetValue("ProfileImagePath") is string s ? Environment.ExpandEnvironmentVariables(s) : null;
        }
        catch { return null; }
    }
}

/// <summary>Crée un compte local standard ou administrateur (mot de passe transmis en mémoire uniquement).</summary>
public sealed class CreateAccountAction : IActionHandler
{
    public string Id => "users.account.create";
    public string Title => "Créer un compte local";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.LocalUserName(Validate.Required(p, "name", 20));
        AccountParams.FullName(p);
        AccountParams.Password(p, allowEmpty: true);
        Validate.OneOf(p, "type", "standard", "admin");
    }

    // Création d'un administrateur : confirmation affichée par le broker (non cliquable par un programme non élevé).
    public bool RequiresElevatedConfirmationFor(IReadOnlyDictionary<string, string> p) =>
        Validate.OneOf(p, "type", "standard", "admin") == "admin";

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p)
    {
        var name = Validate.LocalUserName(Validate.Required(p, "name", 20));
        return AccountParams.Password(p, allowEmpty: true).Length == 0
            ? $"Créer le compte ADMINISTRATEUR « {name} », SANS mot de passe ? Il aura un contrôle total sur ce PC et toute personne ayant accès au PC pourra l'ouvrir."
            : $"Créer le compte ADMINISTRATEUR « {name} » ? Il aura un contrôle total sur ce PC.";
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var name = Validate.LocalUserName(Validate.Required(p, "name", 20));
        var fullName = AccountParams.FullName(p);
        var password = AccountParams.Password(p, allowEmpty: true);
        var admin = Validate.OneOf(p, "type", "standard", "admin") == "admin";

        if (LocalAccounts.Enumerate(ctx.UserSid).Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException($"Un compte « {name} » existe déjà.");

        ctx.Progress?.Report("Création du compte…");
        NetApi.AddUser(name, password, passwordNotRequired: password.Length == 0);
        var sid = NetApi.GetIdentity(name).Sid ?? throw new InvalidOperationException("Compte créé mais SID introuvable.");
        var warnings = new List<string>();
        try { NetApi.AddToGroup(LocalAccounts.UsersGroup, sid); }
        catch (Exception ex) { warnings.Add("ajout au groupe Utilisateurs impossible (" + ex.Message + ")"); }
        if (admin)
        {
            try { NetApi.AddToGroup(LocalAccounts.AdministratorsGroup, sid); }
            catch (Exception ex) { warnings.Add("ajout au groupe Administrateurs impossible (" + ex.Message + ")"); }
        }
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            try { NetApi.SetFullName(name, fullName); }
            catch (Exception ex) { warnings.Add("nom complet non enregistré (" + ex.Message + ")"); }
        }
        Log.Info("Users", $"compte créé : {name} ({(admin ? "admin" : "standard")})");
        var msg = $"Compte « {name} » créé ({(admin ? "administrateur" : "standard")}). Son profil sera préparé à sa première connexion.";
        if (password.Length == 0) msg += " Attention : il n'a pas de mot de passe.";
        if (warnings.Count > 0) msg += " Avertissement : " + string.Join(" ; ", warnings) + ".";
        return Task.FromResult(ActionResult.Ok(msg, new Dictionary<string, string> { ["sid"] = sid }));
    }
}

/// <summary>Supprime un compte local (jamais le compte courant, un compte intégré ni le dernier administrateur).</summary>
public sealed class DeleteAccountAction : IActionHandler
{
    public string Id => "users.account.delete";
    public string Title => "Supprimer un compte local";
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p) =>
        $"Supprimer définitivement le compte {AccountParams.DescribeTarget(p)} ? Il ne pourra plus ouvrir de session. " +
        "Son dossier de profil (documents, bureau…) est conservé sur le disque.";

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => AccountParams.Sid(p);

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        LocalAccount target;
        string? profile;
        lock (LocalAccounts.AdminGate)
        {
            (target, var all) = LocalAccounts.Resolve(AccountParams.Sid(p), ctx.UserSid);
            LocalAccounts.RefuseSessionAccount(target, ctx.UserSid, "supprimer");
            if (target.IsBuiltIn) throw new ValidationException("Les comptes intégrés de Windows ne peuvent pas être supprimés (désactivez-les plutôt).");
            LocalAccounts.RefuseLastAdmin(target, all);

            profile = AccountParams.ProfilePath(target.Sid);
            NetApi.DeleteUser(target.Name);
        }
        Log.Info("Users", "compte supprimé : " + target.Name);
        var msg = $"Compte « {target.Name} » supprimé.";
        msg += profile is not null && Directory.Exists(profile)
            ? $" Son dossier {profile} est conservé : supprimez-le depuis « Profils des utilisateurs » si vous n'en avez plus besoin."
            : " Il n'avait pas encore de dossier de profil.";
        return Task.FromResult(ActionResult.Ok(msg));
    }
}

/// <summary>Active, désactive ou déverrouille un compte local.</summary>
public sealed class SetAccountEnabledAction : IActionHandler
{
    public string Id => "users.account.setenabled";
    public string Title => "Activer ou désactiver un compte";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        AccountParams.Sid(p);
        Validate.Required(p, "enabled", 5);
        Validate.Bool(p, "enabled");
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var enable = Validate.Bool(p, "enabled");
        LocalAccount target;
        lock (LocalAccounts.AdminGate)
        {
            (target, var all) = LocalAccounts.Resolve(AccountParams.Sid(p), ctx.UserSid);
            if (target.Rid is 503 or 504) throw new ValidationException("Ce compte technique est géré par Windows : Timonier ne le modifie pas.");
            if (enable)
            {
                // Ne jamais affaiblir la sécurité : l'Administrateur intégré (sans UAC) et l'Invité restent désactivés.
                if ((target.IsBuiltInAdministrator || target.IsGuest) && !target.Enabled)
                    throw new ValidationException("Par sécurité, Timonier ne réactive pas le compte Administrateur intégré ni le compte Invité.");
            }
            else
            {
                LocalAccounts.RefuseSessionAccount(target, ctx.UserSid, "désactiver");
                LocalAccounts.RefuseLastAdmin(target, all);
            }

            var flags = NetApi.GetFlags(target.Name);
            var newFlags = enable ? flags & ~(NetApi.UF_ACCOUNTDISABLE | NetApi.UF_LOCKOUT) : flags | NetApi.UF_ACCOUNTDISABLE;
            if (newFlags != flags) NetApi.SetFlags(target.Name, newFlags);
        }
        Log.Info("Users", $"compte {target.Name} : {(enable ? "activé" : "désactivé")}");
        var msg = enable
            ? target.LockedOut ? $"Compte « {target.Name} » déverrouillé et actif." : $"Compte « {target.Name} » activé."
            : $"Compte « {target.Name} » désactivé : il ne peut plus ouvrir de session (ses fichiers sont conservés).";
        return Task.FromResult(ActionResult.Ok(msg));
    }
}

/// <summary>Change le type d'un compte : standard ou administrateur (promotion confirmée par le processus élevé).</summary>
public sealed class SetAccountTypeAction : IActionHandler
{
    public string Id => "users.account.settype";
    public string Title => "Changer le type de compte";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        AccountParams.Sid(p);
        Validate.OneOf(p, "type", "standard", "admin");
    }

    // Promotion en administrateur : confirmation affichée par le broker (non cliquable par un programme non élevé).
    public bool RequiresElevatedConfirmationFor(IReadOnlyDictionary<string, string> p) =>
        Validate.OneOf(p, "type", "standard", "admin") == "admin";

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p) =>
        $"Faire du compte {AccountParams.DescribeTarget(p)} un ADMINISTRATEUR ? Ce compte pourra tout modifier sur ce PC, y compris les autres comptes.";

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var admin = Validate.OneOf(p, "type", "standard", "admin") == "admin";
        LocalAccount target;
        lock (LocalAccounts.AdminGate)
        {
            (target, var all) = LocalAccounts.Resolve(AccountParams.Sid(p), ctx.UserSid);
            if (target.IsBuiltIn) throw new ValidationException("Le type des comptes intégrés de Windows ne se modifie pas.");
            if (target.IsAdmin == admin) return Task.FromResult(ActionResult.Ok($"« {target.Name} » est déjà {(admin ? "administrateur" : "standard")}."));

            if (admin)
            {
                NetApi.AddToGroup(LocalAccounts.AdministratorsGroup, target.Sid);
            }
            else
            {
                LocalAccounts.RefuseSessionAccount(target, ctx.UserSid, "rétrograder");
                LocalAccounts.RefuseLastAdmin(target, all);
                NetApi.AddToGroup(LocalAccounts.UsersGroup, target.Sid);
                NetApi.RemoveFromGroup(LocalAccounts.AdministratorsGroup, target.Sid);
            }
        }
        Log.Info("Users", $"compte {target.Name} : type {(admin ? "admin" : "standard")}");
        return Task.FromResult(ActionResult.Ok(
            $"« {target.Name} » est maintenant {(admin ? "administrateur" : "un compte standard")}. Effectif à sa prochaine ouverture de session."));
    }
}

/// <summary>Réinitialise le mot de passe d'un AUTRE compte local (jamais journalisé).</summary>
public sealed class ResetPasswordAction : IActionHandler
{
    public string Id => "users.account.resetpassword";
    public string Title => "Réinitialiser le mot de passe d'un compte";
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p) =>
        $"Réinitialiser le mot de passe du compte {AccountParams.DescribeTarget(p)} ? Ce compte perdra l'accès à ses fichiers " +
        "chiffrés (EFS), à ses mots de passe enregistrés par Windows et à ses certificats personnels.";

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        AccountParams.Sid(p);
        AccountParams.Password(p, allowEmpty: false);
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var password = AccountParams.Password(p, allowEmpty: false);
        var (target, _) = LocalAccounts.Resolve(AccountParams.Sid(p), ctx.UserSid);
        LocalAccounts.RefuseSessionAccount(target, ctx.UserSid, "réinitialiser le mot de passe de");
        if (target.Rid is 501 or 503 or 504) throw new ValidationException("Ce compte intégré n'utilise pas de mot de passe géré par Timonier.");
        if (target.MicrosoftAccount is not null)
            throw new ValidationException($"« {target.Name} » est lié au compte Microsoft {target.MicrosoftAccount} : changez son mot de passe sur account.microsoft.com.");

        NetApi.SetPassword(target.Name, password);
        // Un mot de passe est désormais défini : on retire l'autorisation de mot de passe vide si elle existait.
        try
        {
            var flags = NetApi.GetFlags(target.Name);
            if ((flags & NetApi.UF_PASSWD_NOTREQD) != 0) NetApi.SetFlags(target.Name, flags & ~NetApi.UF_PASSWD_NOTREQD);
        }
        catch (Exception ex) { Log.Warn("Users", "indicateur mot de passe non exigé : " + ex.Message); }
        Log.Info("Users", "mot de passe réinitialisé : " + target.Name);
        return Task.FromResult(ActionResult.Ok($"Mot de passe de « {target.Name} » réinitialisé. Communiquez-le-lui de vive voix."));
    }
}

/// <summary>Modifie le nom complet (nom affiché) d'un compte local.</summary>
public sealed class SetFullNameAction : IActionHandler
{
    public string Id => "users.account.setfullname";
    public string Title => "Modifier le nom affiché d'un compte";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        AccountParams.Sid(p);
        AccountParams.FullName(p);
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var fullName = AccountParams.FullName(p) ?? "";
        var (target, _) = LocalAccounts.Resolve(AccountParams.Sid(p), ctx.UserSid);
        if (target.MicrosoftAccount is not null)
            throw new ValidationException("Le nom d'un compte Microsoft se modifie sur account.microsoft.com.");
        NetApi.SetFullName(target.Name, fullName);
        return Task.FromResult(ActionResult.Ok(fullName.Length == 0
            ? $"Nom affiché de « {target.Name} » effacé."
            : $"« {target.Name} » s'affichera désormais « {fullName} »."));
    }
}

/// <summary>Seuil de verrouillage des comptes après des mots de passe erronés (stratégie locale, via net accounts).</summary>
public sealed class LockoutThresholdAction : IActionHandler
{
    public string Id => "users.lockout.set";
    public string Title => "Seuil de verrouillage des comptes";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Validate.Int(p, "threshold", 0, 50);

    // Désactiver le verrouillage retire une protection contre la force brute : confirmation affichée par le broker.
    public bool RequiresElevatedConfirmationFor(IReadOnlyDictionary<string, string> p) => Validate.Int(p, "threshold", 0, 50) == 0;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p) =>
        "Désactiver le verrouillage des comptes ? Une personne pourra essayer autant de mots de passe qu'elle le souhaite sur ce PC.";

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var n = Validate.Int(p, "threshold", 0, 50);
        var r = await ProcessRunner.RunAsync(SystemTool.Net,
            ["accounts", "/lockoutthreshold:" + n.ToString(CultureInfo.InvariantCulture)],
            new RunOptions { Timeout = TimeSpan.FromSeconds(30), OutputEncoding = ProcessRunner.OemEncoding }, ctx.Cancellation).ConfigureAwait(false);
        if (!r.Success) return ActionResult.Fail("net accounts a échoué : " + r.CombinedOutput.Trim());
        return ActionResult.Ok(n == 0
            ? "Verrouillage des comptes désactivé : les essais de mot de passe ne sont plus limités."
            : $"Un compte sera verrouillé après {n} mots de passe erronés.");
    }
}
