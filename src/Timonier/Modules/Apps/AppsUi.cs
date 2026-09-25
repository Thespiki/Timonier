using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace Timonier.Modules.Apps;

/// <summary>
/// Grille à deux colonnes (une seule sous 640 px) dont chaque ligne prend la hauteur de ses deux éléments visibles :
/// contrairement à UniformGrid, une description longue n'agrandit pas toutes les tuiles de la section.
/// </summary>
internal sealed class TwoColumnPanel : Panel
{
    private int Columns(double width) => double.IsInfinity(width) || width >= 640 ? 2 : 1;

    protected override Size MeasureOverride(Size available)
    {
        var cols = Columns(available.Width);
        var colWidth = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width / cols;
        double height = 0, rowHeight = 0, widest = 0;
        var i = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(colWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            widest = Math.Max(widest, child.DesiredSize.Width);
            if (++i % cols == 0) { height += rowHeight; rowHeight = 0; }
        }
        height += rowHeight;
        return new Size(double.IsInfinity(available.Width) ? widest * cols : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cols = Columns(final.Width);
        var colWidth = final.Width / cols;
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double y = 0;
        for (var r = 0; r < visible.Count; r += cols)
        {
            var row = visible.Skip(r).Take(cols).ToList();
            var h = row.Max(c => c.DesiredSize.Height);
            for (var c = 0; c < row.Count; c++) row[c].Arrange(new Rect(c * colWidth, y, colWidth, h));
            y += h;
        }
        return final;
    }
}

/// <summary>Fabriques d'éléments cohérents avec le système de design (styles Pp.*, ressources dynamiques uniquement).</summary>
internal static class AppsUi
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

