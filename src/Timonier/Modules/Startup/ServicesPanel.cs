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
            L("Services start in the background, often before sign-in. Setting a service to “Manual” lets it start only when an app needs it; “Disabled” prevents it from starting at all. Services essential to Windows are protected. Every change can be undone from History, but a major Windows update may restore the original configuration of some Microsoft services."), ""));

        var search = SearchBox(L("Search for a service (name, description, publisher)"), q => { _query = q; Render(); });
        _filter.Add(LC("plural (services)", "All"));
        _filter.Add(L("Non-Microsoft"));
        _filter.Add(L("Running"));
        _filter.Add(LC("plural", "Disabled"));
        _filter.Select(0, notify: false);
        _filter.SelectionChanged += (_, _) => Render();
        _refresh = Button(L("Refresh"), "", "Pp.Button", async (_, _) => await ReloadAsync());
        var console = Button(L("Services console"), "", "Pp.SubtleButton", (_, _) => OpenConsole("services.msc"));
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
            _body.Content = StateCard("", L("Reading services…"), L("The first read may take a few seconds."), busy: true);
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
            _body.Content = StateCard("", L("Couldn't read the list of services"), ex.Message);
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
            _body.Content = StateCard("", L("No matching services"), L("Change the search or filter."));
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
        titleLine.Children.Add(item.IsRunning ? Badge(L("Running"), "Success") : Badge(item.StatusLabel));
        titleLine.Children.Add(item.IsMicrosoft ? Badge(L("Microsoft service")) : Badge(item.Company is { Length: > 0 } c ? LC("publisher", "Third-party") + " · " + Short(c) : LC("publisher", "Third-party"), "Info"));
        if (item.IsProtected) titleLine.Children.Add(Badge(L("Protected"), "Warning", ""));
        if (item.IsPerUserInstance) titleLine.Children.Add(Badge(L("Per user"), "Neutral", ""));
        body.Children.Add(titleLine);

        var meta = new List<string> { item.Name };
        if (item.AccountLabel.Length > 0) meta.Add(L("account: {0}", item.AccountLabel));
        if (item.IsPerUserInstance) meta.Add(L("template: {0}", item.ConfigName));
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
        System.Windows.Automation.AutomationProperties.SetName(combo, L("Startup type of {0}", item.DisplayName));
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
            ? L("Protected service: essential for Windows startup, networking, updates or security.")
            : item.IsPerUserInstance ? L("Applies to the “{0}” template, for all accounts (administrator rights required).", item.ConfigName)
            : L("Startup type (administrator rights required)");
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
                actions.Children.Add(IconButton("", L("Stop service"), async (s, _) => await ControlAsync(item, "stop", host, (Button)s!, busy)));
                actions.Children.Add(IconButton("", L("Restart service"), async (s, _) => await ControlAsync(item, "restart", host, (Button)s!, busy)));
            }
            else
            {
                var play = IconButton("", item.IsDisabled ? L("Service disabled: change its startup type first") : L("Start service"),
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
            ServiceStartKind.Disabled => L("The service will be stopped and won't be able to start anymore, even if an app or Windows needs it."),
            ServiceStartKind.Manual => L("The service will no longer start automatically: Windows or an app will start it on demand."),
            ServiceStartKind.AutomaticDelayed => L("The service will start automatically, about two minutes after sign-in."),
            _ => L("The service will start automatically with Windows."),
        };
        if (item.IsMicrosoft || target == ServiceStartKind.Disabled)
        {
            var message = string.Join("\n\n", new[]
            {
                $"{item.DisplayName} ({item.Name})",
                item.Description.Length > 0 ? item.Description : null,
                L("New startup type: {0}. {1}", label, consequence),
                item.IsPerUserInstance ? L("This service exists for each account: the setting applies to the “{0}” template, and therefore to all users.", item.ConfigName) : null,
                item.IsMicrosoft ? L("This service is part of Windows: changing it may turn off a feature or cause errors. Only do it if you know what it's for.") : null,
                L("You can undo this change from History."),
            }.Where(p => p is not null));
            var ok = await AppHost.Dialogs.ConfirmAsync(
                item.IsMicrosoft ? L("Change a Windows service?") : L("Disable this service?"),
                message,
                L("Change"), L("Undo"), danger: target == ServiceStartKind.Disabled);
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
            var ok = await AppHost.Dialogs.ConfirmAsync(command == "stop" ? L("Stop this Windows service?") : L("Restart this Windows service?"),
                string.Join("\n\n", new[]
                {
                    $"{item.DisplayName} ({item.Name})",
                    item.Description.Length > 0 ? item.Description : null,
                    L("Features that depend on it will stop working until it restarts. Services that depend on it will also be stopped."),
                }.Where(p => p is not null)),
                command == "stop" ? L("Stop") : L("Restart"), L("Undo"), danger: command == "stop");
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
        LP(_items.Count(s => s.IsRunning), "{0} running", "{0} running"),
        LP(_items.Count(s => !s.IsMicrosoft), "{0} non-Microsoft", "{0} non-Microsoft"),
        shown is { } n ? LP(n, "{0} shown", "{0} shown") : null,
    }.Where(p => p is not null));
}
