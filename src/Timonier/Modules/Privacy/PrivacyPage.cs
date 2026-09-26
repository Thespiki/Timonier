using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using Timonier.UI.Services;

namespace Timonier.Modules.Privacy;

/// <summary>
/// Page « Confidentialité » : score (calculé en arrière-plan), limites expliquées honnêtement, puis les réglages
/// par section. Les cartes sont créées par petits lots après le premier affichage pour garder l'interface fluide
/// (chaque carte coûte plusieurs dizaines de millisecondes sur un PC modeste).
/// </summary>
public sealed class PrivacyPage : UserControl, INavigationAware
{
    /// <summary>Nombre de cartes créées par opération du répartiteur (≈ 0,2 s sur un Pentium N6000).</summary>
    private const int ChunkSize = 5;

    private readonly List<TweakDefinition> _tweaks;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _listsHost = new();
    /// <summary>Listes affichées par section (une section = plusieurs lots de cartes).</summary>
    private readonly Dictionary<string, List<TweakListView>> _lists = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(600) };

    // Carte du score
    private readonly ScoreRing _ring = new();
    private readonly TextBlock _statusIcon = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _detailText = new();
    private readonly TextBlock _progressText = new();
    private readonly ProgressBar _progressBar = new() { IsIndeterminate = true, Height = 3, Width = 140 };
    private readonly StackPanel _progressRow = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _chips = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _applyButton = new();
    private readonly TextBlock _applyText = new();
    private readonly Button _refreshButton = new();
    private readonly Border _errorBar;
    private readonly TextBlock _errorText = new();

    // Barre des listes
    private readonly CheckBox _pendingOnly = new();
    private readonly TextBlock _listSummary = new();

    private PrivacyScoreResult? _last;
    private HashSet<string> _applying = new(StringComparer.Ordinal);
    /// <summary>Réglages affichés quand le filtre « à revoir » est actif (figé à l'activation pour que les cartes ne sautent pas).</summary>
    private HashSet<string>? _filter;
    private List<(PrivacySection Section, List<TweakDefinition> Tweaks)> _plan = [];
    private bool _busy, _listsReady, _firstLoad = true;
    private int _version, _buildGeneration;
    private string? _pendingNavigation;

    public PrivacyPage()
    {
        Focusable = false;
        _tweaks = [.. AppHost.Registry.TweaksIn(PrivacyModule.Category)];

        var stack = new StackPanel();
        stack.SetResourceReference(StyleProperty, "Pp.PageStack");
        _scroll = new ScrollViewer { Content = stack };
        _scroll.SetResourceReference(StyleProperty, "Pp.PageScroll");
        Content = _scroll;

        stack.Children.Add(new PageHeader
        {
            Title = L("Privacy"),
            Subtitle = L("Telemetry, advertising, search, AI and app permissions: choose what Windows shares."),
            Glyph = "",
        });

        _errorBar = BuildErrorBar();
        stack.Children.Add(BuildScoreCard());
        stack.Children.Add(BuildLimitsInfoBar());
        stack.Children.Add(BuildListToolbar());
        stack.Children.Add(_listsHost);

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RefreshScoreAsync(refreshCards: false);
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RebuildLists();
    }

    // ================================================================== Cycle de vie

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppHost.Engine.Changed += OnEngineChanged;
        AppHost.HardwareLoaded += OnHardwareLoaded;
        var first = _firstLoad;
        _firstLoad = false;
        // Au retour sur la page, on relit l'état réel : un autre module (profils, journal…) a pu changer des réglages.
        await RefreshScoreAsync(refreshCards: !first);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppHost.Engine.Changed -= OnEngineChanged;
        AppHost.HardwareLoaded -= OnHardwareLoaded;
        _debounce.Stop();
    }

    private void OnEngineChanged(object? sender, TweakChangedEventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (_busy) return; // recalcul complet à la fin de l'application groupée
            _debounce.Stop();
            _debounce.Start();
        });

    private void OnHardwareLoaded(object? sender, EventArgs e) => _ = RefreshScoreAsync(refreshCards: false);

    // ================================================================== Navigation

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not string s || s.Length == 0) return;
        _pendingNavigation = s;
        TryHandlePendingNavigation(buildFinished: _listsReady);
    }

    /// <summary>
    /// Traite la navigation en attente dès que sa cible est construite (sans attendre la fin de toutes les listes).
    /// <paramref name="buildFinished"/> : toutes les listes existent ; une cible introuvable est alors abandonnée.
    /// </summary>
    private void TryHandlePendingNavigation(bool buildFinished)
    {
        if (_pendingNavigation is not { } parameter) return;

        if (parameter.StartsWith("tweak:", StringComparison.Ordinal))
        {
            var id = parameter["tweak:".Length..];
            var tweak = _tweaks.FirstOrDefault(t => t.Id == id);
            if (tweak is null)
            {
                _pendingNavigation = null;
                return;
            }
            if (_filter is not null && !_filter.Contains(id))
            {
                // Réglage masqué par le filtre « à revoir » : on réaffiche tout ; la navigation reste en attente.
                _pendingOnly.IsChecked = false;
                return;
            }
            var list = PrivacyGroups.ByGroup(tweak.Group) is { } section && _lists.TryGetValue(section.Key, out var lists)
                ? lists.FirstOrDefault(l => l.Items.Any(vm => vm.Definition.Id == id))
                : null;
            if (list is null)
            {
                if (buildFinished) _pendingNavigation = null;
                return;
            }
            _pendingNavigation = null;
            if (list.Highlight(id)) ScrollCardToTop(list, id);
        }
        else if (parameter.StartsWith("section:", StringComparison.Ordinal))
        {
            var key = parameter["section:".Length..];
            if (!_lists.ContainsKey(key) && !buildFinished) return;
            _pendingNavigation = null;
            ScrollToSection(key);
        }
        else
        {
            _pendingNavigation = null;
        }
    }

    /// <summary>Place la carte mise en évidence dans le tiers haut de la page (BringIntoView la laisse collée au bas).</summary>
    private void ScrollCardToTop(TweakListView list, string tweakId)
    {
        Dispatcher.InvokeAsync(() =>
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
            catch (InvalidOperationException)
            {
                // Arbre visuel en cours de reconstruction : le BringIntoView de la liste suffit.
            }
        }, DispatcherPriority.Loaded);
    }

    private void ScrollToSection(string key)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!_lists.TryGetValue(key, out var lists) || lists.Count == 0 || !lists[0].IsVisible) return;
            try
            {
                var top = lists[0].TransformToAncestor(_scroll).Transform(new Point(0, 0)).Y + _scroll.VerticalOffset;
                // La liste commence par la marge haute de son titre de section : on s'aligne juste au-dessus du titre.
                _scroll.ScrollToVerticalOffset(Math.Max(0, top + 8));
            }
            catch (InvalidOperationException)
            {
                lists[0].BringIntoView();
            }
        }, DispatcherPriority.Loaded);
    }

    // ================================================================== Listes de réglages

    /// <summary>(Re)construit les listes de réglages, par lots, à basse priorité.</summary>
    private void RebuildLists()
    {
        var generation = ++_buildGeneration;
        _listsReady = false;
        _lists.Clear();
        _listsHost.Children.Clear();
        _plan = [.. PrivacyGroups.All
            .Select(s => (Section: s, Tweaks: _tweaks.Where(t => t.Group == s.Title && (_filter is null || _filter.Contains(t.Id))).ToList()))
            .Where(p => p.Tweaks.Count > 0)];
        Dispatcher.InvokeAsync(() => BuildNextChunk(0, 0, generation), DispatcherPriority.Background);
    }

    private void BuildNextChunk(int sectionIndex, int offset, int generation)
    {
        if (generation != _buildGeneration) return; // reconstruction relancée entre-temps
        if (sectionIndex >= _plan.Count)
        {
            if (_filter is { Count: 0 })
            {
                _listsHost.Children.Add(PageScaffold.InfoBar(
                    L("Nothing to review: all evaluated settings already follow Timonier's recommendation for this PC."),
                    "", "Pp.InfoBar.Success"));
            }
            _listsReady = true;
            TryHandlePendingNavigation(buildFinished: true);
            return;
        }

        var (section, tweaks) = _plan[sectionIndex];
        var chunk = tweaks.Skip(offset).Take(ChunkSize).ToList();
        try
        {
            var list = new TweakListView(chunk, showRecommendations: false);
            System.Windows.Automation.AutomationProperties.SetName(list, section.Title);
            if (offset > 0) HideGroupTitle(list, section.Title);
            if (!_lists.TryGetValue(section.Key, out var lists)) _lists[section.Key] = lists = [];
            lists.Add(list);
            _listsHost.Children.Add(list);
        }
        catch (Exception ex)
        {
            Log.Error("Privacy", "création de la section " + section.Key, ex);
            _listsHost.Children.Add(PageScaffold.InfoBar(L("Part of the “{0}” section couldn't be displayed: {1}", section.Title, ex.Message),
                "", "Pp.InfoBar.Danger"));
        }

        TryHandlePendingNavigation(buildFinished: false);
        var next = offset + chunk.Count;
        var (nextSection, nextOffset) = next >= tweaks.Count ? (sectionIndex + 1, 0) : (sectionIndex, next);
        Dispatcher.InvokeAsync(() => BuildNextChunk(nextSection, nextOffset, generation), DispatcherPriority.Background);
    }

    /// <summary>Un lot qui prolonge une section ne répète pas le titre du groupe (affiché par la liste en tête).</summary>
    private static void HideGroupTitle(TweakListView list, string title)
    {
        if (list.Content is Panel { Children.Count: > 0 } root && root.Children[0] is TextBlock header && header.Text == title)
            header.Visibility = Visibility.Collapsed;
    }

    private IEnumerable<TweakItemViewModel> AllItems() => _lists.Values.SelectMany(l => l).SelectMany(l => l.Items);

    /// <summary>Filtre « à revoir » : fige la liste des réglages non conformes au moment de l'activation.</summary>
    private void ApplyFilter(bool pendingOnly)
    {
        var next = pendingOnly && _last is not null
            ? new HashSet<string>(_last.Pending.Select(p => p.Tweak.Id), StringComparer.Ordinal)
            : null;
        if (next is null && _filter is null) return;
        _filter = next;
        RebuildLists();
        UpdateListSummary();
    }

    private void UpdateListSummary()
    {
        var total = _tweaks.Count(t => AppHost.Settings.AdvancedMode || t.Risk != RiskLevel.Advanced);
        if (_last is null)
        {
            _listSummary.Text = LP(total, "{0} setting", "{0} settings");
            return;
        }
        var pending = _last.Pending.Count;
        _listSummary.Text = _filter is not null
            ? LP(_filter.Count, "{0} setting to review shown out of {1}", "{0} settings to review shown out of {1}", total)
            : pending == 0
                ? LP(total, "{0} setting, matches the recommendation", "{0} settings, all matching the recommendation")
                : LP(total, "{0} setting, {1} to review", "{0} settings, {1} to review", pending);
    }

    // ================================================================== Score

    private async Task RefreshScoreAsync(bool refreshCards)
    {
        var version = ++_version;
        SetComputing(true);
        try
        {
            var profile = AppHost.Profile;
            var tweaks = _tweaks;
            var result = await Task.Run(() => PrivacyScore.Compute(tweaks, profile));
            if (version != _version) return;

            var previous = _last;
            _last = result;
            _errorBar.Visibility = Visibility.Collapsed;
            Render(result);
            if (refreshCards && previous is not null) await RefreshChangedCardsAsync(previous, result);
        }
        catch (Exception ex)
        {
            if (version != _version) return;
            Log.Error("Privacy", "calcul du score de confidentialité", ex);
            _errorText.Text = L("The score couldn't be calculated: {0}", ex.Message);
            _errorBar.Visibility = Visibility.Visible;
            if (_last is null)
            {
                _ring.Update(null, "—", "", "Pp.TextTertiary");
                SetStatus("", "Pp.Danger", L("Score unavailable"), "Pp.TextPrimary");
                _detailText.Text = L("You can still use the settings below.");
            }
        }
        finally
        {
            if (version == _version) SetComputing(false);
        }
    }

    /// <summary>Rafraîchit uniquement les cartes dont l'état a changé depuis la dernière analyse.</summary>
    private async Task RefreshChangedCardsAsync(PrivacyScoreResult previous, PrivacyScoreResult current)
    {
        foreach (var vm in AllItems().ToList())
        {
            if (vm.IsBusy) continue;
            var id = vm.Definition.Id;
            if (previous.States.TryGetValue(id, out var before) && current.States.TryGetValue(id, out var now) && before == now) continue;
            await vm.RefreshAsync();
        }
    }

    private void SetComputing(bool computing)
    {
        _progressRow.Visibility = computing || _busy ? Visibility.Visible : Visibility.Collapsed;
        if (computing && !_busy) _progressText.Text = _last is null ? L("Analyzing settings…") : L("Refreshing…");
        if (computing && _last is null)
        {
            _ring.Update(null, "…", "", "Pp.Accent");
            SetStatus("", "Pp.TextSecondary", L("Scanning"), "Pp.TextPrimary");
            _detailText.Text = L("Timonier is checking the actual state of each setting (read-only).");
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var pending = _last?.Pending.Count ?? 0;
        _applyButton.IsEnabled = !_busy && _last is not null && pending > 0;
        _refreshButton.IsEnabled = !_busy;
        _pendingOnly.IsEnabled = !_busy && _last is not null;
        _applyText.Text = _last is null ? L("Apply the recommended level")
            : pending == 0 ? L("Recommended level reached")
            : L("Apply the recommended level ({0})", pending);
    }

    private void Render(PrivacyScoreResult result)
    {
        if (result.Total == 0)
        {
            _ring.Update(null, "—", "", "Pp.TextTertiary");
            SetStatus("", "Pp.TextSecondary", L("No settings to evaluate"), "Pp.TextPrimary");
            _detailText.Text = L("None of the settings on this page has a recommendation that applies to this PC.");
        }
        else
        {
            var (brush, glyph, label) = result.Level switch
            {
                ScoreLevel.Good => ("Pp.Success", "", L("Good protection")),
                ScoreLevel.Medium => ("Pp.Info", "", L("Partial protection")),
                _ => ("Pp.Warning", "", L("Weak protection")),
            };
            _ring.Update(result.Ratio, L("{0}%", result.Percent), "", brush);
            SetStatus(glyph, brush, label, brush);

            var detail = result.Compliant == result.Total
                ? LP(result.Total, "The evaluated setting follows Timonier's recommendation for this PC.",
                    "All {0} evaluated settings follow Timonier's recommendation for this PC.")
                : LP(result.Compliant, "{0} of {1} settings follows Timonier's recommendation for this PC.",
                    "{0} of {1} settings follow Timonier's recommendation for this PC.", result.Total);
            if (result.Unavailable > 0)
                detail += " " + LP(result.Unavailable,
                    "{0} setting doesn't apply to this PC (edition, Windows version or hardware) and isn't counted.",
                    "{0} settings don't apply to this PC (edition, Windows version or hardware) and aren't counted.");
            _detailText.Text = detail;
        }
        BuildChips(result);
        UpdateButtons();
        UpdateListSummary();
    }

    private void SetStatus(string glyph, string iconBrush, string text, string textBrush)
    {
        _statusIcon.Text = glyph;
        _statusIcon.SetResourceReference(TextBlock.ForegroundProperty, iconBrush);
        _statusText.Text = text;
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, textBrush);
    }

    private void BuildChips(PrivacyScoreResult result)
    {
        _chips.Children.Clear();
        foreach (var section in PrivacyGroups.All)
        {
            if (_tweaks.All(t => t.Group != section.Title)) continue;
            var (ok, total) = result.CountFor(section.Key);

            var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.FillProperty, total == 0 ? "Pp.TextTertiary" : ok == total ? "Pp.Success" : "Pp.Warning");

            var title = new TextBlock { Text = section.ShortTitle, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextPrimary");
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(dot);
            content.Children.Add(title);
            if (total > 0)
            {
                var count = new TextBlock { Text = $"{ok}/{total}", FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                count.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextSecondary");
                content.Children.Add(count);
            }

            var chip = new Button
            {
                Content = content,
                Padding = new Thickness(10, 3, 10, 3),
                MinHeight = 28,
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = total == 0
                    ? L("{0}: no recommendation for this PC. Click to show the section.", section.Title)
                    : LP(ok, "{1}: {0} of {2} settings at the recommended level. Click to show the section.",
                        "{1}: {0} of {2} settings at the recommended level. Click to show the section.", section.Title, total),
            };
            chip.SetResourceReference(StyleProperty, "Pp.Button");
            System.Windows.Automation.AutomationProperties.SetName(chip, total == 0 ? section.Title : L("{0}, {1} of {2}", section.Title, ok, total));
            var key = section.Key;
            chip.Click += (_, _) =>
            {
                _pendingNavigation = "section:" + key;
                TryHandlePendingNavigation(buildFinished: _listsReady);
            };
            _chips.Children.Add(chip);
        }
    }

    // ================================================================== Actions

    private async Task ApplyRecommendedAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateButtons();
        _progressRow.Visibility = Visibility.Visible;
        _progressText.Text = L("Preparing the list of changes…");
        var progress = new Progress<string>(s => _progressText.Text = L("Applying: {0}", s));
        try
        {
            await PrivacyRecommendations.RunAsync(progress, targets =>
            {
                _progressText.Text = L("Applying…");
                _applying = new HashSet<string>(targets.Select(t => t.Id), StringComparer.Ordinal);
                foreach (var vm in AllItems())
                    if (_applying.Contains(vm.Definition.Id)) vm.IsBusy = true;
            });
        }
        catch (Exception ex)
        {
            Log.Error("Privacy", "application du niveau recommandé", ex);
            AppHost.Toasts.Show(L("Error while applying: {0}", ex.Message), ToastKind.Error);
        }
        finally
        {
            var applied = _applying;
            _applying = new HashSet<string>(StringComparer.Ordinal);
            foreach (var vm in AllItems().ToList())
            {
                if (!applied.Contains(vm.Definition.Id)) continue;
                vm.IsBusy = false;
                await vm.RefreshAsync();
            }
            _busy = false;
            await RefreshScoreAsync(refreshCards: false);
            // Filtre actif : la liste « à revoir » est mise à jour après l'application groupée.
            if (_filter is not null && applied.Count > 0) ApplyFilter(true);
        }
    }

    private static void OpenWindowsSettings()
    {
        try { ProcessRunner.OpenSettingsUri("ms-settings:privacy"); }
        catch (Exception ex) { AppHost.Toasts.Show(L("Couldn't open Windows Settings: {0}", ex.Message), ToastKind.Error); }
    }

    // ================================================================== Construction de l'interface

    private Border BuildScoreCard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _ring.Margin = new Thickness(4, 4, 24, 4);
        _ring.VerticalAlignment = VerticalAlignment.Top;
        _ring.Update(null, "…", "", "Pp.Accent");
        grid.Children.Add(_ring);

        var right = new StackPanel();
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        // Titre + bouton d'actualisation
        var titleRow = new DockPanel();
        _refreshButton.Content = Icon("", 14);
        _refreshButton.ToolTip = L("Re-read the state of the settings");
        _refreshButton.Padding = new Thickness(8, 4, 8, 4);
        _refreshButton.MinHeight = 28;
        _refreshButton.SetResourceReference(StyleProperty, "Pp.SubtleButton");
        System.Windows.Automation.AutomationProperties.SetName(_refreshButton, L("Refresh the score"));
        _refreshButton.Click += async (_, _) => await RefreshScoreAsync(refreshCards: true);
        DockPanel.SetDock(_refreshButton, Dock.Right);
        titleRow.Children.Add(_refreshButton);
        var title = new TextBlock { Text = L("Privacy score"), FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(StyleProperty, "Pp.CardTitle");
        titleRow.Children.Add(title);
        right.Children.Add(titleRow);

        // Niveau
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _statusIcon.SetResourceReference(StyleProperty, "Pp.Icon");
        _statusIcon.FontSize = 14;
        _statusIcon.Margin = new Thickness(0, 0, 8, 0);
        _statusText.SetResourceReference(StyleProperty, "Pp.Body");
        _statusText.FontWeight = FontWeights.SemiBold;
        statusRow.Children.Add(_statusIcon);
        statusRow.Children.Add(_statusText);
        right.Children.Add(statusRow);

        _detailText.SetResourceReference(StyleProperty, "Pp.Caption");
        _detailText.Margin = new Thickness(0, 4, 0, 0);
        right.Children.Add(_detailText);

        // Progression (analyse / application)
        _progressRow.Margin = new Thickness(0, 8, 0, 0);
        _progressBar.VerticalAlignment = VerticalAlignment.Center;
        _progressText.SetResourceReference(StyleProperty, "Pp.Caption");
        _progressText.Margin = new Thickness(10, 0, 0, 0);
        _progressText.VerticalAlignment = VerticalAlignment.Center;
        _progressText.TextTrimming = TextTrimming.CharacterEllipsis;
        _progressText.TextWrapping = TextWrapping.NoWrap;
        _progressText.MaxWidth = 520;
        _progressRow.Children.Add(_progressBar);
        _progressRow.Children.Add(_progressText);
        _progressRow.Visibility = Visibility.Collapsed;
        right.Children.Add(_progressRow);

        // Répartition par section (sert aussi de sommaire)
        right.Children.Add(_chips);

        // Boutons
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var applyContent = new StackPanel { Orientation = Orientation.Horizontal };
        var applyIcon = Icon("", 14);
        applyIcon.Margin = new Thickness(0, 0, 8, 0);
        applyIcon.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextOnAccent");
        applyContent.Children.Add(applyIcon);
        applyContent.Children.Add(_applyText);
        _applyButton.Content = applyContent;
        _applyButton.Margin = new Thickness(0, 0, 8, 6);
        _applyButton.SetResourceReference(StyleProperty, "Pp.AccentButton");
        _applyButton.ToolTip = L("Shows the list of changes before applying them. Everything can be undone from History.");
        _applyButton.Click += async (_, _) => await ApplyRecommendedAsync();
        buttons.Children.Add(_applyButton);

        var settingsContent = new StackPanel { Orientation = Orientation.Horizontal };
        var settingsIcon = Icon("", 14);
        settingsIcon.Margin = new Thickness(0, 0, 8, 0);
        settingsContent.Children.Add(settingsIcon);
        settingsContent.Children.Add(new TextBlock { Text = L("Windows privacy settings"), VerticalAlignment = VerticalAlignment.Center });
        var settingsButton = new Button { Content = settingsContent, Margin = new Thickness(0, 0, 8, 6) };
        settingsButton.SetResourceReference(StyleProperty, "Pp.Button");
        settingsButton.Click += (_, _) => OpenWindowsSettings();
        buttons.Children.Add(settingsButton);
        right.Children.Add(buttons);

        right.Children.Add(_errorBar);
        UpdateButtons();

        var card = new Border { Child = grid, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(20, 16, 20, 14) };
        card.SetResourceReference(StyleProperty, "Pp.Card");
        return card;
    }

    private Border BuildErrorBar()
    {
        _errorText.SetResourceReference(StyleProperty, "Pp.Body");
        var retry = new Button { Content = L("Try again"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        retry.SetResourceReference(StyleProperty, "Pp.Button");
        retry.Click += async (_, _) => await RefreshScoreAsync(refreshCards: true);
        var icon = Icon("", 16);
        icon.Margin = new Thickness(0, 0, 10, 0);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Danger");
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(retry, Dock.Right);
        dock.Children.Add(icon);
        dock.Children.Add(retry);
        dock.Children.Add(_errorText);
        var bar = new Border { Child = dock, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        bar.SetResourceReference(StyleProperty, "Pp.InfoBar.Danger");
        return bar;
    }

    private static Border BuildLimitsInfoBar()
    {
        var p = AppHost.Profile;
        var body = new StackPanel();
        var title = new TextBlock { Text = L("What Timonier can do, and its limits"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        title.SetResourceReference(StyleProperty, "Pp.Body");
        body.Children.Add(title);

        body.Children.Add(Bullet(p.SupportsTelemetryOff
            ? L("Your edition ({0}) lets you turn off diagnostic data completely (level 0).", p.EditionLabel)
            : L("On your edition ({0}), Windows always sends “required” diagnostic data: turning it off completely (level 0) is only possible on the Enterprise, Education and IoT editions.", p.EditionLabel)));
        body.Children.Add(Bullet(L("Major Windows updates can turn some services, scheduled tasks or suggestions back on: come back and check the score after an update.")));
        body.Children.Add(Bullet(L("Timonier doesn't block Microsoft servers (hosts file, firewall): such blocking disrupts Windows Update, activation and the Microsoft Store, with no guarantee that it works.")));
        body.Children.Add(Bullet(L("App permissions apply to your account. Blocking the camera, microphone and location for the whole PC is set on the Devices page.")));
        if (p.IsManaged)
            body.Children.Add(Bullet(L("This PC is managed by an organization: its policies may override the settings below.")));

        var icon = Icon("", 16);
        icon.Margin = new Thickness(0, 1, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Pp.AccentText");
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(body);
        var bar = new Border { Child = dock, Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 4) };
        bar.SetResourceReference(StyleProperty, "Pp.InfoBar");
        return bar;
    }

    /// <summary>Barre au-dessus des listes : résumé et filtre « uniquement les réglages à revoir ».</summary>
    private FrameworkElement BuildListToolbar()
    {
        var label = new TextBlock { Text = L("Only settings to review"), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(StyleProperty, "Pp.Body");
        _pendingOnly.Content = label;
        _pendingOnly.IsThreeState = false;
        _pendingOnly.IsEnabled = false;
        _pendingOnly.ToolTip = L("Hides settings that already match the recommendation and those with no recommendation for this PC.");
        _pendingOnly.SetResourceReference(StyleProperty, "Pp.ToggleSwitch");
        System.Windows.Automation.AutomationProperties.SetName(_pendingOnly, L("Show only settings to review"));
        _pendingOnly.Checked += (_, _) => ApplyFilter(true);
        _pendingOnly.Unchecked += (_, _) => ApplyFilter(false);

        _listSummary.SetResourceReference(StyleProperty, "Pp.Caption");
        _listSummary.VerticalAlignment = VerticalAlignment.Center;
        UpdateListSummary();

        var dock = new DockPanel { Margin = new Thickness(2, 14, 0, 0), LastChildFill = true };
        DockPanel.SetDock(_pendingOnly, Dock.Right);
        dock.Children.Add(_pendingOnly);
        dock.Children.Add(_listSummary);
        return dock;
    }

    private static FrameworkElement Bullet(string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dot = new TextBlock { Text = "•" };
        dot.SetResourceReference(StyleProperty, "Pp.Caption");
        var body = new TextBlock { Text = text };
        body.SetResourceReference(StyleProperty, "Pp.Caption");
        Grid.SetColumn(body, 1);
        grid.Children.Add(dot);
        grid.Children.Add(body);
        return grid;
    }

    private static TextBlock Icon(string glyph, double size)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(StyleProperty, "Pp.Icon");
        return t;
    }
}
