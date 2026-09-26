using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Timonier.Core.Platform;

namespace Timonier.Modules.Maintenance;

/// <summary>Petites fabriques d'éléments cohérents avec le système de design (styles Pp.*, pinceaux dynamiques uniquement).</summary>
internal static class MaintUi
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

    public static TextBlock Text(string text, string style = "Pp.Body", bool wrap = true) =>
        new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap }.Styled(style);

    public static TextBlock Icon(string glyph, double size = 16, string? brushKey = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Icon");
        if (brushKey is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    /// <summary>Bouton avec icône Segoe Fluent et libellé (l'icône prend la couleur du texte du bouton).</summary>
    public static Button Button(string text, string? glyph, string style = "Pp.Button", RoutedEventHandler? click = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icon(glyph, 14);
            icon.Margin = new Thickness(0, 0, 8, 0);
            icon.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
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

    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var b = new Border { Child = child }.Styled("Pp.Card");
        if (margin is { } m) b.Margin = m;
        return b;
    }

    /// <summary>Pastille (badge) : texte court sur fond coloré discret (ex. « Admin », « 12× »).</summary>
    public static Border Badge(string text, string foreground = "Pp.TextSecondary", string background = "Pp.CardSecondary", string? glyph = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
        {
            var i = Icon(glyph, 11, foreground);
            i.Margin = new Thickness(0, 0, 5, 0);
            row.Children.Add(i);
        }
        var t = new TextBlock { Text = text, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        row.Children.Add(t);
        var b = new Border
        {
            Child = row, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.SetResourceReference(Border.BackgroundProperty, background);
        return b;
    }

    /// <summary>Carré d'icône coloré (en tête de ligne d'outil ou de catégorie).</summary>
    public static Border IconTile(string glyph, string foreground = "Pp.AccentText", string background = "Pp.AccentSubtle", double size = 36)
    {
        var b = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = glyph, FontSize = size * 0.46, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                .Styled("Pp.Icon").Themed(TextBlock.ForegroundProperty, foreground),
        };
        b.SetResourceReference(Border.BackgroundProperty, background);
        return b;
    }

    /// <summary>En-tête de section de la page (titre + phrase d'explication).</summary>
    public static StackPanel SectionHeader(string title, string subtitle)
    {
        var s = new StackPanel { Margin = new Thickness(0, 26, 0, 10) };
        var t = Text(title, "Pp.SectionTitle");
        t.Margin = new Thickness(0);
        s.Children.Add(t);
        var sub = Text(subtitle, "Pp.Caption");
        sub.Margin = new Thickness(0, 4, 0, 0);
        s.Children.Add(sub);
        return s;
    }

    /// <summary>Barre d'information (texte modifiable) : renvoie la barre et son texte.</summary>
    public static (Border Bar, TextBlock Text, TextBlock Icon) InfoBar(string style = "Pp.InfoBar", string glyph = "")
    {
        var icon = new TextBlock { Text = glyph, Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Top }.Styled("Pp.Icon");
        var body = new TextBlock { TextWrapping = TextWrapping.Wrap }.Styled("Pp.Body");
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(body);
        var bar = new Border { Child = dock, Visibility = Visibility.Collapsed }.Styled(style);
        return (bar, body, icon);
    }

    public static void SetInfo(Border bar, TextBlock text, TextBlock icon, string message, string style, string glyph)
    {
        bar.SetResourceReference(FrameworkElement.StyleProperty, style);
        text.Text = message;
        icon.Text = glyph;
        bar.Visibility = Visibility.Visible;
    }

    public static ProgressBar BusyBar(double width = 120) => new()
    {
        IsIndeterminate = true, Width = width, Height = 3, VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>
    /// Lance un outil graphique de Windows (chemin absolu de la liste blanche, arguments constants) via ShellExecute :
    /// nécessaire pour les outils qui demandent eux-mêmes l'élévation (rstrui, propriétés système), sans aucune
    /// interprétation de ligne de commande.
    /// </summary>
    public static void OpenTool(SystemTool tool, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(SystemTools.Resolve(tool)) { UseShellExecute = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // L'utilisateur a refusé l'invite UAC de l'outil : rien à signaler.
        }
        catch (Exception ex)
        {
            Log.Warn("Maintenance", $"ouverture de {tool} : {ex.Message}");
            AppHost.Toasts.Show(L("Couldn't open this Windows tool: {0}", ex.Message), UI.Services.ToastKind.Error);
        }
    }

    public static void OpenSettings(string uri)
    {
        try { ProcessRunner.OpenSettingsUri(uri); }
        catch (Exception ex)
        {
            Log.Warn("Maintenance", $"ouverture de {uri} : {ex.Message}");
            AppHost.Toasts.Show(L("Couldn't open Windows Settings."), UI.Services.ToastKind.Error);
        }
    }
}
