using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using static Timonier.Modules.Startup.StartupUi;

namespace Timonier.Modules.Startup;

/// <summary>Onglet « Tâches planifiées » : tâches des applications (par défaut), déclencheurs, activation.</summary>
internal sealed class TasksPanel : StackPanel, IStartupPanel
{
    private readonly TextBlock _summary = Caption("");
    private readonly SegmentedBar _filter = new(compact: true);
    private readonly Button _refresh;
    private readonly ContentControl _body = new();
    private readonly PagedList<TaskItem> _list;
    private List<TaskItem> _items = [];
    private string _query = "";
    private bool _loading;
    private bool _microsoftPending;

    public bool IsDataLoaded { get; private set; }
    public event EventHandler<string>? CountChanged;

    public TasksPanel()
    {
        Children.Add(PageScaffold.InfoBar(
            L("Beaucoup d'applications installent des tâches planifiées (mises à jour, vérifications, lancement à l'ouverture de session). Par défaut, seules les tâches hors du dossier Microsoft sont affichées. Les tâches système de Windows restent en lecture seule ; désactiver une tâche est annulable depuis le journal."), ""));

        var search = SearchBox(L("Rechercher une tâche, un auteur, une commande"), q => { _query = q; Render(); });
        _filter.Add(L("Applications"));
        _filter.Add(L("Au démarrage"));
        _filter.Add(L("Désactivées"));
        _filter.Add(L("Toutes"));
        _filter.Select(0, notify: false);
        _filter.SelectionChanged += (_, _) => Render();
        _refresh = Button(L("Actualiser"), "", "Pp.Button", async (_, _) => await ReloadAsync());
        var console = Button(L("Planificateur de tâches"), "", "Pp.SubtleButton", (_, _) => OpenConsole("taskschd.msc"));
        Children.Add(Toolbar(search, _filter, _refresh, console));

        _summary.Margin = new Thickness(2, 0, 0, 10);
        Children.Add(_summary);
        _list = new PagedList<TaskItem>(RowHost, 30);
        Children.Add(_body);
    }

