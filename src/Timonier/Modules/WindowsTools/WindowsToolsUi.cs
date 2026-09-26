using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.WindowsTools;

/// <summary>Aides d'interface du module (thread UI uniquement).</summary>
internal static class WindowsToolsUi
{
    public static T Styled<T>(this T element, string styleKey) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return element;
    }

    public static T Brush<T>(this T element, DependencyProperty property, string resourceKey) where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }

    public static TextBlock Text(string text, string style = "Pp.Body") => new TextBlock { Text = text }.Styled(style);

    public static TextBlock Icon(string glyph, double size = 16, string? brushKey = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Icon");
        if (brushKey is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    /// <summary>Pastille (badge) avec icône optionnelle ; couleurs du thème.</summary>
    public static Border Badge(string text, string? glyph = null, string background = "Pp.NeutralBackground",
        string foreground = "Pp.Neutral", bool mono = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
        {
            var i = Icon(glyph, 10, foreground);
            i.Margin = new Thickness(0, 0, 4, 0);
            panel.Children.Add(i);
        }
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }.Styled("Pp.BadgeText");
        t.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        if (mono) t.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        panel.Children.Add(t);
        var b = new Border { Child = panel, Margin = new Thickness(0, 0, 6, 4) }.Styled("Pp.Badge");
        b.SetResourceReference(Border.BackgroundProperty, background);
        return b;
    }

    /// <summary>Rend un élément « cliquable » (souris, Entrée, Espace) et accessible.</summary>
    public static void MakeClickable(FrameworkElement element, string name, Action onClick)
    {
        element.Cursor = Cursors.Hand;
        element.Focusable = true;
        KeyboardNavigation.SetIsTabStop(element, true);
        System.Windows.Automation.AutomationProperties.SetName(element, name);
        var pressed = false;
        element.MouseLeftButtonDown += (_, e) => { pressed = true; e.Handled = true; };
        element.MouseLeave += (_, _) => pressed = false;
        element.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            e.Handled = true;
            onClick();
        };
        element.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                e.Handled = true;
                onClick();
            }
        };
    }

    /// <summary>Ouvre un outil ; une erreur est signalée par une notification.</summary>
    public static bool Open(WinTool tool)
    {
        try
        {
            tool.Launch();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("WindowsTools", $"ouverture de {tool.Key} : {ex.Message}");
            AppHost.Toasts.Show(L("Impossible d'ouvrir « {0} » : {1}", tool.Title, ex.Message), ToastKind.Error);
            return false;
        }
    }

    /// <summary>Ouvre une page des Paramètres Windows.</summary>
    public static bool Open(SettingLink link)
    {
        try
        {
            ProcessRunner.OpenSettingsUri(link.Uri);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("WindowsTools", $"ouverture de {link.Uri} : {ex.Message}");
            AppHost.Toasts.Show(L("Impossible d'ouvrir « {0} » : {1}", link.Title, ex.Message), ToastKind.Error);
            return false;
        }
    }
}

/// <summary>
/// Grille adaptative : autant de colonnes de largeur égale que possible (largeur minimale imposée),
/// hauteur de ligne = élément le plus haut de la ligne. Les éléments masqués sont ignorés.
/// </summary>
internal sealed class CardGrid : Panel
{
    public double MinItemWidth { get; init; } = 240;
    public double Spacing { get; init; } = 8;

    private int Columns(double width)
    {
        if (double.IsInfinity(width) || width <= 0) return 3;
        return Math.Max(1, (int)((width + Spacing) / (MinItemWidth + Spacing)));
    }

    private double ItemWidth(double width, int cols) =>
        double.IsInfinity(width) ? MinItemWidth : Math.Max(0, (width - Spacing * (cols - 1)) / cols);

    protected override Size MeasureOverride(Size available)
    {
        var cols = Columns(available.Width);
        var itemW = ItemWidth(available.Width, cols);
        double total = 0, rowH = 0;
        var col = 0;
        var rows = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(itemW, double.PositiveInfinity));
            rowH = Math.Max(rowH, child.DesiredSize.Height);
            if (++col == cols)
            {
                total += rowH + (rows > 0 ? Spacing : 0);
                rows++;
                rowH = 0;
                col = 0;
            }
        }
        if (col > 0) total += rowH + (rows > 0 ? Spacing : 0);
        var width = double.IsInfinity(available.Width) ? cols * itemW + Spacing * (cols - 1) : available.Width;
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cols = Columns(final.Width);
        var itemW = ItemWidth(final.Width, cols);
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double y = 0;
        for (var start = 0; start < visible.Count; start += cols)
        {
            var row = visible.Skip(start).Take(cols).ToList();
            var rowH = row.Max(c => c.DesiredSize.Height);
            for (var i = 0; i < row.Count; i++)
                row[i].Arrange(new Rect(i * (itemW + Spacing), y, itemW, rowH));
            y += rowH + Spacing;
        }
        return final;
    }
}
