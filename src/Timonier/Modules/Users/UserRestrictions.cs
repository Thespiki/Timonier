using System.Text.RegularExpressions;
using Microsoft.Win32;
using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Users;

/// <summary>Restriction écrite dans la ruche d'un utilisateur (stratégie « Configuration utilisateur » documentée).</summary>
internal sealed record RestrictionDef(string Key, string Title, string Description, string Glyph, string RegPath, string ValueName, int[] Values)
{
    /// <summary>Remarque honnête sur les limites (édition, contournement…).</summary>
    public string? Note { get; init; }
    public bool EnterpriseOrEducationOnly { get; init; }
}

/// <summary>
/// Liste blanche des restrictions par compte. Seules ces clés/valeurs peuvent être écrites par le broker, et uniquement
/// dans la ruche d'un compte standard qui n'est pas celui de la session en cours.
/// </summary>
internal static partial class UserRestrictions
{
    private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string SysPol = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string ActiveDesktop = @"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop";
    private const string PolSystem = @"Software\Policies\Microsoft\Windows\System";
    private const string Store = @"Software\Policies\Microsoft\WindowsStore";
    public const string DisallowRunKey = "DisallowRun";
    private const string DisallowRunList = Explorer + @"\DisallowRun";
    public const int MaxBlockedApps = 50;

    [GeneratedRegex(@"^[A-Za-z0-9 ._-]{1,64}\.exe$", RegexOptions.IgnoreCase)] private static partial Regex ExeRx();

    public static readonly RestrictionDef[] All =
    [
        new("NoControlPanel", L("Panneau de configuration et Paramètres"),
            L("Bloque l'application Paramètres et le Panneau de configuration (le raccourci Windows + I compris)."),
            "", Explorer, "NoControlPanel", [0, 1]),
        new("DisableTaskMgr", L("Gestionnaire des tâches"),
            L("Empêche d'ouvrir le Gestionnaire des tâches, donc de fermer de force une application ou de voir les processus."),
            "", SysPol, "DisableTaskMgr", [0, 1]),
        new("DisableRegistryTools", L("Éditeur du Registre"),
            L("Empêche de lancer regedit, y compris l'import silencieux de fichiers .reg."),
            "", SysPol, "DisableRegistryTools", [0, 1]),
        new("DisableCMD", L("Invite de commandes"),
            L("Bloque cmd.exe. Choisissez si les scripts .bat/.cmd (parfois utilisés à l'ouverture de session) restent autorisés."),
            "", PolSystem, "DisableCMD", [0, 1, 2])
        {
            Note = L("Ne bloque pas PowerShell : ajoutez powershell.exe et pwsh.exe à la liste d'applications interdites si besoin."),
        },
        new("NoRun", L("Boîte de dialogue Exécuter"),
            L("Retire « Exécuter » du menu Démarrer et désactive le raccourci Windows + R."),
            "", Explorer, "NoRun", [0, 1]),
        new("NoChangingWallPaper", L("Changement du fond d'écran"),
            L("Empêche de modifier l'arrière-plan du bureau depuis Paramètres › Personnalisation."),
            "", ActiveDesktop, "NoChangingWallPaper", [0, 1]),
        new(DisallowRunKey, L("Applications interdites"),
            L("Empêche l'Explorateur de lancer les programmes listés (nom du fichier .exe)."),
            "", Explorer, "DisallowRun", [0, 1])
        {
            Note = L("Protection légère : ne concerne que les lancements depuis l'Explorateur (menu Démarrer, bureau, dossiers). Un programme renommé ou lancé par un autre programme n'est pas bloqué."),
        },
        new("RemoveWindowsStore", "Microsoft Store",
            L("Empêche d'ouvrir l'application Microsoft Store (installation d'applications et de jeux)."),
            "", Store, "RemoveWindowsStore", [0, 1])
        {
            Note = L("Windows n'applique cette stratégie que sur les éditions Entreprise et Éducation."),
            EnterpriseOrEducationOnly = true,
        },
    ];

    public static RestrictionDef? Find(string key) => All.FirstOrDefault(d => d.Key == key);

