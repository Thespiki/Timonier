using System.Globalization;
using PcPilot.Core.Settings;

namespace PcPilot.Modules.GuidedAccess;

/// <summary>Options d'une session d'accès guidé (mémorisées dans les préférences de PC Pilot, rien dans le système).</summary>
internal sealed class GuidedOptions
{
    private const string Prefix = "guided.opt.";

    /// <summary>Bloque la touche Windows, Alt+Tab, Alt+Échap, Ctrl+Échap, Ctrl+Maj+Échap, Alt+Espace et les touches de lancement.</summary>
    public bool BlockShortcuts { get; set; } = true;
    /// <summary>Bloque aussi Alt+F4 (fermeture de la fenêtre au clavier).</summary>
    public bool BlockAltF4 { get; set; } = true;
    /// <summary>Ramène l'application au premier plan si une autre fenêtre prend le focus.</summary>
    public bool KeepForeground { get; set; } = true;
    public bool HideTaskbar { get; set; } = true;
    public bool Maximize { get; set; } = true;
    /// <summary>Touches de volume et multimédia autorisées.</summary>
    public bool AllowMediaKeys { get; set; } = true;
    /// <summary>Limite de temps en minutes (0 = aucune).</summary>
    public int TimeLimitMinutes { get; set; }

    public static readonly int[] TimeChoices = [5, 10, 15, 20, 30, 45, 60, 90, 120, 180];

    public GuidedOptions Clone() => (GuidedOptions)MemberwiseClone();

    public static GuidedOptions Load()
    {
        var d = SettingsStore.Current.ModuleData;
        var o = new GuidedOptions();
        o.BlockShortcuts = ReadBool(d, "shortcuts", o.BlockShortcuts);
        o.BlockAltF4 = ReadBool(d, "altf4", o.BlockAltF4);
        o.KeepForeground = ReadBool(d, "foreground", o.KeepForeground);
        o.HideTaskbar = ReadBool(d, "taskbar", o.HideTaskbar);
        o.Maximize = ReadBool(d, "maximize", o.Maximize);
        o.AllowMediaKeys = ReadBool(d, "media", o.AllowMediaKeys);
        if (d.TryGetValue(Prefix + "time", out var t) && int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            && (minutes == 0 || TimeChoices.Contains(minutes)))
            o.TimeLimitMinutes = minutes;
        return o;
    }

    public void Save()
    {
        var d = SettingsStore.Current.ModuleData;
        d[Prefix + "shortcuts"] = BlockShortcuts ? "1" : "0";
        d[Prefix + "altf4"] = BlockAltF4 ? "1" : "0";
        d[Prefix + "foreground"] = KeepForeground ? "1" : "0";
        d[Prefix + "taskbar"] = HideTaskbar ? "1" : "0";
        d[Prefix + "maximize"] = Maximize ? "1" : "0";
        d[Prefix + "media"] = AllowMediaKeys ? "1" : "0";
        d[Prefix + "time"] = TimeLimitMinutes.ToString(CultureInfo.InvariantCulture);
        SettingsStore.Save();
    }

    private static bool ReadBool(Dictionary<string, string> d, string key, bool fallback) =>
        d.TryGetValue(Prefix + key, out var v) ? v == "1" : fallback;
}
