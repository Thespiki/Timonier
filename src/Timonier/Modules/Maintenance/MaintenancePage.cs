using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Timonier.Core.Model;
using Timonier.UI.Controls;
using Timonier.UI.Services;

namespace Timonier.Modules.Maintenance;

/// <summary>
/// Page « Maintenance » : tuiles de synthèse, puis cinq sections (Nettoyage, Réparation, Points de restauration,
/// Windows Update, Journal des erreurs). Tout ce qui est lent (analyse du disque, COM Windows Update, journaux)
/// est chargé hors du thread UI à la première visite ; aucune interrogation périodique.
/// </summary>
public sealed class MaintenancePage : UserControl, INavigationAware
{
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, FrameworkElement> _anchors = new(StringComparer.Ordinal);
    private readonly List<TweakListView> _lists = [];
    private readonly CleanupPanel _cleanup;
    private readonly RepairPanel _repair;
    private readonly RestorePanel _restore;
    private readonly UpdatesPanel _updates;
    private readonly EventsPanel _events;

    private readonly Tile _tileJunk, _tileUpdates, _tileEvents;
    private bool _loadedOnce;
    private string? _pendingNavigation;

    public MaintenancePage()
    {
        Focusable = false;
        var stack = new StackPanel().Styled("Pp.PageStack");
        _scroll = new ScrollViewer { Content = stack }.Styled("Pp.PageScroll");
        Content = _scroll;

        stack.Children.Add(new PageHeader
        {
            Title = L("Maintenance"),
            Subtitle = L("Free up space, repair Windows, keep a restore point and keep an eye on updates and errors."),
            Glyph = MaintenanceModule.Glyph,
        });

        // Tuiles de synthèse (cliquables : elles mènent à la section concernée).
        _tileJunk = new Tile("", L("Space that can be freed"), () => ScrollTo("cleanup"));
        _tileUpdates = new Tile("", "Windows Update", () => ScrollTo("updates"));
        _tileEvents = new Tile("", L("Errors (7 days)"), () => ScrollTo("events"));
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 0, -4, 4) };
        tiles.Children.Add(_tileJunk.Root);
        tiles.Children.Add(_tileUpdates.Root);
        tiles.Children.Add(_tileEvents.Root);
        stack.Children.Add(tiles);
        stack.Children.Add(BuildNavBar());

        var tweaks = AppHost.Registry.TweaksIn(MaintenanceModule.Category).ToList();

        _cleanup = new CleanupPanel();
        _cleanup.Estimated += (bytes, partial) => _tileJunk.Set(
            (partial ? "≥ " : "") + Core.Platform.Format.Bytes(bytes), L("Temporary files, caches and Recycle Bin"), bytes >= 1L << 30 ? "Pp.Warning" : "Pp.AccentText");
        AddSection(stack, "cleanup", L("Cleanup"),
            L("Scan, then delete unnecessary files. Files in use, links and anything outside the listed folders are never touched."),
            _cleanup.Root);
        AddTweaks(stack, tweaks.Where(t => t.Group == MaintenanceTweaks.GroupStorage));

        _repair = new RepairPanel();
        AddSection(stack, "repair", L("Repair"),
            L("Official Windows tools to check and repair the system. Long operations keep running even if you switch pages."),
            _repair.Root);

        _restore = new RestorePanel();
        AddSection(stack, "restore", L("Restore points"),
            L("A restore point saves system files, drivers and the registry so you can go back after a problem. Your documents aren't affected."),
            _restore.Root);

        _updates = new UpdatesPanel();
        _updates.StatusChanged += s =>
        {
            var h = s.ToHealth();
            _tileUpdates.Set(s.IsPaused ? L("Paused") : h.Status switch
            {
                Core.Catalog.HealthStatus.Critical => L("Out of date"),
                Core.Catalog.HealthStatus.Warning => L("Needs review"),
                Core.Catalog.HealthStatus.Unknown => L("Unknown"),
                _ => s.RebootPending ? L("Restart required") : L("Up to date"),
            }, h.Summary, h.Status switch
            {
                Core.Catalog.HealthStatus.Critical => "Pp.Danger",
                Core.Catalog.HealthStatus.Warning => "Pp.Warning",
                _ => s.IsPaused || s.RebootPending ? "Pp.Warning" : "Pp.Success",
            });
        };
        AddSection(stack, "updates", "Windows Update",
            L("Update status, temporary pause and active hours. Timonier never offers to turn off updates permanently."),
            _updates.Root);
        AddTweaks(stack, tweaks.Where(t => t.Group == MaintenanceTweaks.GroupUpdates));

        _events = new EventsPanel();
        _events.Scanned += scan =>
        {
            var serious = scan.Groups.Where(g => g.Hint?.Tone != HintTone.Harmless).Sum(g => g.Count);
            var critical = scan.Groups.Where(g => g.Critical).Sum(g => g.Count);
            _tileEvents.Set(scan.Error is not null && scan.Total == 0 ? "—" : (scan.Capped ? "≥ " : "") + serious,
                critical > 0 ? LP(critical, "including {0} critical", "including {0} critical") : scan.Total == 0 ? L("No recent errors") : L("errors to review"),
                critical > 0 ? "Pp.Danger" : serious > 20 ? "Pp.Warning" : "Pp.Success");
        };
        AddSection(stack, "events", L("Error log"),
            L("Errors and critical events from the last 7 days (System and Application logs), grouped and explained when they're known."),
            _events.Root);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            // Retour sur la page : on relit seulement ce qui est instantané.
            _restore.RefreshStatus();
            _ = _updates.RefreshAsync();
            return;
        }
        _loadedOnce = true;
        _ = _cleanup.AnalyzeAsync();
        _ = _updates.RefreshAsync();
        _restore.RefreshStatus();
        // Le journal des événements est lu une fois l'interface affichée, à basse priorité.
        Dispatcher.InvokeAsync(() => _ = _events.LoadAsync(), DispatcherPriority.Background);
        HandlePendingNavigation();
    }

    // ================================================================== Construction

    private FrameworkElement BuildNavBar()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (key, title, glyph) in new[]
                 {
                     ("cleanup", L("Cleanup"), ""), ("repair", L("Repair"), ""), ("restore", LC("noun (section)", "Restore"), ""),
                     ("updates", "Windows Update", ""), ("events", L("Error log"), ""),
                 })
        {
            var b = MaintUi.Button(title, glyph, "Pp.SubtleButton", (_, _) => ScrollTo(key));
            b.Margin = new Thickness(0, 0, 6, 6);
            bar.Children.Add(b);
        }
        return bar;
    }

    private void AddSection(StackPanel stack, string key, string title, string subtitle, FrameworkElement content)
    {
        var header = MaintUi.SectionHeader(title, subtitle);
        _anchors[key] = header;
        stack.Children.Add(header);
        stack.Children.Add(content);
    }

    private void AddTweaks(StackPanel stack, IEnumerable<TweakDefinition> tweaks)
    {
        var list = new TweakListView(tweaks, showRecommendations: false) { Margin = new Thickness(0, 4, 0, 0) };
        _lists.Add(list);
        stack.Children.Add(list);
    }

    // ================================================================== Navigation

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not string s || s.Length == 0) return;
        _pendingNavigation = s;
        if (IsLoaded) HandlePendingNavigation();
    }

    private void HandlePendingNavigation()
    {
        if (_pendingNavigation is not { } p) return;
        _pendingNavigation = null;
        if (p.StartsWith("section:", StringComparison.Ordinal))
        {
            ScrollTo(p["section:".Length..]);
        }
        else if (p.StartsWith("tweak:", StringComparison.Ordinal))
        {
            var id = p["tweak:".Length..];
            var list = _lists.FirstOrDefault(l => l.Items.Any(i => i.Definition.Id == id));
            if (list is not null && list.Highlight(id)) ScrollToTweak(list, id);
        }
        else if (p == "run:sfc")
        {
            ScrollTo("repair");
            Dispatcher.InvokeAsync(() => _ = _repair.StartSfcAsync(), DispatcherPriority.Background);
        }
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
            catch (InvalidOperationException) { anchor.BringIntoView(); }
        }, DispatcherPriority.Loaded);
    }

    private void ScrollToTweak(TweakListView list, string id)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (list.Content is not Panel root) return;
            var card = root.Children.OfType<FrameworkElement>()
                .FirstOrDefault(c => c.DataContext is TweakItemViewModel vm && vm.Definition.Id == id);
            if (card is null) return;
            try
            {
                var top = card.TransformToAncestor(_scroll).Transform(new Point(0, 0)).Y + _scroll.VerticalOffset;
                _scroll.ScrollToVerticalOffset(Math.Max(0, top - Math.Min(160, _scroll.ViewportHeight / 4)));
            }
            catch (InvalidOperationException) { card.BringIntoView(); }
        }, DispatcherPriority.Loaded);
    }

    // ================================================================== Tuile de synthèse

    private sealed class Tile
    {
        public Border Root { get; }
        private readonly TextBlock _value = MaintUi.Text("…", "Pp.Metric", wrap: false);
        private readonly TextBlock _caption = MaintUi.Text(L("Analyzing…"), "Pp.Caption");
        private readonly TextBlock _icon;

        public Tile(string glyph, string title, Action click)
        {
            _icon = MaintUi.Icon(glyph, 16, "Pp.AccentText");
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(_icon);
            var t = MaintUi.Text(title, "Pp.Caption", wrap: false);
            t.Margin = new Thickness(8, 0, 0, 0);
            head.Children.Add(t);
            _value.Margin = new Thickness(0, 8, 0, 2);
            _value.TextTrimming = TextTrimming.CharacterEllipsis;
            _caption.TextTrimming = TextTrimming.CharacterEllipsis;
            _caption.TextWrapping = TextWrapping.NoWrap;
            var body = new StackPanel();
            body.Children.Add(head);
            body.Children.Add(_value);
            body.Children.Add(_caption);
            Root = new Border { Child = body, Margin = new Thickness(4), Cursor = Cursors.Hand }.Styled("Pp.CardInteractive");
            Root.MouseLeftButtonUp += (_, _) => click();
            System.Windows.Automation.AutomationProperties.SetName(Root, title);
        }

        public void Set(string value, string caption, string brushKey)
        {
            _value.Text = value;
            _caption.Text = caption;
            _caption.ToolTip = caption;
            _icon.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }
    }
}
