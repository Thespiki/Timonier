using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Timonier.Modules.Performance;

/// <summary>Petites fabriques d'éléments cohérents avec le système de design (styles Pp.* et pinceaux dynamiques).</summary>
internal static class PerfUi
{
    public static TextBlock Text(string style, string text = "", Thickness? margin = null)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (margin is { } m) t.Margin = m;
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, string? brush = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Icon");
        if (brush is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>Bouton avec icône facultative ; l'icône hérite de la couleur du bouton (lisible sur fond accentué).</summary>
    public static Button Button(string text, string style = "Pp.Button", string? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
        {
            var icon = new TextBlock { Text = glyph, FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "Pp.IconFont");
            content.Children.Add(icon);
        }
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content };
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        return b;
    }

    public static Border Divider(double top = 12, double bottom = 12)
    {
        var d = new Border { Height = 1, Margin = new Thickness(0, top, 0, bottom) };
        d.SetResourceReference(Border.BackgroundProperty, "Pp.Divider");
        return d;
    }

    public static Border Badge(string text, AdviceTone tone)
    {
        var (bg, fg) = tone switch
        {
            AdviceTone.Good => ("Pp.SuccessBackground", "Pp.Success"),
            AdviceTone.Limit => ("Pp.WarningBackground", "Pp.Warning"),
            _ => ("Pp.NeutralBackground", "Pp.Neutral"),
        };
        var t = Text("Pp.BadgeText", text);
        t.SetResourceReference(TextBlock.ForegroundProperty, fg);
        var b = new Border { Child = t, Margin = new Thickness(0) };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Badge");
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    public static Border AccentBadge(string text)
    {
        var t = Text("Pp.BadgeText", text);
        t.SetResourceReference(TextBlock.ForegroundProperty, "Pp.AccentText");
        var b = new Border { Child = t, Margin = new Thickness(8, 0, 0, 0) };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Badge");
        b.SetResourceReference(Border.BackgroundProperty, "Pp.AccentSubtle");
        return b;
    }

    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Card");
        if (margin is { } m) b.Margin = m;
        return b;
    }

    /// <summary>Pastille ronde colorée contenant une icône (en-têtes de carte).</summary>
    public static Border IconCircle(string glyph, double size = 40, double iconSize = 18)
    {
        var icon = Icon(glyph, iconSize, "Pp.AccentText");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = icon };
        b.SetResourceReference(Border.BackgroundProperty, "Pp.AccentSubtle");
        return b;
    }

    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
