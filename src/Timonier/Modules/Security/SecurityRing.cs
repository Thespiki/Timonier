using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace Timonier.Modules.Security;

/// <summary>Jauge circulaire du score de sécurité (couleurs par ressources dynamiques : suit le thème à chaud).</summary>
internal sealed class SecurityRing : Grid
{
    private const double RingSize = 104;
    private const double Stroke = 10;

    private readonly ShapePath _arc;
    private readonly TextBlock _value;
    private readonly TextBlock _caption;
    private readonly TextBlock _icon;

    public SecurityRing()
    {
        Width = RingSize;
        Height = RingSize;
        SnapsToDevicePixels = true;

        var track = new Ellipse { StrokeThickness = Stroke, Width = RingSize, Height = RingSize };
        track.SetResourceReference(Shape.StrokeProperty, "Pp.Divider");

        _arc = new ShapePath { StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _arc.SetResourceReference(Shape.StrokeProperty, "Pp.Accent");

        _icon = new TextBlock { Text = "", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center };
        _icon.SetResourceReference(StyleProperty, "Pp.Icon");
        _icon.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextSecondary");
        _value = new TextBlock { FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center };
        _value.SetResourceReference(StyleProperty, "Pp.Metric");
        _caption = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -3, 0, 0), Text = "sur 100" };
        _caption.SetResourceReference(StyleProperty, "Pp.Caption");

        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        center.Children.Add(_icon);
        center.Children.Add(_value);
        center.Children.Add(_caption);

        Children.Add(track);
        Children.Add(_arc);
        Children.Add(center);
        System.Windows.Automation.AutomationProperties.SetName(this, "Score de sécurité");
        Update(null, "");
    }

    /// <summary><paramref name="score"/> null = chargement (icône seule).</summary>
    public void Update(int? score, string brushKey)
    {
        _icon.Visibility = score is null ? Visibility.Visible : Visibility.Collapsed;
        _value.Visibility = _caption.Visibility = score is null ? Visibility.Collapsed : Visibility.Visible;
        _value.Text = score?.ToString() ?? "";
        if (brushKey.Length > 0)
        {
            _arc.SetResourceReference(Shape.StrokeProperty, brushKey);
            _value.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }
        _arc.Data = score is { } s ? BuildArc(s / 100.0) : null;
    }

    private static Geometry? BuildArc(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        if (ratio < 0.005) return null;
        var radius = (RingSize - Stroke) / 2;
        var c = RingSize / 2;
        if (ratio > 0.995)
        {
            var full = new EllipseGeometry(new Point(c, c), radius, radius);
            full.Freeze();
            return full;
        }
        var angle = ratio * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(c, c - radius), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(new Point(c + radius * Math.Sin(angle), c - radius * Math.Cos(angle)),
            new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }
}
