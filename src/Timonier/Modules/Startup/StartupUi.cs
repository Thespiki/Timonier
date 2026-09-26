using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Timonier.Core.Platform;

namespace Timonier.Modules.Startup;

/// <summary>Fabriques d'éléments cohérents avec le système de design (styles Pp.*, pinceaux dynamiques uniquement).</summary>
internal static class StartupUi
{
    public static T Styled<T>(this T element, string styleKey) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return element;
    }

    public static T Themed<T>(this T element, DependencyProperty property, string resourceKey) where T : DependencyObject
    {
        if (element is FrameworkElement fe) fe.SetResourceReference(property, resourceKey);
        else if (element is FrameworkContentElement fce) fce.SetResourceReference(property, resourceKey);
        return element;
    }

    public static TextBlock Text(string text, string style = "Pp.Body") => new TextBlock { Text = text }.Styled(style);

    public static TextBlock Caption(string text, bool tertiary = false, bool trim = false)
    {
        var t = new TextBlock { Text = text }.Styled("Pp.Caption");
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
        var t = new TextBlock { Text = glyph, FontSize = size }.Styled("Pp.Icon");
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
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        if (click is not null) b.Click += click;
        return b;
    }

    /// <summary>Bouton icône discret (avec info-bulle et nom accessible).</summary>
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

    /// <summary>Pastille (fond + texte) ; <paramref name="tone"/> : Neutral, Success, Warning, Danger, Info, Accent.</summary>
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
        t.SetResourceReference(TextBlock.ForegroundProperty, fg);
        panel.Children.Add(t);
        var b = new Border { Child = panel }.Styled("Pp.Badge");
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    /// <summary>Case d'icône (application sans icône, service, tâche).</summary>
    public static Border GlyphTile(string glyph, string bgKey = "Pp.AccentSubtle", string fgKey = "Pp.AccentText")
    {
        var icon = Icon(glyph, 16, fgKey);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Child = icon, VerticalAlignment = VerticalAlignment.Top };
        b.SetResourceReference(Border.BackgroundProperty, bgKey);
        return b;
    }

    public static Border ImageTile(ImageSource image)
    {
        var img = new Image { Source = image, Width = 28, Height = 28, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var b = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Child = img, VerticalAlignment = VerticalAlignment.Top };
        b.SetResourceReference(Border.BackgroundProperty, "Pp.CardSecondary");
        return b;
    }

    public static TextBox SearchBox(string placeholder, Action<string> changed, double width = 280)
    {
        var box = new TextBox { Tag = placeholder, Width = width, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.SearchBox");
        System.Windows.Automation.AutomationProperties.SetName(box, placeholder);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { timer.Stop(); changed(box.Text.Trim()); };
        box.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        return box;
    }

    /// <summary>Barre d'outils : recherche (largeur souple) et boutons à droite, filtres sur la ligne suivante.</summary>
    public static StackPanel Toolbar(TextBox search, SegmentedBar filter, params Button[] buttons)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in buttons)
        {
            b.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(b);
        }
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        search.Width = double.NaN;
        search.MaxWidth = 460;
        search.HorizontalAlignment = HorizontalAlignment.Stretch;
        search.Margin = new Thickness(0);
        row.Children.Add(new Border { Child = search, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220, Width = 460, MaxWidth = 460 });
        filter.Margin = new Thickness(0);
        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 12) };
        panel.Children.Add(row);
        panel.Children.Add(filter);
        return panel;
    }

    public static Border Divider(Thickness margin) =>
        new Border { Height = 1, Margin = margin, SnapsToDevicePixels = true }.Themed(Border.BackgroundProperty, "Pp.Divider");

    /// <summary>État vide / erreur / chargement dans une carte centrée.</summary>
    public static Border StateCard(string glyph, string title, string? detail = null, bool busy = false)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 18) };
        if (busy)
            stack.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 160, Height = 3, Margin = new Thickness(0, 0, 0, 14) });
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
        return new Border { Child = stack }.Styled("Pp.Card");
    }

    /// <summary>Ouvre une console MMC de System32 (chemin absolu, sans shell).</summary>
    public static void OpenConsole(string msc)
    {
        try { ProcessRunner.Launch(SystemTool.Mmc, Path.Combine(Environment.SystemDirectory, msc)); }
        catch (Exception ex)
        {
            Log.Warn("Startup", $"ouverture de {msc} : {ex.Message}");
            AppHost.Toasts.Show(L("Impossible d'ouvrir la console : {0}", ex.Message), UI.Services.ToastKind.Error);
        }
    }

    /// <summary>Ouvre l'Explorateur sur un fichier (sélectionné) ou un dossier local.</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path)) ProcessRunner.OpenFolder(path);
            else if (File.Exists(path))
            {
                var full = Path.GetFullPath(path);
                // L'Explorateur découpe ses arguments sur les virgules : un nom de fichier qui en contient serait mal interprété,
                // on ouvre alors simplement le dossier parent.
                if (full.Contains(',') && Path.GetDirectoryName(full) is { } parent) ProcessRunner.OpenFolder(parent);
                else ProcessRunner.Launch(SystemTool.Explorer, "/select,", full);
            }
            else AppHost.Toasts.Show(L("Emplacement introuvable : {0}", path), UI.Services.ToastKind.Warning);
        }
        catch (Exception ex)
        {
            AppHost.Toasts.Show(L("Impossible d'ouvrir l'emplacement : {0}", ex.Message), UI.Services.ToastKind.Error);
        }
    }
}

