using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using PcPilot.Core.Model;
using PcPilot.UI.Controls;
using PcPilot.UI.Services;

namespace PcPilot.Modules.Maintenance;

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
            Title = "Maintenance",
            Subtitle = "Libérez de l'espace, réparez Windows, gardez un point de retour et surveillez les mises à jour et les erreurs.",
            Glyph = MaintenanceModule.Glyph,
        });

        // Tuiles de synthèse (cliquables : elles mènent à la section concernée).
        _tileJunk = new Tile("", "Espace récupérable", () => ScrollTo("cleanup"));
        _tileUpdates = new Tile("", "Windows Update", () => ScrollTo("updates"));
        _tileEvents = new Tile("", "Erreurs (7 jours)", () => ScrollTo("events"));
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 0, -4, 4) };
        tiles.Children.Add(_tileJunk.Root);
        tiles.Children.Add(_tileUpdates.Root);
        tiles.Children.Add(_tileEvents.Root);
        stack.Children.Add(tiles);
        stack.Children.Add(BuildNavBar());

        var tweaks = AppHost.Registry.TweaksIn(MaintenanceModule.Category).ToList();

        _cleanup = new CleanupPanel();
        _cleanup.Estimated += (bytes, partial) => _tileJunk.Set(
            (partial ? "≥ " : "") + Core.Platform.Format.Bytes(bytes), "Fichiers temporaires, caches et corbeille", bytes >= 1L << 30 ? "Pp.Warning" : "Pp.AccentText");
        AddSection(stack, "cleanup", "Nettoyage",
            "Analysez puis supprimez les fichiers inutiles. Les fichiers en cours d'utilisation, les liens et tout ce qui se trouve hors des dossiers listés ne sont jamais touchés.",
            _cleanup.Root);
        AddTweaks(stack, tweaks.Where(t => t.Group == MaintenanceTweaks.GroupStorage));

        _repair = new RepairPanel();
        AddSection(stack, "repair", "Réparation",
            "Outils officiels de Windows pour vérifier et réparer le système. Les opérations longues continuent même si vous changez de page.",
            _repair.Root);

        _restore = new RestorePanel();
        AddSection(stack, "restore", "Points de restauration",
            "Un point de restauration enregistre les fichiers système, pilotes et registre pour revenir en arrière après un problème. Vos documents ne sont pas concernés.",
            _restore.Root);

        _updates = new UpdatesPanel();
        _updates.StatusChanged += s =>
        {
            var h = s.ToHealth();
            _tileUpdates.Set(s.IsPaused ? "En pause" : h.Status switch
            {
                Core.Catalog.HealthStatus.Critical => "En retard",
                Core.Catalog.HealthStatus.Warning => "À vérifier",
                Core.Catalog.HealthStatus.Unknown => "Inconnu",
                _ => s.RebootPending ? "Redémarrage requis" : "À jour",
            }, h.Summary, h.Status switch
            {
                Core.Catalog.HealthStatus.Critical => "Pp.Danger",
                Core.Catalog.HealthStatus.Warning => "Pp.Warning",
                _ => s.IsPaused || s.RebootPending ? "Pp.Warning" : "Pp.Success",
            });
        };
        AddSection(stack, "updates", "Windows Update",
            "État des mises à jour, pause temporaire et heures d'activité. PC Pilot ne propose jamais de désactiver les mises à jour de façon permanente.",
            _updates.Root);
        AddTweaks(stack, tweaks.Where(t => t.Group == MaintenanceTweaks.GroupUpdates));

        _events = new EventsPanel();
        _events.Scanned += scan =>
        {
            var serious = scan.Groups.Where(g => g.Hint?.Tone != HintTone.Harmless).Sum(g => g.Count);
            var critical = scan.Groups.Where(g => g.Critical).Sum(g => g.Count);
            _tileEvents.Set(scan.Error is not null && scan.Total == 0 ? "—" : (scan.Capped ? "≥ " : "") + serious,
                critical > 0 ? $"dont {critical} critique(s)" : scan.Total == 0 ? "Aucune erreur récente" : "erreurs à examiner",
                critical > 0 ? "Pp.Danger" : serious > 20 ? "Pp.Warning" : "Pp.Success");
        };
        AddSection(stack, "events", "Journal des erreurs",
            "Erreurs et événements critiques des 7 derniers jours (journaux Système et Application), regroupés et expliqués quand ils sont connus.",
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
                     ("cleanup", "Nettoyage", ""), ("repair", "Réparation", ""), ("restore", "Restauration", ""),
                     ("updates", "Windows Update", ""), ("events", "Journal des erreurs", ""),
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
        private readonly TextBlock _caption = MaintUi.Text("Analyse en cours…", "Pp.Caption");
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
