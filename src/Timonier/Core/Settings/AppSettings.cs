using System.Text.Json;
using Timonier.Core.Platform;

namespace Timonier.Core.Settings;

public enum ThemePreference { System, Light, Dark }

/// <summary>Préférences de Timonier (fichier JSON local, par utilisateur).</summary>
public sealed class AppSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    /// <summary>Langue de l'interface : « auto » (langue d'affichage de Windows) ou un code de Languages.All. Pris en compte au prochain lancement.</summary>
    public string Language { get; set; } = "auto";
    /// <summary>Affiche les réglages « Avancé » (risque élevé).</summary>
    public bool AdvancedMode { get; set; }
    /// <summary>Minutes d'inactivité avant fermeture automatique de la session administrateur (1–60).</summary>
    public int BrokerIdleMinutes { get; set; } = 5;
    /// <summary>Autorise Timonier à rester dans la zone de notification quand une fonction active en a besoin.</summary>
    public bool AllowBackground { get; set; } = true;
    public bool StartWithWindows { get; set; }
    /// <summary>Hachage PBKDF2 du code qui protège l'ouverture de Timonier (null = pas de code).</summary>
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

    /// <summary>
    /// Remet les préférences de l'application à leur valeur par défaut (même instance : les références déjà obtenues
    /// restent valides). Sont conservés : la langue, les données des modules (<see cref="AppSettings.ModuleData"/> :
    /// de quoi retirer un mode kiosque, un fond d'écran précédent…), le code de l'accès guidé, l'assistant de premier
    /// lancement et la dernière page ouverte. Le démarrage avec Windows (registre) reste à retirer par l'appelant.
    /// </summary>
    public static void Reset()
    {
        var d = new AppSettings();
        lock (Gate)
        {
            var s = Current;
            s.Theme = d.Theme;
            s.AdvancedMode = d.AdvancedMode;
            s.BrokerIdleMinutes = d.BrokerIdleMinutes;
            s.AllowBackground = d.AllowBackground;
            s.StartWithWindows = d.StartWithWindows;
            s.AppPinHash = d.AppPinHash;
            s.ConfirmBeforeAdminActions = d.ConfirmBeforeAdminActions;
            s.ReduceAnimations = d.ReduceAnimations;
        }
        Save();
    }

    private static AppSettings Load()
    {
        AppSettings? loaded = null;
        try
        {
            if (File.Exists(FilePath))
                loaded = JsonSerializer.Deserialize(File.ReadAllText(FilePath), CoreJson.Default.AppSettings);
        }
        catch (Exception ex) { Log.Warn("Settings", "fichier illisible, valeurs par défaut : " + ex.Message); }
        return Normalize(loaded ?? new AppSettings());
    }

    /// <summary>Le fichier est modifiable à la main : valeurs nulles ou hors bornes ramenées à des valeurs sûres.</summary>
    private static AppSettings Normalize(AppSettings s)
    {
        s.Language ??= "auto";
        s.ModuleData ??= [];
        s.BrokerIdleMinutes = Math.Clamp(s.BrokerIdleMinutes, 1, 60);
        if (!Enum.IsDefined(s.Theme)) s.Theme = ThemePreference.System;
        return s;
    }
}