/// <summary>Sélecteur segmenté (onglets ou filtres) construit avec les styles de boutons existants.</summary>
internal sealed class SegmentedBar : Border
{
    private readonly List<Button> _buttons = [];
    private readonly List<TextBlock> _counts = [];
    private readonly bool _compact;
    public int SelectedIndex { get; private set; } = -1;
    public event EventHandler<int>? SelectionChanged;

    public SegmentedBar(bool compact = false)
    {
        _compact = compact;
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(3);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Left;
        this.Themed(BackgroundProperty, "Pp.ControlFill");
        this.Themed(BorderBrushProperty, "Pp.ControlStroke");
        Child = new WrapPanel();
    }

    public void Add(string text, string? glyph = null)
    {
        var index = _buttons.Count;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
        {
            var i = StartupUi.Icon(glyph, _compact ? 12 : 14);
            i.Margin = new Thickness(0, 0, 8, 0);
            i.VerticalAlignment = VerticalAlignment.Center;
            i.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
            });
            panel.Children.Add(i);
        }
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var count = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Visibility = Visibility.Collapsed };
        count.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
        });
        count.Opacity = 0.75;
        panel.Children.Add(count);
        var b = new Button { Content = panel, Margin = new Thickness(index == 0 ? 0 : 2, 0, 0, 0) }.Styled("Pp.SubtleButton");
        b.Padding = _compact ? new Thickness(12, 3, 12, 3) : new Thickness(16, 6, 16, 6);
        if (_compact) { b.MinHeight = 28; b.FontSize = 13; }
        System.Windows.Automation.AutomationProperties.SetName(b, text);
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
            var b = _buttons[i];
            // Segment choisi : style « accent » (ses propres états de survol restent lisibles dans les deux thèmes).
            b.SetResourceReference(StyleProperty, i == index ? "Pp.AccentButton" : "Pp.SubtleButton");
            b.FontWeight = i == index ? FontWeights.SemiBold : FontWeights.Normal;
        }
        if (changed && notify) SelectionChanged?.Invoke(this, index);
    }
}

/// <summary>
/// Liste paginée : n'affiche que les N premiers éléments puis « Afficher plus » (évite de créer des centaines
/// de cartes d'un coup sur les PC modestes).
/// </summary>
internal sealed class PagedList<T> : StackPanel
{
    private readonly Func<T, FrameworkElement> _factory;
    private readonly int _pageSize;
    private readonly StackPanel _items = new();
    private readonly Button _more;
    private IReadOnlyList<T> _source = [];
    private int _shown;

    public PagedList(Func<T, FrameworkElement> factory, int pageSize = 40)
    {
        _factory = factory;
        _pageSize = pageSize;
        _more = StartupUi.Button(L("Afficher plus"), "", "Pp.Button", (_, _) => ShowMore());
        _more.HorizontalAlignment = HorizontalAlignment.Center;
        _more.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(_items);
        Children.Add(_more);
    }

    public void SetItems(IReadOnlyList<T> items)
    {
        _source = items;
        _items.Children.Clear();
        _shown = 0;
        ShowMore();
    }

    private void ShowMore()
    {
        var end = Math.Min(_source.Count, _shown + _pageSize);
        for (var i = _shown; i < end; i++) _items.Children.Add(_factory(_source[i]));
        _shown = end;
        var remaining = _source.Count - _shown;
        _more.Visibility = remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(_more, LP(remaining, "Afficher plus ({0} restant)", "Afficher plus ({0} restants)"));
        if (_more.Content is StackPanel p && p.Children.Count > 1 && p.Children[1] is TextBlock t)
            t.Text = LP(remaining, "Afficher plus ({0} restant)", "Afficher plus ({0} restants)");
    }
}
