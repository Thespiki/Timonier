using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PcPilot.Core.Platform;
using PcPilot.Core.Settings;

namespace PcPilot.UI.Theme;

/// <summary>Thème clair/sombre (suit Windows par défaut) et couleur d'accent du système.</summary>
public static class ThemeManager
{
    private static ResourceDictionary? _palette;
    private static Windows.UI.ViewManagement.UISettings? _uiSettings;

    public static bool IsDark { get; private set; }
    public static event EventHandler? ThemeChanged;

    public static void Initialize()
    {
        var app = Application.Current;
        _palette = app.Resources.MergedDictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Palette.") == true);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                app.Dispatcher.InvokeAsync(Apply);
        };
        try
        {
            _uiSettings = new Windows.UI.ViewManagement.UISettings();
            _uiSettings.ColorValuesChanged += (_, _) => app.Dispatcher.InvokeAsync(Apply);
        }
        catch (Exception ex) { Log.Warn("Theme", "UISettings indisponible : " + ex.Message); }
        Apply();
    }

    public static bool SystemUsesDarkTheme()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    public static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;
        var pref = SettingsStore.Current.Theme;
        var dark = pref == ThemePreference.Dark || (pref == ThemePreference.System && SystemUsesDarkTheme());

        var next = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PCPilot;component/UI/Theme/Palette.{(dark ? "Dark" : "Light")}.xaml") };
        var dictionaries = app.Resources.MergedDictionaries;
        var index = _palette is null ? -1 : dictionaries.IndexOf(_palette);
        if (index >= 0) dictionaries[index] = next;
        else dictionaries.Insert(0, next);
        _palette = next;

        ApplyAccent(dark);
        try
        {
#pragma warning disable WPF0001
            app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        }
        catch (Exception ex) { Log.Warn("Theme", "ThemeMode : " + ex.Message); }

        IsDark = dark;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyAccent(bool dark)
    {
        if (_uiSettings is null) return;
        try
        {
            var type = dark ? Windows.UI.ViewManagement.UIColorType.AccentLight2 : Windows.UI.ViewManagement.UIColorType.AccentDark1;
            var c = _uiSettings.GetColorValue(type);
            var accent = Color.FromRgb(c.R, c.G, c.B);
            var hover = Blend(accent, dark ? Colors.White : Colors.White, 0.12);
            var bg = (Color)((SolidColorBrush)Application.Current.Resources["Pp.ContentBackground"]).Color;
            var subtle = Blend(bg, accent, dark ? 0.22 : 0.12);
            SetBrush("Pp.Accent", accent);
            SetBrush("Pp.AccentText", accent);
            SetBrush("Pp.AccentHover", hover);
            SetBrush("Pp.AccentSubtle", subtle);
            // Texte lisible sur l'accent : noir sur accent clair, blanc sur accent foncé.
            var luminance = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255;
            SetBrush("Pp.TextOnAccent", luminance > 0.55 ? Colors.Black : Colors.White);
        }
        catch (Exception ex) { Log.Warn("Theme", "accent : " + ex.Message); }
    }

    private static void SetBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _palette![key] = brush;
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
