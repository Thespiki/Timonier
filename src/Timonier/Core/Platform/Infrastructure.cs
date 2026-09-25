using System.Text;
using System.Text.Json.Serialization;
using Timonier.Broker;
using Timonier.Core.Engine;
using Timonier.Core.Settings;

namespace Timonier.Core.Platform;

public static class AppPaths
{
    public const string AppName = "Timonier";

    /// <summary>%LOCALAPPDATA%\Timonier : préférences, cache, journal utilisateur, logs.</summary>
    public static string LocalData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string Logs => Path.Combine(LocalData, "logs");

    /// <summary>Clé HKLM réservée aux administrateurs (journal machine, écrit uniquement par le broker).</summary>
    public const string MachineRegistryKey = @"SOFTWARE\Timonier";

    public static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath indisponible");
}

/// <summary>Journal de diagnostic minimal (fichier texte tournant, 1 Mo). Ne contient jamais de secret.</summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private static string? _file = Path.Combine(AppPaths.Logs, "timonier.log");
    private static bool _protectedDirectory;

    public static void UseFile(string fileName)
    {
        _file = Path.Combine(AppPaths.Logs, fileName);
        _protectedDirectory = false;
    }

    /// <summary>
    /// Processus élevé (broker) : journal dans %ProgramData%\Timonier\logs, dossier réservé aux administrateurs.
    /// Jamais dans %LOCALAPPDATA% : un programme non élevé de la même session pourrait y placer une jonction et détourner
    /// les créations, suppressions et renommages de fichiers faits avec les droits administrateur. Si le dossier ne peut
    /// pas être sécurisé (créé par un autre compte, lien…), la journalisation fichier est simplement désactivée.
    /// </summary>
    public static void UseProtectedFile(string fileName)
    {
        _file = null;
        _protectedDirectory = true;
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppPaths.AppName);
            var dir = Path.Combine(root, "logs");
            if (ProtectedDirectory.Ensure(root) && ProtectedDirectory.Ensure(dir)) _file = Path.Combine(dir, fileName);
        }
        catch
        {
            _file = null;
        }
    }

    public static void Info(string area, string message) => Write("INFO", area, message);
    public static void Warn(string area, string message) => Write("WARN", area, message);
    public static void Error(string area, string message, Exception? ex = null) =>
        Write("ERROR", area, ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string area, string message)
    {
        try
        {
            lock (Gate)
            {
                if (_file is null) return;
                if (!_protectedDirectory) Directory.CreateDirectory(AppPaths.Logs);
                var fi = new FileInfo(_file);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var old = _file + ".1";
                    File.Delete(old);
                    File.Move(_file, old);
                }
                using var fs = new FileStream(_file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs, Encoding.UTF8);
                sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{Environment.ProcessId}] {area}: {message}");
            }
        }
        catch
        {
            // La journalisation ne doit jamais faire échouer l'application.
        }
    }
}

/// <summary>
/// Dossier réservé aux administrateurs pour les fichiers écrits par le processus élevé : ACL explicite et protégée
/// (SYSTEM et Administrateurs : contrôle total ; Utilisateurs : lecture). Un dossier existant n'est accepté que s'il
/// appartient aux Administrateurs ou à SYSTEM et n'est pas un lien ; son ACL est alors réappliquée.
/// </summary>
internal static class ProtectedDirectory
{
    public static bool Ensure(string path)
    {
        var admins = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null);
        var users = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);
        const System.Security.AccessControl.InheritanceFlags inherit =
            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit;

        var security = new System.Security.AccessControl.DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(system, System.Security.AccessControl.FileSystemRights.FullControl,
            inherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(admins, System.Security.AccessControl.FileSystemRights.FullControl,
            inherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(users, System.Security.AccessControl.FileSystemRights.ReadAndExecute,
            inherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));

        var info = new DirectoryInfo(path);
        if (!info.Exists)
        {
            try { info.Create(security); }
            catch (IOException) { /* créé entre-temps : vérifié ci-dessous */ }
            info.Refresh();
        }
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        var owner = info.GetAccessControl().GetOwner(typeof(System.Security.Principal.SecurityIdentifier));
        if (owner is null || (!owner.Equals(admins) && !owner.Equals(system))) return false;
        info.SetAccessControl(security);
        return true;
    }
}

/// <summary>Sérialisation JSON générée à la compilation (rapide, sans réflexion).</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false)]
[JsonSerializable(typeof(SystemProfile))]
[JsonSerializable(typeof(JournalEntry))]
[JsonSerializable(typeof(List<JournalEntry>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(BrokerRequest))]
[JsonSerializable(typeof(BrokerResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
public sealed partial class CoreJson : JsonSerializerContext;
