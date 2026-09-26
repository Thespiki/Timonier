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
        if (pwd.Length == 0 && !allowEmpty) throw new ValidationException(L("Enter a password."));
        if (pwd.Length > MaxPassword) throw new ValidationException(L("Password too long (maximum {0} characters).", MaxPassword));
        if (pwd.Any(char.IsControl)) throw new ValidationException(L("The password contains characters that aren't allowed."));
        return pwd;
    }

    public static string? FullName(IReadOnlyDictionary<string, string> p)
    {
        var v = Validate.Optional(p, "fullName", 64);
        if (v is not null && v.Any(c => char.IsControl(c) || c is '<' or '>' or '"' or '\\' or '/' or '[' or ']' or ':' or '|' or '=' or '+' or '*' or '?'))
            throw new ValidationException(L("The full name contains characters that aren't allowed."));
        return v;
    }

    public static string DescribeTarget(IReadOnlyDictionary<string, string> p)
    {
        try
        {
            var sid = Sid(p);
            var a = LocalAccounts.Enumerate().FirstOrDefault(x => string.Equals(x.Sid, sid, StringComparison.OrdinalIgnoreCase));
            if (a is null) return L("(account not found)");
            return a.FullName.Length > 0 && a.FullName != a.Name
                ? L("“{0}” ({1})", a.Name, a.FullName)
                : L("“{0}”", a.Name);
        }
        catch { return L("(unknown account)"); }
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
    public string Title => L("Create a local account");
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
            ? L("Create the ADMINISTRATOR account “{0}” WITHOUT a password? It will have full control over this PC and anyone with access to the PC will be able to open it.", name)
            : L("Create the ADMINISTRATOR account “{0}”? It will have full control over this PC.", name);
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var name = Validate.LocalUserName(Validate.Required(p, "name", 20));
        var fullName = AccountParams.FullName(p);
        var password = AccountParams.Password(p, allowEmpty: true);
        var admin = Validate.OneOf(p, "type", "standard", "admin") == "admin";

        if (LocalAccounts.Enumerate(ctx.UserSid).Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException(L("An account “{0}” already exists.", name));

        ctx.Progress?.Report(L("Creating the account…"));
        NetApi.AddUser(name, password, passwordNotRequired: password.Length == 0);
        var sid = NetApi.GetIdentity(name).Sid ?? throw new InvalidOperationException(L("Account created but SID not found."));
        var warnings = new List<string>();
        try { NetApi.AddToGroup(LocalAccounts.UsersGroup, sid); }
        catch (Exception ex) { warnings.Add(L("couldn't add to the Users group ({0})", ex.Message)); }
        if (admin)
        {
            try { NetApi.AddToGroup(LocalAccounts.AdministratorsGroup, sid); }
            catch (Exception ex) { warnings.Add(L("couldn't add to the Administrators group ({0})", ex.Message)); }
        }
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            try { NetApi.SetFullName(name, fullName); }
            catch (Exception ex) { warnings.Add(L("full name not saved ({0})", ex.Message)); }
        }
        Log.Info("Users", $"compte créé : {name} ({(admin ? "admin" : "standard")})");
        var msg = admin
            ? L("Account “{0}” created (administrator). Its profile will be prepared at its first sign-in.", name)
            : L("Account “{0}” created (standard user). Its profile will be prepared at its first sign-in.", name);
        if (password.Length == 0) msg += L(" Warning: it has no password.");
        if (warnings.Count > 0) msg += LC("user accounts", " Warning: {0}.", string.Join(" ; ", warnings));
        return Task.FromResult(ActionResult.Ok(msg, new Dictionary<string, string> { ["sid"] = sid }));
    }
}

