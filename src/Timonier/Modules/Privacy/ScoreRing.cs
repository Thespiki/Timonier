using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace Timonier.Modules.Privacy;

/// <summary>
/// Jauge circulaire (anneau) avec le pourcentage au centre. Couleurs par clés de ressources dynamiques :
/// suit le thème clair/sombre à chaud.
/// </summary>
public sealed class ScoreRing : Grid
{
    private const double RingSize = 96;
    private const double Stroke = 9;

    private readonly ShapePath _arc;
    private readonly TextBlock _value;
    private readonly TextBlock _caption;

    public ScoreRing()
    {
        Width = RingSize;
        Height = RingSize;
        SnapsToDevicePixels = true;

        var track = new Ellipse { StrokeThickness = Stroke, Width = RingSize, Height = RingSize };
        track.SetResourceReference(Shape.StrokeProperty, "Pp.Divider");

        _arc = new ShapePath
        {
            StrokeThickness = Stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        _arc.SetResourceReference(Shape.StrokeProperty, "Pp.Accent");

        _value = new TextBlock { FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center };
        _value.SetResourceReference(StyleProperty, "Pp.Metric");
        _caption = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
        _caption.SetResourceReference(StyleProperty, "Pp.Caption");

        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        center.Children.Add(_value);
        center.Children.Add(_caption);

        Children.Add(track);
        Children.Add(_arc);
        Children.Add(center);
        System.Windows.Automation.AutomationProperties.SetName(this, L("Score de confidentialité"));
    }

    /// <summary>Met à jour la jauge. <paramref name="ratio"/> null = état indéterminé (chargement).</summary>
    public void Update(double? ratio, string text, string caption, string brushKey)
    {
        _value.Text = text;
        _caption.Text = caption;
        _caption.Visibility = string.IsNullOrEmpty(caption) ? Visibility.Collapsed : Visibility.Visible;
        _arc.SetResourceReference(Shape.StrokeProperty, brushKey);
        _arc.Data = ratio is { } r ? BuildArc(r) : null;
        ToolTip = ratio is { } v ? L("{0} % des réglages évalués suivent la recommandation", Math.Round(v * 100)) : null;
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
        var start = new Point(c, c - radius);
        var end = new Point(c + radius * Math.Sin(angle), c - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }
}
