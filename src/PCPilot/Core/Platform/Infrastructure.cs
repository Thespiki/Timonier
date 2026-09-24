using System.Text;
using System.Text.Json.Serialization;
using PcPilot.Broker;
using PcPilot.Core.Engine;
using PcPilot.Core.Settings;

namespace PcPilot.Core.Platform;

public static class AppPaths
{
    public const string AppName = "PCPilot";

    /// <summary>%LOCALAPPDATA%\PCPilot : préférences, cache, journal utilisateur, logs.</summary>
    public static string LocalData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string Logs => Path.Combine(LocalData, "logs");

    /// <summary>Clé HKLM réservée aux administrateurs (journal machine, écrit uniquement par le broker).</summary>
    public const string MachineRegistryKey = @"SOFTWARE\PCPilot";

    public static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath indisponible");
}

/// <summary>Journal de diagnostic minimal (fichier texte tournant, 1 Mo). Ne contient jamais de secret.</summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private static string _file = Path.Combine(AppPaths.Logs, "pcpilot.log");

    public static void UseFile(string fileName) => _file = Path.Combine(AppPaths.Logs, fileName);

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
                Directory.CreateDirectory(AppPaths.Logs);
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
