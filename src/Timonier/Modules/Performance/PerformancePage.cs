using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using Timonier.UI.Services;

namespace Timonier.Modules.Performance;

/// <summary>
/// Page « Performances » : profil matériel et conseils, alimentation (plans, mode, veille), puis les réglages par groupe.
/// Les listes de réglages sont créées groupe par groupe à basse priorité après le premier affichage (fluidité sur PC modeste) ;
/// les données d'alimentation sont lues hors du thread UI à chaque affichage, sans minuterie.
/// </summary>
public sealed partial class PerformancePage : UserControl, INavigationAware
{
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _stack;
    private readonly List<TweakDefinition> _tweaks;
    private readonly Dictionary<string, FrameworkElement> _anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TweakListView> _lists = new(StringComparer.Ordinal);
    private readonly StackPanel _listsHost = new();
    private readonly DispatcherTimer _recoDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };

    // Barre de recommandations (tous groupes confondus)
    private readonly Border _recoBar;
    private readonly TextBlock _recoText = new();
    private readonly Button _recoButton;

    private string? _pendingNavigation;
    private bool _listsReady, _firstLoad = true, _applyingReco;

    public PerformancePage()
    {
        Focusable = false;
        _tweaks = [.. AppHost.Registry.TweaksIn(PerformanceModule.Category)];

        _stack = new StackPanel();
        _stack.SetResourceReference(StyleProperty, "Pp.PageStack");
        _scroll = new ScrollViewer { Content = _stack };
        _scroll.SetResourceReference(StyleProperty, "Pp.PageScroll");
        Content = _scroll;

        _stack.Children.Add(new PageHeader
        {
            Title = L("Performances"),
            Subtitle = L("Alimentation, veille, effets visuels, jeux et services : adaptez Windows à la puissance réelle de ce PC."),
            Glyph = PerformanceModule.Glyph,
        });
        _stack.Children.Add(BuildJumpBar());

        AddAnchor("hardware", PageScaffold.Section(L("Profil matériel de ce PC")));
        _stack.Children.Add(BuildHardwareCard());

        AddAnchor("power", PageScaffold.Section(L("Alimentation")));
        _stack.Children.Add(BuildPowerCard());

        AddAnchor("sleep", PageScaffold.Section(L("Veille et écran")));
        _stack.Children.Add(BuildSleepCard());

        (_recoBar, _recoButton) = BuildRecommendationBar();
        _recoBar.Margin = new Thickness(0, 22, 0, 0);
        _stack.Children.Add(_recoBar);
        _stack.Children.Add(_listsHost);

        _recoDebounce.Tick += (_, _) => { _recoDebounce.Stop(); UpdateRecommendationBar(); };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Dispatcher.InvokeAsync(() => BuildNextGroup(0), DispatcherPriority.Background);
    }

    // ================================================================== Cycle de vie

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppHost.HardwareLoaded += OnHardwareLoaded;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        var first = _firstLoad;
        _firstLoad = false;
        if (!first) RefreshHardware();
#if DEBUG
        if (first && Environment.GetEnvironmentVariable("TMN_DEV_ANCHOR") is { Length: > 0 } devAnchor) OnNavigatedTo(devAnchor);
