using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;

namespace Timonier.Modules.Network;

/// <summary>Onglet « Connexions » : une carte par adaptateur réseau (adresses, passerelle, DNS, DHCP, MAC masquée).</summary>
internal sealed class ConnectionsPanel : UserControl
{
    private readonly NetworkPage _page;
    private readonly StackPanel _list = new();
    private readonly CheckBox _showAll;
    private readonly StackPanel _hints = new();

    public ConnectionsPanel(NetworkPage page)
    {
        _page = page;
        Focusable = false;
        var root = new StackPanel();

        _showAll = new CheckBox { Content = L("Show inactive and virtual adapters"), Margin = new Thickness(0, 0, 0, 12) }.Styled("Pp.ToggleSwitch");
        _showAll.Checked += (_, _) => Render();
        _showAll.Unchecked += (_, _) => Render();
        root.Children.Add(_showAll);
        root.Children.Add(_hints);
        root.Children.Add(_list);
        Content = root;

        Loaded += (_, _) => { _page.SnapshotChanged += OnSnapshot; Render(); };
        Unloaded += (_, _) => _page.SnapshotChanged -= OnSnapshot;
    }

    private void OnSnapshot(object? sender, EventArgs e) => Render();

    private void Render()
    {
        _list.Children.Clear();
        _hints.Children.Clear();
        var snap = _page.Snapshot;
        if (snap is null)
        {
            _list.Children.Add(NetUi.EmptyState("", L("Reading network adapters…")));
            return;
        }

        if (snap.Wifi?.NeedsLocation == true && snap.WifiName is null)
        {
            var open = NetUi.Button(L("Open Location"), "", "Pp.Button", (_, _) => OpenSettings("ms-settings:privacy-location"));
            _hints.Children.Add(NetUi.InfoBar(
                L("Since Windows 11 24H2, the Wi-Fi network name (SSID) is only shared with desktop apps if they're allowed to access location (“Let desktop apps access your location”). Timonier doesn't use your location."),
                "", "Pp.InfoBar", open, L("Wi-Fi name hidden by Windows")));
        }

        var showAll = _showAll.IsChecked == true;
        var adapters = snap.Adapters.Where(a => showAll || IsRelevant(a)).ToList();
        if (adapters.Count == 0)
        {
            _list.Children.Add(NetUi.EmptyState("", L("No network adapters to show"),
                showAll ? L("Windows doesn't report any network adapters.") : L("Turn on “Show inactive and virtual adapters” to see everything.")));
            return;
        }
        foreach (var a in adapters) _list.Children.Add(BuildAdapterCard(a, snap));
        var hidden = snap.Adapters.Count - adapters.Count;
        if (hidden > 0 && !showAll)
            _list.Children.Add(NetUi.Text(LP(hidden, "{0} inactive or virtual adapter hidden.", "{0} inactive or virtual adapters hidden."), "Pp.Caption", new Thickness(2, 8, 0, 0)));
    }

    /// <summary>Par défaut : cartes physiques (même déconnectées), VPN actifs, cartes virtuelles actives ayant une adresse IPv4.</summary>
    private static bool IsRelevant(AdapterInfo a) => a.Kind switch
    {
        AdapterKind.Ethernet or AdapterKind.Wifi or AdapterKind.Mobile => true,
        AdapterKind.Vpn => a.IsUp,
        _ => a.IsUp && a.IPv4.Count > 0 && a.HasGateway,
    };

