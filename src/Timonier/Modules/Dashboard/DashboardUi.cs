using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Timonier.Core.Catalog;

namespace Timonier.Modules.Dashboard;

/// <summary>Petites briques visuelles du tableau de bord (styles du thème uniquement, aucune couleur codée en dur).</summary>
internal static class DashUi
{
    public static TextBlock Text(string text, string style, double? size = null, bool semiBold = false, string? brush = null)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (size is { } s) t.FontSize = s;
        if (semiBold) t.FontWeight = FontWeights.SemiBold;
        if (brush is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, string brush = "Pp.TextPrimary")
    {
        var t = new TextBlock { Text = glyph, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Icon");
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>Pastille carrée arrondie contenant une icône (fond et couleur du thème).</summary>
    public static Border IconBox(string glyph, double box = 36, double size = 18, string fg = "Pp.AccentText", string bg = "Pp.AccentSubtle", double radius = 8)
    {
        var b = new Border
        {
            Width = box, Height = box, CornerRadius = new CornerRadius(radius), VerticalAlignment = VerticalAlignment.Top,
            Child = Icon(glyph, size, fg),
        };
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    public static Button Button(string text, string? glyph = null, string style = "Pp.Button")
    {
        var b = new Button();
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (string.IsNullOrEmpty(glyph)) b.Content = text;
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock { Text = glyph, FontSize = 13, Margin = new Thickness(0, 0, 8, 0) };
            icon.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Icon");
            icon.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Control.Foreground)) { Source = b });
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            b.Content = row;
        }
        return b;
    }

    public static Border Badge(string text, string fg = "Pp.Neutral", string bg = "Pp.NeutralBackground")
    {
        var t = Text(text, "Pp.BadgeText", brush: fg);
        var b = new Border { Child = t, Margin = new Thickness(0, 0, 6, 4) };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Badge");
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    /// <summary>En-tête de section : titre, légende facultative et élément aligné à droite.</summary>
    public static FrameworkElement SectionHeader(string title, TextBlock? caption = null, FrameworkElement? right = null)
    {
        var dock = new DockPanel { Margin = new Thickness(2, 26, 0, 10), LastChildFill = true };
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
        }
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        var t = Text(title, "Pp.SectionTitle", 16, semiBold: true);
        t.Margin = new Thickness(0);
        stack.Children.Add(t);
        if (caption is not null)
        {
            caption.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(caption);
        }
        dock.Children.Add(stack);
        return dock;
    }

    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Pp.Card");
        if (padding is { } p) b.Padding = p;
        return b;
    }

    /// <summary>Grille à colonnes égales dont chaque ligne prend la hauteur de son plus grand élément.</summary>
    public static Grid Columns(IReadOnlyList<UIElement> items, int columns, double gap = 8)
    {
        var g = new Grid();
        for (var c = 0; c < columns; c++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = (items.Count + columns - 1) / columns;
        for (var r = 0; r < rows; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < items.Count; i++)
        {
            var el = items[i];
            int col = i % columns, row = i / columns;
            if (el is FrameworkElement fe)
                fe.Margin = new Thickness(col == 0 ? 0 : gap / 2, row == 0 ? 0 : gap / 2, col == columns - 1 ? 0 : gap / 2, row == rows - 1 ? 0 : gap / 2);
            Grid.SetColumn(el, col);
            Grid.SetRow(el, row);
            g.Children.Add(el);
        }
        return g;
    }

    public static (string Fg, string Bg, string Glyph, string Label) StatusVisual(HealthStatus s) => s switch
    {
        HealthStatus.Good => ("Pp.Success", "Pp.SuccessBackground", "", "OK"),
        HealthStatus.Info => ("Pp.Info", "Pp.InfoBackground", "", "Info"),
        HealthStatus.Warning => ("Pp.Warning", "Pp.WarningBackground", "", "À vérifier"),
        HealthStatus.Critical => ("Pp.Danger", "Pp.DangerBackground", "", "Critique"),
        _ => ("Pp.Neutral", "Pp.NeutralBackground", "", "Inconnu"),
    };

    public static int Severity(HealthStatus s) => s switch
    {
        HealthStatus.Critical => 0,
        HealthStatus.Warning => 1,
        HealthStatus.Info => 2,
        HealthStatus.Unknown => 3,
        _ => 4,
    };

    /// <summary>Rectangle gris arrondi (squelette de chargement).</summary>
    public static Border Skeleton(double width, double height, Thickness margin)
    {
        var b = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(4), Margin = margin, HorizontalAlignment = HorizontalAlignment.Left };
        b.SetResourceReference(Border.BackgroundProperty, "Pp.CardSecondary");
        return b;
    }

    private static Style? _cardButton;

    /// <summary>Bouton présenté comme une carte (actions rapides, recommandations).</summary>
    public static Style CardButtonStyle => _cardButton ??= (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Setter Property="Foreground" Value="{DynamicResource Pp.TextPrimary}" />
          <Setter Property="Background" Value="{DynamicResource Pp.CardBackground}" />
          <Setter Property="BorderBrush" Value="{DynamicResource Pp.CardBorder}" />
          <Setter Property="Cursor" Value="Hand" />
          <Setter Property="FocusVisualStyle" Value="{x:Null}" />
          <Setter Property="HorizontalContentAlignment" Value="Stretch" />
          <Setter Property="VerticalContentAlignment" Value="Stretch" />
          <Setter Property="Padding" Value="14,12" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="1" CornerRadius="8" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
                  <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}" />
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Pp.CardHover}" />
                    <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Pp.ControlStroke}" />
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Pp.CardSecondary}" />
                  </Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Pp.Accent}" />
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.5" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);
}

/// <summary>Anneau de progression (0 à 1) dessiné avec les pinceaux du thème.</summary>
internal sealed class DashRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(DashRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(DashRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(DashRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double StrokeThickness { get; init; } = 6;

    public DashRing()
    {
        SetResourceReference(TrackProperty, "Pp.Divider");
        SetResourceReference(FillProperty, "Pp.Accent");
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= StrokeThickness * 2) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - StrokeThickness) / 2;
        if (Track is { } track) dc.DrawEllipse(null, new Pen(track, StrokeThickness), center, radius, radius);

        var v = Math.Clamp(Value, 0, 1);
        if (v <= 0.001 || Fill is not { } fill) return;
        var pen = new Pen(fill, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (v >= 0.999) { dc.DrawEllipse(null, pen, center, radius, radius); return; }

        var angle = v * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, v > 0.5, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}

/// <summary>Barre horizontale fine (0 à 1).</summary>
internal sealed class DashBar : Grid
{
    private readonly Border _fill;
    private double _value;

    public DashBar(double height = 6)
    {
        Height = height;
        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "Pp.Divider");
        _fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        FillKey = "Pp.Accent";
        Children.Add(track);
        Children.Add(_fill);
        SizeChanged += (_, _) => Apply();
    }

    public string FillKey { set => _fill.SetResourceReference(Border.BackgroundProperty, value); }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); Apply(); }
    }

    private void Apply() => _fill.Width = ActualWidth * _value;
}