    public static TextBlock Strong(string text, double size = 14)
    {
        var t = Text(text);
        t.FontWeight = FontWeights.SemiBold;
        t.FontSize = size;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.TextWrapping = TextWrapping.NoWrap;
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, string? brushKey = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size }.Styled("Pp.Icon");
        if (brushKey is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    /// <summary>Icône qui suit la couleur de texte de son contrôle parent (bouton, segment…).</summary>
    public static TextBlock InheritingIcon(string glyph, double size, Type ancestor)
    {
        var icon = Icon(glyph, size);
        icon.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, ancestor, 1),
        });
        return icon;
    }

    public static Border IconTile(string glyph, double size = 36, string background = "Pp.AccentSubtle", string foreground = "Pp.AccentText")
    {
        var icon = Icon(glyph, size * 0.48, foreground);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 4.5), Child = icon, VerticalAlignment = VerticalAlignment.Center }
            .Themed(Border.BackgroundProperty, background);
    }

    /// <summary>Pastille de lettre (initiale) pour les listes de programmes.</summary>
    public static Border LetterTile(string name, double size = 32)
    {
        var letter = name.FirstOrDefault(char.IsLetterOrDigit);
        var t = new TextBlock
        {
            Text = letter == default ? "?" : char.ToUpperInvariant(letter).ToString(),
            FontSize = size * 0.45, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        }.Themed(TextBlock.ForegroundProperty, "Pp.AccentText");
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 4.5), Child = t, VerticalAlignment = VerticalAlignment.Center }
            .Themed(Border.BackgroundProperty, "Pp.AccentSubtle");
    }

    public static Button Button(string text, string? glyph, string style = "Pp.Button", RoutedEventHandler? click = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = InheritingIcon(glyph, 14, typeof(Button));
            icon.Margin = new Thickness(0, 0, 8, 0);
            panel.Children.Add(icon);
            panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = panel;
        }
        var b = new Button { Content = content }.Styled(style);
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        if (click is not null) b.Click += click;
        return b;
    }

    public static void SetButtonText(Button button, string text)
    {
        if (button.Content is StackPanel { Children.Count: 2 } sp && sp.Children[1] is TextBlock t) t.Text = text;
        else button.Content = text;
        System.Windows.Automation.AutomationProperties.SetName(button, text);
    }

    /// <summary>Badge coloré (Pp.Success, Pp.Warning, Pp.Danger, Pp.Info, Pp.Neutral) ou accent (« Pp.Accent »).</summary>
    public static Border Badge(string text, string tone = "Pp.Neutral")
    {
        var accent = tone == "Pp.Accent";
        var t = new TextBlock { Text = text }.Styled("Pp.BadgeText").Themed(TextBlock.ForegroundProperty, accent ? "Pp.AccentText" : tone);
        return new Border { Child = t }.Styled("Pp.Badge").Themed(Border.BackgroundProperty, accent ? "Pp.AccentSubtle" : tone + "Background");
    }

    public static Border Divider(Thickness margin) =>
        new Border { Height = 1, Margin = margin, SnapsToDevicePixels = true }.Themed(Border.BackgroundProperty, "Pp.Divider");

    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var b = new Border { Child = child }.Styled("Pp.Card");
        if (padding is { } p) b.Padding = p;
        return b;
    }

    public static Border InfoBar(string text, string glyph, string style = "Pp.InfoBar", UIElement? action = null)
    {
        var icon = Icon(glyph, 16);
        icon.Margin = new Thickness(0, 1, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var body = Text(text);
        body.FontSize = 13;
        body.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        if (action is not null)
        {
            DockPanel.SetDock(action, Dock.Right);
            if (action is FrameworkElement fe) { fe.Margin = new Thickness(12, 0, 0, 0); fe.VerticalAlignment = VerticalAlignment.Center; }
            dock.Children.Add(action);
        }
        dock.Children.Add(body);
        return new Border { Child = dock }.Styled(style);
    }

    /// <summary>Titre de section avec icône et légende facultative (compteur).</summary>
    public static DockPanel SectionHeader(string title, string? glyph, out TextBlock counter)
    {
        var dock = new DockPanel { Margin = new Thickness(2, 22, 0, 8) };
        if (glyph is not null)
        {
            var icon = Icon(glyph, 15, "Pp.AccentText");
            icon.Margin = new Thickness(0, 0, 8, 0);
            dock.Children.Add(icon);
        }
        var t = Text(title, "Pp.SectionTitle");
        t.Margin = new Thickness(0);
        dock.Children.Add(t);
        counter = Caption("");
        counter.Margin = new Thickness(8, 1, 0, 0);
        counter.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(counter);
        return dock;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) sp.Children.Add(c);
        return sp;
    }

    /// <summary>État vide/erreur centré dans une carte.</summary>
    public static Border EmptyState(string glyph, string title, string detail, string iconBrush = "Pp.TextTertiary")
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 18) };
        var icon = Icon(glyph, 30, iconBrush);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(icon);
        var t = Text(title, "Pp.CardTitle");
        t.FontWeight = FontWeights.SemiBold;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 10, 0, 2);
        sp.Children.Add(t);
        var d = Caption(detail);
        d.HorizontalAlignment = HorizontalAlignment.Center;
        d.TextAlignment = TextAlignment.Center;
        d.MaxWidth = 520;
        sp.Children.Add(d);
        return Card(sp);
    }

    /// <summary>État de chargement : barre indéterminée et texte.</summary>
    public static Border Loading(string text)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 14, 0, 14), HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 220, Height = 4 });
        var t = Caption(text);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(t);
        return Card(sp);
    }

    public static CheckBox CheckBox(string? automationName = null)
    {
        var cb = new CheckBox { MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0), Focusable = true };
        if (automationName is not null) System.Windows.Automation.AutomationProperties.SetName(cb, automationName);
        return cb;
    }

    public static TextBox SearchBox(string placeholder)
    {
        var tb = new TextBox { Tag = placeholder }.Styled("Pp.SearchBox");
        System.Windows.Automation.AutomationProperties.SetName(tb, placeholder);
        return tb;
    }

    private static Style? _segment;

    /// <summary>Style des segments du sélecteur de vue (RadioButton), couleurs dynamiques du thème.</summary>
    public static Style SegmentStyle => _segment ??= (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RadioButton">
          <Setter Property="Foreground" Value="{DynamicResource Pp.TextSecondary}" />
          <Setter Property="Cursor" Value="Hand" />
          <Setter Property="FocusVisualStyle" Value="{x:Null}" />
          <Setter Property="FontSize" Value="13" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="RadioButton">
                <Border x:Name="Bd" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="6" Margin="2">
                  <Grid>
                    <ContentPresenter Margin="12,8,12,8" HorizontalAlignment="Center" VerticalAlignment="Center" />
                    <Border x:Name="Ind" Height="3" Width="18" CornerRadius="1.5" VerticalAlignment="Bottom" Margin="0,0,0,2"
                            Background="{DynamicResource Pp.Accent}" Visibility="Collapsed" />
                  </Grid>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Pp.SubtleHover}" />
                    <Setter Property="Foreground" Value="{DynamicResource Pp.TextPrimary}" />
                  </Trigger>
                  <Trigger Property="IsChecked" Value="True">
                    <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Pp.CardBackground}" />
                    <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Pp.CardBorder}" />
                    <Setter TargetName="Ind" Property="Visibility" Value="Visible" />
                    <Setter Property="Foreground" Value="{DynamicResource Pp.TextPrimary}" />
                    <Setter Property="FontWeight" Value="SemiBold" />
                  </Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Pp.Accent}" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);
}
