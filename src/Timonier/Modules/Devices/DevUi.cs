using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Timonier.Modules.Devices;

/// <summary>Petites fabriques d'éléments cohérents avec le système de design (styles Pp.*, ressources dynamiques uniquement).</summary>
internal static class DevUi
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

    public static TextBlock Text(string text, string style = "Pp.Body") => new TextBlock { Text = text }.Styled(style);

    public static TextBlock Caption(string text) => Text(text, "Pp.Caption");

    public static TextBlock Icon(string glyph, double size = 16, string? brushKey = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size }.Styled("Pp.Icon");
        if (brushKey is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    /// <summary>Pastille d'icône (fond accent léger), comme les en-têtes de page.</summary>
    public static Border IconTile(string glyph, double size = 36, string background = "Pp.AccentSubtle", string foreground = "Pp.AccentText")
    {
        var icon = Icon(glyph, size * 0.5, foreground);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 4.5), Child = icon, VerticalAlignment = VerticalAlignment.Center }
            .Themed(Border.BackgroundProperty, background);
    }

    /// <summary>Bouton avec icône Segoe Fluent et libellé (l'icône suit la couleur du texte du bouton).</summary>
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

    /// <summary>Badge coloré (Pp.Success, Pp.Warning, Pp.Danger, Pp.Info, Pp.Neutral).</summary>
    public static Border Badge(string text, string tone = "Pp.Neutral")
    {
        var t = new TextBlock { Text = text }.Styled("Pp.BadgeText").Themed(TextBlock.ForegroundProperty, tone);
        return new Border { Child = t }.Styled("Pp.Badge").Themed(Border.BackgroundProperty, tone + "Background");
    }

    public static void SetBadge(Border badge, string text, string tone)
    {
        if (badge.Child is TextBlock t)
        {
            t.Text = text;
            t.SetResourceReference(TextBlock.ForegroundProperty, tone);
        }
        badge.SetResourceReference(Border.BackgroundProperty, tone + "Background");
    }

    public static Border Divider(Thickness margin) =>
        new Border { Height = 1, Margin = margin, SnapsToDevicePixels = true }.Themed(Border.BackgroundProperty, "Pp.Divider");

    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var b = new Border { Child = child }.Styled("Pp.Card");
        if (padding is { } p) b.Padding = p;
        return b;
    }

    public static ProgressBar BusyBar(double width = 48) => new()
    {
        IsIndeterminate = true, Width = width, Height = 3, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed,
    };

    /// <summary>Ligne « libellé : valeur » compacte ; la valeur est sélectionnable si <paramref name="selectable"/>.</summary>
    public static Grid KeyValue(string key, string value, bool selectable = false, double keyWidth = 150)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keyWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var k = Caption(key);
        k.VerticalAlignment = VerticalAlignment.Top;
        k.Margin = new Thickness(0, 1, 8, 0);
        g.Children.Add(k);
        FrameworkElement v;
        if (selectable)
        {
            var tb = new TextBox
            {
                Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = null, Padding = new Thickness(0),
                TextWrapping = TextWrapping.Wrap, FontSize = 12,
            };
            tb.SetResourceReference(Control.ForegroundProperty, "Pp.TextPrimary");
            tb.SetResourceReference(TextBox.CaretBrushProperty, "Pp.TextPrimary");
            v = tb;
        }
        else
        {
            var t = Text(value);
            t.FontSize = 12;
            v = t;
        }
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }

    /// <summary>Titre de section avec légende facultative à droite (ex. compteur).</summary>
    public static DockPanel SectionHeader(string title, out TextBlock heading, UIElement? right = null)
    {
        heading = Text(title, "Pp.SectionTitle");
        var dock = new DockPanel { LastChildFill = true };
        if (right is not null)
        {
            DockPanel.SetDock(right, Dock.Right);
            if (right is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Bottom; fe.Margin = new Thickness(0, 0, 0, 8); }
            dock.Children.Add(right);
        }
        dock.Children.Add(heading);
        return dock;
    }
}
