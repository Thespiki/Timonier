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
        new("NoControlPanel", L("Control Panel and Settings"),
            L("Blocks the Settings app and Control Panel (including the Windows + I shortcut)."),
            "", Explorer, "NoControlPanel", [0, 1]),
        new("DisableTaskMgr", L("Task Manager"),
            L("Prevents opening Task Manager, and therefore force-closing an app or viewing processes."),
            "", SysPol, "DisableTaskMgr", [0, 1]),
        new("DisableRegistryTools", L("Registry Editor"),
            L("Prevents running regedit, including silent import of .reg files."),
            "", SysPol, "DisableRegistryTools", [0, 1]),
        new("DisableCMD", L("Command Prompt"),
            L("Blocks cmd.exe. Choose whether .bat/.cmd scripts (sometimes used at sign-in) stay allowed."),
            "", PolSystem, "DisableCMD", [0, 1, 2])
        {
            Note = L("Doesn't block PowerShell: add powershell.exe and pwsh.exe to the list of blocked apps if needed."),
        },
        new("NoRun", L("Run dialog box"),
            L("Removes “Run” from the Start menu and turns off the Windows + R shortcut."),
            "", Explorer, "NoRun", [0, 1]),
        new("NoChangingWallPaper", L("Changing the wallpaper"),
            L("Prevents changing the desktop background from Settings › Personalization."),
            "", ActiveDesktop, "NoChangingWallPaper", [0, 1]),
        new(DisallowRunKey, L("Blocked apps"),
            L("Prevents File Explorer from starting the listed programs (.exe file name)."),
            "", Explorer, "DisallowRun", [0, 1])
        {
            Note = L("Light protection: only covers launches from File Explorer (Start menu, desktop, folders). A renamed program or one started by another program isn't blocked."),
        },
        new("RemoveWindowsStore", "Microsoft Store",
            L("Prevents opening the Microsoft Store app (installing apps and games)."),
            "", Store, "RemoveWindowsStore", [0, 1])
        {
            Note = L("Windows only applies this policy on the Enterprise and Education editions."),
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
            if (!ExeRx().IsMatch(part) || part.Contains("..")) throw new ValidationException(L("Invalid program name: “{0}” (expected example: game.exe).", part));
            if (!list.Contains(part, StringComparer.OrdinalIgnoreCase)) list.Add(part);
        }
        if (list.Count > MaxBlockedApps) throw new ValidationException(LP(MaxBlockedApps, "{0} program maximum.", "{0} programs maximum."));
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
                        ? new InvalidOperationException(L("This account's profile is in use: try again after it signs out."))
                        : new System.ComponentModel.Win32Exception(rc, L("Couldn't load the profile (code {0}).", rc));
                }
                try
                {
                    using var key = users.OpenSubKey(mount, writable) ?? throw new InvalidOperationException(L("Loaded hive not found."));
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
        LocalAccounts.RefuseSessionAccount(target, clientSid, L("For security, Timonier refuses to restrict the account you're signed in with."));
        if (target.IsAdmin) throw new ValidationException(L("Restrictions are reserved for standard accounts: an administrator could remove them themselves."));
        if (target.IsBuiltIn) throw new ValidationException(L("Windows built-in accounts aren't affected."));
        return target;
    }

    public static string ProfileMissingMessage =>
        L("This account has never signed in yet: its profile doesn't exist. Ask the person to sign in once, then come back here.");
}

/// <summary>Lit l'état des restrictions d'un compte standard (ruche chargée temporairement si besoin).</summary>
public sealed class GetRestrictionsAction : IActionHandler
{
    public string Id => "users.restrictions.get";
    public string Title => L("Read an account's restrictions");
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
            return ActionResult.Ok(L("Restrictions for “{0}” read.", target.Name), data);
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
    public string Title => LC("feature name", "Account restrictions");
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
                if (raw.Length > 50 * 70) throw new ValidationException(L("Program list too long."));
                apps = UserRestrictions.ParseAppList(raw);
                continue;
            }
            var def = UserRestrictions.Find(key) ?? throw new ValidationException(L("Unknown restriction: {0}", key));
            if (def.Key == UserRestrictions.DisallowRunKey) throw new ValidationException(L("Use DisallowRunList for the list of programs."));
            if (!int.TryParse(raw, out var v) || !def.Values.Contains(v)) throw new ValidationException(L("Value not allowed for {0}.", key));
            values[def] = v;
        }
        if (values.Count == 0 && apps is null) throw new ValidationException(L("No restrictions to change."));
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
                ? L("Restrictions for “{0}” saved. They're signed in: some will only apply at their next sign-in.", target.Name)
                : L("Restrictions for “{0}” saved: they'll apply at their next sign-in.", target.Name);
            return ActionResult.Ok(msg, data);
        });
        return Task.FromResult(result);
    }
}
