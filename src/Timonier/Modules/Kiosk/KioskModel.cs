using System.Text.RegularExpressions;
using Microsoft.Win32;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Kiosk;

/// <summary>Modes de borne proposés par l'assistant.</summary>
internal static class KioskModes
{
    /// <summary>Application du Store en plein écran (accès attribué à application unique, Set-AssignedAccess).</summary>
    public const string Store = "store";
    /// <summary>Microsoft Edge en mode kiosque, lancé comme interpréteur de commandes du compte.</summary>
    public const string Edge = "edge";
    /// <summary>Application classique (.exe) lancée comme interpréteur de commandes du compte.</summary>
    public const string Win32 = "win32";

    public static string Label(string? mode) => mode switch
    {
        Store => L("Full-screen Store app (assigned access)"),
        Edge => L("Microsoft Edge in kiosk mode"),
        Win32 => L("Classic app instead of the desktop"),
        _ => L("Unknown"),
    };
}

/// <summary>Restriction de stratégie utilisateur appliquée dans la ruche du compte kiosque (stratégies ADMX documentées).</summary>
internal sealed record KioskRestriction(string Key, string Title, string Description, string RegKey, string Value, int Data, bool Default);

internal static class KioskRestrictions
{
    private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string System = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string WinSystem = @"Software\Policies\Microsoft\Windows\System";

    public static readonly IReadOnlyList<KioskRestriction> All =
    [
        new("taskmgr", L("Block Task Manager"),
            L("Task Manager no longer opens (neither with Ctrl+Shift+Esc nor from the Ctrl+Alt+Del screen): it can't be used to close the kiosk app or start another one."),
            System, "DisableTaskMgr", 1, true),
        new("norun", L("Remove the “Run” command"),
            L("Removes “Run” from the Start menu and disables Win+R, so the user can't start a program by typing its name."),
            Explorer, "NoRun", 1, true),
        new("controlpanel", L("Block Control Panel and Settings"),
            L("Prevents this account from opening Control Panel and the Settings app."),
            Explorer, "NoControlPanel", 1, true),
        new("regedit", L("Block Registry Editor"),
            L("Regedit refuses to open for this account, including in silent mode (.reg files)."),
            System, "DisableRegistryTools", 1, true),
        new("cmd", L("Block Command Prompt"),
            L("The interactive Command Prompt (cmd.exe) is blocked; apps' .bat scripts keep working. PowerShell isn't affected by this policy."),
            WinSystem, "DisableCMD", 2, true),
        new("winkeys", L("Turn off Windows key shortcuts"),
            L("Win+E, Win+R, Win+X and other shortcuts are ignored. Mostly useful if File Explorer can be opened from the app."),
            Explorer, "NoWinKeys", 1, true),
        new("contextmenu", L("Remove the File Explorer context menu"),
            L("Right-clicking no longer shows a menu on the desktop or in File Explorer."),
            Explorer, "NoViewContextMenu", 1, true),
        new("noclose", L("Hide Shut down and Restart"),
            L("Removes Shut down, Restart, Sleep and Hibernate from the Start menu and the Ctrl+Alt+Del screen for this account. The physical power button still works."),
            Explorer, "NoClose", 1, false),
    ];

    public static KioskRestriction? Get(string key) => All.FirstOrDefault(r => r.Key == key);

    /// <summary>Liste de clés séparées par des virgules, chacune dans la liste blanche.</summary>
    public static List<KioskRestriction> Parse(string? list)
    {
        var result = new List<KioskRestriction>();
        if (string.IsNullOrWhiteSpace(list)) return result;
        foreach (var raw in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var r = Get(raw) ?? throw new ValidationException(L("Unknown restriction: {0}", raw));
            if (!result.Contains(r)) result.Add(r);
        }
        return result;
    }
}

/// <summary>Validation et chemins partagés entre l'interface et le broker.</summary>
internal static partial class KioskRules
{
    /// <summary>Clé propre à Timonier (HKLM) où l'on mémorise ce qui a été configuré, pour pouvoir le retirer proprement.</summary>
    public const string StateKey = AppPaths.MachineRegistryKey + @"\Kiosk";

    /// <summary>Stratégie « Interface utilisateur personnalisée » (System.admx, configuration utilisateur).</summary>
    public const string ShellPolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string ShellPolicyValue = "Shell";

    public const string WinlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

