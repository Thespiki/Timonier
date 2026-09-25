using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PcPilot.Modules.GuidedAccess;

/// <summary>Petites fabriques cohérentes avec le système de design (styles Pp.*, pinceaux dynamiques uniquement).</summary>
internal static class GuidedUi
{
    public static T Styled<T>(this T element, string styleKey) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return element;
    }

    public static T Brush<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    public static TextBlock Text(string text, string style = "Pp.Body", bool wrap = true)
    {
        var t = new TextBlock { Text = text }.Styled(style);
        t.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, string? brushKey = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Icon");
        if (brushKey is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    public static Button Button(string text, string? glyph, string style, RoutedEventHandler click)
    {
        var b = new Button().Styled(style);
        SetContent(b, text, glyph);
        b.Click += click;
        return b;
    }

    /// <summary>Contenu « icône + texte » d'un bouton (l'icône suit la couleur du texte du bouton).</summary>
    public static void SetContent(Button button, string text, string? glyph)
    {
        object content = text;
        if (glyph is not null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icon(glyph, 14);
            icon.Margin = new Thickness(0, 0, text.Length == 0 ? 0 : 8, 0);
            icon.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
            });
            panel.Children.Add(icon);
            if (text.Length > 0) panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = panel;
        }
        button.Content = content;
        System.Windows.Automation.AutomationProperties.SetName(button, text);
    }

    public static Border GlyphTile(string glyph, double size = 40, string bg = "Pp.AccentSubtle", string fg = "Pp.AccentText")
    {
        var icon = Icon(glyph, size * 0.45, fg);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 4.5), Child = icon, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    public static Border ImageTile(ImageSource image, double size = 40)
    {
        var img = new Image { Source = image, Width = size * 0.7, Height = size * 0.7, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 4.5), Child = img, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, "Pp.CardSecondary");
        return b;
    }

    /// <summary>Pastille ; <paramref name="tone"/> : Neutral, Success, Warning, Danger, Info, Accent.</summary>
    public static Border Badge(string text, string tone = "Neutral", string? glyph = null)
    {
        var (bg, fg) = tone switch
        {
            "Accent" => ("Pp.AccentSubtle", "Pp.AccentText"),
            "Neutral" => ("Pp.NeutralBackground", "Pp.Neutral"),
            _ => ($"Pp.{tone}Background", $"Pp.{tone}"),
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
        {
            var i = Icon(glyph, 10, fg);
            i.Margin = new Thickness(0, 0, 5, 0);
            panel.Children.Add(i);
        }
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.BadgeText");
        t.SetResourceReference(TextBlock.ForegroundProperty, fg);
        panel.Children.Add(t);
        var b = new Border { Child = panel }.Styled("Pp.Badge");
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    /// <summary>Touche de clavier stylisée (ex. « Échap »).</summary>
    public static Border KeyCap(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextPrimary");
        var b = new Border
        {
            Child = t, Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1, 1, 1, 2),
            Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        b.SetResourceReference(Border.BackgroundProperty, "Pp.ControlFill");
        b.SetResourceReference(Border.BorderBrushProperty, "Pp.ControlStroke");
        return b;
    }

    public static Border Divider(double top = 10, double bottom = 10) =>
        new Border { Height = 1, Margin = new Thickness(0, top, 0, bottom), SnapsToDevicePixels = true }.Brush(Border.BackgroundProperty, "Pp.Divider");

    public static Border StateBlock(string glyph, string title, string? detail, bool busy = false)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };
        if (busy)
            stack.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 160, Height = 3, Margin = new Thickness(0, 4, 0, 14) });
        else
        {
            var i = Icon(glyph, 26, "Pp.TextTertiary");
            i.HorizontalAlignment = HorizontalAlignment.Center;
            i.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(i);
        }
        var t = Text(title, "Pp.CardTitle");
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.TextAlignment = TextAlignment.Center;
        stack.Children.Add(t);
        if (detail is not null)
        {
            var d = Text(detail, "Pp.Caption");
            d.HorizontalAlignment = HorizontalAlignment.Center;
            d.TextAlignment = TextAlignment.Center;
            d.MaxWidth = 520;
            d.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(d);
        }
        return new Border { Child = stack }.Styled("Pp.Card");
    }
}
