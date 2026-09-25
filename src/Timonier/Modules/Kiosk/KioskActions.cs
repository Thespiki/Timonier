using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Kiosk;

// Toutes ces actions s'exécutent dans le broker élevé : aucune référence à AppHost ni à l'interface.

/// <summary>Crée un compte local STANDARD destiné à la borne.</summary>
internal sealed partial class CreateKioskAccountAction : IActionHandler
{
    public string Id => "kiosk.account.create";
    public string Title => "Créer un compte kiosque";
    public bool RequiresAdmin => true;

    [GeneratedRegex(@"^[\p{L}\p{N} .'_\-]{1,64}$")]
    private static partial Regex FullNameRx();

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.LocalUserName(Validate.Required(p, "name", 20));
        if (p.TryGetValue("password", out var pw) && pw.Length > 127) throw new ValidationException("Mot de passe trop long (127 caractères au maximum).");
        if (Validate.Optional(p, "fullName", 64) is { } f && !FullNameRx().IsMatch(f)) throw new ValidationException("Nom complet invalide.");
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var name = Validate.LocalUserName(Validate.Required(p, "name", 20));
        p.TryGetValue("password", out var password);
        ctx.Progress?.Report("Création du compte…");
        var sid = KioskAccounts.Create(name, string.IsNullOrEmpty(password) ? null : password, Validate.Optional(p, "fullName", 64));
        Log.Info("Kiosk", "compte kiosque créé");
        return Task.FromResult(ActionResult.Ok($"Compte standard « {name} » créé.", new Dictionary<string, string> { ["sid"] = sid, ["name"] = name }));
    }
}

/// <summary>Configure la borne : accès attribué (application du Store) ou interpréteur par utilisateur (Edge / .exe) + restrictions.</summary>
internal sealed class ApplyKioskAction : IActionHandler
{
    public string Id => "kiosk.apply";
    public string Title => "Configurer le mode kiosque";
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    private const string SetAssignedAccessScript =
        "Import-Module AssignedAccess; Set-AssignedAccess -AppUserModelId $env:TMN_AUMID -UserSID $env:TMN_SID -Confirm:$false; 'OK'";
    internal const string ClearAssignedAccessScript = "Import-Module AssignedAccess; Clear-AssignedAccess -Confirm:$false; 'OK'";

    private sealed record Plan(string Mode, string User, string? Aumid, string? Exe, string? Url, bool PublicBrowsing, int Idle, List<KioskRestriction> Restrictions);

