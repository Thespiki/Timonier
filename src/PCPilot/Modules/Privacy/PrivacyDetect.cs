using System.Runtime.InteropServices;
using PcPilot.Core.Model;
using PcPilot.Core.Platform;

namespace PcPilot.Modules.Privacy;

/// <summary>Aides de détection en lecture seule, propres au module Confidentialité.</summary>
internal static class PrivacyDetect
{
    /// <summary>
    /// Ne garde que les tâches planifiées réellement présentes sur ce PC (fichier de définition dans %windir%\System32\Tasks).
    /// Contrôle instantané (quelques appels système), lisible sans élévation, identique dans l'interface et dans le broker.
    /// Nécessaire car une tâche absente fait échouer l'opération côté cœur (voir rapport du module).
    /// </summary>
    public static string[] ExistingTasks(IEnumerable<string> taskPaths)
    {
        string root;
        try { root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks"); }
        catch { return []; }
        var result = new List<string>();
        foreach (var path in taskPaths)
        {
            if (!TaskSchedulerHelper.IsValidPath(path)) continue;
            try
            {
                if (File.Exists(Path.Combine(root, path.TrimStart('\\')))) result.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Chemin illisible : la tâche est simplement ignorée.
            }
        }
        return [.. result];
    }

    /// <summary>
    /// État groupé d'une liste de tâches avec une seule connexion au Planificateur (au lieu d'une par tâche et par option) :
    /// "on" si au moins une tâche est active, "off" si toutes sont désactivées, null si aucune n'est lisible.
    /// </summary>
    public static string? TasksState(IReadOnlyList<string> taskPaths)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return null;
        object? service = null;
        try
        {
            service = Activator.CreateInstance(type);
            if (service is null) return null;
            dynamic scheduler = service;
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder("\\");
            var found = 0;
            foreach (var path in taskPaths)
            {
                try
                {
                    dynamic task = folder.GetTask(path);
                    found++;
                    if ((bool)task.Enabled) return TweakDefinition.On;
                }
                catch (Exception ex) when (ex is COMException or FileNotFoundException or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    // Tâche absente ou illisible : ignorée.
                }
            }
            return found > 0 ? TweakDefinition.Off : null;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            Log.Warn("Privacy", "lecture des tâches planifiées : " + ex.Message);
            return null;
        }
        finally
        {
            if (service is not null && Marshal.IsComObject(service)) Marshal.FinalReleaseComObject(service);
        }
    }

    /// <summary>Lecture DWORD (null si absente) pour les détections personnalisées.</summary>
    public static int? Dword(RegHive hive, string key, string name) => RegistryAccess.ReadDword(hive, key, name);

    public static int? Cu(string key, string name) => RegistryAccess.ReadDword(RegHive.CurrentUser, key, name);

    public static int? Lm(string key, string name) => RegistryAccess.ReadDword(RegHive.LocalMachine, key, name);

    /// <summary>
    /// État effectif d'une fonctionnalité réglable à la fois par l'utilisateur et par une stratégie : il suffit que
    /// l'un des deux la désactive pour qu'elle soit inactive (sinon la détection générique la verrait « activée »).
    /// </summary>
    public static string OffWhenAny(params bool[] disabledBy) => disabledBy.Any(d => d) ? TweakDefinition.Off : TweakDefinition.On;

    /// <summary>Build de Windows (lecture instantanée, utilisable dans une détection).</summary>
    public static int WindowsBuild => Environment.OSVersion.Version.Build;
}
