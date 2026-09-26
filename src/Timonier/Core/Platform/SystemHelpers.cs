using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Timonier.Core.Model;

namespace Timonier.Core.Platform;

/// <summary>Accès registre avec résolution des ruches (HKCU -> HKU\SID dans le broker).</summary>
public static class RegistryAccess
{
    /// <summary>
    /// Ouvre la racine d'une ruche. Si <paramref name="userSid"/> est fourni (cas du broker élevé), HKCU est résolu
    /// vers HKEY_USERS\SID de l'utilisateur client : même si l'élévation a été faite avec un autre compte admin,
    /// les réglages « utilisateur » s'appliquent bien au compte qui utilise l'application.
    /// </summary>
    public static RegistryKey OpenRoot(RegHive hive, string? userSid, bool writable = true)
    {
        return hive switch
        {
            RegHive.LocalMachine => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
            RegHive.DefaultUser => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64).OpenSubKey(".DEFAULT", writable)
                                   ?? throw new InvalidOperationException("HKU\\.DEFAULT introuvable"),
            RegHive.CurrentUser when !string.IsNullOrEmpty(userSid) =>
                RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64).OpenSubKey(userSid, writable)
                ?? throw new InvalidOperationException(L("Ruche utilisateur non chargée : {0}", userSid)),
            _ => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
        };
    }

    public static object? Read(RegHive hive, string key, string name, string? userSid = null)
    {
        try
        {
            using var root = OpenRoot(hive, userSid, writable: false);
            using var k = root.OpenSubKey(key, false);
            return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    public static int? ReadDword(RegHive hive, string key, string name) => Read(hive, key, name) switch
    {
        int i => i,
        long l => (int)l,
        _ => null,
    };

    public static string? ReadString(RegHive hive, string key, string name) => Read(hive, key, name) as string;

    public static bool KeyExists(RegHive hive, string key, string? userSid = null)
    {
        try
        {
            using var root = OpenRoot(hive, userSid, writable: false);
            using var k = root.OpenSubKey(key, false);
            return k is not null;
        }
        catch { return false; }
    }

    public static bool ValueEquals(object? current, RegistryValueKind kind, object expected)
    {
        if (current is null) return false;
        return (current, expected) switch
        {
            (int a, int b) => a == b,
            (long a, long b) => a == b,
            (int a, long b) => a == b,
            (long a, int b) => a == b,
            (string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
            (string[] a, string[] b) => a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase),
            (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
            _ => Equals(current, expected),
        };
    }
}

/// <summary>Configuration des services via l'API du gestionnaire de services (pas de sc.exe, pas de chaîne de commande).</summary>
public static partial class ServiceConfig
{
    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,256}$")]
    private static partial Regex ServiceName();

    public static bool IsValidName(string name) => ServiceName().IsMatch(name);

    public static bool Exists(string name) =>
        IsValidName(name) && Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name) is { } k && Dispose(k);

    private static bool Dispose(RegistryKey k) { k.Dispose(); return true; }

    /// <summary>Type de démarrage actuel (null si le service n'existe pas).</summary>
    public static ServiceStartKind? ReadStart(string name)
    {
        if (!IsValidName(name)) return null;
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
        if (k?.GetValue("Start") is not int start) return null;
        if (start == 2 && k.GetValue("DelayedAutostart") is int d && d == 1) return ServiceStartKind.AutomaticDelayed;
        return (ServiceStartKind)start;
    }

    public static void SetStart(string name, ServiceStartKind kind)
    {
        if (!IsValidName(name)) throw new ArgumentException(L("Nom de service invalide : {0}", name));
        var scm = Native.OpenSCManager(null, null, Native.SC_MANAGER_CONNECT);
        if (scm == 0) throw Native.LastError("OpenSCManager");
        try
        {
            var svc = Native.OpenService(scm, name, Native.SERVICE_CHANGE_CONFIG | Native.SERVICE_QUERY_CONFIG);
            if (svc == 0) throw Native.LastError($"OpenService({name})");
            try
            {
                var start = kind == ServiceStartKind.AutomaticDelayed ? 2u : (uint)kind;
                if (!Native.ChangeServiceConfig(svc, Native.SERVICE_NO_CHANGE, start, Native.SERVICE_NO_CHANGE, null, null, 0, null, null, null, null))
                    throw Native.LastError($"ChangeServiceConfig({name})");
                if (kind is ServiceStartKind.Automatic or ServiceStartKind.AutomaticDelayed)
                {
                    var delayed = kind == ServiceStartKind.AutomaticDelayed ? 1 : 0;
                    Native.ChangeServiceConfig2(svc, Native.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref delayed);
                }
            }
            finally { Native.CloseServiceHandle(svc); }
        }
        finally { Native.CloseServiceHandle(scm); }
    }

    public static void TryStop(string name, TimeSpan timeout)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) return;
            if (!sc.CanStop) return;
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
        }
        catch (Exception ex) { Log.Warn("Service", $"arrêt de {name} : {ex.Message}"); }
    }

    public static void TryStart(string name, TimeSpan timeout)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending) return;
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
        }
        catch (Exception ex) { Log.Warn("Service", $"démarrage de {name} : {ex.Message}"); }
    }
}

