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

        _showAll = new CheckBox { Content = "Afficher les cartes inactives et virtuelles", Margin = new Thickness(0, 0, 0, 12) }.Styled("Pp.ToggleSwitch");
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
            _list.Children.Add(NetUi.EmptyState("", "Lecture des cartes réseau…"));
            return;
        }

        if (snap.Wifi?.NeedsLocation == true && snap.WifiName is null)
        {
            var open = NetUi.Button("Ouvrir Localisation", "", "Pp.Button", (_, _) => OpenSettings("ms-settings:privacy-location"));
            _hints.Children.Add(NetUi.InfoBar(
                "Depuis Windows 11 24H2, le nom du réseau Wi-Fi (SSID) n'est communiqué aux applications de bureau que si l'accès à la " +
                "localisation est autorisé pour elles (« Autoriser les applications de bureau à accéder à votre position »). Timonier n'utilise pas votre position.",
                "", "Pp.InfoBar", open, "Nom du Wi-Fi masqué par Windows"));
        }

        var showAll = _showAll.IsChecked == true;
        var adapters = snap.Adapters.Where(a => showAll || IsRelevant(a)).ToList();
        if (adapters.Count == 0)
        {
            _list.Children.Add(NetUi.EmptyState("", "Aucune carte réseau à afficher",
                showAll ? "Windows ne signale aucune carte réseau." : "Activez « Afficher les cartes inactives et virtuelles » pour tout voir."));
            return;
        }
        foreach (var a in adapters) _list.Children.Add(BuildAdapterCard(a, snap));
        var hidden = snap.Adapters.Count - adapters.Count;
        if (hidden > 0 && !showAll)
            _list.Children.Add(NetUi.Text($"{hidden} carte(s) inactive(s) ou virtuelle(s) masquée(s).", "Pp.Caption", new Thickness(2, 8, 0, 0)));
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
        var titleText = a.Name;
        if (a.Kind == AdapterKind.Wifi && a.IsUp && snap.WifiName is { } ssid) titleText += $" — « {ssid} »";
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = titleText, FontWeight = FontWeights.SemiBold }.Styled("Pp.CardTitle"));
        names.Children.Add(NetUi.Text(a.Description, "Pp.Caption"));
        var badges = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        badges.Children.Add(a.IsUp ? NetUi.Badge("Connecté", "Success") : NetUi.Badge("Déconnecté"));
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
                    ? "Wi-Fi non connecté (ou mode Avion activé)."
                    : "Aucun lien réseau : câble débranché ou carte désactivée.", "Pp.Caption", new Thickness(50, 8, 0, 0)));
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

        left.Children.Add(NetUi.KeyValue("Adresse IPv4", a.IPv4.Count > 0 ? string.Join("\n", a.IPv4) : "—", 120, selectable: true));
        var v6 = a.IPv6.Where(x => !x.StartsWith("fe80", StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        left.Children.Add(NetUi.KeyValue("Adresse IPv6", v6.Count > 0 ? string.Join("\n", v6) : a.SupportsIPv6 ? "Locale uniquement" : "IPv6 désactivé", 120, selectable: v6.Count > 0));
        left.Children.Add(NetUi.KeyValue("Passerelle", a.HasGateway ? string.Join("\n", a.Gateways.Take(2)) : "Aucune (pas d'accès Internet par cette carte)", 120, selectable: a.HasGateway));
        left.Children.Add(NetUi.KeyValue("DHCP", a.DhcpEnabled ? "Activé" + (a.DhcpServer is { } d ? $" (serveur {d})" : "") : "Désactivé (adresse fixe)", 120));

        var dnsText = a.DnsServers.Count == 0 ? "Aucun" : string.Join("\n", a.DnsServers.Take(4));
        right.Children.Add(NetUi.KeyValue("Serveurs DNS", dnsText, 120, selectable: a.DnsServers.Count > 0));
        var provider = DnsProviders.Identify(a.DnsServers);
        right.Children.Add(NetUi.KeyValue("Origine des DNS",
            (a.DnsIsManual ? "Saisis manuellement" : "Automatiques (réseau)") + (provider is not null ? " — " + provider.Name : ""), 120));
        right.Children.Add(NetUi.KeyValue("Vitesse du lien", a.SpeedLabel, 120));
        right.Children.Add(MacRow(a, new Thickness(0)));
        if (a.DnsSuffix is { } suffix) right.Children.Add(NetUi.KeyValue("Suffixe DNS", suffix, 120));
        root.Children.Add(grid);

        if (a.CanSetDns)
        {
            var actions = NetUi.Row(NetUi.Button("Changer le DNS", "", "Pp.Button", (_, _) => _page.OpenDnsFor(a)));
            actions.Margin = new Thickness(50, 10, 0, 2);
            root.Children.Add(actions);
        }
        return NetUi.Card(root, new Thickness(0, 0, 0, 8));
    }

    /// <summary>Adresse MAC masquée par défaut (identifiant matériel unique), avec un bouton « Révéler ».</summary>
    private static Grid MacRow(AdapterInfo a, Thickness margin)
    {
        var row = NetUi.KeyValue("Adresse MAC", a.MacDisplay(false), 120);
        row.Margin = new Thickness(margin.Left, margin.Top + 3, 0, 3);
        if (a.Mac.Length != 12) return row;
        var value = (TextBlock)row.Children[1];
        var revealed = false;
        var toggle = new Button { Content = "Révéler", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.LinkButton");
        toggle.Click += (_, _) =>
        {
            revealed = !revealed;
            value.Text = a.MacDisplay(revealed);
            toggle.Content = revealed ? "Masquer" : "Révéler";
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
            AppHost.Toasts.Show("Impossible d'ouvrir cette page des Paramètres.", UI.Services.ToastKind.Error);
        }
    }
}
