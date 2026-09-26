using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Security;
using Timonier.UI.Controls;
using Timonier.UI.Services;

namespace Timonier.Modules.Security;

/// <summary>
/// Page « Sécurité » : score et état de sécurité (sondes en lecture seule, hors du thread UI), blocage réseau d'applications
/// par le pare-feu, puis réglages de renforcement créés par petits lots après le premier affichage.
/// Aucun minuteur : l'état est relu à l'arrivée sur la page, après une correction ou sur demande.
/// </summary>
public sealed class SecurityPage : UserControl, INavigationAware
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    private readonly ScrollViewer _scroll;

    // Score
    private readonly SecurityRing _ring = new();
    private readonly TextBlock _headline = new();
    private readonly TextBlock _summary = new();
    private readonly TextBlock _updated = new();
    private readonly StackPanel _progressRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _refreshButton;

    // État détaillé
    private readonly StackPanel _statusHost = new();

    // Pare-feu
    private readonly TextBlock _firewallTitle;
    private readonly StackPanel _firewallList = new();
    private readonly Button _blockButton;

    // Renforcement
    private readonly TextBlock _hardeningTitle;
    private readonly StackPanel _hardeningHost = new();
    private readonly List<TweakListView> _lists = [];
    private bool _listsBuilt, _listsStarted;

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private SecReport? _report;
    private bool _loading, _firewallBusy;
    private string? _pendingNavigation;

    public SecurityPage()
    {
        Focusable = false;
        var stack = new StackPanel();
        stack.SetResourceReference(StyleProperty, "Pp.PageStack");
        _scroll = new ScrollViewer { Content = stack };
        _scroll.SetResourceReference(StyleProperty, "Pp.PageScroll");
        Content = _scroll;

        stack.Children.Add(new PageHeader
        {
            Title = L("Sécurité"),
            Subtitle = L("État de la protection de ce PC, pare-feu par application et renforcement de Windows."),
            Glyph = SecurityModule.Glyph,
        });

        _refreshButton = MakeButton(L("Actualiser"), "", "Pp.SubtleButton");
        _refreshButton.Click += async (_, _) => await RefreshAsync(includeFirewall: true);
        stack.Children.Add(BuildScoreCard());

        stack.Children.Add(TopSection(L("État de sécurité")));
        stack.Children.Add(_statusHost);
        ShowStatusPlaceholder();

        _firewallTitle = TopSection(L("Pare-feu : bloquer Internet pour une application"));
        stack.Children.Add(_firewallTitle);
        _blockButton = MakeButton(L("Choisir un programme…"), "", "Pp.AccentButton");
        _blockButton.Click += async (_, _) => await BlockProgramAsync();
        stack.Children.Add(BuildFirewallCard());

        _hardeningTitle = TopSection(L("Renforcement de Windows"));
        stack.Children.Add(_hardeningTitle);
        stack.Children.Add(PageScaffold.InfoBar(
            L("Ces réglages réduisent la surface d'attaque de Windows. Chacun indique ses effets et ses risques, et reste annulable depuis le Journal. Timonier ne propose jamais de désactiver Defender, le pare-feu, l'UAC, SmartScreen ni Secure Boot."),
            ""));
        stack.Children.Add(_hardeningHost);

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RefreshAsync(includeFirewall: false);
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ================================================================== Cycle de vie

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppHost.Engine.Changed += OnEngineChanged;
        if (!_listsStarted)
        {
            _listsStarted = true;
            _ = Dispatcher.InvokeAsync(() => BuildNextList(0), DispatcherPriority.Background);
        }
        if (_report is null || DateTime.Now - _report.At > StaleAfter)
            await RefreshAsync(includeFirewall: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppHost.Engine.Changed -= OnEngineChanged;
        _debounce.Stop();
    }

    private void OnEngineChanged(object? sender, TweakChangedEventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        });

    // ================================================================== Navigation

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not string s || s.Length == 0) return;
        _pendingNavigation = s;
        HandlePendingNavigation();
    }

    private void HandlePendingNavigation()
    {
        if (_pendingNavigation is not { } p) return;
        if (p == "section:firewall") { _pendingNavigation = null; ScrollTo(_firewallTitle); return; }
        if (p == "section:hardening") { _pendingNavigation = null; ScrollTo(_hardeningTitle); return; }
        if (!p.StartsWith("tweak:", StringComparison.Ordinal)) { _pendingNavigation = null; return; }

        var id = p["tweak:".Length..];
        var list = _lists.FirstOrDefault(l => l.Items.Any(vm => vm.Definition.Id == id));
        if (list is null)
        {
            if (_listsBuilt) _pendingNavigation = null; // réglage masqué (mode avancé) ou inconnu
            return;
        }
        _pendingNavigation = null;
        list.Highlight(id);
    }

    private void ScrollTo(FrameworkElement target) =>
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var top = target.TransformToAncestor(_scroll).Transform(new Point(0, 0)).Y + _scroll.VerticalOffset;
                _scroll.ScrollToVerticalOffset(Math.Max(0, top - 12));
            }
            catch (InvalidOperationException) { target.BringIntoView(); }
        }, DispatcherPriority.Loaded);

    // ================================================================== Score

    private Border BuildScoreCard()
    {
        _headline.SetResourceReference(StyleProperty, "Pp.PageSubtitle");
        _headline.FontSize = 20;
        _headline.FontWeight = FontWeights.SemiBold;
        _headline.Margin = new Thickness(0);
        _headline.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextPrimary");
        _summary.SetResourceReference(StyleProperty, "Pp.Body");
        _summary.Margin = new Thickness(0, 4, 0, 0);
        _updated.SetResourceReference(StyleProperty, "Pp.Caption");
        _updated.Margin = new Thickness(0, 6, 0, 0);

        var progress = new ProgressBar { IsIndeterminate = true, Width = 140, Height = 3, VerticalAlignment = VerticalAlignment.Center };
        var progressText = new TextBlock { Text = L("Analyse en cours…"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        progressText.SetResourceReference(StyleProperty, "Pp.Caption");
        _progressRow.Children.Add(progress);
        _progressRow.Children.Add(progressText);

        var explain = new TextBlock
        {
            Text = L("Score pondéré : antivirus 25, pare-feu 20, UAC 15, chiffrement 10, SmartScreen 8, puis Secure Boot, intégrité de la mémoire, SMBv1, comptes intégrés… Un point « à vérifier » compte pour 40 %, un point critique pour 0 ; les états inconnus et purement informatifs ne pénalisent pas."),
            Margin = new Thickness(0, 10, 0, 0),
        };
        explain.SetResourceReference(StyleProperty, "Pp.Caption");

        var openSecurity = MakeButton(L("Ouvrir Sécurité Windows"), SecurityModule.Glyph, "Pp.AccentButton");
        openSecurity.Click += (_, _) => OpenUri("windowsdefender:");
        var scan = MakeButton(L("Analyse rapide"), "", "Pp.Button");
        scan.ToolTip = L("Ouvre Protection contre les virus et menaces pour lancer une analyse rapide.");
        scan.Click += (_, _) => OpenUri("windowsdefender://threat/");
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in new[] { openSecurity, scan, _refreshButton })
        {
            b.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(b);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_headline);
        text.Children.Add(_summary);
        text.Children.Add(_progressRow);
        text.Children.Add(_updated);
        text.Children.Add(explain);
        text.Children.Add(buttons);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        _ring.Margin = new Thickness(4, 4, 24, 4);
        _ring.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(_ring);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        _headline.Text = L("Analyse de la sécurité…");
        _summary.Text = L("Timonier lit l'état de l'antivirus, du pare-feu, du chiffrement et des protections de Windows.");
        var card = PageScaffold.Card(grid);
        card.Padding = new Thickness(20, 18, 20, 18);
        return card;
    }

    private void RenderScore(SecReport report)
    {
        var critical = report.CriticalCount;
        var warnings = report.WarningCount;
        var brush = critical > 0 || report.Score < 60 ? "Pp.Danger" : report.Score < 85 || warnings > 2 ? "Pp.Warning" : "Pp.Success";
        _ring.Update(report.Score, brush);
        _headline.Text = critical > 0 ? L("Protection insuffisante")
            : warnings == 0 ? L("Votre PC est bien protégé")
            : report.Score >= 80 ? L("Bonne protection, quelques points à vérifier")
            : L("Protection à renforcer");
        _summary.Text = critical == 0 && warnings == 0
            ? L("Aucun point à corriger parmi les contrôles effectués.")
            : L("{0} : utilisez les boutons de correction ci-dessous.", string.Join(" · ", new[]
            {
                critical > 0 ? LP(critical, "{0} point critique", "{0} points critiques") : null,
                warnings > 0 ? LP(warnings, "{0} point à vérifier", "{0} points à vérifier") : null,
            }.Where(s => s is not null)));
        _updated.Text = LP(report.Items.Count, "Dernière analyse à {1} · {0} contrôle", "Dernière analyse à {1} · {0} contrôles",
            report.At.ToString("t", Culture));
        _updated.Visibility = Visibility.Visible;
    }

    // ================================================================== Actualisation

    private async Task RefreshAsync(bool includeFirewall)
    {
        if (_loading) return;
        _loading = true;
        _refreshButton.IsEnabled = false;
        _progressRow.Visibility = Visibility.Visible;
        _updated.Visibility = Visibility.Collapsed;
        if (_report is null) _ring.Update(null, "");
        var firewallTask = includeFirewall ? RefreshFirewallAsync() : Task.CompletedTask;
        try
        {
            var profile = AppHost.Profile;
            var report = await Task.Run(() => SecurityProbe.Collect(profile));
            _report = report;
            RenderScore(report);
            RenderStatus(report);
        }
        catch (Exception ex)
        {
            Log.Error("Security", "analyse de sécurité", ex);
            _headline.Text = L("Analyse impossible");
            _summary.Text = L("L'état de sécurité n'a pas pu être lu : {0}", ex.Message);
            if (_report is null)
            {
                _statusHost.Children.Clear();
                _statusHost.Children.Add(PageScaffold.InfoBar(L("L'analyse a échoué. Réessayez avec « Actualiser » ou ouvrez Sécurité Windows."),
                    "", "Pp.InfoBar.Warning"));
            }
        }
        finally
        {
            _loading = false;
            _refreshButton.IsEnabled = true;
            _progressRow.Visibility = Visibility.Collapsed;
        }
        await firewallTask;
    }

    // ================================================================== État détaillé

    private void ShowStatusPlaceholder()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 120, Height = 3, VerticalAlignment = VerticalAlignment.Center });
        var t = new TextBlock { Text = L("Lecture de l'état de sécurité…"), Margin = new Thickness(12, 0, 0, 0) };
        t.SetResourceReference(StyleProperty, "Pp.Caption");
        row.Children.Add(t);
        var card = PageScaffold.Card(row);
        card.Padding = new Thickness(16, 18, 16, 18);
        _statusHost.Children.Add(card);
    }

    private void RenderStatus(SecReport report)
    {
        _statusHost.Children.Clear();
        foreach (var section in SecurityProbe.Sections)
        {
            var items = report.Items.Where(i => i.Section == section).ToList();
            if (items.Count == 0) continue;
            var heading = PageScaffold.Section(section);
            if (_statusHost.Children.Count == 0) heading.Margin = new Thickness(2, 4, 0, 8);
            _statusHost.Children.Add(heading);

            var rows = new StackPanel();
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0) rows.Children.Add(Divider());
                rows.Children.Add(BuildStatusRow(items[i]));
            }
            var card = PageScaffold.Card(rows);
            card.Padding = new Thickness(0);
            _statusHost.Children.Add(card);
        }
    }

    private static (string Fg, string Bg, string Word) LevelStyle(SecLevel level) => level switch
    {
        SecLevel.Good => ("Pp.Success", "Pp.SuccessBackground", LC("security level", "Bon")),
        SecLevel.Info => ("Pp.Info", "Pp.InfoBackground", LC("security level", "Info")),
        SecLevel.Warning => ("Pp.Warning", "Pp.WarningBackground", L("À vérifier")),
        SecLevel.Critical => ("Pp.Danger", "Pp.DangerBackground", L("Critique")),
        _ => ("Pp.Neutral", "Pp.NeutralBackground", L("Inconnu")),
    };

    private FrameworkElement BuildStatusRow(SecItem item)
    {
        var (fg, bg, word) = LevelStyle(item.Level);

        var icon = new TextBlock { Text = item.Glyph, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetResourceReference(StyleProperty, "Pp.Icon");
        icon.SetResourceReference(TextBlock.ForegroundProperty, fg);
        var iconBox = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Child = icon, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 14, 0) };
        iconBox.SetResourceReference(Border.BackgroundProperty, bg);

        var title = new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(StyleProperty, "Pp.Body");
        var badgeText = new TextBlock { Text = word };
        badgeText.SetResourceReference(StyleProperty, "Pp.BadgeText");
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, fg);
        var badge = new Border { Child = badgeText, Margin = new Thickness(10, 0, 0, 0) };
        badge.SetResourceReference(StyleProperty, "Pp.Badge");
        badge.SetResourceReference(Border.BackgroundProperty, bg);
        var titleRow = new DockPanel { LastChildFill = false };
        titleRow.Children.Add(title);
        titleRow.Children.Add(badge);

        var status = new TextBlock { Text = item.Status, Margin = new Thickness(0, 2, 0, 0) };
        status.SetResourceReference(StyleProperty, "Pp.Body");
        var detail = new TextBlock { Text = item.Detail, Margin = new Thickness(0, 2, 0, 0) };
        detail.SetResourceReference(StyleProperty, "Pp.Caption");

        var text = new StackPanel();
        text.Children.Add(titleRow);
        text.Children.Add(status);
        text.Children.Add(detail);

        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconBox);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (item.Fix is { } fix)
        {
            var style = item.Level is SecLevel.Warning or SecLevel.Critical && (fix.TweakId is not null || fix.ActionId is not null)
                ? "Pp.AccentButton" : "Pp.Button";
            var button = new Button { Content = fix.Label, MinWidth = 96, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            button.SetResourceReference(StyleProperty, style);
            if (fix.TweakId is { } tid && AppHost.Registry.GetTweak(tid) is null)
            {
                button.IsEnabled = false;
                button.ToolTip = L("Réglage non disponible dans cette version de Timonier.");
            }
            button.Click += async (_, _) => await RunFixAsync(item, fix, button);
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }
        System.Windows.Automation.AutomationProperties.SetName(grid, L("{0} : {1}, {2}", item.Title, word, item.Status));
        return grid;
    }

    private async Task RunFixAsync(SecItem item, SecFix fix, Button button)
    {
        button.IsEnabled = false;
        var changed = false;
        try
        {
            if (fix.Uri is { } uri) { OpenUri(uri); return; }
            if (fix.Tool is { } tool) { ProcessRunner.Launch(tool, fix.ToolArgs); return; }
            if (fix.PageId is { } pageId)
            {
                if (AppHost.Registry.GetPage(pageId) is not null) AppHost.Navigator.Navigate(pageId);
                else OpenUri("ms-settings:otherusers");
                return;
            }
            if (fix.TweakId is { } tweakId)
            {
                if (AppHost.Registry.GetTweak(tweakId) is not { } tweak)
                {
                    AppHost.Toasts.Show(L("Ce réglage n'est pas disponible dans cette version de Timonier."), ToastKind.Warning);
                    return;
                }
                if (AppHost.Engine.Unavailability(tweak) is { } reason)
                {
                    AppHost.Toasts.Show(reason, ToastKind.Warning);
                    return;
                }
                var option = tweak.GetOption(fix.Option ?? TweakDefinition.On) ?? tweak.Options[0];
                var message = (tweak.Kind == TweakKind.Action ? L("Action : « {0} »", tweak.Title) : L("« {0} » → {1}", tweak.Title, option.Label)) +
                              "\n\n" + tweak.Description +
                              (tweak.Warning is { } w ? "\n\n" + L("À savoir : {0}", w) : "") +
                              (tweak.RequiresAdmin ? "\n\n" + L("Une autorisation administrateur sera demandée.") : "") +
                              (tweak.IsReversible ? "\n" + L("La modification reste annulable depuis le Journal.") : "");
                if (!await AppHost.Dialogs.ConfirmAsync(L("Corriger : {0}", item.Title), message, L("Appliquer"))) return;
                var outcome = await AppHost.Engine.ApplyAsync(tweak, option.Key);
                AppHost.Toasts.ShowOutcome(outcome);
                changed = outcome.Success;
                return;
            }
            if (fix.ActionId is { } actionId)
            {
                if (!await AppHost.Dialogs.ConfirmAsync(L("{0} : {1}", fix.Label, item.Title),
                        (fix.Confirm ?? item.Detail) + "\n\n" + L("Une autorisation administrateur sera demandée."), fix.Label))
                    return;
                var outcome = await AppHost.Engine.RunActionAsync(actionId);
                AppHost.Toasts.ShowOutcome(outcome);
                changed = outcome.Success;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Security", "correction " + item.Key, ex);
            AppHost.Toasts.Show(L("Impossible d'appliquer la correction : {0}", ex.Message), ToastKind.Error);
        }
        finally
        {
            button.IsEnabled = true;
            if (changed) await RefreshAsync(includeFirewall: false);
        }
    }

    private static void OpenUri(string uri)
    {
        try { ProcessRunner.OpenSettingsUri(uri); }
        catch (Exception ex)
        {
            Log.Warn("Security", $"ouverture de {uri} : {ex.Message}");
            AppHost.Toasts.Show(L("Sécurité Windows n'a pas pu être ouvert sur ce PC."), ToastKind.Warning);
        }
    }

    // ================================================================== Pare-feu par application

    private Border BuildFirewallCard()
    {
        var intro = new TextBlock
        {
            Text = L("Empêche un programme de se connecter à Internet et au réseau local : jeu hors ligne, logiciel qui se met à jour sans prévenir, outil qui n'a pas à communiquer… Timonier crée des règles du pare-feu Windows (sortantes et entrantes) regroupées sous « Timonier » et ne modifie jamais les autres règles."),
        };
        intro.SetResourceReference(StyleProperty, "Pp.Body");
        var limits = new TextBlock
        {
            Text = L("Sans effet si le pare-feu Windows est désactivé ou remplacé par un pare-feu tiers. Les applications du Microsoft Store ainsi que les programmes de Windows et de Microsoft Defender ne peuvent pas être bloqués ici."),
            Margin = new Thickness(0, 6, 0, 0),
        };
        limits.SetResourceReference(StyleProperty, "Pp.Caption");

        var textStack = new StackPanel();
        textStack.Children.Add(intro);
        textStack.Children.Add(limits);

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(textStack);
        _blockButton.VerticalAlignment = VerticalAlignment.Top;
        _blockButton.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(_blockButton, 1);
        top.Children.Add(_blockButton);

        var listTitle = new TextBlock { Text = L("Applications bloquées par Timonier"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) };
        listTitle.SetResourceReference(StyleProperty, "Pp.Body");

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(Divider(new Thickness(0, 14, 0, 0)));
        stack.Children.Add(listTitle);
        stack.Children.Add(_firewallList);
        SetFirewallMessage(L("Lecture des règles…"));

        var card = PageScaffold.Card(stack);
        card.Padding = new Thickness(18, 16, 18, 14);
        return card;
    }

    private void SetFirewallMessage(string text)
    {
        _firewallList.Children.Clear();
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 2, 0, 2) };
        t.SetResourceReference(StyleProperty, "Pp.Caption");
        _firewallList.Children.Add(t);
    }

    private async Task RefreshFirewallAsync()
    {
        try
        {
            var rules = await Task.Run(FirewallRules.List);
            RenderFirewall(rules);
        }
        catch (Exception ex)
        {
            Log.Error("Security", "lecture des règles de pare-feu", ex);
            SetFirewallMessage(L("Les règles du pare-feu n'ont pas pu être lues : {0}", ex.Message));
        }
    }

    private void RenderFirewall(List<TimonierFirewallRule> rules)
    {
        if (rules.Count == 0)
        {
            SetFirewallMessage(L("Aucune application n'est bloquée par Timonier pour le moment."));
            return;
        }
        _firewallList.Children.Clear();
        foreach (var group in rules.GroupBy(r => r.Name))
        {
            var first = group.First();
            var display = first.DisplayName;

            var icon = new TextBlock { Text = "", Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(StyleProperty, "Pp.Icon");
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextSecondary");

            var name = new TextBlock { Text = display, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(StyleProperty, "Pp.Body");
            var nameRow = new WrapPanel();
            nameRow.Children.Add(name);
            foreach (var dir in group.Select(r => r.Direction).Distinct().OrderByDescending(d => d))
                nameRow.Children.Add(Badge(dir == "Out" ? LC("direction", "Sortant") : LC("direction", "Entrant"), "Pp.NeutralBackground", "Pp.Neutral"));
            if (group.Any(r => !r.Enabled)) nameRow.Children.Add(Badge(L("Règle désactivée"), "Pp.WarningBackground", "Pp.Warning"));
            if (first.Legacy) nameRow.Children.Add(Badge(L("Créée sous le nom PC Pilot"), "Pp.NeutralBackground", "Pp.Neutral"));

            var path = new TextBlock { Text = first.Application ?? L("(programme inconnu)"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, ToolTip = first.Application };
            path.SetResourceReference(StyleProperty, "Pp.Caption");
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(nameRow);
            texts.Children.Add(path);

            var unblock = MakeButton(L("Débloquer"), "", "Pp.Button");
            unblock.VerticalAlignment = VerticalAlignment.Center;
            unblock.Margin = new Thickness(12, 0, 0, 0);
            unblock.Click += async (_, _) => await UnblockAsync(first.Name, display, unblock);

            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(icon);
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            Grid.SetColumn(unblock, 2);
            row.Children.Add(unblock);
            _firewallList.Children.Add(row);
        }
    }

    private async Task BlockProgramAsync()
    {
        if (_firewallBusy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L("Choisir le programme à bloquer"),
            Filter = L("Programmes (*.exe)") + "|*.exe",
            CheckFileExists = true,
            DereferenceLinks = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        string path;
        try { path = FirewallRules.ValidateExecutable(dialog.FileName); }
        catch (ValidationException ex)
        {
            AppHost.Toasts.Show(ex.Message, ToastKind.Warning);
            return;
        }
        var exe = System.IO.Path.GetFileName(path);
        if (!await AppHost.Dialogs.ConfirmAsync(L("Bloquer l'accès réseau"),
                L("« {0} » ne pourra plus se connecter à Internet ni au réseau local (connexions sortantes et entrantes), sur tous les types de réseau.\n\nEmplacement : {1}\n\nVous pourrez le débloquer à tout moment depuis cette page. Une autorisation administrateur sera demandée.", exe, path), L("Bloquer")))
            return;

        _firewallBusy = true;
        _blockButton.IsEnabled = false;
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(FirewallBlockAction.ActionId,
                new Dictionary<string, string> { ["path"] = path, ["inbound"] = "true" });
            AppHost.Toasts.ShowOutcome(outcome);
        }
        finally
        {
            _firewallBusy = false;
            _blockButton.IsEnabled = true;
            await RefreshFirewallAsync();
        }
    }

    private async Task UnblockAsync(string ruleName, string display, Button button)
    {
        if (_firewallBusy) return;
        if (!await AppHost.Dialogs.ConfirmAsync(L("Débloquer l'application"),
                L("« {0} » pourra de nouveau accéder au réseau. Seules les règles « Timonier » de cette application sont supprimées.", display),
                L("Débloquer")))
            return;
        _firewallBusy = true;
        button.IsEnabled = false;
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(FirewallUnblockAction.ActionId,
                new Dictionary<string, string> { ["name"] = ruleName });
            AppHost.Toasts.ShowOutcome(outcome);
        }
        finally
        {
            _firewallBusy = false;
            button.IsEnabled = true;
            await RefreshFirewallAsync();
        }
    }

    // ================================================================== Renforcement (construction par lots)

    private void BuildNextList(int index)
    {
        if (index >= SecurityTweaks.Groups.Length)
        {
            _listsBuilt = true;
            HandlePendingNavigation();
            return;
        }
        var group = SecurityTweaks.Groups[index];
        var tweaks = AppHost.Registry.TweaksIn(SecurityModule.Category).Where(t => t.Group == group).ToList();
        try
        {
            if (tweaks.Count > 0 && tweaks.All(t => t.Risk == RiskLevel.Advanced) && !AppHost.Settings.AdvancedMode)
            {
                _hardeningHost.Children.Add(PageScaffold.Section(group));
                var hint = new TextBlock
                {
                    Text = LP(tweaks.Count,
                        "{0} règle de Microsoft Defender (audit ou blocage) est réservée au mode avancé : activez-le dans les paramètres de Timonier pour l'afficher.",
                        "{0} règles de Microsoft Defender (audit ou blocage) sont réservées au mode avancé : activez-le dans les paramètres de Timonier pour les afficher."),
                    Margin = new Thickness(2, 0, 0, 0),
                };
                hint.SetResourceReference(StyleProperty, "Pp.Caption");
                _hardeningHost.Children.Add(hint);
            }
            else if (tweaks.Count > 0)
            {
                var list = new TweakListView(tweaks, showRecommendations: false);
                System.Windows.Automation.AutomationProperties.SetName(list, group);
                _lists.Add(list);
                _hardeningHost.Children.Add(list);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Security", "liste de réglages " + group, ex);
            _hardeningHost.Children.Add(PageScaffold.InfoBar(L("La section « {0} » n'a pas pu être affichée.", group), "", "Pp.InfoBar.Warning"));
        }
        HandlePendingNavigation();
        Dispatcher.InvokeAsync(() => BuildNextList(index + 1), DispatcherPriority.Background);
    }

    // ================================================================== Aides visuelles

    private static TextBlock TopSection(string text)
    {
        var t = PageScaffold.Section(text);
        t.FontSize = 18;
        t.Margin = new Thickness(2, 30, 0, 10);
        return t;
    }

    private static Border Divider(Thickness? margin = null)
    {
        var d = new Border { Height = 1, Margin = margin ?? new Thickness(0) };
        d.SetResourceReference(Border.BackgroundProperty, "Pp.Divider");
        return d;
    }

    private static Border Badge(string text, string background, string foreground)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "Pp.BadgeText");
        t.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        var b = new Border { Child = t, Margin = new Thickness(8, 0, 0, 0) };
        b.SetResourceReference(StyleProperty, "Pp.Badge");
        b.SetResourceReference(Border.BackgroundProperty, background);
        return b;
    }

    private static Button MakeButton(string text, string glyph, string style)
    {
        var icon = new TextBlock { Text = glyph, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) };
        icon.SetResourceReference(StyleProperty, "Pp.Icon");
        icon.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Foreground))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1),
        });
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content };
        button.SetResourceReference(StyleProperty, style);
        return button;
    }
}