/// <summary>Supprime un compte local (jamais le compte courant, un compte intégré ni le dernier administrateur).</summary>
public sealed class DeleteAccountAction : IActionHandler
{
    public string Id => "users.account.delete";
    public string Title => L("Delete a local account");
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p) =>
        L("Permanently delete the account {0}? It will no longer be able to sign in. Its profile folder (documents, desktop…) is kept on the disk.", AccountParams.DescribeTarget(p));

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => AccountParams.Sid(p);

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        LocalAccount target;
        string? profile;
        lock (LocalAccounts.AdminGate)
        {
            (target, var all) = LocalAccounts.Resolve(AccountParams.Sid(p), ctx.UserSid);
            LocalAccounts.RefuseSessionAccount(target, ctx.UserSid, L("For security, Timonier refuses to delete the account you're signed in with."));
            if (target.IsBuiltIn) throw new ValidationException(L("Windows built-in accounts can't be deleted (disable them instead)."));
            LocalAccounts.RefuseLastAdmin(target, all);

            profile = AccountParams.ProfilePath(target.Sid);
            NetApi.DeleteUser(target.Name);
        }
        Log.Info("Users", "compte supprimé : " + target.Name);
        var msg = L("Account “{0}” deleted.", target.Name);
        msg += profile is not null && Directory.Exists(profile)
            ? L(" Its folder {0} is kept: delete it from “User Profiles” if you no longer need it.", profile)
            : L(" It didn't have a profile folder yet.");
        return Task.FromResult(ActionResult.Ok(msg));
    }
}

/// <summary>Active, désactive ou déverrouille un compte local.</summary>
public sealed class SetAccountEnabledAction : IActionHandler
{
    public string Id => "users.account.setenabled";
    public string Title => L("Enable or disable an account");
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
            if (target.Rid is 503 or 504) throw new ValidationException(L("This system account is managed by Windows: Timonier doesn't change it."));
            if (enable)
            {
                // Ne jamais affaiblir la sécurité : l'Administrateur intégré (sans UAC) et l'Invité restent désactivés.
                if ((target.IsBuiltInAdministrator || target.IsGuest) && !target.Enabled)
                    throw new ValidationException(L("For security, Timonier doesn't re-enable the built-in Administrator account or the Guest account."));
            }
            else
            {
                LocalAccounts.RefuseSessionAccount(target, ctx.UserSid, L("For security, Timonier refuses to disable the account you're signed in with."));
                LocalAccounts.RefuseLastAdmin(target, all);
            }

            var flags = NetApi.GetFlags(target.Name);
            var newFlags = enable ? flags & ~(NetApi.UF_ACCOUNTDISABLE | NetApi.UF_LOCKOUT) : flags | NetApi.UF_ACCOUNTDISABLE;
            if (newFlags != flags) NetApi.SetFlags(target.Name, newFlags);
        }
        Log.Info("Users", $"compte {target.Name} : {(enable ? "activé" : "désactivé")}");
        var msg = enable
            ? target.LockedOut ? L("Account “{0}” unlocked and active.", target.Name) : L("Account “{0}” enabled.", target.Name)
            : L("Account “{0}” disabled: it can no longer sign in (its files are kept).", target.Name);
        return Task.FromResult(ActionResult.Ok(msg));
    }
}

/// <summary>Change le type d'un compte : standard ou administrateur (promotion confirmée par le processus élevé).</summary>
public sealed class SetAccountTypeAction : IActionHandler
{
    public string Id => "users.account.settype";
    public string Title => L("Change account type");
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
        L("Make the account {0} an ADMINISTRATOR? This account will be able to change anything on this PC, including other accounts.", AccountParams.DescribeTarget(p));

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var admin = Validate.OneOf(p, "type", "standard", "admin") == "admin";
        LocalAccount target;
        lock (LocalAccounts.AdminGate)
        {
            (target, var all) = LocalAccounts.Resolve(AccountParams.Sid(p), ctx.UserSid);
            if (target.IsBuiltIn) throw new ValidationException(L("The type of Windows built-in accounts can't be changed."));
            if (target.IsAdmin == admin) return Task.FromResult(ActionResult.Ok(admin
                ? L("“{0}” is already an administrator.", target.Name)
                : L("“{0}” is already a standard user.", target.Name)));

            if (admin)
            {
                NetApi.AddToGroup(LocalAccounts.AdministratorsGroup, target.Sid);
            }
            else
            {
                LocalAccounts.RefuseSessionAccount(target, ctx.UserSid, L("For security, Timonier refuses to demote the account you're signed in with."));
                LocalAccounts.RefuseLastAdmin(target, all);
                NetApi.AddToGroup(LocalAccounts.UsersGroup, target.Sid);
                NetApi.RemoveFromGroup(LocalAccounts.AdministratorsGroup, target.Sid);
            }
        }
        Log.Info("Users", $"compte {target.Name} : type {(admin ? "admin" : "standard")}");
        return Task.FromResult(ActionResult.Ok(
            admin
                ? L("“{0}” is now an administrator. Takes effect at their next sign-in.", target.Name)
                : L("“{0}” is now a standard account. Takes effect at their next sign-in.", target.Name)));
    }
}

