using System.Runtime.InteropServices;
using Timonier.Core.Platform;

namespace Timonier.Modules.Startup;

/// <summary>Tâche planifiée, avec un résumé lisible de ses déclencheurs et actions.</summary>
public sealed class TaskItem
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public string Folder => Path.Length > Name.Length ? Path[..^Name.Length] : "\\";
    public bool Enabled { get; set; }
    public string State { get; set; } = "";
    public bool IsRunning { get; set; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public DateTime? LastRun { get; init; }
    public DateTime? NextRun { get; init; }
    public int? LastResult { get; init; }
    public string Actions { get; init; } = "";
    public string Triggers { get; init; } = "";
    /// <summary>Déclenchée au démarrage de Windows ou à l'ouverture de session.</summary>
    public bool RunsAtStartup { get; init; }
    public bool Hidden { get; init; }

    public bool IsMicrosoftFolder => Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);
    public bool IsWindowsSystem => TaskInventory.IsWindowsSystemPath(Path);
    public bool CanToggle => TaskInventory.CanToggle(Path);
}

/// <summary>Lecture des tâches planifiées (API COM du Planificateur, sans élévation) et règles de modification.</summary>
public static class TaskInventory
{
    /// <summary>
    /// Tâches de \Microsoft\Windows\ que l'on peut désactiver sans risque (fonctions facultatives) ;
    /// toutes les autres tâches système restent en lecture seule.
    /// </summary>
    private static readonly HashSet<string> WindowsAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        @"\Microsoft\Windows\Maps\MapsUpdateTask",
        @"\Microsoft\Windows\Maps\MapsToastTask",
        @"\Microsoft\Windows\Work Folders\Work Folders Logon Synchronization",
        @"\Microsoft\Windows\Work Folders\Work Folders Maintenance Work",
        @"\Microsoft\Windows\RemoteAssistance\RemoteAssistanceTask",
        @"\Microsoft\Windows\Printing\EduPrintProv",
    };

    public static bool IsWindowsSystemPath(string path) => path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase);

    public static bool CanToggle(string path) => !IsWindowsSystemPath(path) || WindowsAllowlist.Contains(path);

    /// <summary>Validation d'un chemin reçu par l'action admin : format, existence, hors tâches système non autorisées.</summary>
    public static void EnsureToggleable(string path)
    {
        if (!TaskSchedulerHelper.IsValidPath(path)) throw new Core.Security.ValidationException(L("Chemin de tâche invalide."));
        if (!CanToggle(path))
            throw new Core.Security.ValidationException(L("Les tâches système de Windows ne sont pas modifiables ici (seules quelques tâches facultatives le sont)."));
        if (TaskSchedulerHelper.IsEnabled(path) is null)
            throw new Core.Security.ValidationException(L("Cette tâche n'existe plus. Actualisez la liste."));
    }

    /// <summary>
    /// Énumère les tâches lisibles sans élévation (lent : hors du thread UI). <paramref name="microsoft"/> :
    /// false = tout sauf le dossier \Microsoft (rapide, vue par défaut), true = uniquement \Microsoft (plusieurs secondes).
    /// </summary>
    public static List<TaskItem> Load(bool microsoft, int max = 3000)
    {
        var result = new List<TaskItem>();
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException(L("Planificateur de tâches indisponible"));
        dynamic service = Activator.CreateInstance(type)!;
        try
        {
            service.Connect();
            if (microsoft)
            {
                dynamic? folder = null;
                try { folder = service.GetFolder(@"\Microsoft"); } catch (COMException) { }
                if (folder is not null) Walk(folder, result, max, 0, skipMicrosoft: false);
            }
            else Walk(service.GetFolder("\\"), result, max, 0, skipMicrosoft: true);
        }
        finally { Marshal.FinalReleaseComObject(service); }
        result.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static void Walk(dynamic folder, List<TaskItem> result, int max, int depth, bool skipMicrosoft)
    {
        if (depth > 12) return;
        try
        {
            foreach (dynamic t in folder.GetTasks(1 /* TASK_ENUM_HIDDEN */))
            {
                if (result.Count >= max) return;
                try { result.Add(Read(t)); }
                catch { /* tâche illisible */ }
            }
            foreach (dynamic sub in folder.GetFolders(0))
            {
                if (skipMicrosoft && depth == 0 && string.Equals((string)sub.Path, @"\Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                Walk(sub, result, max, depth + 1, skipMicrosoft);
            }
        }
        catch (COMException) { /* dossier non accessible sans élévation */ }
        catch (UnauthorizedAccessException) { }
    }

    private static TaskItem Read(dynamic t)
    {
        string path = t.Path;
        string name = t.Name;
        string? author = null, description = null, actions = "", triggers = "";
        bool hidden = false, atStartup = false;
        try
        {
            dynamic def = t.Definition;
            try { author = ServiceInventory.ResolveIndirect((string?)def.RegistrationInfo.Author); } catch { }
            try { description = ServiceInventory.ResolveIndirect((string?)def.RegistrationInfo.Description); } catch { }
            try { hidden = (bool)def.Settings.Hidden; } catch { }
            try
            {
                var parts = new List<string>();
                foreach (dynamic a in def.Actions)
                {
                    int kind = a.Type;
                    if (kind == 0) parts.Add((((string?)a.Path ?? "") + " " + ((string?)a.Arguments ?? "")).Trim());
                    else if (kind == 5) parts.Add(L("Gestionnaire COM personnalisé"));
                    else if (kind == 6) parts.Add(L("Envoi d'un courriel (obsolète)"));
                    else if (kind == 7) parts.Add(L("Affichage d'un message (obsolète)"));
                }
                actions = string.Join("  |  ", parts);
            }
            catch { }
            try
            {
                var parts = new List<string>();
                foreach (dynamic trig in def.Triggers)
                {
                    int kind = trig.Type;
                    bool enabled = true;
                    try { enabled = trig.Enabled; } catch { }
                    if (kind is 8 or 9 && enabled) atStartup = true;
                    var label = TriggerLabel(kind);
                    if (!enabled) label = L("{0} (inactif)", label);
                    if (!parts.Contains(label)) parts.Add(label);
                }
                triggers = parts.Count == 0 ? L("Aucun déclencheur (lancement manuel)") : string.Join(", ", parts);
            }
            catch { }
        }
        catch { /* définition non lisible sans élévation */ }

        DateTime? last = null, next = null;
        try { var d = (DateTime)t.LastRunTime; if (d.Year > 2000) last = d; } catch { }
        try { var d = (DateTime)t.NextRunTime; if (d.Year > 2000) next = d; } catch { }
        int? lastResult = null;
        try { lastResult = (int)t.LastTaskResult; } catch { }
        int state = 0;
        try { state = (int)t.State; } catch { }

        return new TaskItem
        {
            Path = path, Name = name, Enabled = (bool)t.Enabled, State = StateLabel(state), IsRunning = state == 4,
            Author = author, Description = description, LastRun = last, NextRun = next, LastResult = lastResult,
            Actions = actions ?? "", Triggers = triggers ?? "", RunsAtStartup = atStartup, Hidden = hidden,
        };
    }

    public static string StateLabel(int state) => state switch
    {
        1 => L("Désactivée"),
        2 => L("En file d'attente"),
        3 => L("Prête"),
        4 => L("En cours d'exécution"),
        _ => L("Inconnu"),
    };

    private static string TriggerLabel(int kind) => kind switch
    {
        0 => L("Sur un événement"),
        1 => L("À une date précise"),
        2 => L("Tous les jours"),
        3 => L("Chaque semaine"),
        4 or 5 => L("Chaque mois"),
        6 => L("Pendant l'inactivité"),
        7 => L("À l'inscription de la tâche"),
        8 => L("Au démarrage de Windows"),
        9 => L("À l'ouverture de session"),
        11 => L("Au verrouillage ou déverrouillage"),
        _ => L("Déclencheur personnalisé"),
    };

    /// <summary>Code de dernier résultat lisible (0 = réussite).</summary>
    public static string? ResultLabel(int? code) => code switch
    {
        null => null,
        0 => L("Réussite"),
        0x41301 => L("En cours"),
        0x41303 => L("Jamais exécutée"),
        0x41306 => L("Arrêtée par l'utilisateur"),
        unchecked((int)0x8004131F) => L("Déjà en cours"),
        _ => L("Code 0x{0:X8}", code.Value),
    };
}