    private static Plan Parse(IReadOnlyDictionary<string, string> p)
    {
        var mode = Validate.OneOf(p, "mode", KioskModes.Store, KioskModes.Edge, KioskModes.Win32);
        var user = Validate.LocalUserName(Validate.Required(p, "user", 20));
        string? aumid = null, exe = null, url = null;
        var publicBrowsing = false;
        var idle = 0;
        switch (mode)
        {
            case KioskModes.Store:
                aumid = Validate.Aumid(Validate.Required(p, "aumid", 256));
                break;
            case KioskModes.Win32:
                exe = KioskRules.ExeProblemCheck(Validate.ExistingLocalFile(Validate.Required(p, "exe", 1024), ".exe"));
                break;
            case KioskModes.Edge:
                url = KioskRules.Url(Validate.Required(p, "url", 2048));
                publicBrowsing = Validate.OneOf(p, "edgeType", "fullscreen", "public") == "public";
                idle = p.ContainsKey("idle") ? Validate.Int(p, "idle", 0, 1440) : 0;
                break;
        }
        return new Plan(mode, user, aumid, exe, url, publicBrowsing, idle, KioskRestrictions.Parse(Validate.Optional(p, "restrictions", 400)));
    }

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Parse(p);

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p)
    {
        var plan = Parse(p);
        var target = plan.Mode switch
        {
            KioskModes.Store => "application du Store " + plan.Aumid,
            KioskModes.Edge => $"Microsoft Edge ({(plan.PublicBrowsing ? "navigation publique" : "affichage plein écran")}) sur {plan.Url}",
            _ => plan.Exe!,
        };
        var restrictions = plan.Restrictions.Count == 0 ? "aucune" : string.Join(", ", plan.Restrictions.Select(r => r.Title.ToLowerInvariant()));
        return $"Transformer ce PC en borne pour le compte « {plan.User} ».\n\n"
             + $"• Mode : {KioskModes.Label(plan.Mode)}\n• Application : {target}\n• Restrictions du compte : {restrictions}\n\n"
             + $"À sa prochaine connexion, « {plan.User} » n'aura accès qu'à cette application. "
             + "Pour quitter une session kiosque : Ctrl+Alt+Suppr puis « Se déconnecter ».";
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        var plan = Parse(p);
        ctx.Progress?.Report("Vérification du compte…");
        var account = KioskAccounts.ResolveEligible(plan.User, ctx.UserSid);

        var previous = KioskState.Read();
        if (previous is { IsConfigured: true } && !string.Equals(previous.Sid, account.Sid, StringComparison.OrdinalIgnoreCase))
            return ActionResult.Fail($"Une borne est déjà configurée pour le compte « {previous.User} ». Désactivez d'abord le mode kiosque, puis recommencez.");

        string? shellValue = null;
        switch (plan.Mode)
        {
            case KioskModes.Store:
                EnsurePackageExists(plan.Aumid!);
                break;
            case KioskModes.Win32:
                if (KioskRules.ExeLocationProblem(plan.Exe!, account.ProfilePath) is { } problem) return ActionResult.Fail(problem);
                shellValue = "\"" + plan.Exe + "\"";
                break;
            case KioskModes.Edge:
                var edge = KioskRules.EdgePath() ?? throw new InvalidOperationException("Microsoft Edge n'est pas installé sur ce PC.");
                shellValue = KioskRules.EdgeCommandLine(edge, plan.Url!, plan.PublicBrowsing, plan.Idle);
                break;
        }

        // 1. Ruche du compte : interpréteur personnalisé et restrictions (avec mémorisation de l'état antérieur).
        var oldRestrictions = (previous?.Restrictions ?? []).Select(r => r.Split('=', 2)).Where(a => a.Length == 2)
            .ToDictionary(a => a[0], a => a[1], StringComparer.Ordinal);
        var newRestrictions = new List<string>();
        string? shellPrev = previous?.IsConfigured == true ? ReadStateString("ShellPrev") : null;
        var shellWasSet = previous?.IsConfigured == true && previous.ShellValue is not null;
        var needHive = shellValue is not null || plan.Restrictions.Count > 0 || oldRestrictions.Count > 0 || shellWasSet;
        if (needHive)
        {
            ctx.Progress?.Report("Configuration du compte kiosque…");
            using var hive = UserHive.Open(account, createProfile: true)!;
            if (shellValue is not null)
            {
                if (!shellWasSet) shellPrev = hive.ReadEncoded(KioskRules.ShellPolicyKey, KioskRules.ShellPolicyValue);
                hive.SetString(KioskRules.ShellPolicyKey, KioskRules.ShellPolicyValue, shellValue);
            }
            else if (shellWasSet)
            {
                hive.Restore(KioskRules.ShellPolicyKey, KioskRules.ShellPolicyValue, shellPrev ?? "");
                shellPrev = null;
            }

            foreach (var (key, prev) in oldRestrictions)
            {
                if (plan.Restrictions.Any(r => r.Key == key)) continue;
                if (KioskRestrictions.Get(key) is { } old) hive.Restore(old.RegKey, old.Value, prev);
            }
            foreach (var r in plan.Restrictions)
            {
                var prev = oldRestrictions.TryGetValue(r.Key, out var o) ? o : hive.ReadEncoded(r.RegKey, r.Value);
                hive.SetDword(r.RegKey, r.Value, r.Data);
                newRestrictions.Add(r.Key + "=" + prev);
            }
        }

        // 2. Accès attribué : configuré pour une application du Store, retiré si Timonier l'avait posé et qu'on change de mode.
        var assigned = false;
        if (plan.Mode == KioskModes.Store)
        {
            ctx.Progress?.Report("Configuration de l'accès attribué…");
            var r = await PowerShellRunner.RunAsync(SetAssignedAccessScript,
                new Dictionary<string, string> { ["AUMID"] = plan.Aumid!, ["SID"] = account.Sid }, TimeSpan.FromMinutes(2), ct: ctx.Cancellation);
            if (!r.Success || !r.Output.Contains("OK", StringComparison.Ordinal))
            {
                WriteState(account, plan, shellValue, shellPrev, newRestrictions, assigned: false);
                return ActionResult.Fail("Windows a refusé l'accès attribué : " + FirstLine(r.Error, r.Output)
                    + " (les comptes liés à un compte Microsoft ne sont pas acceptés par cette méthode).");
            }
            assigned = true;
        }
        else if (previous?.AssignedAccess == true)
        {
            await PowerShellRunner.RunAsync(ClearAssignedAccessScript, null, TimeSpan.FromMinutes(1), ct: ctx.Cancellation);
        }

        WriteState(account, plan, shellValue, shellPrev, newRestrictions, assigned);
        Log.Info("Kiosk", "borne configurée : " + plan.Mode);
        return ActionResult.Ok($"Mode kiosque configuré pour « {account.Name} ». Il prendra effet à sa prochaine ouverture de session.",
            new Dictionary<string, string> { ["sid"] = account.Sid, ["profileCreated"] = account.HasProfile ? "0" : "1" });
    }

    private static void EnsurePackageExists(string aumid)
    {
        var family = aumid.Split('!')[0];
        try
        {
            var pm = new Windows.Management.Deployment.PackageManager();
            if (!pm.FindPackages(family).Any())
                throw new ValidationException("Cette application n'est pas installée sur ce PC.");
        }
        catch (ValidationException) { throw; }
        catch (Exception ex) { Log.Warn("Kiosk", "vérification du paquet : " + ex.Message); }
    }

    internal static string FirstLine(params string[] texts)
    {
        foreach (var t in texts)
        {
            var line = t.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("Au caractère", StringComparison.Ordinal)
                                                                             && !l.StartsWith("At line", StringComparison.Ordinal));
            if (line is not null) return line.Length > 300 ? line[..300] + "…" : line;
        }
        return "erreur inconnue";
    }

    private static string? ReadStateString(string name) =>
        RegistryAccess.Read(RegHive.LocalMachine, KioskRules.StateKey, name) as string;

    private static void WriteState(KioskAccount account, Plan plan, string? shellValue, string? shellPrev, List<string> restrictions, bool assigned)
    {
        using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(KioskRules.StateKey, true);
        k.SetValue("Mode", plan.Mode);
        k.SetValue("User", account.Name);
        k.SetValue("Sid", account.Sid);
        k.SetValue("Target", plan.Aumid ?? plan.Exe ?? plan.Url ?? "");
        if (shellValue is not null)
        {
            k.SetValue("ShellValue", shellValue);
            k.SetValue("ShellPrev", shellPrev ?? "");
        }
        else
        {
            k.DeleteValue("ShellValue", false);
            k.DeleteValue("ShellPrev", false);
        }
        k.SetValue("AssignedAccess", assigned ? 1 : 0, RegistryValueKind.DWord);
        k.SetValue("Restrictions", restrictions.ToArray(), RegistryValueKind.MultiString);
        k.SetValue("AppliedAt", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
    }
}

