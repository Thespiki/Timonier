using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace Timonier.Modules.Profiles;

/// <summary>Fabriques d'éléments cohérents avec le système de design (styles Pp.*, pinceaux dynamiques uniquement).</summary>
internal static class ProfilesUi
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

    public static TextBlock Text(string text, string style = "Pp.Body", bool wrap = true) =>
        new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap }.Styled(style);

    public static TextBlock Caption(string text, string? brush = null)
    {
        var t = Text(text, "Pp.Caption");
        if (brush is not null) t.Themed(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, string? brush = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size }.Styled("Pp.Icon");
        if (brush is not null) t.Themed(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>Bouton avec icône Segoe Fluent (l'icône suit la couleur du texte du bouton).</summary>
    public static Button Button(string text, string? glyph, string style = "Pp.Button", RoutedEventHandler? click = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icon(glyph, 14);
            icon.Margin = new Thickness(0, 0, text.Length == 0 ? 0 : 8, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
            });
            panel.Children.Add(icon);
            if (text.Length > 0) panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = panel;
        }
        var b = new Button { Content = content }.Styled(style);
        AutomationProperties.SetName(b, text);
        if (click is not null) b.Click += click;
        return b;
    }

    /// <summary>Pastille ; <paramref name="tone"/> : Neutral, Success, Warning, Danger, Info, Accent.</summary>
    public static Border Badge(string text, string tone = "Neutral", string? glyph = null, string? tooltip = null)
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
            i.Margin = new Thickness(0, 0, 4, 0);
            i.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(i);
        }
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.BadgeText");
        t.Themed(TextBlock.ForegroundProperty, fg);
        panel.Children.Add(t);
        var b = new Border { Child = panel, Margin = new Thickness(0, 0, 6, 4), ToolTip = tooltip }.Styled("Pp.Badge");
        b.Themed(Border.BackgroundProperty, bg);
        return b;
    }

    public static Border GlyphTile(string glyph, string bg = "Pp.AccentSubtle", string fg = "Pp.AccentText", double size = 40)
    {
        var icon = Icon(glyph, size * 0.45, fg);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(8), Child = icon, VerticalAlignment = VerticalAlignment.Top };
        b.Themed(Border.BackgroundProperty, bg);
        return b;
    }

    public static Border Divider(double top = 8, double bottom = 8) =>
        new Border { Height = 1, Margin = new Thickness(0, top, 0, bottom), SnapsToDevicePixels = true }.Themed(Border.BackgroundProperty, "Pp.Divider");

    public static Border Card(UIElement child, double bottom = 12)
    {
        var b = new Border { Child = child }.Styled("Pp.Card");
        b.Margin = new Thickness(0, 0, 0, bottom);
        return b;
    }

    public static Border InfoBar(string text, string glyph, string style = "Pp.InfoBar", UIElement? action = null)
    {
        var icon = Icon(glyph, 16);
        icon.Margin = new Thickness(0, 1, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        if (action is FrameworkElement fe)
        {
            fe.Margin = new Thickness(12, 0, 0, 0);
            fe.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(fe, Dock.Right);
            dock.Children.Add(fe);
        }
        dock.Children.Add(Text(text));
        var b = new Border { Child = dock, Margin = new Thickness(0, 0, 0, 12) }.Styled(style);
        return b;
    }

    /// <summary>État vide / erreur / chargement dans une carte centrée.</summary>
    public static Border StateCard(string glyph, string title, string? detail = null, bool busy = false, UIElement? action = null)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 18) };
        if (busy)
            stack.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 180, Height = 3, Margin = new Thickness(0, 0, 0, 14) });
        else
        {
            var i = Icon(glyph, 28, "Pp.TextTertiary");
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
            var d = Caption(detail);
            d.HorizontalAlignment = HorizontalAlignment.Center;
            d.TextAlignment = TextAlignment.Center;
            d.MaxWidth = 560;
            d.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(d);
        }
        if (action is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(fe);
        }
        return Card(stack);
    }

    /// <summary>Ligne libellé / valeur (fiche « Ce PC »).</summary>
    public static Grid KeyValue(string key, string value, UIElement? extra = null)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var k = Caption(key);
        k.VerticalAlignment = VerticalAlignment.Center;
        var v = Text(value);
        v.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(v, 1);
        g.Children.Add(k);
        g.Children.Add(v);
        if (extra is FrameworkElement fe)
        {
            fe.Margin = new Thickness(12, 0, 0, 0);
            fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(fe, 2);
            g.Children.Add(fe);
        }
        return g;
    }

    /// <summary>Section repliable : en-tête cliquable (chevron) et contenu masqué par défaut.</summary>
    public static StackPanel Collapsible(string header, UIElement content, bool expanded = false)
    {
        var chevron = Icon(expanded ? "" : "", 12);
        chevron.Margin = new Thickness(0, 0, 10, 0);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        chevron.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
        });
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(chevron);
        head.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = head, HorizontalAlignment = HorizontalAlignment.Left }.Styled("Pp.SubtleButton");
        AutomationProperties.SetName(button, header);
        content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        button.Click += (_, _) =>
        {
            var show = content.Visibility != Visibility.Visible;
            content.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = show ? "" : "";
        };
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(button);
        panel.Children.Add(content);
        return panel;
    }
}
