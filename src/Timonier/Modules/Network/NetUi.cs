using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Timonier.Modules.Network;

/// <summary>Fabriques d'éléments cohérents avec le système de design (styles Pp.*, pinceaux dynamiques uniquement).</summary>
internal static class NetUi
{
    public static T Styled<T>(this T element, string styleKey) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return element;
    }

    public static T Themed<T>(this T element, DependencyProperty property, string resourceKey) where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }

    public static TextBlock Text(string text, string style = "Pp.Body", Thickness? margin = null)
    {
        var t = new TextBlock { Text = text }.Styled(style);
        if (margin is { } m) t.Margin = m;
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, string? brushKey = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size }.Styled("Pp.Icon");
        if (brushKey is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    /// <summary>Pastille d'icône arrondie (fond accent léger) pour les en-têtes de carte.</summary>
    public static Border IconTile(string glyph, string background = "Pp.AccentSubtle", string foreground = "Pp.AccentText", double size = 36)
    {
        var icon = Icon(glyph, size * 0.5, foreground);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(8), Child = icon,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0),
        }.Themed(Border.BackgroundProperty, background);
    }

    /// <summary>Bouton avec icône Segoe Fluent et libellé ; l'icône suit la couleur du texte du bouton.</summary>
    public static Button Button(string text, string? glyph, string style = "Pp.Button", RoutedEventHandler? click = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icon(glyph, 14);
            icon.Margin = new Thickness(0, 0, 8, 0);
            icon.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
            });
            panel.Children.Add(icon);
            panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = panel;
        }
        var b = new Button { Content = content }.Styled(style);
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        if (click is not null) b.Click += click;
        return b;
    }

    /// <summary>Badge coloré (Success, Warning, Danger, Info, Neutral, Accent).</summary>
    public static Border Badge(string text, string tone = "Neutral")
    {
        var (bg, fg) = tone switch
        {
            "Accent" => ("Pp.AccentSubtle", "Pp.AccentText"),
            _ => ($"Pp.{tone}Background", $"Pp.{tone}"),
        };
        var t = new TextBlock { Text = text }.Styled("Pp.BadgeText");
        t.SetResourceReference(TextBlock.ForegroundProperty, fg);
        var b = new Border { Child = t }.Styled("Pp.Badge");
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var b = new Border { Child = child }.Styled("Pp.Card");
        if (margin is { } m) b.Margin = m;
        return b;
    }

    public static Border Divider(Thickness margin) =>
        new Border { Height = 1, Margin = margin, SnapsToDevicePixels = true }.Themed(Border.BackgroundProperty, "Pp.Divider");

    public static TextBlock Section(string title) => Text(title, "Pp.SectionTitle");

    /// <summary>Barre d'information : icône + texte (+ contenu optionnel à droite).</summary>
    public static Border InfoBar(string text, string glyph, string style = "Pp.InfoBar", UIElement? trailing = null, string? title = null)
    {
        var icon = Icon(glyph, 16);
        icon.Margin = new Thickness(0, 1, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (title is not null) body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) }.Styled("Pp.Body"));
        body.Children.Add(Text(text));
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        if (trailing is FrameworkElement fe)
        {
            fe.Margin = new Thickness(12, 0, 0, 0);
            fe.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(fe, Dock.Right);
            dock.Children.Add(fe);
        }
        dock.Children.Add(body);
        return new Border { Child = dock }.Styled(style);
    }

    /// <summary>Ligne « libellé : valeur » compacte (colonne de libellés fixe).</summary>
    public static Grid KeyValue(string key, string value, double keyWidth = 150, bool selectable = false)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keyWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(Text(key, "Pp.Caption"));
        FrameworkElement v;
        if (selectable)
        {
            // TextBox en lecture seule : permet de copier une adresse sans style de champ de saisie.
            v = new TextBox
            {
                Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(0), TextWrapping = TextWrapping.Wrap, FontSize = 14, MinHeight = 0,
            }.Themed(Control.ForegroundProperty, "Pp.TextPrimary");
        }
        else
        {
            v = Text(value);
        }
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }

    /// <summary>Bloc d'état vide / chargement / erreur centré.</summary>
    public static Border EmptyState(string glyph, string title, string? detail = null, UIElement? action = null)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 18) };
        var icon = Icon(glyph, 28, "Pp.TextTertiary");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 0, 10);
        s.Children.Add(icon);
        s.Children.Add(new TextBlock { Text = title, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.SemiBold }.Styled("Pp.Body"));
        if (detail is not null)
        {
            var d = Text(detail, "Pp.Caption");
            d.TextAlignment = TextAlignment.Center;
            d.MaxWidth = 520;
            d.Margin = new Thickness(0, 4, 0, 0);
            s.Children.Add(d);
        }
        if (action is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.Margin = new Thickness(0, 12, 0, 0);
            s.Children.Add(fe);
        }
        return Card(s);
    }

    public static ProgressBar Busy(double width = 120) => new()
    {
        IsIndeterminate = true, Width = width, Height = 3, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed,
    };

    public static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }
}