    /// <summary>Valide une liste de noms d'exécutables (séparés par |), dédoublonnée, 50 au maximum.</summary>
    public static List<string> ParseAppList(string? raw)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        foreach (var part in raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ExeRx().IsMatch(part) || part.Contains("..")) throw new ValidationException(L("Nom de programme invalide : « {0} » (exemple attendu : jeu.exe).", part));
            if (!list.Contains(part, StringComparer.OrdinalIgnoreCase)) list.Add(part);
        }
        if (list.Count > MaxBlockedApps) throw new ValidationException(LP(MaxBlockedApps, "{0} programme au maximum.", "{0} programmes au maximum."));
        return list;
    }

    public static bool IsValidExeName(string name) => ExeRx().IsMatch(name) && !name.Contains("..");

    /// <summary>Version tolérante pour l'affichage : ignore les entrées invalides au lieu d'échouer.</summary>
    public static List<string> ParseAppListSafe(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? [] :
        [.. raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Where(IsValidExeName).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxBlockedApps)];

    // ------------------------------------------------------------------ Accès à la ruche (processus élevé)

    public enum HiveSource { Loaded, File, Missing }

    private static readonly Lock HiveGate = new();

    /// <summary>
    /// Ouvre la ruche utilisateur du SID : HKU\SID si la session est chargée, sinon NTUSER.DAT du profil chargé sous un
    /// nom temporaire unique (RegLoadKey), déchargé dans tous les cas à la fin. Le profil doit exister.
    /// </summary>
    public static T WithUserHive<T>(string sid, bool writable, Func<RegistryKey?, HiveSource, T> work)
    {
        using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        using (var loaded = users.OpenSubKey(sid, writable))
        {
            if (loaded is not null) return work(loaded, HiveSource.Loaded);
        }

        var hiveFile = ProfileHiveFile(sid);
        if (hiveFile is null) return work(null, HiveSource.Missing);

        var mount = "Timonier_" + Guid.NewGuid().ToString("N");
        // Les privilèges sont propres au processus : deux requêtes simultanées ne doivent pas les retirer l'une à l'autre.
        lock (HiveGate)
        {
            try
            {
                NetApi.SetPrivilege("SeRestorePrivilege", true);
                NetApi.SetPrivilege("SeBackupPrivilege", true);
                var rc = NetApi.RegLoadKey(NetApi.HKEY_USERS, mount, hiveFile);
                if (rc != 0)
                {
                    throw rc == 32
                        ? new InvalidOperationException(L("Le profil de ce compte est en cours d'utilisation : réessayez après sa déconnexion."))
                        : new System.ComponentModel.Win32Exception(rc, L("Chargement du profil impossible (code {0}).", rc));
                }
                try
                {
                    using var key = users.OpenSubKey(mount, writable) ?? throw new InvalidOperationException(L("Ruche chargée introuvable."));
                    return work(key, HiveSource.File);
                }
                finally
                {
                    Unload(mount);
                }
            }
            finally
            {
                try { NetApi.SetPrivilege("SeBackupPrivilege", false); NetApi.SetPrivilege("SeRestorePrivilege", false); }
                catch { /* sans conséquence : le broker est éphémère */ }
            }
        }
    }

    private static void Unload(string mount)
    {
        // Le déchargement exige SeRestorePrivilege : réactivé au cas où une autre requête (kiosque) l'aurait retiré.
        try { NetApi.SetPrivilege("SeRestorePrivilege", true); }
        catch (Exception ex) { Log.Warn("Users", "privilège de restauration : " + ex.Message); }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var rc = NetApi.RegUnLoadKey(NetApi.HKEY_USERS, mount);
            if (rc == 0) return;
            // Des poignées encore ouvertes (finaliseurs en attente) empêchent le déchargement : on insiste un peu.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(200);
        }
        Log.Warn("Users", "ruche temporaire non déchargée : " + mount);
    }

    /// <summary>Chemin validé de NTUSER.DAT (ProfileList\SID\ProfileImagePath), ou null si le profil n'existe pas encore.</summary>
    private static string? ProfileHiveFile(string sid)
    {
        var raw = AccountParams.ProfilePath(sid);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string dir;
        try { dir = Validate.ExistingLocalDirectory(raw); }
        catch (ValidationException) { return null; }
        var file = Path.Combine(dir, "NTUSER.DAT");
        return File.Exists(file) ? file : null;
    }

    public static Dictionary<string, string> ReadAll(RegistryKey root)
    {
        var data = new Dictionary<string, string>();
        foreach (var d in All)
        {
            using var k = root.OpenSubKey(d.RegPath, false);
            var v = k?.GetValue(d.ValueName) switch { int i => i, _ => 0 };
            data[d.Key] = d.Values.Contains(v) ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : (v != 0 ? "1" : "0");
        }
        using (var list = root.OpenSubKey(DisallowRunList, false))
        {
            var names = list?.GetValueNames()
                .Select(n => list.GetValue(n) as string)
                .Where(s => s is not null && IsValidExeName(s))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxBlockedApps)
                .ToList() ?? [];
            data["DisallowRunList"] = string.Join("|", names);
        }
        return data;
    }

    public static void WriteValue(RegistryKey root, RestrictionDef d, int value)
    {
        if (value == 0)
        {
            using var k = root.OpenSubKey(d.RegPath, true);
            k?.DeleteValue(d.ValueName, false);
            return;
        }
        using var key = root.CreateSubKey(d.RegPath, true);
        key.SetValue(d.ValueName, value, RegistryValueKind.DWord);
    }

    public static void WriteAppList(RegistryKey root, List<string> apps)
    {
        root.DeleteSubKeyTree(DisallowRunList, false);
        var d = Find(DisallowRunKey)!;
        if (apps.Count == 0)
        {
            WriteValue(root, d, 0);
            return;
        }
        using (var list = root.CreateSubKey(DisallowRunList, true))
        {
            for (var i = 0; i < apps.Count; i++) list.SetValue((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), apps[i], RegistryValueKind.String);
        }
        WriteValue(root, d, 1);
    }

    /// <summary>Contrôles communs : compte standard existant, autre que celui de la session.</summary>
    public static LocalAccount ResolveTarget(string sid, string? clientSid)
    {
        var (target, _) = LocalAccounts.Resolve(sid, clientSid);
        LocalAccounts.RefuseSessionAccount(target, clientSid, L("Par sécurité, Timonier refuse de restreindre le compte avec lequel vous êtes connecté."));
        if (target.IsAdmin) throw new ValidationException(L("Les restrictions sont réservées aux comptes standard : un administrateur pourrait les retirer lui-même."));
        if (target.IsBuiltIn) throw new ValidationException(L("Les comptes intégrés de Windows ne sont pas concernés."));
        return target;
    }

    public static string ProfileMissingMessage =>
        L("Ce compte n'a encore jamais ouvert de session : son profil n'existe pas. Demandez-lui de se connecter une fois, puis revenez ici.");
}