#endif
        await RefreshPowerAsync();
        await RefreshGraphicsAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppHost.HardwareLoaded -= OnHardwareLoaded;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _recoDebounce.Stop();
    }

    private void OnHardwareLoaded(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        RefreshHardware();
        _ = RefreshPowerAsync();
    });

    /// <summary>Branchement/débranchement du secteur : événement système (pas de sondage).</summary>
    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.StatusChange) Dispatcher.InvokeAsync(() => _ = RefreshPowerAsync());
    }

    // ================================================================== Navigation

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not string s || s.Length == 0) return;
        _pendingNavigation = s;
        Dispatcher.InvokeAsync(TryHandlePendingNavigation, DispatcherPriority.Loaded);
    }

    private void TryHandlePendingNavigation()
    {
        if (_pendingNavigation is not { } parameter) return;
        if (parameter.StartsWith("tweak:", StringComparison.Ordinal))
        {
            var id = parameter["tweak:".Length..];
            var tweak = _tweaks.FirstOrDefault(t => t.Id == id);
            if (tweak is null) { _pendingNavigation = null; return; }
            if (!_lists.TryGetValue(tweak.Group ?? "", out var list))
            {
                if (_listsReady) _pendingNavigation = null; // groupe masqué (réglage avancé) : rien à montrer
                return;
            }
            _pendingNavigation = null;
            if (list.Highlight(id)) ScrollToCard(list, id);
        }
        else if (parameter.StartsWith("section:", StringComparison.Ordinal))
        {
            var key = parameter["section:".Length..];
            if (!_anchors.TryGetValue(key, out var anchor))
            {
                if (_listsReady) _pendingNavigation = null;
                return;
            }
            _pendingNavigation = null;
            ScrollTo(anchor);
        }
        else
        {
            _pendingNavigation = null;
        }
    }

    private void AddAnchor(string key, FrameworkElement element, Panel? host = null)
    {
        _anchors[key] = element;
        (host ?? _stack).Children.Add(element);
    }

    private void ScrollTo(FrameworkElement target) => Dispatcher.InvokeAsync(() =>
    {
        if (!target.IsVisible) return;
        try
        {
            var top = target.TransformToAncestor(_scroll).Transform(new Point(0, 0)).Y + _scroll.VerticalOffset;
            _scroll.ScrollToVerticalOffset(Math.Max(0, top - 12));
        }
        catch (InvalidOperationException) { target.BringIntoView(); }
    }, DispatcherPriority.Loaded);

    private void ScrollToCard(TweakListView list, string tweakId) => Dispatcher.InvokeAsync(() =>
    {
        if (list.Content is not Panel root) return;
        var card = root.Children.OfType<FrameworkElement>()
            .FirstOrDefault(c => c.DataContext is TweakItemViewModel vm && vm.Definition.Id == tweakId);
        if (card is null || !card.IsVisible) return;
        try
        {
            var top = card.TransformToAncestor(_scroll).Transform(new Point(0, 0)).Y + _scroll.VerticalOffset;
            _scroll.ScrollToVerticalOffset(Math.Max(0, top - Math.Min(160, _scroll.ViewportHeight / 4)));
        }
        catch (InvalidOperationException) { /* arbre en reconstruction : Highlight a déjà fait BringIntoView */ }
    }, DispatcherPriority.Loaded);

    /// <summary>Barre de raccourcis vers les sections de la page.</summary>
    private WrapPanel BuildJumpBar()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        (string Key, string Label, string Glyph)[] items =
        [
            ("power", L("Alimentation"), ""), ("sleep", L("Veille"), ""),
            ("group:" + PerformanceTweaks.GroupVisual, L("Effets visuels"), ""), ("group:" + PerformanceTweaks.GroupGames, L("Jeux"), ""),
            ("group:" + PerformanceTweaks.GroupEnergy, L("Énergie"), ""), ("group:" + PerformanceTweaks.GroupBackground, L("Arrière-plan"), ""),
            ("group:" + PerformanceTweaks.GroupServices, L("Services"), ""), ("group:" + PerformanceTweaks.GroupStorage, L("Stockage"), ""),
        ];
        foreach (var (key, label, glyph) in items)
        {
            var b = PerfUi.Button(label, "Pp.SubtleButton", glyph);
            b.Margin = new Thickness(0, 0, 4, 4);
            b.Click += (_, _) =>
            {
                if (key.StartsWith("group:", StringComparison.Ordinal))
                {
                    var group = key["group:".Length..];
                    if (_lists.TryGetValue(group, out var list) && list.GroupNames.Contains(group)) list.ScrollToGroup(group);
                    else if (_lists.TryGetValue(group, out list)) ScrollTo(list);
                }
                else if (_anchors.TryGetValue(key, out var anchor)) ScrollTo(anchor);
            };
            bar.Children.Add(b);
        }
        return bar;
    }

    // ================================================================== Listes de réglages

    private void BuildNextGroup(int index)
    {
        var groups = PerformanceTweaks.Groups;
        if (index >= groups.Length)
        {
            _listsReady = true;
            TryHandlePendingNavigation();
            _recoDebounce.Start();
            return;
        }
        var group = groups[index];
        var tweaks = _tweaks.Where(t => t.Group == group).ToList();
        if (tweaks.Count > 0)
        {
            try
            {
                var list = new TweakListView(tweaks, showRecommendations: false);
                System.Windows.Automation.AutomationProperties.SetName(list, group);
                _lists[group] = list;
                _listsHost.Children.Add(list);
                foreach (var vm in list.Items) vm.PropertyChanged += OnItemPropertyChanged;
                if (group == PerformanceTweaks.GroupGames)
                {
                    AddAnchor("graphics", BuildGraphicsCard(), _listsHost);
                    _ = RefreshGraphicsAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Performance", "liste " + group, ex);
                _listsHost.Children.Add(PageScaffold.InfoBar(L("Impossible d'afficher le groupe « {0} » : {1}", group, ex.Message), "", "Pp.InfoBar.Danger"));
            }
        }
        TryHandlePendingNavigation();
        Dispatcher.InvokeAsync(() => BuildNextGroup(index + 1), DispatcherPriority.Background);
    }

    // ================================================================== Recommandations (tous groupes)

    private (Border Bar, Button Apply) BuildRecommendationBar()
    {
        var icon = PerfUi.Icon("", 16, "Pp.AccentText");
        icon.Margin = new Thickness(0, 0, 10, 0);
        _recoText.SetResourceReference(StyleProperty, "Pp.Body");
        _recoText.VerticalAlignment = VerticalAlignment.Center;
        var apply = PerfUi.Button(L("Appliquer les recommandations"), "Pp.AccentButton");
        apply.Margin = new Thickness(12, 0, 0, 0);
        apply.Click += async (_, _) => await ApplyRecommendationsAsync();
        var dock = new DockPanel();
        DockPanel.SetDock(apply, Dock.Right);
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(apply);
        dock.Children.Add(icon);
        dock.Children.Add(_recoText);
        var bar = new Border { Child = dock, Visibility = Visibility.Collapsed };
        bar.SetResourceReference(StyleProperty, "Pp.InfoBar");
        return (bar, apply);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TweakItemViewModel.RecommendationText) or nameof(TweakItemViewModel.IsAvailable) && !_applyingReco)
        {
            _recoDebounce.Stop();
            _recoDebounce.Start();
        }
    }

    private List<(TweakItemViewModel Vm, string Option)> PendingRecommendations() =>
        [.. _lists.Values.SelectMany(l => l.Items)
            .Where(vm => vm.IsAvailable && vm.RecommendationText is not null && vm.Definition.RecommendationFor(AppHost.Profile) is not null)
            .Select(vm => (vm, vm.Definition.RecommendationFor(AppHost.Profile)!))];

    private void UpdateRecommendationBar()
    {
        var pending = PendingRecommendations();
        _recoBar.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _recoText.Text = LP(pending.Count,
            "{0} réglage de performances diffère de la recommandation pour ce PC.",
            "{0} réglages de performances diffèrent des recommandations pour ce PC.");
    }

    private async Task ApplyRecommendationsAsync()
    {
        var pending = PendingRecommendations();
        if (pending.Count == 0) return;
        var lines = string.Join("\n", pending.Select(p => $"• {p.Vm.Title} → {p.Vm.Definition.GetOption(p.Option)?.Label}"));
        var needsAdmin = pending.Any(p => p.Vm.Definition.RequiresAdmin);
        if (!await AppHost.Dialogs.ConfirmAsync(L("Appliquer les recommandations"),
                needsAdmin
                    ? L("Les réglages suivants vont être modifiés :\n\n{0}\n\nUne autorisation administrateur sera demandée une seule fois.\nChaque modification reste annulable depuis le Journal.", lines)
                    : L("Les réglages suivants vont être modifiés :\n\n{0}\n\nChaque modification reste annulable depuis le Journal.", lines),
                L("Appliquer")))
            return;

        _applyingReco = true;
        _recoButton.IsEnabled = false;
        foreach (var (vm, _) in pending) vm.IsBusy = true;
        try
        {
            var results = await AppHost.Engine.ApplyManyAsync(pending.Select(p => (p.Vm.Definition, p.Option)));
            var ok = results.Count(r => r.Outcome.Success);
            var failed = results.Where(r => !r.Outcome.Success).ToList();
            AppHost.Toasts.Show(failed.Count == 0
                    ? LP(ok, "{0} réglage appliqué.", "{0} réglages appliqués.")
                    : LP(ok, "{0} appliqué, {1} en échec : {2}", "{0} appliqués, {1} en échec : {2}", failed.Count, string.Join(" ; ", failed.Select(f => f.Tweak.Title + " (" + f.Outcome.Message + ")"))),
                failed.Count == 0 ? ToastKind.Success : ToastKind.Warning);
            var effects = results.Where(r => r.Outcome.Success).Aggregate(ApplyEffect.None, (acc, r) => acc | r.Tweak.Effect);
            if (effects != ApplyEffect.None) AppHost.Toasts.ShowOutcome(new ApplyOutcome(true, L("Certains changements demandent une action"), effects));
        }
        finally
        {
            foreach (var (vm, _) in pending) vm.IsBusy = false;
            _applyingReco = false;
            _recoButton.IsEnabled = true;
        }
        foreach (var list in _lists.Values) await list.RefreshAllAsync();
        UpdateRecommendationBar();
    }
}
