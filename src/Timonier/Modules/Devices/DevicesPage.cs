using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using Timonier.UI.Services;

namespace Timonier.Modules.Devices;

/// <summary>
/// Page « Périphériques » : aperçu (tuiles cliquables), radios, inventaire, composants protégés, batterie, écrans et
/// blocages matériels. Tout ce qui est lent (WMI, rapport de batterie, cartes de réglages) est chargé après le premier
/// affichage, hors du thread UI ; les rafraîchissements ne tournent que pendant que la page est visible.
/// </summary>
public sealed class DevicesPage : UserControl, INavigationAware
{
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, FrameworkElement> _anchors = new(StringComparer.Ordinal);
    private readonly RadiosPanel _radios = new();
    private readonly InventoryPanel _inventory = new();
    private readonly DisplaysPanel _displays = new();
    private readonly BatteryPanel? _battery;
    private readonly ContentControl _tweaksHost = new() { Focusable = false };
    private readonly List<TweakDefinition> _tweakDefs;
    private TweakListView? _tweaks;
    private readonly Tile _devicesTile, _batteryTile, _radiosTile, _blocksTile;
    private bool _firstLoad = true;
    private string? _pendingNavigation;

    public DevicesPage()
    {
        Focusable = false;
        _tweakDefs = [.. AppHost.Registry.TweaksIn(DevicesModule.Category)];

        var stack = new StackPanel().Styled("Pp.PageStack");
        _scroll = new ScrollViewer { Content = stack }.Styled("Pp.PageScroll");
        Content = _scroll;

        stack.Children.Add(new PageHeader
        {
            Title = L("Devices"),
            Subtitle = L("Radios, hardware with errors, battery, displays, and blocking USB drives, the camera or the microphone for the whole PC."),
            Glyph = DevicesModule.Glyph,
        });

        var hasBattery = AppHost.Profile.HasBattery || BatteryService.Now().HasBattery;

        // Aperçu : tuiles cliquables qui mènent aux sections.
        _devicesTile = new Tile(DevicesModule.Glyph, L("Devices"), LC("short (tile)", "Analyzing…"), () => ScrollTo("inventory"));
        _radiosTile = new Tile("", L("Wireless"), L("Reading…"), () => ScrollTo("radios"));
        _batteryTile = hasBattery
            ? new Tile("", L("Battery"), LC("short (tile)", "Calculating…"), () => ScrollTo("battery"))
            : new Tile("", L("Monitors"), L("Reading…"), () => ScrollTo("displays"));
        _blocksTile = new Tile("", L("Blocks"), L("Reading…"), () => ScrollTo("blocks"));
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 0, -4, 4) };
        foreach (var t in new[] { _devicesTile, _radiosTile, _batteryTile, _blocksTile }) tiles.Children.Add(t);
        stack.Children.Add(tiles);

        AddSection(stack, "radios", L("Wi-Fi, Bluetooth and cellular"), _radios);
        AddSection(stack, "inventory", L("Device inventory"), _inventory);
        AddSection(stack, "protected", L("Protected components"), BuildProtectedCard());
        if (hasBattery)
        {
            _battery = new BatteryPanel();
            AddSection(stack, "battery", L("Battery"), _battery);
            _battery.SummaryChanged += (text, tone) => _batteryTile.Set(text, tone);
        }
        AddSection(stack, "displays", L("Monitors"), _displays);
        AddSection(stack, "blocks", L("Hardware blocks"), BuildBlocksSection());

        _radios.SummaryChanged += (value, detail) => _radiosTile.Set(value, null, detail);
        _inventory.SummaryChanged += (total, problems, disabled) =>
        {
            if (total < 0) { _devicesTile.Set(L("List unavailable"), "Pp.Danger"); return; }
            _devicesTile.Set(problems > 0 ? LP(problems, "{0} with errors", "{0} with errors") : L("No errors"), problems > 0 ? "Pp.Warning" : "Pp.Success",
                LP(total, "{0} detected", "{0} detected") + (disabled > 0 ? " · " + LP(disabled, "{0} disabled", "{0} disabled") : ""));
        };
        if (!hasBattery) _displays.SummaryChanged += s => _batteryTile.Set(s, null);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ================================================================== Cycle de vie

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppHost.Engine.Changed += OnEngineChanged;
        _battery?.Start();
        if (!_firstLoad)
        {
            // Retour sur la page : l'état a pu changer ailleurs (branchements, Paramètres Windows).
            _radios.Attach();
            _ = _radios.LoadAsync();
            _ = _inventory.LoadAsync();
            _ = UpdateBlocksTileAsync();
            return;
        }
        _firstLoad = false;
        try
        {
            // Laisse d'abord la page s'afficher, puis charge le reste en parallèle.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            var tasks = new List<Task> { _radios.LoadAsync(), _inventory.LoadAsync(), _displays.LoadAsync(), UpdateBlocksTileAsync() };
            if (_battery is not null) tasks.Add(_battery.LoadAsync());
            _ = Dispatcher.InvokeAsync(BuildTweaks, DispatcherPriority.ApplicationIdle);
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Log.Error("Devices", "chargement de la page", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppHost.Engine.Changed -= OnEngineChanged;
        _battery?.Stop();
        _radios.Detach();
    }

    private void OnEngineChanged(object? sender, TweakChangedEventArgs e)
    {
        if (e.SourceId.StartsWith("devices.", StringComparison.Ordinal))
            Dispatcher.InvokeAsync(() => _ = UpdateBlocksTileAsync());
    }

    // ================================================================== Navigation

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not string s || s.Length == 0) return;
        _pendingNavigation = s;
        Dispatcher.InvokeAsync(HandleNavigation, DispatcherPriority.Loaded);
    }

    private void HandleNavigation()
    {
        if (_pendingNavigation is not { } p) return;
        if (p.StartsWith("section:", StringComparison.Ordinal))
        {
            _pendingNavigation = null;
            ScrollTo(p["section:".Length..]);
        }
        else if (p.StartsWith("tweak:", StringComparison.Ordinal))
        {
            if (_tweaks is null) { BuildTweaks(); return; } // BuildTweaks rappelle HandleNavigation
            _pendingNavigation = null;
            _tweaks.Highlight(p["tweak:".Length..]);
        }
        else _pendingNavigation = null;
    }

    private void ScrollTo(string key)
    {
        if (!_anchors.TryGetValue(key, out var anchor)) return;
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var top = anchor.TransformToAncestor(_scroll).Transform(new Point(0, 0)).Y + _scroll.VerticalOffset;
                _scroll.ScrollToVerticalOffset(Math.Max(0, top - 8));
            }
            catch (InvalidOperationException)
            {
                anchor.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
    }

    // ================================================================== Sections

    private void AddSection(StackPanel stack, string key, string title, UIElement content)
    {
        var heading = DevUi.Text(title, "Pp.SectionTitle");
        _anchors[key] = heading;
        stack.Children.Add(heading);
        stack.Children.Add(content);
    }

    private FrameworkElement BuildBlocksSection()
    {
        var s = new StackPanel();
        var intro = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var privacy = DevUi.Button(L("Your account's permissions"), "", "Pp.SubtleButton", (_, _) => AppHost.Navigator.Navigate("privacy", "section:permissions"));
        privacy.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(privacy, Dock.Right);
        intro.Children.Add(privacy);
        var icon = DevUi.Icon("", 16, "Pp.AccentText");
        icon.Margin = new Thickness(0, 1, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        intro.Children.Add(icon);
        intro.Children.Add(DevUi.Text(L("These blocks apply to all accounts on the PC, including administrators, and require administrator authorization. Every change can still be undone from History."), "Pp.Caption"));
        s.Children.Add(new Border { Child = intro }.Styled("Pp.InfoBar"));
        _tweaksHost.Content = new TextBlock { Text = L("Loading settings…"), Margin = new Thickness(2, 4, 0, 0) }.Styled("Pp.Caption");
        s.Children.Add(_tweaksHost);
        return s;
    }

    /// <summary>Cartes de réglages (coûteuses à créer) : construites quand l'interface est au repos.</summary>
    private void BuildTweaks()
    {
        if (_tweaks is not null) return;
        try
        {
            _tweaks = new TweakListView(_tweakDefs);
            _tweaksHost.Content = _tweaks;
        }
        catch (Exception ex)
        {
            Log.Error("Devices", "création des réglages", ex);
            _tweaksHost.Content = PageScaffold.InfoBar(L("The block settings couldn't be displayed."), "", "Pp.InfoBar.Danger");
        }
        if (_pendingNavigation is not null) Dispatcher.InvokeAsync(HandleNavigation, DispatcherPriority.Loaded);
    }

    private async Task UpdateBlocksTileAsync()
    {
        var states = await Task.Run(() => _tweakDefs
            .Where(t => AppHost.Engine.Unavailability(t) is null)
            .Select(t => (Tweak: t, State: SafeDetect(t)))
            .ToList());
        var active = states.Where(x => x.State.OptionKey is { } k && x.Tweak.WindowsDefault is { } d && k != d).Select(x => x.Tweak.Title).ToList();
        _blocksTile.Set(active.Count == 0 ? L("None active") : LP(active.Count, "{0} active block", "{0} active blocks"),
            null,
            active.Count == 0 ? L("USB, camera, microphone, CD/DVD…") : string.Join(", ", active.Take(2)) + (active.Count > 2 ? "…" : ""));
    }

    private static TweakState SafeDetect(TweakDefinition t)
    {
        try { return AppHost.Engine.Detect(t); }
        catch (Exception ex)
        {
            Log.Warn("Devices", $"détection {t.Id} : {ex.Message}");
            return TweakState.UnknownState;
        }
    }

    private static FrameworkElement BuildProtectedCard()
    {
        var s = new StackPanel();
        s.Children.Add(DevUi.Text(L("To avoid making the PC unusable, Timonier refuses to disable the following components, even with administrator rights. These checks are run again by the administrator process when it acts."), "Pp.Caption"));
        s.Children.Add(DevUi.Divider(new Thickness(0, 10, 0, 6)));
        foreach (var (_, title, reason) in DeviceCatalog.ProtectedClasses)
            s.Children.Add(Row("", "Pp.TextSecondary", title, reason));
        s.Children.Add(DevUi.Divider(new Thickness(0, 8, 0, 6)));
        s.Children.Add(new TextBlock { Text = L("Can be disabled, with a second confirmation shown by the administrator process:"), Margin = new Thickness(0, 0, 0, 4) }
            .Styled("Pp.Caption"));
        foreach (var (cls, why) in DeviceCatalog.SensitiveClasses)
            s.Children.Add(Row("", "Pp.Warning", DeviceCatalog.ClassInfo(cls).Title, char.ToUpper(why[0], Culture) + why[1..] + "."));
        return DevUi.Card(s, new Thickness(18, 14, 18, 12));

        static FrameworkElement Row(string glyph, string brush, string title, string reason)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = DevUi.Icon(glyph, 13, brush);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            g.Children.Add(icon);
            var t = DevUi.Text(title);
            t.FontSize = 13;
            t.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            var r = DevUi.Caption(reason);
            r.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(r, 2);
            g.Children.Add(r);
            return g;
        }
    }

    // ================================================================== Tuile d'aperçu

    private sealed class Tile : Border
    {
        private readonly TextBlock _value;
        private readonly TextBlock _detail;

        public Tile(string glyph, string label, string initial, Action click)
        {
            this.Styled("Pp.CardInteractive");
            Margin = new Thickness(4, 0, 4, 0);
            Padding = new Thickness(14, 12, 14, 12);
            Cursor = Cursors.Hand;
            Focusable = true;
            System.Windows.Automation.AutomationProperties.SetName(this, label);

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var icon = DevUi.IconTile(glyph, 28);
            icon.Margin = new Thickness(0, 0, 10, 0);
            head.Children.Add(icon);
            head.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Caption"));

            _value = new TextBlock { Text = initial, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }.Styled("Pp.CardTitle");
            _value.TextWrapping = TextWrapping.NoWrap;
            _detail = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Visibility = Visibility.Collapsed }.Styled("Pp.Caption");

            var s = new StackPanel();
            s.Children.Add(head);
            s.Children.Add(_value);
            s.Children.Add(_detail);
            Child = s;

            MouseLeftButtonUp += (_, _) => click();
            KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { click(); e.Handled = true; } };
        }

        public void Set(string value, string? tone, string? detail = null)
        {
            _value.Text = value;
            _value.ToolTip = value;
            if (tone is null) _value.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextPrimary");
            else _value.SetResourceReference(TextBlock.ForegroundProperty, tone);
            _detail.Text = detail ?? "";
            _detail.ToolTip = detail;
            _detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
