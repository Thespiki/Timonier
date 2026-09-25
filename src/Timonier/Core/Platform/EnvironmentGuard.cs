using System.Collections;
using Microsoft.Win32;

namespace Timonier.Core.Platform;

/// <summary>
/// Variables d'environnement qui font charger du code dans tout programme .NET au démarrage (profileurs CLR,
/// dépendances additionnelles). Un processus élevé via l'UAC reçoit l'environnement de l'utilisateur
/// (HKCU\Environment, modifiable sans droits) : si l'une d'elles est définie, un programme non élevé pourrait
/// exécuter son code dans le broker administrateur dès que l'utilisateur accepte l'invite. On refuse donc
/// d'ouvrir la session administrateur tant qu'elles existent. (Les « startup hooks » sont désactivés à la
/// compilation : StartupHookSupport=false.)
/// </summary>
public static class EnvironmentGuard
{
    private static readonly string[] ExactNames =
        ["COR_ENABLE_PROFILING", "CORECLR_ENABLE_PROFILING", "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE"];

    private static readonly string[] Prefixes = ["COR_PROFILER", "CORECLR_PROFILER"];

    public static bool IsDangerous(string name) =>
        ExactNames.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase))
        || Prefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Noms des variables dangereuses définies dans ce processus, pour l'utilisateur ou pour la machine.</summary>
    public static IReadOnlyList<string> Find()
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
            if (e.Key is string name && IsDangerous(name) && e.Value is string { Length: > 0 }) found.Add(name);
        AddFrom(Registry.CurrentUser, @"Environment", found);
        AddFrom(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", found);
        return [.. found];
    }

    private static void AddFrom(RegistryKey root, string path, SortedSet<string> found)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
                if (IsDangerous(name) && key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string { Length: > 0 })
                    found.Add(name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn("EnvGuard", $"lecture de {path} : {ex.Message}");
        }
    }
}