/// <summary>Retire tout ce que Timonier a configuré pour la borne (et, sur demande, l'accès attribué et l'ouverture automatique).</summary>
internal sealed class RemoveKioskAction : IActionHandler
{
    public string Id => "kiosk.remove";
    public string Title => "Désactiver le mode kiosque";
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.Bool(p, "assignedAccess");
        Validate.Bool(p, "autologon");
    }

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p)
    {
        var state = KioskState.Read();
        var lines = new List<string>();
        if (state is { IsConfigured: true }) lines.Add($"• Rendre au compte « {state.User} » son Bureau Windows et retirer ses restrictions");
        if (Validate.Bool(p, "assignedAccess")) lines.Add("• Supprimer la configuration d'accès attribué (application unique) de Windows");
        if (Validate.Bool(p, "autologon")) lines.Add("• Désactiver l'ouverture de session automatique");
        if (lines.Count == 0) lines.Add("• Aucune configuration connue : vérification uniquement");
        return "Désactiver le mode kiosque :\n\n" + string.Join("\n", lines);
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var done = new List<string>();
        var warnings = new List<string>();
        var state = KioskState.Read();

        if (state is { IsConfigured: true } && state.Sid is { } sid)
        {
            ctx.Progress?.Report("Restauration du compte kiosque…");
            try
            {
                Validate.Sid(sid);
                var account = new KioskAccount(state.User ?? sid, sid, null, false, true, false, KioskAccounts.ProfilePath(sid));
                using var hive = UserHive.Open(account, createProfile: false);
                if (hive is null) warnings.Add("profil du compte introuvable (compte supprimé ?)");
                else
                {
                    if (state.ShellValue is not null)
                    {
                        var current = hive.ReadEncoded(KioskRules.ShellPolicyKey, KioskRules.ShellPolicyValue);
                        // On ne touche à la valeur que si elle est toujours celle posée par Timonier.
                        if (current == "s:" + state.ShellValue)
                            hive.Restore(KioskRules.ShellPolicyKey, KioskRules.ShellPolicyValue,
                                RegistryAccess.Read(RegHive.LocalMachine, KioskRules.StateKey, "ShellPrev") as string ?? "");
                    }
                    foreach (var entry in state.Restrictions)
                    {
                        var parts = entry.Split('=', 2);
                        if (parts.Length == 2 && KioskRestrictions.Get(parts[0]) is { } r) hive.Restore(r.RegKey, r.Value, parts[1]);
                    }
                    done.Add($"Bureau et droits habituels rendus à « {state.User} »");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Kiosk", "restauration du compte", ex);
                warnings.Add("restauration du compte incomplète : " + ex.Message);
            }
        }

        if (Validate.Bool(p, "assignedAccess") || state?.AssignedAccess == true)
        {
            ctx.Progress?.Report("Suppression de l'accès attribué…");
            var r = await PowerShellRunner.RunAsync(ApplyKioskAction.ClearAssignedAccessScript, null, TimeSpan.FromMinutes(1), ct: ctx.Cancellation);
            if (r.Success) done.Add("accès attribué supprimé");
            else warnings.Add("accès attribué : " + ApplyKioskAction.FirstLine(r.Error, r.Output));
        }

        if (Validate.Bool(p, "autologon"))
        {
            ctx.Progress?.Report("Désactivation de l'ouverture automatique…");
            Autologon.Clear();
            done.Add("ouverture de session automatique désactivée");
        }

        ClearState(keepAutologon: !Validate.Bool(p, "autologon"));
        Log.Info("Kiosk", "mode kiosque retiré");
        var message = done.Count == 0 ? "Aucune configuration de borne à retirer." : char.ToUpper(string.Join(", ", done)[0]) + string.Join(", ", done)[1..] + ".";
        if (warnings.Count > 0) message += " Attention : " + string.Join(" ; ", warnings) + ".";
        return warnings.Count > 0 && done.Count == 0 ? ActionResult.Fail(message) : ActionResult.Ok(message);
    }

    private static void ClearState(bool keepAutologon)
    {
        using var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using (var k = lm.OpenSubKey(KioskRules.StateKey, true))
        {
            if (k is null) return;
            foreach (var name in k.GetValueNames())
                if (!keepAutologon || !name.StartsWith("Autologon", StringComparison.Ordinal)) k.DeleteValue(name, false);
            if (k.ValueCount > 0) return;
        }
        lm.DeleteSubKey(KioskRules.StateKey, false);
    }
}