/// <summary>Réinitialise le mot de passe d'un AUTRE compte local (jamais journalisé).</summary>
public sealed class ResetPasswordAction : IActionHandler
{
    public string Id => "users.account.resetpassword";
    public string Title => L("Reset an account's password");
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p) =>
        L("Reset the password for the account {0}? This account will lose access to its encrypted files (EFS), its passwords saved by Windows and its personal certificates.", AccountParams.DescribeTarget(p));

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
        LocalAccounts.RefuseSessionAccount(target, ctx.UserSid, L("For security, Timonier refuses to reset the password of the account you're signed in with."));
        if (target.Rid is 501 or 503 or 504) throw new ValidationException(L("This built-in account doesn't use a password managed by Timonier."));
        if (target.MicrosoftAccount is not null)
            throw new ValidationException(L("“{0}” is linked to the Microsoft account {1}: change its password at account.microsoft.com.", target.Name, target.MicrosoftAccount));

        NetApi.SetPassword(target.Name, password);
        // Un mot de passe est désormais défini : on retire l'autorisation de mot de passe vide si elle existait.
        try
        {
            var flags = NetApi.GetFlags(target.Name);
            if ((flags & NetApi.UF_PASSWD_NOTREQD) != 0) NetApi.SetFlags(target.Name, flags & ~NetApi.UF_PASSWD_NOTREQD);
        }
        catch (Exception ex) { Log.Warn("Users", "indicateur mot de passe non exigé : " + ex.Message); }
        Log.Info("Users", "mot de passe réinitialisé : " + target.Name);
        return Task.FromResult(ActionResult.Ok(L("Password for “{0}” reset. Tell it to them in person.", target.Name)));
    }
}

/// <summary>Modifie le nom complet (nom affiché) d'un compte local.</summary>
public sealed class SetFullNameAction : IActionHandler
{
    public string Id => "users.account.setfullname";
    public string Title => L("Change an account's display name");
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
            throw new ValidationException(L("A Microsoft account's name is changed at account.microsoft.com."));
        NetApi.SetFullName(target.Name, fullName);
        return Task.FromResult(ActionResult.Ok(fullName.Length == 0
            ? L("Display name of “{0}” cleared.", target.Name)
            : L("“{0}” will now be shown as “{1}”.", target.Name, fullName)));
    }
}

/// <summary>Seuil de verrouillage des comptes après des mots de passe erronés (stratégie locale, via net accounts).</summary>
public sealed class LockoutThresholdAction : IActionHandler
{
    public string Id => "users.lockout.set";
    public string Title => L("Account lockout threshold");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Validate.Int(p, "threshold", 0, 50);

    // Désactiver le verrouillage retire une protection contre la force brute : confirmation affichée par le broker.
    public bool RequiresElevatedConfirmationFor(IReadOnlyDictionary<string, string> p) => Validate.Int(p, "threshold", 0, 50) == 0;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p) =>
        L("Turn off account lockout? Anyone will be able to try as many passwords as they like on this PC.");

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var n = Validate.Int(p, "threshold", 0, 50);
        var r = await ProcessRunner.RunAsync(SystemTool.Net,
            ["accounts", "/lockoutthreshold:" + n.ToString(CultureInfo.InvariantCulture)],
            new RunOptions { Timeout = TimeSpan.FromSeconds(30), OutputEncoding = ProcessRunner.OemEncoding }, ctx.Cancellation).ConfigureAwait(false);
        if (!r.Success) return ActionResult.Fail(L("net accounts failed: {0}", r.CombinedOutput.Trim()));
        return ActionResult.Ok(n == 0
            ? L("Account lockout turned off: password attempts are no longer limited.")
            : LP(n, "An account will be locked after {0} wrong password.", "An account will be locked after {0} wrong passwords."));
    }
}
