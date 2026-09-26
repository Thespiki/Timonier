using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Services;
using Timonier.UI.Theme;

namespace Timonier.Modules.Customization;

/// <summary>
/// Carte « Mode de couleur » : trois vignettes (Clair / Sombre / Mixte) appliquées en un clic,
/// et la couleur d'accent actuelle avec un accès direct aux Paramètres pour choisir une teinte précise.
/// </summary>
internal sealed class AppearancePanel : UserControl
{
    private static readonly string[] WatchedIds =
    [
        ThemeSwitcher.AppsId, ThemeSwitcher.SystemId, "custom.colors.autoaccent", "custom.colors.accentstart", "custom.colors.accenttitlebars",
    ];

    private readonly List<ThemeTile> _tiles = [];
    private readonly Border _accentSwatch;
    private readonly TextBlock _accentCaption;
    private readonly ProgressBar _busy = UiKit.BusyBar();
    private bool _isBusy;

    public AppearancePanel()
    {
        Focusable = false;
        var light = LoadPalette("Light");
        var dark = LoadPalette("Dark");

        var root = new StackPanel();

        var header = new DockPanel();
        DockPanel.SetDock(_busy, Dock.Right);
        header.Children.Add(_busy);
        var titles = new StackPanel();
        titles.Children.Add(UiKit.Text(L("Mode de couleur"), "Pp.CardTitle"));
        titles.Children.Add(UiKit.Text(L("Un clic applique le mode à Windows (barre des tâches, Démarrer) et aux applications. Annulable."), "Pp.Caption"));
        header.Children.Add(titles);
        root.Children.Add(header);

        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-6, 14, -6, 0) };
        AddTile(grid, new ThemeTile(L("Clair"), L("Windows et applications en clair"), "light", "light", light, light));
        AddTile(grid, new ThemeTile(L("Sombre"), L("Windows et applications en sombre"), "dark", "dark", dark, dark));
        AddTile(grid, new ThemeTile(L("Mixte"), L("Windows sombre, applications claires"), "light", "dark", light, dark));
        root.Children.Add(grid);

        root.Children.Add(UiKit.Divider(new Thickness(0, 16, 0, 14)));

        // Couleur d'accent actuelle.
        var accentRow = new DockPanel();
        var pick = UiKit.Button(L("Choisir une couleur…"), "", "Pp.Button", (_, _) => OpenSettings("ms-settings:colors"));
        pick.ToolTip = L("Ouvre Paramètres › Personnalisation › Couleurs pour choisir une couleur d'accent précise.");
        pick.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(pick, Dock.Right);
        accentRow.Children.Add(pick);
        _accentSwatch = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 14, 0) }
            .Themed(Border.BorderBrushProperty, "Pp.ControlStroke")
            .Themed(Border.BackgroundProperty, "Pp.Accent");
        DockPanel.SetDock(_accentSwatch, Dock.Left);
        accentRow.Children.Add(_accentSwatch);
        var accentTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        accentTexts.Children.Add(UiKit.Text(L("Couleur d'accent"), "Pp.CardTitle"));
        _accentCaption = UiKit.Text("", "Pp.Caption");
        accentTexts.Children.Add(_accentCaption);
        accentRow.Children.Add(accentTexts);
        root.Children.Add(accentRow);

        Content = new Border { Child = root, Padding = new Thickness(16, 14, 16, 16) }.Styled("Pp.Card");

        Loaded += (_, _) =>
        {
            AppHost.Engine.Changed += OnEngineChanged;
            ThemeManager.ThemeChanged += OnThemeChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            AppHost.Engine.Changed -= OnEngineChanged;
            ThemeManager.ThemeChanged -= OnThemeChanged;
        };
    }

    /// <summary>Fond d'écran actuel, utilisé comme « bureau » dans les vignettes (null = fond neutre).</summary>
    public void SetDesktopPreview(ImageSource? image)
    {
        foreach (var t in _tiles) t.SetDesktop(image);
    }

    private void AddTile(Panel host, ThemeTile tile)
    {
        tile.Margin = new Thickness(6, 0, 6, 0);
        tile.Click += async (_, _) => await ApplyAsync(tile);
        _tiles.Add(tile);
        host.Children.Add(tile);
    }

    private async Task ApplyAsync(ThemeTile tile)
    {
        if (_isBusy) return;
        SetBusy(true);
        try
        {
            await ThemeSwitcher.ApplyAsync(tile.AppsMode, tile.SystemMode);
        }
        finally
        {
            SetBusy(false);
            Refresh();
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        _busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        foreach (var t in _tiles) t.IsEnabled = !busy;
    }

    private void OnEngineChanged(object? sender, TweakChangedEventArgs e)
    {
        if (WatchedIds.Contains(e.SourceId)) Dispatcher.InvokeAsync(Refresh);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>Relit l'état (quelques valeurs de registre : instantané).</summary>
    private void Refresh()
    {
        var apps = ThemeSwitcher.AppsLight() ? "light" : "dark";
        var system = ThemeSwitcher.SystemLight() ? "light" : "dark";
        foreach (var t in _tiles) t.IsSelected = t.AppsMode == apps && t.SystemMode == system;

        var auto = RegistryAccess.ReadDword(RegHive.CurrentUser, CustomizationTweaks.DesktopKey, "AutoColorization") == 1;
        _accentCaption.Text = auto
            ? L("Choisie automatiquement d'après le fond d'écran.")
            : L("Choisie manuellement dans les Paramètres de Windows.");

        // Couleur d'accent réelle du système (donnée, pas un choix de style) ; à défaut, celle de l'interface.
        try
        {
            var c = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            var brush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            brush.Freeze();
            _accentSwatch.Background = brush;
        }
        catch (Exception ex)
        {
            Log.Warn("Customization", "couleur d'accent : " + ex.Message);
            _accentSwatch.SetResourceReference(Border.BackgroundProperty, "Pp.Accent");
        }
    }

    internal static void OpenSettings(string uri)
    {
        try { ProcessRunner.OpenSettingsUri(uri); }
        catch (Exception ex) { AppHost.Toasts.Show(L("Impossible d'ouvrir les Paramètres : {0}", ex.Message), ToastKind.Error); }
    }

    private static ResourceDictionary LoadPalette(string name) =>
        new() { Source = new Uri($"pack://application:,,,/Timonier;component/UI/Theme/Palette.{name}.xaml") };
}

/// <summary>
/// Vignette de mode de couleur : miniature d'un bureau (fond d'écran réel) avec une fenêtre aux couleurs des
/// applications et une barre des tâches aux couleurs de Windows. Les teintes proviennent des palettes claire
/// et sombre de Timonier (aucune couleur codée en dur).
/// </summary>
internal sealed class ThemeTile : Button
{
    private readonly Border _frame;
    private readonly Border _check;
    private readonly Image _desktop;
    private readonly Rectangle _desktopFallback;
    private bool _selected;

    public ThemeTile(string title, string caption, string appsMode, string systemMode, ResourceDictionary appsPalette, ResourceDictionary systemPalette)
    {
        Title = title;
        AppsMode = appsMode;
        SystemMode = systemMode;
        this.Styled("Pp.SubtleButton");
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        System.Windows.Automation.AutomationProperties.SetName(this, L("{0} : {1}", ThemeSwitcher.ModeName(appsMode, systemMode), caption));
        ToolTip = caption;

        // ---- Aperçu
        var preview = new Grid { Height = 104 };
        UiKit.ClipRounded(preview, 7, bottomRounded: false);
        _desktopFallback = new Rectangle().Themed(Shape.FillProperty, "Pp.AccentSubtle");
        preview.Children.Add(_desktopFallback);
        _desktop = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        preview.Children.Add(_desktop);

        Brush P(ResourceDictionary d, string key) => (Brush)d[key];

        var windowBody = new StackPanel { Margin = new Thickness(8, 7, 8, 8) };
        windowBody.Children.Add(new Rectangle { Height = 5, Width = 54, RadiusX = 2.5, RadiusY = 2.5, HorizontalAlignment = HorizontalAlignment.Left, Fill = P(appsPalette, "Pp.TextPrimary") });
        windowBody.Children.Add(new Rectangle { Height = 4, Width = 78, RadiusX = 2, RadiusY = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Fill = P(appsPalette, "Pp.TextTertiary") });
        windowBody.Children.Add(new Rectangle { Height = 4, Width = 62, RadiusX = 2, RadiusY = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), Fill = P(appsPalette, "Pp.TextTertiary") });
        windowBody.Children.Add(new Border { Height = 8, Width = 28, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0) }
            .Themed(Border.BackgroundProperty, "Pp.Accent"));
        var windowStack = new StackPanel();
        windowStack.Children.Add(new Border { Height = 10, CornerRadius = new CornerRadius(4, 4, 0, 0), Background = P(appsPalette, "Pp.WindowBackground") });
        windowStack.Children.Add(windowBody);
        var window = new Border
        {
            Margin = new Thickness(20, 12, 36, 26),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Background = P(appsPalette, "Pp.ContentBackground"),
            BorderBrush = P(appsPalette, "Pp.ControlStroke"),
            VerticalAlignment = VerticalAlignment.Top,
            Child = windowStack,
        };
        preview.Children.Add(window);

        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 4; i++)
        {
            var square = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Margin = new Thickness(2.5, 0, 2.5, 0) };
            if (i == 0) square.SetResourceReference(Border.BackgroundProperty, "Pp.Accent");
            else square.Background = P(systemPalette, "Pp.TextTertiary");
            icons.Children.Add(square);
        }
        var taskbar = new Border
        {
            Height = 16,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = P(systemPalette, "Pp.WindowBackground"),
            BorderBrush = P(systemPalette, "Pp.ControlStroke"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = icons,
        };
        preview.Children.Add(taskbar);

        // ---- Libellés
        _check = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden }
            .Themed(Border.BackgroundProperty, "Pp.Accent");
        _check.Child = UiKit.Icon("", 10, "Pp.TextOnAccent").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center);
        var labels = new DockPanel { Margin = new Thickness(12, 10, 12, 12) };
        DockPanel.SetDock(_check, Dock.Right);
        labels.Children.Add(_check);
        var texts = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        var titleText = UiKit.Text(title, "Pp.CardTitle");
        titleText.FontWeight = FontWeights.SemiBold;
        texts.Children.Add(titleText);
        var captionText = UiKit.Text(caption, "Pp.Caption");
        captionText.TextWrapping = TextWrapping.Wrap;
        texts.Children.Add(captionText);
        labels.Children.Add(texts);

        var body = new StackPanel();
        body.Children.Add(preview);
        body.Children.Add(labels);
        _frame = new Border { CornerRadius = new CornerRadius(8), Child = body, SnapsToDevicePixels = true }
            .Themed(Border.BackgroundProperty, "Pp.CardBackground");
        Content = _frame;

        MouseEnter += (_, _) => UpdateVisual();
        MouseLeave += (_, _) => UpdateVisual();
        GotKeyboardFocus += (_, _) => UpdateVisual();
        LostKeyboardFocus += (_, _) => UpdateVisual();
        UpdateVisual();
    }

    public string Title { get; }
    public string AppsMode { get; }
    public string SystemMode { get; }

    public bool IsSelected
    {
        get => _selected;
        set { _selected = value; UpdateVisual(); }
    }

    public void SetDesktop(ImageSource? image)
    {
        _desktop.Source = image;
        _desktopFallback.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateVisual()
    {
        var emphasized = _selected || IsKeyboardFocused;
        _frame.BorderThickness = new Thickness(emphasized ? 2 : 1);
        _frame.Padding = new Thickness(emphasized ? 0 : 1);
        _frame.SetResourceReference(Border.BorderBrushProperty,
            emphasized ? "Pp.Accent" : IsMouseOver ? "Pp.ControlStroke" : "Pp.CardBorder");
        _check.Visibility = _selected ? Visibility.Visible : Visibility.Hidden;
    }
}

internal static class FluentExtensions
{
    /// <summary>Applique une configuration à un élément puis le renvoie (construction en une expression).</summary>
    public static T Also<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
