using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Engine;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using Timonier.UI.Services;

namespace Timonier.Modules.Network;

/// <summary>État Wi-Fi de la connexion en cours (nom du réseau éventuellement masqué par Windows).</summary>
internal sealed record WifiStatus(WlanInterface Interface, WlanConnection? Connection, uint Error)
{
    /// <summary>Windows refuse le SSID aux applications de bureau sans autorisation « Localisation » (Windows 11 24H2+).</summary>
    public bool NeedsLocation => Error == WlanApi.ErrorAccessDenied;
}

/// <summary>Instantané de l'état réseau, lu hors du thread UI, sans aucune requête sur le réseau.</summary>
internal sealed class NetworkSnapshot
{
    public List<AdapterInfo> Adapters { get; init; } = [];
    public ConnectivityInfo Connectivity { get; init; } = new(ConnectivityLevel.Unknown, null, false, false);
    public WifiStatus? Wifi { get; init; }
    public bool HasWifiAdapter { get; init; }

    public AdapterInfo? Primary => NetworkInfo.Primary(Adapters);

    public static NetworkSnapshot Load()
    {
        var adapters = NetworkInfo.ReadAdapters(includeHidden: false);
        var connectivity = NetworkInfo.ReadConnectivity();
        WifiStatus? wifi = null;
        var hasWifi = false;
        var ifaces = WlanApi.Interfaces();
        if (ifaces.Ok && ifaces.Value!.Count > 0)
        {
            hasWifi = true;
            var connected = ifaces.Value.FirstOrDefault(i => i.IsConnected);
            if (connected is not null)
            {
                var conn = WlanApi.CurrentConnection(connected.Id);
                wifi = new WifiStatus(connected, conn.Value, conn.Error);
            }
        }
        return new NetworkSnapshot { Adapters = adapters, Connectivity = connectivity, Wifi = wifi, HasWifiAdapter = hasWifi };
    }

    /// <summary>Nom du réseau Wi-Fi connecté s'il est lisible (SSID, sinon nom du profil connu de Windows).</summary>
    public string? WifiName => Wifi?.Connection?.Ssid ?? Wifi?.Connection?.ProfileName.NullIfEmpty() ??
                               (Connectivity.IsWlan ? Connectivity.ProfileName : null);
}

