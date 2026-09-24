using System.Text.Json;
using PcPilot.Core.Platform;

namespace PcPilot.Core.Settings;

public enum ThemePreference { System, Light, Dark }

/// <summary>Préférences de PC Pilot (fichier JSON local, par utilisateur).</summary>
public sealed class AppSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    /// <summary>Affiche les réglages « Avancé » (risque élevé).</summary>
    public bool AdvancedMode { get; set; }
    /// <summary>Minutes d'inactivité avant fermeture automatique de la session administrateur (1–60).</summary>
    public int BrokerIdleMinutes { get; set; } = 5;
    /// <summary>Autorise PC Pilot à rester dans la zone de notification quand une fonction active en a besoin.</summary>
    public bool AllowBackground { get; set; } = true;
    public bool StartWithWindows { get; set; }
    /// <summary>Hachage PBKDF2 du code qui protège l'ouverture de PC Pilot (null = pas de code).</summary>
    public string? AppPinHash { get; set; }
    /// <summary>Hachage du code de sortie de l'accès guidé.</summary>
    public string? GuidedAccessPinHash { get; set; }
    public bool ConfirmBeforeAdminActions { get; set; } = true;
    public bool ReduceAnimations { get; set; }
    public string? LastPageId { get; set; }
    public bool FirstRunCompleted { get; set; }
    /// <summary>Données libres des modules (clé = "module.cle"). Aucune donnée sensible en clair.</summary>
    public Dictionary<string, string> ModuleData { get; set; } = [];
}

public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.LocalData, "settings.json");
    private static readonly Lock Gate = new();
    private static AppSettings? _current;

    public static event EventHandler? Changed;

    /// <summary>Mode capture : les préférences ne sont jamais écrites sur disque.</summary>
    public static bool ReadOnly { get; set; }

    public static AppSettings Current
    {
        get
        {
            lock (Gate) return _current ??= Load();
        }
    }

    public static void Save()
    {
        if (ReadOnly) { Changed?.Invoke(null, EventArgs.Empty); return; }
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LocalData);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, CoreJson.Default.AppSettings));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception ex) { Log.Error("Settings", "sauvegarde", ex); }
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Update(Action<AppSettings> change)
    {
        change(Current);
        Save();
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), CoreJson.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) { Log.Warn("Settings", "fichier illisible, valeurs par défaut : " + ex.Message); }
        return new AppSettings();
    }
}