/// <summary>Lit l'état des restrictions d'un compte standard (ruche chargée temporairement si besoin).</summary>
public sealed class GetRestrictionsAction : IActionHandler
{
    public string Id => "users.restrictions.get";
    public string Title => L("Lire les restrictions d'un compte");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => AccountParams.Sid(p);

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var target = UserRestrictions.ResolveTarget(AccountParams.Sid(p), ctx.UserSid);
        var result = UserRestrictions.WithUserHive(target.Sid, writable: false, (root, source) =>
        {
            if (root is null) return ActionResult.Ok(UserRestrictions.ProfileMissingMessage, new Dictionary<string, string> { ["profile"] = "missing" });
            var data = UserRestrictions.ReadAll(root);
            data["profile"] = source == UserRestrictions.HiveSource.Loaded ? "loaded" : "file";
            return ActionResult.Ok(L("Restrictions de « {0} » lues.", target.Name), data);
        });
        return Task.FromResult(result);
    }
}

/// <summary>
/// Écrit des restrictions (clés de la liste blanche uniquement) dans la ruche d'un compte standard.
/// Paramètres : sid, puis une ou plusieurs clés (NoControlPanel=0|1, DisableCMD=0|1|2, DisallowRunList=a.exe|b.exe…).
/// </summary>
public sealed class SetRestrictionsAction : IActionHandler
{
    public string Id => "users.restrictions.set";
    public string Title => L("Restrictions d'un compte");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Parse(p);

    private static (string Sid, Dictionary<RestrictionDef, int> Values, List<string>? Apps) Parse(IReadOnlyDictionary<string, string> p)
    {
        var sid = AccountParams.Sid(p);
        var values = new Dictionary<RestrictionDef, int>();
        List<string>? apps = null;
        foreach (var (key, raw) in p)
        {
            if (key == "sid") continue;
            if (key == "DisallowRunList")
            {
                if (raw.Length > 50 * 70) throw new ValidationException(L("Liste de programmes trop longue."));
                apps = UserRestrictions.ParseAppList(raw);
                continue;
            }
            var def = UserRestrictions.Find(key) ?? throw new ValidationException(L("Restriction inconnue : {0}", key));
            if (def.Key == UserRestrictions.DisallowRunKey) throw new ValidationException(L("Utilisez DisallowRunList pour la liste des programmes."));
            if (!int.TryParse(raw, out var v) || !def.Values.Contains(v)) throw new ValidationException(L("Valeur non autorisée pour {0}.", key));
            values[def] = v;
        }
        if (values.Count == 0 && apps is null) throw new ValidationException(L("Aucune restriction à modifier."));
        return (sid, values, apps);
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        var (sid, values, apps) = Parse(p);
        var target = UserRestrictions.ResolveTarget(sid, ctx.UserSid);
        var result = UserRestrictions.WithUserHive(target.Sid, writable: true, (root, source) =>
        {
            if (root is null) return ActionResult.Fail(UserRestrictions.ProfileMissingMessage);
            foreach (var (def, v) in values) UserRestrictions.WriteValue(root, def, v);
            if (apps is not null) UserRestrictions.WriteAppList(root, apps);
            root.Flush();
            var data = UserRestrictions.ReadAll(root);
            data["profile"] = source == UserRestrictions.HiveSource.Loaded ? "loaded" : "file";
            Log.Info("Users", $"restrictions {target.Name} : {values.Count} valeur(s){(apps is null ? "" : $", {apps.Count} programme(s)")}");
            var msg = source == UserRestrictions.HiveSource.Loaded
                ? L("Restrictions de « {0} » enregistrées. Sa session est ouverte : certaines ne s'appliqueront qu'à sa prochaine connexion.", target.Name)
                : L("Restrictions de « {0} » enregistrées : elles s'appliqueront à sa prochaine ouverture de session.", target.Name);
            return ActionResult.Ok(msg, data);
        });
        return Task.FromResult(result);
    }
}