public sealed record ScheduledTaskInfo(string Path, string Name, bool Enabled, string State, string? Author, DateTime? LastRun, DateTime? NextRun, string? Actions);

/// <summary>Planificateur de tâches via son API COM (Schedule.Service) : lecture sans admin, modification via le broker.</summary>
public static class TaskSchedulerHelper
{
    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException(L("Planificateur de tâches indisponible"));
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    public static bool IsValidPath(string path) =>
        path.StartsWith('\\') && path.Length < 512 && !path.Contains("..") && path.IndexOfAny(['\0', '"', '*', '?', '<', '>', '|']) < 0;

    /// <summary>État d'une tâche : true/false, ou null si elle n'existe pas (ou n'est pas lisible).</summary>
    public static bool? IsEnabled(string path)
    {
        if (!IsValidPath(path)) return null;
        dynamic? service = null;
        try
        {
            service = Connect();
            dynamic task = service.GetFolder("\\").GetTask(path);
            return (bool)task.Enabled;
        }
        catch (COMException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        finally { if (service is not null) Marshal.FinalReleaseComObject(service); }
    }

    public static void SetEnabled(string path, bool enabled)
    {
        if (!IsValidPath(path)) throw new ArgumentException(L("Chemin de tâche invalide : {0}", path));
        dynamic service = Connect();
        try
        {
            dynamic task = service.GetFolder("\\").GetTask(path);
            task.Enabled = enabled;
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }

    /// <summary>Liste les tâches d'un dossier (récursif). Lecture seule, sans admin (les tâches non lisibles sont ignorées).</summary>
    public static List<ScheduledTaskInfo> List(string folderPath = "\\", bool recursive = true, int max = 2000)
    {
        var result = new List<ScheduledTaskInfo>();
        dynamic service = Connect();
        try { Walk(service.GetFolder(folderPath), recursive, result, max); }
        finally { Marshal.FinalReleaseComObject(service); }
        return result;
    }

    private static void Walk(dynamic folder, bool recursive, List<ScheduledTaskInfo> result, int max)
    {
        try
        {
            foreach (dynamic t in folder.GetTasks(1 /* TASK_ENUM_HIDDEN */))
            {
                if (result.Count >= max) return;
                try
                {
                    string? actions = null;
                    try
                    {
                        var parts = new List<string>();
                        foreach (dynamic a in t.Definition.Actions)
                            if ((int)a.Type == 0) parts.Add(((string)a.Path + " " + (string)a.Arguments).Trim());
                        actions = string.Join(" | ", parts);
                    }
                    catch { /* définition non lisible */ }
                    DateTime? last = null, next = null;
                    try { var d = (DateTime)t.LastRunTime; if (d.Year > 2000) last = d; } catch { }
                    try { var d = (DateTime)t.NextRunTime; if (d.Year > 2000) next = d; } catch { }
                    string? author = null;
                    try { author = (string)t.Definition.RegistrationInfo.Author; } catch { }
                    var state = (int)t.State switch { 1 => L("Désactivée"), 2 => L("En file"), 3 => L("Prête"), 4 => L("En cours"), _ => L("Inconnu") };
                    result.Add(new ScheduledTaskInfo((string)t.Path, (string)t.Name, (bool)t.Enabled, state, author, last, next, actions));
                }
                catch { /* tâche illisible */ }
            }
            if (!recursive) return;
            foreach (dynamic sub in folder.GetFolders(0))
                Walk(sub, true, result, max);
        }
        catch (COMException) { /* dossier non accessible sans élévation */ }
        catch (UnauthorizedAccessException) { }
    }
}
