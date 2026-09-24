using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcPilot.Core.Platform;

namespace PcPilot.UI.Shell;

/// <summary>Demande de capture : fichier PNG, page à afficher, thème (light/dark), paramètre de navigation, défilement vertical.</summary>
public sealed record CaptureRequest(string OutputPath, string? PageId, string? Theme, string? Parameter = null, double? ScrollOffset = null);

/// <summary>
/// Outil de développement : affiche la fenêtre hors écran, ouvre la page demandée, attend le chargement
/// (détections asynchrones), rend le contenu dans un PNG puis quitte. Ne modifie rien sur le système.
/// </summary>
public static class CaptureRenderer
{
    public static async Task RunAsync(App app, MainWindow window, CaptureRequest request)
    {
        try
        {
            var output = Path.GetFullPath(request.OutputPath);
            if (!output.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Le fichier de capture doit être un .png");

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
            window.Width = 1200;
            window.Height = 820;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Show();
            await Task.Delay(400);
            if (request.PageId is { Length: > 0 } page) window.Navigate(page, string.IsNullOrEmpty(request.Parameter) ? null : request.Parameter);
            await Task.Delay(3500); // détections, chargement du matériel, animations

            if (request.ScrollOffset is > 0 && FindScrollViewer(window.PageHostContent) is { } scroller)
            {
                scroller.ScrollToVerticalOffset(request.ScrollOffset.Value);
                await Task.Delay(600);
            }

            var root = (FrameworkElement)window.Content;
            root.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(root);
            var width = (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle((Brush)Application.Current.FindResource("Pp.WindowBackground"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using (var fs = File.Create(output)) encoder.Save(fs);
            Log.Info("Capture", $"{output} ({width}x{height})");
        }
        catch (Exception ex)
        {
            Log.Error("Capture", "échec", ex);
        }
        finally
        {
            await app.ExitAsync();
        }
    }

    private static System.Windows.Controls.ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root is null) return null;
        if (root is System.Windows.Controls.ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
}
