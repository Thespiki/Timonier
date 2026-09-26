using Microsoft.Win32;

namespace Timonier.Core.Model;

/// <summary>Ruche de registre ciblée par une opération.</summary>
public enum RegHive
{
    /// <summary>HKEY_CURRENT_USER de l'utilisateur de la session (dans le broker : HKU\&lt;SID du client&gt;).</summary>
    CurrentUser,
    /// <summary>HKEY_LOCAL_MACHINE (admin requis en écriture).</summary>
    LocalMachine,
    /// <summary>HKEY_USERS\.DEFAULT (écran de connexion, admin requis).</summary>
    DefaultUser,
}

public enum ServiceStartKind
{
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4,
    /// <summary>Automatique (début différé) : Start=2 + DelayedAutostart=1.</summary>
    AutomaticDelayed = 102,
}

/// <summary>
/// Opération élémentaire, déclarative et sans effet de bord par elle-même.
/// L'exécution est faite par <see cref="Timonier.Core.Engine.OperationExecutor"/>, qui journalise
/// l'état précédent pour permettre l'annulation.
/// Les opérations sont définies dans le code (catalogue compilé) : elles ne transitent JAMAIS
/// par le canal du broker, seul l'identifiant du réglage y circule.
/// </summary>
public abstract record Operation
{
    /// <summary>Vrai si l'opération ne peut s'exécuter qu'avec des droits administrateur.</summary>
    public abstract bool RequiresAdmin { get; }

    /// <summary>Description lisible (FR) affichée dans le panneau « Ce que ça change ».</summary>
    public abstract string Describe();
}

/// <summary>Écrit une valeur de registre (crée la clé si nécessaire).</summary>
public sealed record RegSet(RegHive Hive, string Key, string Name, RegistryValueKind Kind, object Value) : Operation
{
    public override bool RequiresAdmin => RegPaths.RequiresAdmin(Hive, Key);
    public override string Describe() =>
        L("Registre : {0} = {1}", $"{RegPaths.Display(Hive, Key)}\\{(Name.Length == 0 ? L("(par défaut)") : Name)}", RegPaths.FormatValue(Kind, Value));
}

/// <summary>Supprime une valeur de registre (état « par défaut de Windows » le plus souvent).</summary>
public sealed record RegDeleteValue(RegHive Hive, string Key, string Name) : Operation
{
    public override bool RequiresAdmin => RegPaths.RequiresAdmin(Hive, Key);
    public override string Describe() => L("Registre : supprime {0} (valeur par défaut de Windows)", $"{RegPaths.Display(Hive, Key)}\\{Name}");
}

/// <summary>
/// Supprime une clé et son contenu. Réservé aux clés que Timonier sait recréer (ex. menu contextuel classique) :
/// le contenu est sauvegardé intégralement dans le journal avant suppression.
/// </summary>
public sealed record RegDeleteKey(RegHive Hive, string Key) : Operation
{
    public override bool RequiresAdmin => RegPaths.RequiresAdmin(Hive, Key);
    public override string Describe() => L("Registre : supprime la clé {0}", RegPaths.Display(Hive, Key));
}

/// <summary>Change le type de démarrage d'un service (via le registre des services, sans ligne de commande).</summary>
public sealed record ServiceStartOp(string ServiceName, ServiceStartKind Start, bool StopIfDisabled = true) : Operation
{
    public override bool RequiresAdmin => true;
    public override string Describe() => Start == ServiceStartKind.Disabled && StopIfDisabled
        ? L("Service « {0} » : démarrage {1} (et arrêt immédiat)", ServiceName, ServiceStartText(Start))
        : L("Service « {0} » : démarrage {1}", ServiceName, ServiceStartText(Start));

    internal static string ServiceStartText(ServiceStartKind k) => k switch
    {
        ServiceStartKind.Automatic => LC("service start", "automatique"),
        ServiceStartKind.AutomaticDelayed => LC("service start", "automatique (différé)"),
        ServiceStartKind.Manual => LC("service start", "manuel"),
        ServiceStartKind.Disabled => LC("service start", "désactivé"),
        _ => k.ToString(),
    };
}

/// <summary>Active ou désactive une tâche planifiée existante (API COM du Planificateur, pas de ligne de commande).</summary>
public sealed record ScheduledTaskOp(string TaskPath, bool Enabled) : Operation
{
    public override bool RequiresAdmin => !TaskPath.StartsWith(@"\Users\", StringComparison.OrdinalIgnoreCase);
    public override string Describe() => Enabled ? L("Tâche planifiée {0} : activée", TaskPath) : L("Tâche planifiée {0} : désactivée", TaskPath);
}

/// <summary>
/// Exécute un outil système de la liste blanche (<see cref="Timonier.Core.Platform.SystemTool"/>) avec des arguments
/// CONSTANTS (définis dans le code). Aucune donnée utilisateur ne doit être placée ici : utiliser une action
/// paramétrée (<see cref="Timonier.Core.Catalog.IActionHandler"/>) avec validation stricte à la place.
/// </summary>
public sealed record RunToolOp(Platform.SystemTool Tool, string[] Args, bool Admin, string? Explanation = null) : Operation
{
    public override bool RequiresAdmin => Admin;
    public override string Describe() =>
        Explanation ?? L("Commande : {0}", $"{Platform.SystemTools.FileName(Tool)} {string.Join(' ', Args)}");
}

/// <summary>Notifie Windows d'un changement de paramètre (rafraîchit Explorer/thème sans redémarrage quand c'est possible).</summary>
public sealed record BroadcastSettingChangeOp(string Area = "ImmersiveColorSet") : Operation
{
    public override bool RequiresAdmin => false;
    public override string Describe() => L("Notifie Windows du changement (rafraîchissement de l'interface)");
}

public static class RegPaths
{
    public static bool RequiresAdmin(RegHive hive, string key) => hive switch
    {
        RegHive.CurrentUser => key.StartsWith(@"Software\Policies", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    public static string Display(RegHive hive, string key) => hive switch
    {
        RegHive.CurrentUser => @"HKCU\" + key,
        RegHive.LocalMachine => @"HKLM\" + key,
        RegHive.DefaultUser => @"HKU\.DEFAULT\" + key,
        _ => key,
    };

    public static string FormatValue(RegistryValueKind kind, object value) => value switch
    {
        byte[] b => kind + ":" + Convert.ToHexString(b.Length > 16 ? b[..16] : b) + (b.Length > 16 ? "…" : ""),
        string[] m => "[" + string.Join(", ", m) + "]",
        string s => "\"" + s + "\"",
        _ => $"{value} ({kind})",
    };
}
