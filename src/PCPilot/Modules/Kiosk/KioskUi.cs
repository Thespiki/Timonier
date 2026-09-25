using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace PcPilot.Modules.Kiosk;

/// <summary>Fabriques d'éléments cohérents avec le système de design (styles Pp.*, pinceaux dynamiques uniquement).</summary>
internal static class KioskUi
{
    public static T Styled<T>(this T element, string styleKey) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return element;
    }

    public static T Brushed<T>(this T element, DependencyProperty property, string resourceKey) where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }

    public static TextBlock Text(string text, string style = "Pp.Body", bool wrap = true)
    {
        var t = new TextBlock { Text = text }.Styled(style);
        if (wrap) t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    public static TextBlock Caption(string text, bool tertiary = false)
    {
        var t = Text(text, "Pp.Caption");
        if (tertiary) t.Brushed(TextBlock.ForegroundProperty, "Pp.TextTertiary");
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, string? brushKey = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size }.Styled("Pp.Icon");
        if (brushKey is not null) t.Brushed(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    private static Binding ButtonForeground() => new(nameof(Control.Foreground))
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
    };

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
            icon.SetBinding(TextBlock.ForegroundProperty, ButtonForeground());
            panel.Children.Add(icon);
            if (text.Length > 0) panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = panel;
        }
        var b = new Button { Content = content }.Styled(style);
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        if (click is not null) b.Click += click;
        return b;
    }

    public static Button IconButton(string glyph, string tooltip, RoutedEventHandler click)
    {
        var b = Button("", glyph, "Pp.SubtleButton", click);
        b.ToolTip = tooltip;
        b.Width = 34;
        b.Height = 32;
        b.Padding = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(b, tooltip);
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
            i.Margin = new Thickness(0, 0, 4, 0);
            i.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(i);
        }
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.BadgeText");
        t.Brushed(TextBlock.ForegroundProperty, fg);
        panel.Children.Add(t);
        var b = new Border { Child = panel }.Styled("Pp.Badge");
        b.Brushed(Border.BackgroundProperty, bg);
        return b;
    }

    public static Border GlyphTile(string glyph, string bgKey = "Pp.AccentSubtle", string fgKey = "Pp.AccentText", double size = 36)
    {
        var icon = Icon(glyph, size * 0.45, fgKey);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(8), Child = icon, VerticalAlignment = VerticalAlignment.Top };
        b.Brushed(Border.BackgroundProperty, bgKey);
        return b;
    }

    public static Border Divider(Thickness margin) =>
        new Border { Height = 1, Margin = margin, SnapsToDevicePixels = true }.Brushed(Border.BackgroundProperty, "Pp.Divider");

    /// <summary>Titre de section interne à une carte.</summary>
    public static TextBlock Heading(string text, double top = 0)
    {
        var t = Text(text, "Pp.CardTitle");
        t.Margin = new Thickness(0, top, 0, 6);
        return t;
    }

    /// <summary>Ligne icône + titre + description + élément à droite (bouton, badge, interrupteur).</summary>
    public static DockPanel Row(string? glyph, string title, string? description, UIElement? right = null, string glyphBrush = "Pp.AccentText")
    {
        var dock = new DockPanel { LastChildFill = true };
        if (right is not null)
        {
            if (right is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
                fe.Margin = new Thickness(16, 0, 0, 0);
            }
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
        }
        if (glyph is not null)
        {
            var i = Icon(glyph, 18, glyphBrush);
            i.Margin = new Thickness(0, 2, 14, 0);
            i.VerticalAlignment = VerticalAlignment.Top;
            i.Width = 20;
            DockPanel.SetDock(i, Dock.Left);
            dock.Children.Add(i);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(title, "Pp.Body"));
        if (!string.IsNullOrEmpty(description))
        {
            var d = Caption(description);
            d.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(d);
        }
        dock.Children.Add(text);
        return dock;
    }

    /// <summary>Puce de liste (texte avec retrait).</summary>
    public static DockPanel Bullet(string text, string glyph = "", string brush = "Pp.AccentText")
    {
        var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var i = Icon(glyph, 12, brush);
        i.Margin = new Thickness(0, 3, 10, 0);
        i.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(i, Dock.Left);
        dock.Children.Add(i);
        dock.Children.Add(Text(text));
        return dock;
    }

    /// <summary>Étape numérotée (guide pas à pas).</summary>
    public static DockPanel NumberedStep(int number, string text)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var n = new TextBlock
        {
            Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 12, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        }.Brushed(TextBlock.ForegroundProperty, "Pp.AccentText");
        var circle = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Child = n, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top }
            .Brushed(Border.BackgroundProperty, "Pp.AccentSubtle");
        DockPanel.SetDock(circle, Dock.Left);
        dock.Children.Add(circle);
        var t = Text(text);
        t.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(t);
        return dock;
    }

    /// <summary>Tuile sélectionnable (choix de compte, de mode, d'application).</summary>
    public static Border SelectableTile(UIElement content, bool selected, bool enabled, Action? onClick, double padX = 14, double padY = 10)
    {
        // Bordure de 2 px quand la tuile est choisie : le remplissage diminue d'autant pour que le contenu ne bouge pas.
        var shift = selected ? 1 : 0;
        var b = new Border
        {
            Child = content, CornerRadius = new CornerRadius(8), Padding = new Thickness(padX - shift, padY - shift, padX - shift, padY - shift),
            Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(selected ? 2 : 1), SnapsToDevicePixels = true,
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow, Opacity = enabled ? 1 : 0.6, Focusable = enabled,
        };
        b.Brushed(Border.BorderBrushProperty, selected ? "Pp.Accent" : "Pp.CardBorder");
        b.Brushed(Border.BackgroundProperty, selected ? "Pp.AccentSubtle" : "Pp.CardSecondary");
        if (enabled && onClick is not null)
        {
            b.MouseEnter += (_, _) => { if (!selected) b.Brushed(Border.BackgroundProperty, "Pp.CardHover"); };
            b.MouseLeave += (_, _) => { if (!selected) b.Brushed(Border.BackgroundProperty, "Pp.CardSecondary"); };
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
            b.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; onClick(); } };
            KeyboardNavigation.SetIsTabStop(b, true);
        }
        return b;
    }

    /// <summary>État vide / erreur / chargement.</summary>
    public static StackPanel State(string glyph, string title, string? detail = null, bool busy = false)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };
        if (busy) stack.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 160, Height = 3, Margin = new Thickness(0, 0, 0, 12) });
        else
        {
            var i = Icon(glyph, 26, "Pp.TextTertiary");
            i.HorizontalAlignment = HorizontalAlignment.Center;
            i.Margin = new Thickness(0, 0, 0, 8);
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
            d.MaxWidth = 520;
            d.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(d);
        }
        return stack;
    }

    /// <summary>Libellé de champ de formulaire.</summary>
    public static TextBlock FieldLabel(string text)
    {
        var t = Text(text, "Pp.Caption");
        t.Margin = new Thickness(0, 10, 0, 4);
        t.Brushed(TextBlock.ForegroundProperty, "Pp.TextSecondary");
        return t;
    }

    public static StackPanel Horizontal(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static Brush? Find(string key) => Application.Current?.TryFindResource(key) as Brush;
}
