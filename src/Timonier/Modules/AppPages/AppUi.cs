using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace Timonier.Modules.AppPages;

/// <summary>Fabriques d'éléments cohérents avec le système de design (styles Pp.*, pinceaux dynamiques uniquement).</summary>
internal static class AppUi
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

    public static TextBlock Text(string text, string style = "Pp.Body", bool wrap = true)
    {
        var t = new TextBlock { Text = text }.Styled(style);
        if (wrap) t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    public static TextBlock Caption(string text, bool tertiary = false, bool trim = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }.Styled("Pp.Caption");
        if (tertiary) t.Themed(TextBlock.ForegroundProperty, "Pp.TextTertiary");
        if (trim)
        {
            t.TextWrapping = TextWrapping.NoWrap;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.ToolTip = text;
        }
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, string? brushKey = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Icon");
        if (brushKey is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
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

    /// <summary>Change le libellé d'un bouton créé par <see cref="Button"/>.</summary>
    public static void SetButtonText(Button b, string text)
    {
        AutomationProperties.SetName(b, text);
        if (b.Content is StackPanel p && p.Children.Count > 1 && p.Children[1] is TextBlock t) t.Text = text;
        else if (b.Content is string) b.Content = text;
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
            panel.Children.Add(i);
        }
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.BadgeText");
        t.SetResourceReference(TextBlock.ForegroundProperty, fg);
        panel.Children.Add(t);
        var b = new Border { Child = panel, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Badge");
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    public static Border GlyphTile(string glyph, string bgKey = "Pp.AccentSubtle", string fgKey = "Pp.AccentText", double size = 36)
    {
        var icon = Icon(glyph, size * 0.44, fgKey);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(8), Child = icon, VerticalAlignment = VerticalAlignment.Top };
        b.SetResourceReference(Border.BackgroundProperty, bgKey);
        return b;
    }

    public static TextBox SearchBox(string placeholder, Action<string> changed)
    {
        var box = new TextBox { Tag = placeholder, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.SearchBox");
        AutomationProperties.SetName(box, placeholder);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { timer.Stop(); changed(box.Text.Trim()); };
        box.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        return box;
    }

    public static Border Divider(Thickness margin) =>
        new Border { Height = 1, Margin = margin, SnapsToDevicePixels = true }.Themed(Border.BackgroundProperty, "Pp.Divider");

    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var b = new Border { Child = child }.Styled("Pp.Card");
        if (margin is { } m) b.Margin = m;
        return b;
    }

    /// <summary>État vide / erreur / chargement dans une carte centrée.</summary>
    public static Border StateCard(string glyph, string title, string? detail = null, bool busy = false)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 22) };
        if (busy)
            stack.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 160, Height = 3, Margin = new Thickness(0, 0, 0, 14) });
        else
        {
            var i = Icon(glyph, 32, "Pp.TextTertiary");
            i.HorizontalAlignment = HorizontalAlignment.Center;
            i.Margin = new Thickness(0, 0, 0, 12);
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
            d.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(d);
        }
        return Card(stack);
    }

    /// <summary>Titre de section (avec marge adaptée).</summary>
    public static TextBlock Section(string title) => new TextBlock { Text = title }.Styled("Pp.SectionTitle");

    /// <summary>
    /// Ligne de réglage : icône, titre, description, contrôle à droite (et contenu optionnel sous la description).
    /// </summary>
    public static Grid SettingRow(string glyph, string title, string? description, FrameworkElement? control, UIElement? below = null)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = Icon(glyph, 18, "Pp.TextSecondary");
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(2, 3, 16, 0);
        g.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = Text(title, "Pp.Body");
        t.FontWeight = FontWeights.SemiBold;
        text.Children.Add(t);
        if (!string.IsNullOrEmpty(description))
        {
            var d = Caption(description);
            d.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(d);
        }
        if (below is not null) text.Children.Add(below);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);

        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(20, 0, 0, 0);
            Grid.SetColumn(control, 2);
            g.Children.Add(control);
        }
        return g;
    }

    /// <summary>Interrupteur (style Pp.ToggleSwitch) avec nom accessible.</summary>
    public static CheckBox Toggle(string accessibleName, bool isOn)
    {
        var c = new CheckBox { IsChecked = isOn }.Styled("Pp.ToggleSwitch");
        AutomationProperties.SetName(c, accessibleName);
        return c;
    }

    /// <summary>Tuile de chiffre clé (valeur + libellé).</summary>
    public static Border MetricTile(string glyph, string value, string label, string tone = "Accent")
    {
        var (bg, fg) = tone == "Accent" ? ("Pp.AccentSubtle", "Pp.AccentText") : ($"Pp.{tone}Background", $"Pp.{tone}");
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(GlyphTile(glyph, bg, fg, 30));
        var v = new TextBlock { Text = value, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Metric");
        head.Children.Add(v);
        stack.Children.Add(head);
        var l = Caption(label);
        l.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(l);
        return Card(stack, new Thickness(0, 0, 12, 12));
    }

    /// <summary>Grille régulière de tuiles (3 ou 4 colonnes) : largeurs identiques, alignement propre.</summary>
    public static FrameworkElement Tiles(params Border[] tiles)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = tiles.Length % 4 == 0 ? 4 : 3, Margin = new Thickness(0, 0, -12, 0) };
        foreach (var t in tiles) grid.Children.Add(t);
        return grid;
    }

    /// <summary>Liste à puces (texte simple, pour les explications).</summary>
    public static StackPanel Bullets(params string[] items)
    {
        var s = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var item in items)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = Text("•", "Pp.Body", false);
            dot.Themed(TextBlock.ForegroundProperty, "Pp.TextTertiary");
            g.Children.Add(dot);
            var t = Text(item);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            s.Children.Add(g);
        }
        return s;
    }

    /// <summary>Fait défiler la page jusqu'à l'élément (après mise en page).</summary>
    public static void ScrollTo(FrameworkElement target) =>
        target.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            target.BringIntoView(new Rect(0, 0, target.ActualWidth, Math.Min(target.ActualHeight, 400)));
        });
}