internal static class StringExtensions
{
    public static string? NullIfEmpty(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>
/// Page « Réseau » : état de la connexion, cartes réseau, DNS (familial, chiffré), fichier hosts, outils de dépannage,
/// réseaux Wi-Fi enregistrés et réglages. Chaque onglet est construit à sa première ouverture. L'état est relu à
/// l'ouverture de la page, sur demande et lors des changements d'adresse signalés par Windows (événements, pas de minuteur).
/// </summary>
public sealed class NetworkPage : UserControl, INavigationAware
{
    internal const string TabConnections = "connections";
    internal const string TabDns = "dns";
    internal const string TabHosts = "hosts";
    internal const string TabTools = "tools";
    internal const string TabWifi = "wifi";
    internal const string TabSettings = "settings";

    private sealed record TabInfo(string Key, string Title, string Glyph, Button Button, TextBlock Label, Border Underline);

    private readonly ScrollViewer _scroll;
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly List<TabInfo> _tabs = [];
    private readonly Dictionary<string, FrameworkElement> _panels = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromSeconds(1.5) };

    // Carte d'état
    private readonly Border _statusTile;
    private readonly TextBlock _statusGlyph;
    private readonly TextBlock _statusTitle = NetUi.Text(L("Reading network status…"), "Pp.Body");
    private readonly TextBlock _statusLine1 = NetUi.Text("", "Pp.Caption");
    private readonly TextBlock _statusLine2 = NetUi.Text("", "Pp.Caption");
    private readonly WrapPanel _statusBadges = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _refreshButton;
    private readonly ProgressBar _refreshBusy = NetUi.Busy(60);

    private string _currentTab = TabConnections;
    private bool _loading, _subscribed, _firstLoad = true;
    private string? _pendingHighlight;

    internal NetworkSnapshot? Snapshot { get; private set; }
    internal event EventHandler? SnapshotChanged;

    public NetworkPage()
    {
        Focusable = false;
        var stack = new StackPanel().Styled("Pp.PageStack");
        _scroll = new ScrollViewer { Content = stack }.Styled("Pp.PageScroll");
        Content = _scroll;

        stack.Children.Add(new PageHeader
        {
            Title = L("Network"),
            Subtitle = L("Connections, family or encrypted DNS, site blocking, troubleshooting and saved Wi-Fi networks. Timonier doesn't contact any server."),
            Glyph = "",
        });

        // --- Carte d'état
        _statusGlyph = NetUi.Icon("", 22, "Pp.AccentText");
        _statusGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        _statusTile = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(12), Child = _statusGlyph, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top }
            .Themed(Border.BackgroundProperty, "Pp.AccentSubtle");
        _statusTitle.FontSize = 18;
        _statusTitle.FontWeight = FontWeights.SemiBold;
        _statusLine1.Margin = new Thickness(0, 4, 0, 0);
        _statusLine2.Margin = new Thickness(0, 2, 0, 0);
        _refreshButton = NetUi.Button(L("Refresh"), "", "Pp.Button", async (_, _) => await RefreshAsync());
        var right = NetUi.Row(_refreshBusy, _refreshButton);
        right.VerticalAlignment = VerticalAlignment.Top;
        _refreshButton.Margin = new Thickness(12, 0, 0, 0);

        var statusText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        statusText.Children.Add(_statusTitle);
        statusText.Children.Add(_statusLine1);
        statusText.Children.Add(_statusLine2);
        statusText.Children.Add(_statusBadges);
        var statusDock = new DockPanel();
        DockPanel.SetDock(_statusTile, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        statusDock.Children.Add(_statusTile);
        statusDock.Children.Add(right);
        statusDock.Children.Add(statusText);
        var statusCard = NetUi.Card(statusDock, new Thickness(0, 0, 0, 16));
        statusCard.Padding = new Thickness(18, 16, 18, 16);
        stack.Children.Add(statusCard);

        // --- Onglets
        var tabBar = new WrapPanel();
        AddTab(tabBar, TabConnections, L("Connections"), "");
        AddTab(tabBar, TabDns, L("DNS"), "");
        AddTab(tabBar, TabHosts, L("Hosts file"), "");
        AddTab(tabBar, TabTools, L("Tools"), "");
        AddTab(tabBar, TabWifi, L("Saved Wi-Fi"), "");
        AddTab(tabBar, TabSettings, L("Settings"), "");
        stack.Children.Add(tabBar);
        stack.Children.Add(NetUi.Divider(new Thickness(0, 0, 0, 16)));
        stack.Children.Add(_host);

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RefreshAsync();
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        // Aide au développement : le mode capture (hors écran) peut ouvrir un onglet précis.
        SelectTab(Environment.GetEnvironmentVariable("TMN_CAPTURE_TAB") is { Length: > 0 } devTab ? devTab : TabConnections);
    }

    // ================================================================== Cycle de vie

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            _subscribed = true;
        }
        if (_firstLoad || Snapshot is null) { _firstLoad = false; await RefreshAsync(); }
        else await RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            _subscribed = false;
        }
        _debounce.Stop();
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() => { _debounce.Stop(); _debounce.Start(); });
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => OnNetworkChanged(sender, e);

    /// <summary>Relit l'état du réseau (hors du thread UI) puis notifie les onglets.</summary>
    internal async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        _refreshButton.IsEnabled = false;
        _refreshBusy.Visibility = Visibility.Visible;
        try
        {
            Snapshot = await Task.Run(NetworkSnapshot.Load);
            UpdateStatusCard(Snapshot);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error("Network", "lecture de l'état réseau", ex);
            _statusTitle.Text = L("Network status unreadable");
            _statusLine1.Text = ex.Message;
        }
        finally
        {
            _loading = false;
            _refreshButton.IsEnabled = true;
            _refreshBusy.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateStatusCard(NetworkSnapshot s)
    {
        var primary = s.Primary;
        var level = s.Connectivity.Level;
        if (level == ConnectivityLevel.Unknown)
            level = primary is null ? ConnectivityLevel.None : primary.HasGateway ? ConnectivityLevel.Internet : ConnectivityLevel.LocalAccess;

        var (glyph, bg, fg) = level switch
        {
            ConnectivityLevel.Internet => (primary?.Kind == AdapterKind.Wifi ? "" : "", "Pp.SuccessBackground", "Pp.Success"),
            ConnectivityLevel.ConstrainedInternet or ConnectivityLevel.LocalAccess => ("", "Pp.WarningBackground", "Pp.Warning"),
            _ => ("", "Pp.DangerBackground", "Pp.Danger"),
        };
        _statusGlyph.Text = glyph;
        _statusGlyph.SetResourceReference(TextBlock.ForegroundProperty, fg);
        _statusTile.SetResourceReference(Border.BackgroundProperty, bg);
        _statusTitle.Text = NetworkInfo.ConnectivityLabel(level);

        if (primary is null)
        {
            _statusLine1.Text = L("No active network adapter. Check the cable, the Wi-Fi or Airplane mode.");
            _statusLine2.Text = "";
        }
        else
        {
            var parts = new List<string>();
            if (primary.Kind == AdapterKind.Wifi)
            {
                var name = s.WifiName;
                parts.Add(name is not null ? L("Wi-Fi “{0}”", name) : L("Wi-Fi (network name hidden by Windows)"));
                if (s.Wifi?.Connection is { } c && c.SignalQuality > 0) parts.Add(L("signal {0}%", c.SignalQuality));
            }
            else parts.Add(L("{0} “{1}”", primary.KindLabel, primary.Name));
            if (primary.SpeedBps > 0) parts.Add(primary.SpeedLabel);
            _statusLine1.Text = string.Join(" · ", parts);

            var line2 = new List<string>();
            if (primary.IPv4.Count > 0) line2.Add(L("IP {0}", primary.IPv4[0].Split('/')[0]));
            if (primary.HasGateway) line2.Add(L("gateway {0}", primary.Gateways[0]));
            line2.Add(primary.DnsIsManual ? L("DNS: {0} (manual)", primary.DnsSummary) : L("DNS: automatic ({0})", primary.DnsSummary));
            _statusLine2.Text = string.Join(" · ", line2);
        }

        _statusBadges.Children.Clear();
        if (s.Connectivity.IsMetered) _statusBadges.Children.Add(NetUi.Badge(L("Limited connection (metered)"), "Warning"));
        if (primary is not null && DnsProviders.Identify(primary.DnsServers) is { IsFamily: true } fam)
            _statusBadges.Children.Add(NetUi.Badge(L("Family filter active: {0}", fam.Name), "Success"));
        var vpn = s.Adapters.FirstOrDefault(a => a.IsUp && a.Kind == AdapterKind.Vpn);
        if (vpn is not null) _statusBadges.Children.Add(NetUi.Badge(L("VPN active: {0}", vpn.Name), "Info"));
        if (s.Wifi?.NeedsLocation == true && s.WifiName is null) _statusBadges.Children.Add(NetUi.Badge(L("Wi-Fi name hidden (location turned off)"), "Neutral"));
        _statusBadges.Visibility = _statusBadges.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================== Onglets

    private void AddTab(Panel bar, string key, string title, string glyph)
    {
        var icon = NetUi.Icon(glyph, 14);
        icon.Margin = new Thickness(0, 0, 8, 0);
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
        var underline = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(10, 6, 10, 0), Background = System.Windows.Media.Brushes.Transparent };
        var content = new StackPanel();
        content.Children.Add(NetUi.Row(icon, label));
        content.Children.Add(underline);
        var button = new Button { Content = content, Padding = new Thickness(10, 8, 10, 0), Margin = new Thickness(0, 0, 4, 0) }.Styled("Pp.SubtleButton");
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        icon.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(TextBlock.Foreground)) { Source = label });
        button.Click += (_, _) => SelectTab(key);
        _tabs.Add(new TabInfo(key, title, glyph, button, label, underline));
        bar.Children.Add(button);
    }

    internal void SelectTab(string key)
    {
        var tab = _tabs.FirstOrDefault(t => t.Key == key) ?? _tabs[0];
        _currentTab = tab.Key;
        foreach (var t in _tabs)
        {
            var selected = t == tab;
            t.Label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            t.Label.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Pp.TextPrimary" : "Pp.TextSecondary");
            if (selected) t.Underline.SetResourceReference(Border.BackgroundProperty, "Pp.Accent");
            else t.Underline.Background = System.Windows.Media.Brushes.Transparent;
        }

        if (!_panels.TryGetValue(tab.Key, out var panel))
        {
            try
            {
                panel = CreatePanel(tab.Key);
            }
            catch (Exception ex)
            {
                Log.Error("Network", "onglet " + tab.Key, ex);
                panel = NetUi.EmptyState("", L("Couldn't display this tab"), ex.Message);
            }
            _panels[tab.Key] = panel;
        }
        _host.Content = panel;
        if (tab.Key == TabSettings && _pendingHighlight is { } id && panel is TweakListView list)
        {
            _pendingHighlight = null;
            list.Highlight(id);
        }
    }

    private FrameworkElement CreatePanel(string key) => key switch
    {
        TabConnections => new ConnectionsPanel(this),
        TabDns => new DnsPanel(this),
        TabHosts => new HostsPanel(),
        TabTools => new ToolsPanel(),
        TabWifi => new WifiPanel(this),
        TabSettings => BuildSettingsPanel(),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private static FrameworkElement BuildSettingsPanel() => new TweakListView(NetworkModule.Category);

    /// <summary>Ouvre l'onglet DNS en présélectionnant une carte (depuis une carte de l'onglet Connexions).</summary>
    internal void OpenDnsFor(AdapterInfo adapter)
    {
        SelectTab(TabDns);
        if (_panels.TryGetValue(TabDns, out var p) && p is DnsPanel dns) dns.SelectAdapter(adapter.IfIndex);
        _scroll.ScrollToTop();
    }

    // ================================================================== Navigation

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not string s || s.Length == 0) return;
        if (s.StartsWith("tweak:", StringComparison.Ordinal))
        {
            _pendingHighlight = s["tweak:".Length..];
            SelectTab(TabSettings);
        }
        else if (s.StartsWith("section:", StringComparison.Ordinal))
        {
            SelectTab(s["section:".Length..]);
        }
    }
}

/// <summary>Exécution d'une action du module depuis l'interface : boutons désactivés, retour par notification.</summary>
internal static class NetworkUiActions
{
    public static async Task<ApplyOutcome> RunAsync(string actionId, Dictionary<string, string>? parameters, IEnumerable<UIElement> disable,
        ProgressBar? busy = null, bool showOutcome = true)
    {
        var controls = disable.ToList();
        foreach (var c in controls) c.IsEnabled = false;
        if (busy is not null) busy.Visibility = Visibility.Visible;
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(actionId, parameters ?? []);
            if (showOutcome) AppHost.Toasts.ShowOutcome(outcome);
            return outcome;
        }
        finally
        {
            foreach (var c in controls) c.IsEnabled = true;
            if (busy is not null) busy.Visibility = Visibility.Collapsed;
        }
    }
}