/// <summary>Lit la configuration d'accès attribué de Windows (Get-AssignedAccess exige l'élévation).</summary>
internal sealed class KioskStatusAction : IActionHandler
{
    public string Id => "kiosk.status";
    public string Title => "Détecter la configuration kiosque de Windows";
    public bool RequiresAdmin => true;

    private const string Script =
        "Import-Module AssignedAccess; $i = Get-AssignedAccess; " +
        "if ($i) { foreach ($x in @($i)) { 'AA|' + $x.UserName + '|' + $x.AppName + '|' + $x.AppUserModelId } } else { 'NONE' }";

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ctx.Progress?.Report("Lecture de la configuration d'accès attribué…");
        var r = await PowerShellRunner.RunAsync(Script, null, TimeSpan.FromMinutes(1), ct: ctx.Cancellation);
        if (!r.Success) return ActionResult.Fail("Lecture impossible : " + ApplyKioskAction.FirstLine(r.Error, r.Output));
        var data = new Dictionary<string, string>();
        var n = 0;
        foreach (var line in r.Output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("AA|", StringComparison.Ordinal)))
        {
            var parts = line.Split('|');
            if (parts.Length < 4) continue;
            data[$"aa{n}.user"] = parts[1];
            data[$"aa{n}.app"] = parts[2];
            data[$"aa{n}.aumid"] = parts[3];
            n++;
        }
        data["aa.count"] = n.ToString(CultureInfo.InvariantCulture);
        return ActionResult.Ok(n == 0 ? "Aucun accès attribué (application unique) n'est configuré dans Windows." : "Accès attribué détecté.", data);
    }
}