    private Border BuildAdapterCard(AdapterInfo a, NetworkSnapshot snap)
    {
        var root = new StackPanel();

        // En-tête : icône, nom, description, badges
        var tile = NetUi.IconTile(a.Glyph, a.IsUp ? "Pp.AccentSubtle" : "Pp.NeutralBackground", a.IsUp ? "Pp.AccentText" : "Pp.TextSecondary");
        var titleText = a.Kind == AdapterKind.Wifi && a.IsUp && snap.WifiName is { } ssid ? L("{0} — “{1}”", a.Name, ssid) : a.Name;
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = titleText, FontWeight = FontWeights.SemiBold }.Styled("Pp.CardTitle"));
        names.Children.Add(NetUi.Text(a.Description, "Pp.Caption"));
        var badges = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        badges.Children.Add(a.IsUp ? NetUi.Badge(L("Connected"), "Success") : NetUi.Badge(L("Disconnected")));
        badges.Children.Add(NetUi.Badge(a.KindLabel, "Accent"));
        var header = new DockPanel();
        DockPanel.SetDock(tile, Dock.Left);
        DockPanel.SetDock(badges, Dock.Right);
        header.Children.Add(tile);
        header.Children.Add(badges);
        header.Children.Add(names);
        root.Children.Add(header);

        if (!a.IsUp)
        {
            root.Children.Add(NetUi.Text(a.Kind == AdapterKind.Wifi
                    ? L("Wi-Fi not connected (or Airplane mode on).")
                    : L("No network link: cable unplugged or adapter disabled."), "Pp.Caption", new Thickness(50, 8, 0, 0)));
            root.Children.Add(MacRow(a, new Thickness(50, 4, 0, 0)));
            return NetUi.Card(root, new Thickness(0, 0, 0, 8));
        }

        // Détails sur deux colonnes
        var grid = new Grid { Margin = new Thickness(50, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel();
        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);

        left.Children.Add(NetUi.KeyValue(L("IPv4 address"), a.IPv4.Count > 0 ? string.Join("\n", a.IPv4) : "—", 120, selectable: true));
        var v6 = a.IPv6.Where(x => !x.StartsWith("fe80", StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        left.Children.Add(NetUi.KeyValue(L("IPv6 address"), v6.Count > 0 ? string.Join("\n", v6) : a.SupportsIPv6 ? L("Local only") : L("IPv6 disabled"), 120, selectable: v6.Count > 0));
        left.Children.Add(NetUi.KeyValue(L("Gateway"), a.HasGateway ? string.Join("\n", a.Gateways.Take(2)) : L("None (no internet access through this adapter)"), 120, selectable: a.HasGateway));
        left.Children.Add(NetUi.KeyValue("DHCP", a.DhcpEnabled ? (a.DhcpServer is { } d ? L("On (server {0})", d) : L("On")) : L("Off (static address)"), 120));

        var dnsText = a.DnsServers.Count == 0 ? L("None") : string.Join("\n", a.DnsServers.Take(4));
        right.Children.Add(NetUi.KeyValue(L("DNS servers"), dnsText, 120, selectable: a.DnsServers.Count > 0));
        var provider = DnsProviders.Identify(a.DnsServers);
        right.Children.Add(NetUi.KeyValue(L("DNS source"),
            provider is null
                ? (a.DnsIsManual ? L("Entered manually") : L("Automatic (network)"))
                : (a.DnsIsManual ? L("Entered manually — {0}", provider.Name) : L("Automatic (network) — {0}", provider.Name)), 120));
        right.Children.Add(NetUi.KeyValue(L("Link speed"), a.SpeedLabel, 120));
        right.Children.Add(MacRow(a, new Thickness(0)));
        if (a.DnsSuffix is { } suffix) right.Children.Add(NetUi.KeyValue(L("DNS suffix"), suffix, 120));
        root.Children.Add(grid);

        if (a.CanSetDns)
        {
            var actions = NetUi.Row(NetUi.Button(L("Change DNS"), "", "Pp.Button", (_, _) => _page.OpenDnsFor(a)));
            actions.Margin = new Thickness(50, 10, 0, 2);
            root.Children.Add(actions);
        }
        return NetUi.Card(root, new Thickness(0, 0, 0, 8));
    }

    /// <summary>Adresse MAC masquée par défaut (identifiant matériel unique), avec un bouton « Révéler ».</summary>
    private static Grid MacRow(AdapterInfo a, Thickness margin)
    {
        var row = NetUi.KeyValue(L("MAC address"), a.MacDisplay(false), 120);
        row.Margin = new Thickness(margin.Left, margin.Top + 3, 0, 3);
        if (a.Mac.Length != 12) return row;
        var value = (TextBlock)row.Children[1];
        var revealed = false;
        var toggle = new Button { Content = L("Reveal"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.LinkButton");
        toggle.Click += (_, _) =>
        {
            revealed = !revealed;
            value.Text = a.MacDisplay(revealed);
            toggle.Content = revealed ? L("Hide") : L("Reveal");
        };
        row.Children.Remove(value);
        var panel = NetUi.Row(value, toggle);
        Grid.SetColumn(panel, 1);
        row.Children.Add(panel);
        return row;
    }

    internal static void OpenSettings(string uri)
    {
        try { ProcessRunner.OpenSettingsUri(uri); }
        catch (Exception ex)
        {
            Log.Warn("Network", "ouverture " + uri + " : " + ex.Message);
            AppHost.Toasts.Show(L("Couldn't open this Settings page."), UI.Services.ToastKind.Error);
        }
    }
}
