using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Timonier.Core.Engine;
using Timonier.Core.Platform;

namespace Timonier.Modules.Users;

/// <summary>Fabriques d'éléments cohérents avec le système de design (styles Pp.*, ressources dynamiques uniquement).</summary>
internal static class UsersUi
{
    public const string GlyphUser = "";
    public const string GlyphGroup = "";
    public const string GlyphAdmin = "";
    public const string GlyphLock = "";
    public const string GlyphClock = "";
    public const string GlyphFamily = "";
    public const string GlyphInfo = "";
    public const string GlyphWarning = "";
    public const string GlyphAdd = "";
    public const string GlyphDelete = "";
    public const string GlyphRefresh = "";
    public const string GlyphKey = "";
    public const string GlyphBlock = "";
    public const string GlyphSignIn = "";
    public const string GlyphOpen = "";

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

    public static TextBlock Icon(string glyph, double size = 16, string? brushKey = null)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Icon");
        if (brushKey is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    public static Border IconTile(string glyph, double size = 36, string background = "Pp.AccentSubtle", string foreground = "Pp.AccentText")
    {
        var icon = Icon(glyph, size * 0.48, foreground);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = icon, VerticalAlignment = VerticalAlignment.Top }
            .Themed(Border.BackgroundProperty, background);
    }

    public static Button MakeButton(string text, string? glyph, string style = "Pp.Button", RoutedEventHandler? click = null)
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
        ToolTipService.SetShowOnDisabled(b, true);
        if (click is not null) b.Click += click;
        return b;
    }

    public static Border Badge(string text, string tone = "Pp.Neutral")
    {
        var t = new TextBlock { Text = text }.Styled("Pp.BadgeText").Themed(TextBlock.ForegroundProperty, tone);
        return new Border { Child = t, Margin = new Thickness(0, 2, 6, 2) }.Styled("Pp.Badge").Themed(Border.BackgroundProperty, tone + "Background");
    }

    public static Border Divider(Thickness margin) =>
        new Border { Height = 1, Margin = margin, SnapsToDevicePixels = true }.Themed(Border.BackgroundProperty, "Pp.Divider");

    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var b = new Border { Child = child }.Styled("Pp.Card");
        if (padding is { } p) b.Padding = p;
        return b;
    }

    public static ProgressBar BusyBar(double width = 60) => new()
    {
        IsIndeterminate = true, Width = width, Height = 3, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed,
    };

    /// <summary>Titre de section avec éléments facultatifs à droite (boutons, compteur).</summary>
    public static DockPanel SectionHeader(string title, out TextBlock heading, params UIElement[] right)
    {
        heading = Text(title, "Pp.SectionTitle");
        var dock = new DockPanel { LastChildFill = true };
        if (right.Length > 0)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var r in right)
            {
                if (r is FrameworkElement fe) fe.Margin = new Thickness(6, 0, 0, 0);
                panel.Children.Add(r);
            }
            DockPanel.SetDock(panel, Dock.Right);
            dock.Children.Add(panel);
        }
        dock.Children.Add(heading);
        return dock;
    }

    /// <summary>Ligne titre + description à gauche, contrôle à droite (même rythme que les cartes de réglage).</summary>
    public static Grid SettingRow(string glyph, string title, string description, UIElement control, string? note = null)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Icon(glyph, 18, "Pp.TextSecondary");
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 14, 0);
        g.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var t = Text(title, "Pp.CardTitle");
        text.Children.Add(t);
        var d = Caption(description);
        d.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(d);
        if (note is not null)
        {
            var n = Caption(note);
            n.FontStyle = FontStyles.Italic;
            n.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(n);
        }
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 2);
        g.Children.Add(control);
        return g;
    }

    /// <summary>État vide / erreur centré dans une carte.</summary>
    public static Border EmptyState(string glyph, string title, string text, UIElement? action = null)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
        var icon = Icon(glyph, 28, "Pp.TextTertiary");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var t = Text(title, "Pp.CardTitle");
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 8, 0, 2);
        stack.Children.Add(t);
        var c = Caption(text);
        c.TextAlignment = TextAlignment.Center;
        c.MaxWidth = 520;
        stack.Children.Add(c);
        if (action is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(fe);
        }
        return Card(stack);
    }

    public static void OpenUri(string uri)
    {
        try { ProcessRunner.OpenSettingsUri(uri); }
        catch (Exception ex)
        {
            Log.Warn("Users", "ouverture " + uri + " : " + ex.Message);
            AppHost.Toasts.Show(L("Couldn't open this Settings page."), UI.Services.ToastKind.Error);
        }
    }

    public static void Launch(SystemTool tool, params string[] args)
    {
        try { ProcessRunner.Launch(tool, args); }
        catch (Exception ex)
        {
            Log.Warn("Users", "lancement " + tool + " : " + ex.Message);
            AppHost.Toasts.Show(L("Couldn't open this Windows tool."), UI.Services.ToastKind.Error);
        }
    }

    /// <summary>Navigue vers une autre page de Timonier si elle existe (les modules sont indépendants).</summary>
    public static bool TryNavigate(string pageId, object? parameter = null)
    {
        if (AppHost.Registry.GetPage(pageId) is null) return false;
        AppHost.Navigator.Navigate(pageId, parameter);
        return true;
    }

    /// <summary>Exécute une action (broker si admin), affiche le résultat et renvoie l'issue.</summary>
    public static async Task<ApplyOutcome> RunAsync(string actionId, Dictionary<string, string> parameters, bool toast = true)
    {
        var outcome = await AppHost.Engine.RunActionAsync(actionId, parameters);
        if (toast && !outcome.Cancelled) AppHost.Toasts.ShowOutcome(outcome);
        return outcome;
    }
}
