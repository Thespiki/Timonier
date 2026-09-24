using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcPilot.Core.Platform;

namespace PcPilot.Modules.Customization;

/// <summary>Petites fabriques d'éléments d'interface cohérents avec le système de design (styles Pp.*, ressources dynamiques).</summary>
internal static class UiKit
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

    public static TextBlock Icon(string glyph, double size = 16, string? brushKey = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size }.Styled("Pp.Icon");
        if (brushKey is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    /// <summary>Bouton avec icône Segoe Fluent et libellé.</summary>
    public static Button Button(string text, string? glyph, string style = "Pp.Button", RoutedEventHandler? click = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icon(glyph, 14);
            icon.Margin = new Thickness(0, 0, 8, 0);
            // L'icône prend la couleur du texte du bouton (accent ou normal).
            icon.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Control.Foreground))
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1),
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

    public static Border Divider(Thickness margin) =>
        new Border { Height = 1, Margin = margin, SnapsToDevicePixels = true }.Themed(Border.BackgroundProperty, "Pp.Divider");

    /// <summary>Découpe arrondie (les Border n'écrêtent pas leur contenu) ; <paramref name="bottomRounded"/> = false pour un en-tête de carte.</summary>
    public static void ClipRounded(FrameworkElement element, double radius, bool bottomRounded = true)
    {
        element.SizeChanged += (_, e) =>
        {
            var h = bottomRounded ? e.NewSize.Height : e.NewSize.Height + radius;
            element.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, h), radius, radius);
        };
    }

    /// <summary>Barre de progression discrète pour signaler une opération en cours.</summary>
    public static ProgressBar BusyBar() => new()
    {
        IsIndeterminate = true, Width = 48, Height = 3, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed,
    };

    /// <summary>
    /// Décode une image en miniature (hors du thread UI) : lecture partagée, décodage réduit par WIC, image figée.
    /// Renvoie null si le fichier est absent ou illisible.
    /// </summary>
    public static BitmapSource? LoadThumbnail(string path, int decodeWidth)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Decode(fs, decodeWidth);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            Log.Warn("Customization", "miniature illisible : " + ex.Message);
            return null;
        }
    }

    public static BitmapSource Decode(Stream stream, int decodeWidth)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelWidth = decodeWidth;
        bmp.StreamSource = stream;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Pinceau figé pour une couleur de contenu (couleur choisie par l'utilisateur, couleur du système).</summary>
    public static SolidColorBrush? BrushFromHex(string? hex)
    {
        if (hex is not { Length: 6 } || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
            return null;
        var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        b.Freeze();
        return b;
    }
}
