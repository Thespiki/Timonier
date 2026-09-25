using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Timonier.UI.Controls;

/// <summary>
/// En-tête standard d'une page (icône, titre, sous-titre). Utilisable en XAML :
/// <c>&lt;controls:PageHeader Title="Confidentialité" Subtitle="…" Glyph="&amp;#xE72E;" /&gt;</c>
/// </summary>
public sealed class PageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata("", (d, _) => ((PageHeader)d).Update()));
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader), new PropertyMetadata(null, (d, _) => ((PageHeader)d).Update()));
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(PageHeader), new PropertyMetadata(null, (d, _) => ((PageHeader)d).Update()));

    private readonly TextBlock _title = new();
    private readonly TextBlock _subtitle = new();
    private readonly TextBlock _glyph = new();
    private readonly Border _glyphBox;

    public PageHeader()
    {
        Focusable = false;
        _title.SetResourceReference(StyleProperty, "Pp.PageTitle");
        _subtitle.SetResourceReference(StyleProperty, "Pp.PageSubtitle");
        _glyph.SetResourceReference(StyleProperty, "Pp.Icon");
        _glyph.FontSize = 22;
        _glyph.HorizontalAlignment = HorizontalAlignment.Center;
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, "Pp.AccentText");
        _glyphBox = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 2, 14, 0),
            VerticalAlignment = VerticalAlignment.Top, Child = _glyph,
        };
        _glyphBox.SetResourceReference(Border.BackgroundProperty, "Pp.AccentSubtle");

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_title);
        text.Children.Add(_subtitle);
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(_glyphBox, Dock.Left);
        dock.Children.Add(_glyphBox);
        dock.Children.Add(text);
        Content = dock;
        Update();
    }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    private void Update()
    {
        _title.Text = Title;
        _subtitle.Text = Subtitle ?? "";
        _subtitle.Visibility = string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        _glyph.Text = Glyph ?? "";
        _glyphBox.Visibility = string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>Aides pour construire rapidement des pages cohérentes en code.</summary>
public static class PageScaffold
{
    /// <summary>ScrollViewer + colonne centrale standard ; renvoie la colonne pour y ajouter le contenu.</summary>
    public static StackPanel Create(ContentControl host, string title, string? subtitle, string glyph)
    {
        var stack = new StackPanel();
        stack.SetResourceReference(FrameworkElement.StyleProperty, "Pp.PageStack");
        stack.Children.Add(new PageHeader { Title = title, Subtitle = subtitle, Glyph = glyph });
        var scroll = new ScrollViewer { Content = stack };
        scroll.SetResourceReference(FrameworkElement.StyleProperty, "Pp.PageScroll");
        host.Content = scroll;
        return stack;
    }

    public static TextBlock Section(string title)
    {
        var t = new TextBlock { Text = title };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Pp.SectionTitle");
        return t;
    }

    public static Border Card(UIElement child)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Card");
        return b;
    }

    public static Border InfoBar(string text, string glyph = "", string style = "Pp.InfoBar")
    {
        var icon = new TextBlock { Text = glyph, Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Icon");
        var body = new TextBlock { Text = text };
        body.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Body");
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(body);
        var b = new Border { Child = dock };
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        return b;
    }

    /// <summary>Ligne « libellé : valeur » pour les fiches d'information.</summary>
    public static Grid KeyValue(string key, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var k = new TextBlock { Text = key };
        k.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Caption");
        var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        v.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Body");
        Grid.SetColumn(v, 1);
        g.Children.Add(k);
        g.Children.Add(v);
        return g;
    }

    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