/// <summary>Sélecteur segmenté (onglets ou filtres) construit avec les styles de boutons existants.</summary>
internal sealed class SegmentedBar : Border
{
    private readonly List<Button> _buttons = [];
    private readonly List<TextBlock> _counts = [];
    public int SelectedIndex { get; private set; } = -1;
    public event EventHandler<int>? SelectionChanged;

    public SegmentedBar()
    {
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(3);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Left;
        SetResourceReference(BackgroundProperty, "Pp.ControlFill");
        SetResourceReference(BorderBrushProperty, "Pp.ControlStroke");
        Child = new WrapPanel();
    }

    public void Add(string text, string? glyph = null)
    {
        var index = _buttons.Count;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var fgBinding = () => new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
        };
        if (glyph is not null)
        {
            var i = AppUi.Icon(glyph, 13);
            i.Margin = new Thickness(0, 0, 8, 0);
            i.SetBinding(TextBlock.ForegroundProperty, fgBinding());
            panel.Children.Add(i);
        }
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var count = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = 0.75, Visibility = Visibility.Collapsed };
        count.SetBinding(TextBlock.ForegroundProperty, fgBinding());
        panel.Children.Add(count);
        var b = new Button { Content = panel, Margin = new Thickness(index == 0 ? 0 : 2, 0, 0, 0), Padding = new Thickness(14, 5, 14, 5) }.Styled("Pp.SubtleButton");
        AutomationProperties.SetName(b, text);
        b.Click += (_, _) => Select(index);
        _buttons.Add(b);
        _counts.Add(count);
        ((WrapPanel)Child).Children.Add(b);
    }

    public void SetCount(int index, string? text)
    {
        if (index < 0 || index >= _counts.Count) return;
        _counts[index].Text = text ?? "";
        _counts[index].Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Select(int index, bool notify = true)
    {
        if (index < 0 || index >= _buttons.Count) return;
        var changed = index != SelectedIndex;
        SelectedIndex = index;
        for (var i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].SetResourceReference(StyleProperty, i == index ? "Pp.AccentButton" : "Pp.SubtleButton");
            _buttons[i].FontWeight = i == index ? FontWeights.SemiBold : FontWeights.Normal;
        }
        if (changed && notify) SelectionChanged?.Invoke(this, index);
    }
}
