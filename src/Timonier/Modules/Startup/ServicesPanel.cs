using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using static Timonier.Modules.Startup.StartupUi;

namespace Timonier.Modules.Startup;

/// <summary>Onglet « Services » : type de démarrage, démarrer/arrêter, filtres et recherche.</summary>
internal sealed class ServicesPanel : StackPanel, IStartupPanel
{
    private static readonly ServiceStartKind[] Kinds =
        [ServiceStartKind.Automatic, ServiceStartKind.AutomaticDelayed, ServiceStartKind.Manual, ServiceStartKind.Disabled];

    private readonly TextBlock _summary = Caption("");
    private readonly SegmentedBar _filter = new(compact: true);
    private readonly Button _refresh;
    private readonly ContentControl _body = new();
    private readonly PagedList<ServiceItem> _list;
    private List<ServiceItem> _items = [];
    private string _query = "";
    private bool _loading;

    public bool IsDataLoaded { get; private set; }
    public event EventHandler<string>? CountChanged;

    public ServicesPanel()
    {
        Children.Add(PageScaffold.InfoBar(
            L("Les services démarrent en arrière-plan, souvent avant l'ouverture de session. Passer un service en « Manuel » le laisse démarrer seulement quand une application en a besoin ; « Désactivé » l'empêche totalement de démarrer. Les services indispensables à Windows sont protégés. Chaque changement est annulable depuis le journal, mais une mise à jour majeure de Windows peut rétablir la configuration d'origine de certains services Microsoft."), ""));

        var search = SearchBox(L("Rechercher un service (nom, description, éditeur)"), q => { _query = q; Render(); });
        _filter.Add(L("Tous"));
        _filter.Add(L("Non-Microsoft"));
        _filter.Add(L("En cours"));
        _filter.Add(L("Désactivés"));
        _filter.Select(0, notify: false);
        _filter.SelectionChanged += (_, _) => Render();
        _refresh = Button(L("Actualiser"), "", "Pp.Button", async (_, _) => await ReloadAsync());
        var console = Button(L("Console Services"), "", "Pp.SubtleButton", (_, _) => OpenConsole("services.msc"));
        Children.Add(Toolbar(search, _filter, _refresh, console));

        _summary.Margin = new Thickness(2, 0, 0, 10);
        Children.Add(_summary);
        _list = new PagedList<ServiceItem>(RowHost, 40);
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
            _body.Content = StateCard("", L("Lecture des services…"), L("La première lecture peut prendre quelques secondes."), busy: true);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _items = await Task.Run(ServiceInventory.Load);
            Log.Info("Startup", $"{_items.Count} services lus en {sw.ElapsedMilliseconds} ms");
            IsDataLoaded = true;
            Render();
        }
        catch (Exception ex)
        {
            Log.Error("Startup", "énumération des services", ex);
            _body.Content = StateCard("", L("Impossible de lire la liste des services"), ex.Message);
        }
        finally
        {
            _loading = false;
            _refresh.IsEnabled = true;
        }
    }

    private void Render()
    {
        IEnumerable<ServiceItem> view = _items;
        view = _filter.SelectedIndex switch
        {
            1 => view.Where(s => !s.IsMicrosoft),
            2 => view.Where(s => s.IsRunning),
            3 => view.Where(s => s.IsDisabled),
            _ => view,
        };
        if (_query.Length > 0)
            view = view.Where(s => s.DisplayName.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                                   || s.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)
                                   || s.Description.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                                   || (s.Company?.Contains(_query, StringComparison.CurrentCultureIgnoreCase) ?? false));
        var list = view.ToList();

        _summary.Text = SummaryText(list.Count != _items.Count ? list.Count : null);
        CountChanged?.Invoke(this, _items.Count.ToString(Culture));

        if (list.Count == 0)
        {
            _body.Content = StateCard("", L("Aucun service ne correspond"), L("Modifiez la recherche ou le filtre."));
            return;
        }
        _list.SetItems(list);
        _body.Content = _list;
    }

    private FrameworkElement RowHost(ServiceItem item)
    {
        var host = new ContentControl { Focusable = false };
        host.Content = Row(item, host);
        return host;
    }

    private Border Row(ServiceItem item, ContentControl host)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = item.IsMicrosoft ? GlyphTile(StartupModule.ServicesGlyph) : GlyphTile(StartupModule.ServicesGlyph, "Pp.InfoBackground", "Pp.Info");
        tile.Margin = new Thickness(0, 0, 14, 0);
        if (item.IsDisabled) tile.Opacity = 0.5;
        grid.Children.Add(tile);

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleLine = new WrapPanel();
        titleLine.Children.Add(new TextBlock { Text = item.DisplayName, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 2) }.Styled("Pp.Body"));
        titleLine.Children.Add(item.IsRunning ? Badge(L("En cours"), "Success") : Badge(item.StatusLabel));
        titleLine.Children.Add(item.IsMicrosoft ? Badge(L("Service Microsoft")) : Badge(item.Company is { Length: > 0 } c ? LC("publisher", "Tiers") + " · " + Short(c) : LC("publisher", "Tiers"), "Info"));
        if (item.IsProtected) titleLine.Children.Add(Badge(L("Protégé"), "Warning", ""));
        if (item.IsPerUserInstance) titleLine.Children.Add(Badge(L("Par utilisateur"), "Neutral", ""));
        body.Children.Add(titleLine);

        var meta = new List<string> { item.Name };
        if (item.AccountLabel.Length > 0) meta.Add(L("compte : {0}", item.AccountLabel));
        if (item.IsPerUserInstance) meta.Add(L("modèle : {0}", item.ConfigName));
        body.Children.Add(Caption(string.Join(" · ", meta)));
        if (item.Description.Length > 0)
        {
            var desc = Caption(item.Description, tertiary: true);
            desc.MaxHeight = 34;
            desc.TextTrimming = TextTrimming.WordEllipsis;
            desc.ToolTip = item.Description;
            desc.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(desc);
        }
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var busy = new ProgressBar { IsIndeterminate = true, Width = 40, Height = 3, Margin = new Thickness(0, 0, 10, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(busy);

        var combo = new ComboBox { Width = 196, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        System.Windows.Automation.AutomationProperties.SetName(combo, L("Type de démarrage de {0}", item.DisplayName));
        if (item.Start is { } start && Array.IndexOf(Kinds, start) >= 0)
        {
            foreach (var k in Kinds) combo.Items.Add(ServiceInventory.StartLabel(k));
            combo.SelectedIndex = Array.IndexOf(Kinds, start);
        }
        else
        {
            combo.Items.Add(ServiceInventory.StartLabel(item.Start));
            combo.SelectedIndex = 0;
        }
        combo.IsEnabled = item.CanConfigure;
        combo.ToolTip = item.IsProtected
            ? L("Service protégé : indispensable au démarrage, au réseau, aux mises à jour ou à la sécurité de Windows.")
            : item.IsPerUserInstance ? L("S'applique au modèle « {0} », pour tous les comptes (droits administrateur requis).", item.ConfigName)
            : L("Type de démarrage (droits administrateur requis)");
        var suppress = false;
        combo.SelectionChanged += async (_, _) =>
        {
            if (suppress || combo.SelectedIndex < 0 || !item.CanConfigure) return;
            var target = Kinds[combo.SelectedIndex];
            if (target == item.Start) return;
            combo.IsEnabled = false;
            busy.Visibility = Visibility.Visible;
            var changed = await SetStartAsync(item, target);
            if (!changed)
            {
                suppress = true;
                combo.SelectedIndex = item.Start is { } s ? Array.IndexOf(Kinds, s) : -1;
                suppress = false;
                combo.IsEnabled = item.CanConfigure;
                busy.Visibility = Visibility.Collapsed;
                return;
            }
            await RefreshRowAsync(item, host);
        };
        actions.Children.Add(combo);

        if (!item.IsProtected)
        {
            if (item.IsRunning)
            {
                actions.Children.Add(IconButton("", L("Arrêter le service"), async (s, _) => await ControlAsync(item, "stop", host, (Button)s!, busy)));
                actions.Children.Add(IconButton("", L("Redémarrer le service"), async (s, _) => await ControlAsync(item, "restart", host, (Button)s!, busy)));
            }
            else
            {
                var play = IconButton("", item.IsDisabled ? L("Service désactivé : changez d'abord son type de démarrage") : L("Démarrer le service"),
                    async (s, _) => await ControlAsync(item, "start", host, (Button)s!, busy));
                play.IsEnabled = !item.IsDisabled;
                actions.Children.Add(play);
                actions.Children.Add(new Border { Width = 34 });
            }
        }
        else
        {
            actions.Children.Add(new Border { Width = 68 });
        }
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        return new Border { Child = grid }.Styled("Pp.Card");
    }

    private static string Short(string company) => company.Length > 28 ? company[..26] + "…" : company;

    private static async Task<bool> SetStartAsync(ServiceItem item, ServiceStartKind target)
    {
        var label = ServiceInventory.StartLabel(target);
        var consequence = target switch
        {
            ServiceStartKind.Disabled => L("Le service sera arrêté et ne pourra plus démarrer, même si une application ou Windows en a besoin."),
            ServiceStartKind.Manual => L("Le service ne démarrera plus automatiquement : Windows ou une application le lanceront à la demande."),
            ServiceStartKind.AutomaticDelayed => L("Le service démarrera automatiquement, environ deux minutes après l'ouverture de session."),
            _ => L("Le service démarrera automatiquement avec Windows."),
        };
        if (item.IsMicrosoft || target == ServiceStartKind.Disabled)
        {
            var message = string.Join("\n\n", new[]
            {
                $"{item.DisplayName} ({item.Name})",
                item.Description.Length > 0 ? item.Description : null,
                L("Nouveau type de démarrage : {0}. {1}", label, consequence),
                item.IsPerUserInstance ? L("Ce service existe pour chaque compte : le réglage s'applique au modèle « {0} », donc à tous les utilisateurs.", item.ConfigName) : null,
                item.IsMicrosoft ? L("Ce service fait partie de Windows : le modifier peut désactiver une fonction ou provoquer des erreurs. Ne le faites que si vous savez à quoi il sert.") : null,
                L("La modification est annulable depuis le journal."),
            }.Where(p => p is not null));
            var ok = await AppHost.Dialogs.ConfirmAsync(
                item.IsMicrosoft ? L("Modifier un service de Windows ?") : L("Désactiver ce service ?"),
                message,
                L("Modifier"), L("Annuler"), danger: target == ServiceStartKind.Disabled);
            if (!ok) return false;
        }
        var outcome = await StartupPage.RunAsync("startup.service.setstart",
            new Dictionary<string, string> { ["name"] = item.Name, ["start"] = ServiceInventory.StartParam(target) });
        AppHost.Toasts.ShowOutcome(outcome);
        return outcome.Success;
    }

    private async Task ControlAsync(ServiceItem item, string command, ContentControl host, Button button, ProgressBar busy)
    {
        if (command != "start" && item.IsMicrosoft)
        {
            var ok = await AppHost.Dialogs.ConfirmAsync(command == "stop" ? L("Arrêter ce service de Windows ?") : L("Redémarrer ce service de Windows ?"),
                string.Join("\n\n", new[]
                {
                    $"{item.DisplayName} ({item.Name})",
                    item.Description.Length > 0 ? item.Description : null,
                    L("Les fonctions qui en dépendent cesseront de fonctionner jusqu'à son redémarrage. Les services qui dépendent de lui seront aussi arrêtés."),
                }.Where(p => p is not null)),
                command == "stop" ? L("Arrêter") : L("Redémarrer"), L("Annuler"), danger: command == "stop");
            if (!ok) return;
        }
        button.IsEnabled = false;
        busy.Visibility = Visibility.Visible;
        var outcome = await StartupPage.RunAsync("startup.service.control",
            new Dictionary<string, string> { ["name"] = item.Name, ["command"] = command });
        AppHost.Toasts.ShowOutcome(outcome);
        await RefreshRowAsync(item, host);
    }

    private async Task RefreshRowAsync(ServiceItem item, ContentControl host)
    {
        var (status, start) = await Task.Run(() => ServiceInventory.Refresh(item));
        item.Status = status;
        item.Start = start;
        host.Content = Row(item, host);
        _summary.Text = SummaryText(null);
    }

    /// <summary>« N services · N en cours · N non-Microsoft [· N affichés] ».</summary>
    private string SummaryText(int? shown) => string.Join(" · ", new[]
    {
        LP(_items.Count, "{0} service", "{0} services"),
        LP(_items.Count(s => s.IsRunning), "{0} en cours", "{0} en cours"),
        LP(_items.Count(s => !s.IsMicrosoft), "{0} non-Microsoft", "{0} non-Microsoft"),
        shown is { } n ? LP(n, "{0} affiché", "{0} affichés") : null,
    }.Where(p => p is not null));
}