    public async Task EnsureLoadedAsync()
    {
        if (!IsDataLoaded) await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (_loading) return;
        _loading = true;
        _refresh.IsEnabled = false;
        if (!IsDataLoaded)
            _body.Content = StateCard("", L("Lecture des tâches planifiées…"), L("La première lecture peut prendre quelques secondes."), busy: true);
        try
        {
            // 1) Tâches des applications (vue par défaut, rapide), 2) dossier \Microsoft en complément.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _microsoftPending = true;
            _items = await Task.Run(() => TaskInventory.Load(microsoft: false));
            IsDataLoaded = true;
            Render();
            var ms = await Task.Run(() => TaskInventory.Load(microsoft: true));
            _items = [.. _items.Concat(ms).OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase)];
            _microsoftPending = false;
            Log.Info("Startup", $"{_items.Count} tâches planifiées lues en {sw.ElapsedMilliseconds} ms");
            // La vue par défaut (applications) ne change pas : on évite de reconstruire les cartes.
            Render(rebuildList: _filter.SelectedIndex != 0 || _query.Length > 0);
        }
        catch (Exception ex)
        {
            Log.Error("Startup", "énumération des tâches planifiées", ex);
            _body.Content = StateCard("", L("Impossible de lire les tâches planifiées"), ex.Message);
        }
        finally
        {
            _microsoftPending = false;
            _loading = false;
            _refresh.IsEnabled = true;
        }
    }

    private void Render(bool rebuildList = true)
    {
        IEnumerable<TaskItem> view = _items;
        view = _filter.SelectedIndex switch
        {
            0 => view.Where(t => !t.IsMicrosoftFolder),
            1 => view.Where(t => t.RunsAtStartup),
            2 => view.Where(t => !t.Enabled),
            _ => view,
        };
        if (_query.Length > 0)
            view = view.Where(t => t.Path.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                                   || (t.Author?.Contains(_query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                                   || (t.Description?.Contains(_query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                                   || t.Actions.Contains(_query, StringComparison.CurrentCultureIgnoreCase));
        // Tâches modifiables et actives d'abord.
        var list = view.OrderByDescending(t => t.CanToggle).ThenBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();

        var apps = _items.Count(t => !t.IsMicrosoftFolder);
        var atStartup = _items.Count(t => t.RunsAtStartup && t.Enabled && !t.IsMicrosoftFolder);
        _summary.Text = string.Join(" · ", (_microsoftPending
            ? new[]
            {
                LP(apps, "{0} tâche d'application", "{0} tâches d'applications"),
                LP(atStartup, "{0} lancée au démarrage ou à l'ouverture de session", "{0} lancées au démarrage ou à l'ouverture de session"),
                L("lecture des tâches Microsoft en cours…"),
            }
            : new[]
            {
                LP(_items.Count, "{0} tâche lisible", "{0} tâches lisibles"),
                LP(apps, "{0} d'applications", "{0} d'applications"),
                LP(atStartup, "{0} d'application lancée au démarrage ou à l'ouverture de session", "{0} d'applications lancées au démarrage ou à l'ouverture de session"),
                list.Count != _items.Count ? LP(list.Count, "{0} affichée", "{0} affichées") : null,
            }).Where(p => p is not null));
        CountChanged?.Invoke(this, apps.ToString(Culture));
        if (!rebuildList && _body.Content == _list) return;

        if (list.Count == 0)
        {
            _body.Content = _items.Count == 0
                ? StateCard("", L("Aucune tâche planifiée lisible"), L("Les tâches d'autres comptes ne sont pas visibles sans droits administrateur."))
                : StateCard("", L("Aucune tâche ne correspond"), L("Modifiez la recherche ou le filtre."));
            return;
        }
        _list.SetItems(list);
        _body.Content = _list;
    }

    private FrameworkElement RowHost(TaskItem item)
    {
        var host = new ContentControl { Focusable = false };
        host.Content = Row(item, host);
        return host;
    }

    private Border Row(TaskItem item, ContentControl host)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = item.IsMicrosoftFolder ? GlyphTile(StartupModule.TasksGlyph) : GlyphTile(StartupModule.TasksGlyph, "Pp.InfoBackground", "Pp.Info");
        tile.Margin = new Thickness(0, 0, 14, 0);
        if (!item.Enabled) tile.Opacity = 0.5;
        grid.Children.Add(tile);

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleLine = new WrapPanel();
        titleLine.Children.Add(new TextBlock { Text = DisplayName(item.Name), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 2), ToolTip = item.Path }
            .Styled("Pp.Body"));
        if (!item.Enabled) titleLine.Children.Add(Badge(L("Désactivée")));
        else if (item.IsRunning) titleLine.Children.Add(Badge(L("En cours"), "Success"));
        if (item.RunsAtStartup) titleLine.Children.Add(Badge(L("Au démarrage"), "Accent", ""));
        if (item.IsWindowsSystem) titleLine.Children.Add(Badge(L("Tâche système"), "Neutral", ""));
        if (item.Hidden) titleLine.Children.Add(Badge(L("Masquée")));
        body.Children.Add(titleLine);

        var meta = new List<string> { item.Folder.TrimEnd('\\').Length == 0 ? L("Dossier racine") : item.Folder.TrimEnd('\\') };
        if (item.Author is { Length: > 0 } author) meta.Add(L("auteur : {0}", author));
        meta.Add(item.Triggers.Length > 0 ? item.Triggers : L("Déclencheurs non lisibles"));
        body.Children.Add(Caption(string.Join(" · ", meta)));

        var runs = new List<string>();
        if (item.LastRun is { } last)
            runs.Add(TaskInventory.ResultLabel(item.LastResult) is { } r
                ? L("Dernière exécution : {0} ({1})", Format.Date(last), r)
                : L("Dernière exécution : {0}", Format.Date(last)));
        else runs.Add(L("Jamais exécutée"));
        if (item.Enabled && item.NextRun is { } next) runs.Add(L("prochaine : {0}", Format.Date(next)));
        body.Children.Add(Caption(string.Join(" · ", runs), tertiary: true));
        if (item.Actions.Length > 0)
        {
            var act = Caption(item.Actions, tertiary: true, trim: true);
            act.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(act);
        }
        if (item.Description is { Length: > 0 } d) grid.ToolTip = d;
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var busy = new ProgressBar { IsIndeterminate = true, Width = 40, Height = 3, Margin = new Thickness(0, 0, 10, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(busy);
        var state = new TextBlock { Text = item.Enabled ? L("Activée") : L("Désactivée"), Width = 74, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }
            .Styled("Pp.Body");
        var toggle = new CheckBox { IsChecked = item.Enabled, IsEnabled = item.CanToggle, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.ToggleSwitch");
        System.Windows.Automation.AutomationProperties.SetName(toggle, L("Activer la tâche {0}", item.Name));
        toggle.ToolTip = item.CanToggle
            ? L("Activer ou désactiver la tâche (droits administrateur requis)")
            : L("Tâche système de Windows : non modifiable ici.");
        ToolTipService.SetShowOnDisabled(toggle, true);
        toggle.Click += async (_, _) => await ToggleAsync(item, toggle, host, busy);
        actions.Children.Add(state);
        actions.Children.Add(toggle);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        return new Border { Child = grid }.Styled("Pp.Card");
    }

    /// <summary>« OneDrive Reporting Task-S-1-5-21-… » → « OneDrive Reporting Task » (le SID identifie le compte).</summary>
    private static string DisplayName(string name)
    {
        var i = name.IndexOf("-S-1-5-", StringComparison.Ordinal);
        return i > 0 ? name[..i] : name;
    }

    private async Task ToggleAsync(TaskItem item, CheckBox toggle, ContentControl host, ProgressBar busy)
    {
        var desired = toggle.IsChecked == true;
        if (!desired && item.IsMicrosoftFolder)
        {
            var ok = await AppHost.Dialogs.ConfirmAsync(L("Désactiver cette tâche Microsoft ?"),
                string.Join("\n\n", new[]
                {
                    item.Path,
                    item.Description is { Length: > 0 } d ? d : null,
                    L("Cette tâche appartient à un produit Microsoft (Office, Edge, OneDrive…) : la désactiver peut empêcher ses mises à jour ou une fonction associée. La modification est annulable depuis le journal."),
                }.Where(p => p is not null)), L("Désactiver"), L("Annuler"));
            if (!ok) { toggle.IsChecked = item.Enabled; return; }
        }
        toggle.IsEnabled = false;
        busy.Visibility = Visibility.Visible;
        var outcome = await StartupPage.RunAsync("startup.task.setenabled",
            new Dictionary<string, string> { ["path"] = item.Path, ["enabled"] = desired ? "true" : "false" });
        AppHost.Toasts.ShowOutcome(outcome);
        if (outcome.Success)
        {
            item.Enabled = desired;
            item.State = desired ? L("Prête") : L("Désactivée");
        }
        host.Content = Row(item, host);
    }
}
