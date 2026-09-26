using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.UI.Services;

namespace Timonier.UI.Controls;

/// <summary>
/// Liste de réglages groupés (cartes). Utilisable seule (page générique d'une catégorie) ou intégrée dans une page
/// personnalisée : <c>new TweakListView("privacy")</c> ou <c>new TweakListView(tweaks)</c>.
/// </summary>
public sealed class TweakListView : UserControl
{
    private readonly List<(TweakItemViewModel Vm, TweakCard Card)> _items = [];
    private readonly StackPanel _root = new();
    private readonly Border _recommendBar;
    private readonly TextBlock _recommendText;
    private bool _loadedOnce;

    public TweakListView(string categoryId, bool showRecommendations = true)
        : this(AppHost.Registry.TweaksIn(categoryId), showRecommendations)
    {
    }

    public TweakListView(IEnumerable<TweakDefinition> tweaks, bool showRecommendations = true)
    {
        Focusable = false;
        Content = _root;

        _recommendText = new TextBlock { Style = (Style)FindResource("Pp.Body"), VerticalAlignment = VerticalAlignment.Center };
        var applyButton = new Button { Content = L("Apply recommendations"), Style = (Style)FindResource("Pp.AccentButton"), Margin = new Thickness(12, 0, 0, 0) };
        applyButton.Click += async (_, _) => await ApplyRecommendationsAsync();
        var barGrid = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(applyButton, Dock.Right);
        barGrid.Children.Add(applyButton);
        barGrid.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Style = (Style)FindResource("Pp.Icon"), Text = "", Margin = new Thickness(0, 0, 10, 0), Foreground = (System.Windows.Media.Brush)FindResource("Pp.AccentText") },
                _recommendText,
            },
        });
        _recommendBar = new Border { Style = (Style)FindResource("Pp.InfoBar"), Child = barGrid, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) };
        if (showRecommendations) _root.Children.Add(_recommendBar);

        var advanced = AppHost.Settings.AdvancedMode;
        var visible = tweaks.Where(t => advanced || t.Risk != RiskLevel.Advanced).ToList();
        var hiddenCount = tweaks.Count() - visible.Count;

        foreach (var group in visible.GroupBy(t => t.Group ?? ""))
        {
            if (group.Key.Length > 0)
            {
                var heading = new TextBlock { Text = group.Key, Style = (Style)FindResource("Pp.SectionTitle") };
                _groupHeadings[group.Key] = heading;
                _root.Children.Add(heading);
            }
            foreach (var t in group)
            {
                var vm = new TweakItemViewModel(t);
                var card = new TweakCard { DataContext = vm };
                _items.Add((vm, card));
                _root.Children.Add(card);
            }
        }

        if (hiddenCount > 0)
        {
            _root.Children.Add(new TextBlock
            {
                Text = LP(hiddenCount,
                    "{0} advanced setting hidden. Turn on “advanced mode” in Timonier settings to show it.",
                    "{0} advanced settings hidden. Turn on “advanced mode” in Timonier settings to show them."),
                Style = (Style)FindResource("Pp.Caption"),
                Margin = new Thickness(2, 12, 0, 0),
            });
        }
        if (_items.Count == 0 && hiddenCount == 0)
            _root.Children.Add(new TextBlock { Text = L("No settings in this section."), Style = (Style)FindResource("Pp.Caption") });

        Loaded += async (_, _) =>
        {
            AppHost.Engine.Changed += OnEngineChanged;
            AppHost.HardwareLoaded += OnHardwareLoaded;
            if (!_loadedOnce) { _loadedOnce = true; await RefreshAllAsync(); }
        };
        Unloaded += (_, _) =>
        {
            AppHost.Engine.Changed -= OnEngineChanged;
            AppHost.HardwareLoaded -= OnHardwareLoaded;
        };
    }

    public IReadOnlyList<TweakItemViewModel> Items => [.. _items.Select(i => i.Vm)];

    private readonly Dictionary<string, TextBlock> _groupHeadings = new(StringComparer.Ordinal);

    /// <summary>Noms des groupes affichés (dans l'ordre), pour construire une barre de navigation.</summary>
    public IReadOnlyList<string> GroupNames => [.. _groupHeadings.Keys];

    /// <summary>Fait défiler jusqu'à l'en-tête d'un groupe. Renvoie false si le groupe n'est pas affiché.</summary>
    public bool ScrollToGroup(string group)
    {
        if (!_groupHeadings.TryGetValue(group, out var heading)) return false;
        Dispatcher.InvokeAsync(() => heading.BringIntoView(new Rect(0, 0, 1, 400)), DispatcherPriority.Loaded);
        return true;
    }

    private void OnEngineChanged(object? sender, TweakChangedEventArgs e) =>
        Dispatcher.InvokeAsync(async () =>
        {
            foreach (var (vm, _) in _items)
                if (vm.Definition.Id == e.SourceId && !vm.IsBusy) await vm.RefreshAsync();
            UpdateRecommendationBar();
        });

    private void OnHardwareLoaded(object? sender, EventArgs e) => _ = RefreshAllAsync();

    public async Task RefreshAllAsync()
    {
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(_items.Select(async i =>
        {
            await gate.WaitAsync();
            try { await i.Vm.RefreshAsync(); }
            finally { gate.Release(); }
        }));
        UpdateRecommendationBar();
    }

    private List<(TweakItemViewModel Vm, string Option)> PendingRecommendations() =>
        [.. _items.Select(i => i.Vm)
            .Where(vm => vm.IsAvailable && vm.RecommendationText is not null)
            .Select(vm => (vm, vm.Definition.RecommendationFor(AppHost.Profile)!))];

    private void UpdateRecommendationBar()
    {
        var pending = PendingRecommendations();
        _recommendBar.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _recommendText.Text = LP(pending.Count,
            "{0} setting differs from the recommendation for this PC.",
            "{0} settings differ from the recommendations for this PC.");
    }

    private async Task ApplyRecommendationsAsync()
    {
        var pending = PendingRecommendations();
        if (pending.Count == 0) return;
        var lines = string.Join("\n", pending.Select(p => $"• {p.Vm.Title} → {p.Vm.Definition.GetOption(p.Option)?.Label}"));
        var needsAdmin = pending.Any(p => p.Vm.Definition.RequiresAdmin);
        if (!await AppHost.Dialogs.ConfirmAsync(L("Apply recommendations"),
                needsAdmin
                    ? L("The following settings will be changed:\n\n{0}\n\nAdministrator permission will be requested only once.\nEach change can still be undone from History.", lines)
                    : L("The following settings will be changed:\n\n{0}\n\nEach change can still be undone from History.", lines),
                L("Apply")))
            return;

        foreach (var (vm, _) in pending) vm.IsBusy = true;
        var results = await AppHost.Engine.ApplyManyAsync(pending.Select(p => (p.Vm.Definition, p.Option)));
        foreach (var (vm, _) in pending) vm.IsBusy = false;
        var ok = results.Count(r => r.Outcome.Success);
        var failed = results.Where(r => !r.Outcome.Success).ToList();
        AppHost.Toasts.Show(failed.Count == 0
                ? LP(ok, "{0} setting applied.", "{0} settings applied.")
                : LP(ok, "{0} applied, {1} failed: {2}", "{0} applied, {1} failed: {2}",
                    failed.Count, string.Join(" ; ", failed.Select(f => L("{0} ({1})", f.Tweak.Title, f.Outcome.Message)))),
            failed.Count == 0 ? ToastKind.Success : ToastKind.Warning);
        var effects = results.Where(r => r.Outcome.Success).Aggregate(ApplyEffect.None, (acc, r) => acc | r.Tweak.Effect);
        if (effects != ApplyEffect.None) AppHost.Toasts.ShowOutcome(new ApplyOutcome(true, L("Some changes require action"), effects));
        await RefreshAllAsync();
    }

    /// <summary>Fait défiler jusqu'au réglage et le met en évidence (utilisé par la recherche).</summary>
    public bool Highlight(string tweakId)
    {
        var item = _items.FirstOrDefault(i => i.Vm.Definition.Id == tweakId);
        if (item.Card is null) return false;
        Dispatcher.InvokeAsync(() =>
        {
            item.Card.BringIntoView();
            item.Vm.IsHighlighted = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { item.Vm.IsHighlighted = false; timer.Stop(); };
            timer.Start();
        }, DispatcherPriority.Loaded);
        return true;
    }
}
