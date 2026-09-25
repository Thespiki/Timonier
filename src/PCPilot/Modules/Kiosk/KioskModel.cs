using System.Text.RegularExpressions;
using Microsoft.Win32;
using PcPilot.Core.Platform;
using PcPilot.Core.Security;

namespace PcPilot.Modules.Kiosk;

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
        Store => "Application du Store en plein écran (accès attribué)",
        Edge => "Microsoft Edge en mode kiosque",
        Win32 => "Application classique à la place du Bureau",
        _ => "Inconnu",
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
        new("taskmgr", "Bloquer le Gestionnaire des tâches",
            "Le Gestionnaire des tâches ne s'ouvre plus (ni par Ctrl+Maj+Échap, ni depuis l'écran Ctrl+Alt+Suppr) : impossible de fermer l'application de la borne ou d'en lancer une autre par ce biais.",
            System, "DisableTaskMgr", 1, true),
        new("norun", "Supprimer la commande « Exécuter »",
            "Retire « Exécuter » du menu Démarrer et désactive Win+R, pour que l'utilisateur ne puisse pas lancer un programme en tapant son nom.",
            Explorer, "NoRun", 1, true),
        new("controlpanel", "Bloquer le Panneau de configuration et les Paramètres",
            "Empêche d'ouvrir le Panneau de configuration et l'application Paramètres depuis ce compte.",
            Explorer, "NoControlPanel", 1, true),
        new("regedit", "Bloquer l'Éditeur du Registre",
            "Regedit refuse de s'ouvrir pour ce compte, y compris en mode silencieux (fichiers .reg).",
            System, "DisableRegistryTools", 1, true),
        new("cmd", "Bloquer l'invite de commandes",
            "L'invite de commandes interactive (cmd.exe) est refusée ; les scripts .bat des applications continuent de fonctionner. PowerShell n'est pas concerné par cette stratégie.",
            WinSystem, "DisableCMD", 2, true),
        new("winkeys", "Désactiver les raccourcis de la touche Windows",
            "Les raccourcis Win+E, Win+R, Win+X, etc. sont ignorés. Utile surtout si l'Explorateur peut être ouvert depuis l'application.",
            Explorer, "NoWinKeys", 1, true),
        new("contextmenu", "Supprimer le menu contextuel de l'Explorateur",
            "Le clic droit n'affiche plus de menu sur le Bureau ni dans l'Explorateur de fichiers.",
            Explorer, "NoViewContextMenu", 1, true),
        new("noclose", "Masquer Arrêter et Redémarrer",
            "Retire Arrêter, Redémarrer, Veille et Veille prolongée du menu Démarrer et de l'écran Ctrl+Alt+Suppr pour ce compte. Le bouton d'alimentation physique fonctionne toujours.",
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
            var r = Get(raw) ?? throw new ValidationException("Restriction inconnue : " + raw);
            if (!result.Contains(r)) result.Add(r);
        }
        return result;
    }
}

/// <summary>Validation et chemins partagés entre l'interface et le broker.</summary>
internal static partial class KioskRules
{
    /// <summary>Clé propre à PC Pilot (HKLM) où l'on mémorise ce qui a été configuré, pour pouvoir le retirer proprement.</summary>
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
            throw new ValidationException("Adresse web invalide : elle doit commencer par http:// ou https:// et ne contenir ni espace ni guillemet.");
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
        return "Ce programme se trouve dans le dossier personnel d'un autre utilisateur : le compte kiosque n'y aura pas accès. "
             + "Installez-le pour tous les utilisateurs (par exemple dans Program Files).";
    }

    public static string ExeProblemCheck(string exe)
    {
        var name = Path.GetFileName(exe);
        string[] refused = ["explorer.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "regedit.exe", "taskmgr.exe", "mmc.exe"];
        if (refused.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException($"« {name} » ne peut pas servir d'application de borne (outil système ou Bureau Windows).");
        return exe;
    }
}

/// <summary>Configuration mémorisée par PC Pilot dans HKLM\SOFTWARE\PCPilot\Kiosk (lisible sans élévation).</summary>
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