/// <summary>Ouverture de session automatique : Winlogon + mot de passe dans un secret LSA (jamais en clair dans le registre).</summary>
internal static class Autologon
{
    public static void Set(string user)
    {
        using var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var k = lm.OpenSubKey(KioskRules.WinlogonKey, true) ?? throw new InvalidOperationException("Clé Winlogon introuvable.");
        using (var state = lm.CreateSubKey(KioskRules.StateKey, true))
        {
            if (state.GetValue("AutologonUser") is null)
            {
                state.SetValue("AutologonPrevUser", k.GetValue("DefaultUserName") as string ?? "");
                state.SetValue("AutologonPrevDomain", k.GetValue("DefaultDomainName") as string ?? "");
            }
            state.SetValue("AutologonUser", user);
        }
        k.SetValue("DefaultUserName", user, RegistryValueKind.String);
        k.SetValue("DefaultDomainName", Environment.MachineName, RegistryValueKind.String);
        k.DeleteValue("DefaultPassword", false);   // jamais de mot de passe en clair
        k.DeleteValue("AutoLogonCount", false);    // sinon Windows désactive l'ouverture automatique après N connexions
        k.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
    }

    public static void Clear()
    {
        using var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using (var k = lm.OpenSubKey(KioskRules.WinlogonKey, true))
        {
            if (k is not null)
            {
                k.SetValue("AutoAdminLogon", "0", RegistryValueKind.String);
                k.DeleteValue("DefaultPassword", false);
                using var state = lm.OpenSubKey(KioskRules.StateKey, true);
                if (state?.GetValue("AutologonPrevUser") is string prevUser && prevUser.Length > 0
                    && string.Equals(k.GetValue("DefaultUserName") as string, state.GetValue("AutologonUser") as string, StringComparison.OrdinalIgnoreCase))
                {
                    k.SetValue("DefaultUserName", prevUser, RegistryValueKind.String);
                    if (state.GetValue("AutologonPrevDomain") is string d && d.Length > 0) k.SetValue("DefaultDomainName", d, RegistryValueKind.String);
                }
            }
        }
        KioskNative.StorePrivateData("DefaultPassword", null);
        using var s = lm.OpenSubKey(KioskRules.StateKey, true);
        if (s is null) return;
        foreach (var name in new[] { "AutologonUser", "AutologonPrevUser", "AutologonPrevDomain" }) s.DeleteValue(name, false);
    }
}

internal sealed class SetAutologonAction : IActionHandler
{
    public string Id => "kiosk.autologon.set";
    public string Title => "Ouvrir automatiquement la session du compte kiosque";
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.LocalUserName(Validate.Required(p, "user", 20));
        if (p.TryGetValue("password", out var pw) && pw.Length > 127) throw new ValidationException("Mot de passe trop long (127 caractères au maximum).");
    }

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p) =>
        $"Ouvrir automatiquement la session « {Validate.LocalUserName(p["user"])} » au démarrage du PC.\n\n"
        + "Toute personne qui allume ce PC arrivera directement sur la borne, sans mot de passe. "
        + "Le mot de passe est stocké dans un secret protégé du système (LSA), jamais en clair dans le registre.";

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var account = KioskAccounts.ResolveEligible(p["user"], ctx.UserSid);
        var password = p.TryGetValue("password", out var pw) ? pw : "";
        ctx.Progress?.Report("Vérification du mot de passe…");
        var check = KioskNative.CheckCredentials(account.Name, password);
        if (check == KioskNative.ERROR_LOGON_FAILURE)
            return Task.FromResult(ActionResult.Fail($"Mot de passe incorrect pour « {account.Name} ». L'ouverture automatique n'a pas été configurée."));
        if (check != 0) Log.Warn("Kiosk", $"vérification des identifiants : code {check}");

        KioskNative.StorePrivateData("DefaultPassword", password);
        Autologon.Set(account.Name);
        Log.Info("Kiosk", "ouverture de session automatique configurée");
        return Task.FromResult(ActionResult.Ok($"La session « {account.Name} » s'ouvrira automatiquement au prochain démarrage."));
    }
}

internal sealed class ClearAutologonAction : IActionHandler
{
    public string Id => "kiosk.autologon.clear";
    public string Title => "Désactiver l'ouverture de session automatique";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        Autologon.Clear();
        Log.Info("Kiosk", "ouverture de session automatique désactivée");
        return Task.FromResult(ActionResult.Ok("Ouverture de session automatique désactivée : le mot de passe mémorisé a été effacé."));
    }
}