    [GeneratedRegex(@"^https?://[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]{1,2040}$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRx();

    /// <summary>URL http/https sans espace ni guillemet (elle est placée telle quelle dans la ligne de lancement d'Edge).</summary>
    public static string Url(string value)
    {
        var v = value.Trim();
        if (!UrlRx().IsMatch(v) || !Uri.TryCreate(v, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || string.IsNullOrEmpty(uri.Host))
            throw new ValidationException(L("Invalid web address: it must start with http:// or https:// and contain no spaces or quotes."));
        return v;
    }

    public static bool TryUrl(string value, out string? error)
    {
        try { Url(value); error = null; return true; }
        catch (ValidationException ex) { error = ex.Message; return false; }
    }

    /// <summary>Chemin de Microsoft Edge (installation système), ou null s'il est absent.</summary>
    public static string? EdgePath()
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ];
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            var p = Path.Combine(root, @"Microsoft\Edge\Application\msedge.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Ligne de lancement d'Edge en mode kiosque (options documentées par Microsoft).</summary>
    public static string EdgeCommandLine(string edgePath, string url, bool publicBrowsing, int idleMinutes)
    {
        var line = $"\"{edgePath}\" --kiosk {url} --edge-kiosk-type={(publicBrowsing ? "public-browsing" : "fullscreen")} --no-first-run";
        if (idleMinutes > 0) line += $" --kiosk-idle-timeout-minutes={idleMinutes}";
        return line;
    }

    public static string ProfilesDirectory()
    {
        var raw = RegistryAccess.Read(Core.Model.RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList", "ProfilesDirectory") as string;
        return Environment.ExpandEnvironmentVariables(string.IsNullOrEmpty(raw) ? @"%SystemDrive%\Users" : raw);
    }

    /// <summary>
    /// Un programme situé dans le profil d'un AUTRE utilisateur n'est pas accessible au compte kiosque :
    /// renvoie un message d'erreur, ou null si l'emplacement convient.
    /// </summary>
    public static string? ExeLocationProblem(string exe, string? kioskProfile)
    {
        var profiles = ProfilesDirectory().TrimEnd('\\') + "\\";
        if (!exe.StartsWith(profiles, StringComparison.OrdinalIgnoreCase)) return null;
        var pub = Path.Combine(profiles, "Public") + "\\";
        if (exe.StartsWith(pub, StringComparison.OrdinalIgnoreCase)) return null;
        if (kioskProfile is not null && exe.StartsWith(kioskProfile.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return null;
        return L("This program is in another user's personal folder: the kiosk account won't be able to access it. Install it for all users (for example in Program Files).");
    }

    /// <summary>Mot de passe facultatif : 127 caractères au plus, sans caractère de contrôle. Jamais journalisé.</summary>
    public static string Password(IReadOnlyDictionary<string, string> p)
    {
        if (!p.TryGetValue("password", out var pw) || pw is null) return "";
        if (pw.Length > 127) throw new ValidationException(L("Password too long (127 characters maximum)."));
        if (pw.Any(char.IsControl)) throw new ValidationException(L("The password contains characters that aren't allowed."));
        return pw;
    }

    public static string ExeProblemCheck(string exe)
    {
        var name = Path.GetFileName(exe);
        string[] refused = ["explorer.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "regedit.exe", "taskmgr.exe", "mmc.exe"];
        if (refused.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException(L("“{0}” can't be used as a kiosk app (system tool or Windows desktop).", name));
        return exe;
    }
}

/// <summary>Configuration mémorisée par Timonier dans HKLM\SOFTWARE\Timonier\Kiosk (lisible sans élévation).</summary>
internal sealed class KioskState
{
    public string? Mode { get; init; }
    public string? User { get; init; }
    public string? Sid { get; init; }
    public string? Target { get; init; }
    public string? ShellValue { get; init; }
    public bool AssignedAccess { get; init; }
    public string[] Restrictions { get; init; } = [];
    public DateTime? AppliedAt { get; init; }

    public bool IsConfigured => !string.IsNullOrEmpty(Sid);

    public static KioskState? Read()
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(KioskRules.StateKey, false);
            if (k is null) return null;
            return new KioskState
            {
                Mode = k.GetValue("Mode") as string,
                User = k.GetValue("User") as string,
                Sid = k.GetValue("Sid") as string,
                Target = k.GetValue("Target") as string,
                ShellValue = k.GetValue("ShellValue") as string,
                AssignedAccess = k.GetValue("AssignedAccess") is int a && a == 1,
                Restrictions = k.GetValue("Restrictions") as string[] ?? [],
                AppliedAt = DateTime.TryParse(k.GetValue("AppliedAt") as string, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null,
            };
        }
        catch (Exception ex)
        {
            Log.Warn("Kiosk", "lecture de l'état : " + ex.Message);
            return null;
        }
    }

    /// <summary>Clés des restrictions posées (les entrées sont au format « clé=ancienne valeur »).</summary>
    public IEnumerable<string> RestrictionKeys => Restrictions.Select(r => r.Split('=')[0]);
}

/// <summary>État de l'ouverture de session automatique (valeurs Winlogon lisibles sans élévation).</summary>
internal sealed record AutologonInfo(bool Enabled, string? User, string? Domain, bool PlainPasswordInRegistry)
{
    public static AutologonInfo Read()
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(KioskRules.WinlogonKey, false);
            if (k is null) return new(false, null, null, false);
            var enabled = (k.GetValue("AutoAdminLogon") as string) == "1";
            return new(enabled, k.GetValue("DefaultUserName") as string, k.GetValue("DefaultDomainName") as string,
                k.GetValue("DefaultPassword") is string);
        }
        catch { return new(false, null, null, false); }
    }
}
