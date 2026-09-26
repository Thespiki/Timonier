using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Timonier.Core.Security;
using Timonier.UI.Services;

namespace Timonier.Modules.Network;

/// <summary>
/// Onglet « DNS » : choix du fournisseur par carte réseau (automatique, public, familial, personnalisé), DNS chiffré
/// sous Windows 11, explication honnête des filtres familiaux. L'application passe par l'action admin network.dns.set.
/// </summary>
internal sealed class DnsPanel : UserControl
{
    private readonly NetworkPage _page;
    private readonly ComboBox _adapterBox = new() { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _current = NetUi.Text("", "Pp.Caption", new Thickness(0, 8, 0, 0));
    private readonly Dictionary<string, RadioButton> _options = new(StringComparer.Ordinal);
    private readonly TextBox _custom = new() { MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(28, 8, 0, 0) };
    private readonly TextBlock _customError = NetUi.Text("", "Pp.Caption", new Thickness(28, 4, 0, 0));
    private readonly CheckBox _doh = new() { Content = L("Encrypt DNS queries (DNS over HTTPS)") };
    private readonly TextBlock _dohHelp = NetUi.Text("", "Pp.Caption", new Thickness(28, 2, 0, 0));
    private readonly Border _familyInfo;
    private readonly Button _apply;
    private readonly ProgressBar _busy = NetUi.Busy();
    private readonly StackPanel _body = new();
    private readonly Border _empty;
    private int? _preferredIfIndex;
    private bool _busyApplying;

    public DnsPanel(NetworkPage page)
    {
        _page = page;
        Focusable = false;
        var root = new StackPanel();

        _empty = NetUi.EmptyState("", L("No active network adapter"),
            L("Connect to a network (Wi-Fi or cable) to choose its DNS servers."));
        _empty.Visibility = Visibility.Collapsed;
        root.Children.Add(_empty);
        root.Children.Add(_body);

        root.Children.Add(NetUi.InfoBar(
            L("DNS translates site names (example.com) into addresses. By default, your router or internet provider handles this. A public provider can be faster, more privacy-friendly, or filter out dangerous sites. The choice applies to the selected adapter, on every network it connects to."),
            "", "Pp.InfoBar"));

        // --- Carte sélectionnée
        var adapterCard = new StackPanel();
        adapterCard.Children.Add(new TextBlock { Text = L("Network adapter"), FontWeight = FontWeights.SemiBold }.Styled("Pp.CardTitle"));
        _adapterBox.Margin = new Thickness(0, 8, 0, 0);
        _adapterBox.SelectionChanged += (_, _) => OnAdapterChanged();
        adapterCard.Children.Add(_adapterBox);
        adapterCard.Children.Add(_current);
        _body.Children.Add(NetUi.Card(adapterCard, new Thickness(0, 0, 0, 8)));

        // --- Fournisseurs
        _body.Children.Add(NetUi.Section(L("DNS provider")));
        _body.Children.Add(OptionCard(DnsProviders.Auto, L("Automatic (provided by the network)"),
            L("Goes back to the servers announced by the router or network (DHCP). This is the Windows default."), null, null));

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -8, 0) };
        foreach (var p in DnsProviders.Known)
            grid.Children.Add(OptionCard(p.Key, p.Name, p.Description, p.Filtering, p));
        _body.Children.Add(grid);

        var custom = OptionCard(DnsProviders.Custom, L("Custom"),
            L("Your own servers (Pi-hole, NextDNS, company server…): up to 2 IPv4 and 2 IPv6 addresses, separated by commas."), null, null);
        var customStack = (StackPanel)((RadioButton)((Border)custom).Child).Content;
        _custom.TextChanged += (_, _) => ValidateInput();
        System.Windows.Automation.AutomationProperties.SetName(_custom, L("Custom DNS servers"));
        customStack.Children.Add(_custom);
        customStack.Children.Add(_customError);
        _body.Children.Add(custom);

        // --- Options
        _body.Children.Add(NetUi.Section(L("Options")));
        var opts = new StackPanel();
        _doh.Checked += (_, _) => ValidateInput();
        _doh.Unchecked += (_, _) => ValidateInput();
        opts.Children.Add(_doh);
        opts.Children.Add(_dohHelp);
        opts.Children.Add(NetUi.Text(
            L("The provider's IPv6 addresses are added automatically if the adapter uses IPv6: otherwise, the router's IPv6 DNS would keep answering and could bypass a filter."), "Pp.Caption", new Thickness(0, 10, 0, 0)));
        _body.Children.Add(NetUi.Card(opts, new Thickness(0, 0, 0, 8)));

        _familyInfo = NetUi.InfoBar(
            L("A family DNS blocks adult and dangerous sites across the whole PC, with no software to install. It has limits: it doesn't filter content inside an allowed site (social networks, videos), and it can be bypassed by a VPN, a browser set to its own secure DNS (Chrome, Edge, Firefox), or a DNS change made by an administrator account. For a child, combine it with a standard (non-administrator) account and Microsoft Family Safety."),
            "", "Pp.InfoBar.Success", title: L("Family filter: what it does and doesn't do"));
        _familyInfo.Visibility = Visibility.Collapsed;
        _body.Children.Add(_familyInfo);

        // --- Application
        _apply = NetUi.Button(L("Apply"), "", "Pp.AccentButton", async (_, _) => await ApplyAsync());
        var applyRow = NetUi.Row(_apply, _busy);
        applyRow.Margin = new Thickness(0, 8, 0, 0);
        _body.Children.Add(applyRow);
        _body.Children.Add(NetUi.Text(L("Administrator permission is requested. The change takes effect immediately and can be undone from the notification."),
            "Pp.Caption", new Thickness(0, 6, 0, 0)));

        Content = root;
        Loaded += (_, _) => { _page.SnapshotChanged += OnSnapshot; RenderAdapters(); };
        Unloaded += (_, _) => _page.SnapshotChanged -= OnSnapshot;
        Select(DnsProviders.Auto);
    }

    private Border OptionCard(string key, string title, string description, DnsFiltering? filtering, DnsProvider? provider)
    {
        var stack = new StackPanel();
        var head = new WrapPanel();
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.CardTitle"));
        if (filtering is { } f)
            head.Children.Add(NetUi.Badge(DnsProviders.FilteringLabel(f), f switch
            {
                DnsFiltering.Family => "Success",
                DnsFiltering.Malware => "Info",
                DnsFiltering.AdsTrackers => "Warning",
                _ => "Neutral",
            }));
        stack.Children.Add(head);
        stack.Children.Add(NetUi.Text(description, "Pp.Caption", new Thickness(0, 4, 0, 0)));
        if (provider is not null)
            stack.Children.Add(NetUi.Text(string.Join(" · ", provider.IPv4), "Pp.Caption", new Thickness(0, 4, 0, 0)).Themed(TextBlock.ForegroundProperty, "Pp.TextTertiary"));

        var radio = new RadioButton { GroupName = "tmn-dns-provider", Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(radio, title);
        radio.Checked += (_, _) => OnProviderChanged();
        _options[key] = radio;
        var card = NetUi.Card(radio, new Thickness(0, 0, 8, 8)).Styled("Pp.CardInteractive");
        card.Margin = new Thickness(0, 0, key is DnsProviders.Auto or DnsProviders.Custom ? 0 : 8, 8);
        card.MouseLeftButtonUp += (_, _) => { if (radio.IsEnabled) radio.IsChecked = true; };
        return card;
    }

    private void Select(string key)
    {
        if (_options.TryGetValue(key, out var r)) r.IsChecked = true;
    }

    private string SelectedKey => _options.FirstOrDefault(o => o.Value.IsChecked == true).Key ?? DnsProviders.Auto;

    private AdapterInfo? SelectedAdapter => (_adapterBox.SelectedItem as ComboBoxItem)?.Tag as AdapterInfo;

    internal void SelectAdapter(int ifIndex)
    {
        _preferredIfIndex = ifIndex;
        foreach (ComboBoxItem item in _adapterBox.Items)
            if (item.Tag is AdapterInfo a && a.IfIndex == ifIndex) { _adapterBox.SelectedItem = item; return; }
    }

    private void OnSnapshot(object? sender, EventArgs e) => RenderAdapters();

    private void RenderAdapters()
    {
        var snap = _page.Snapshot;
        if (snap is null) return;
        var keep = SelectedAdapter?.IfIndex ?? _preferredIfIndex ?? snap.Primary?.IfIndex;
        var eligible = snap.Adapters.Where(a => a.CanSetDns).ToList();
        _adapterBox.Items.Clear();
        foreach (var a in eligible)
            _adapterBox.Items.Add(new ComboBoxItem { Content = $"{a.Name} — {a.Description}", Tag = a });
        _empty.Visibility = eligible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _body.Visibility = eligible.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (eligible.Count == 0) return;
        var index = eligible.FindIndex(a => a.IfIndex == keep);
        _adapterBox.SelectedIndex = index >= 0 ? index : 0;
    }

    private void OnAdapterChanged()
    {
        var a = SelectedAdapter;
        if (a is null) { _current.Text = ""; return; }
        _preferredIfIndex = a.IfIndex;
        var manual = a.StaticDns4.Concat(a.StaticDns6).ToList();
        var provider = DnsProviders.Identify(a.DnsServers);
        var servers = a.DnsServers.Count > 0 ? string.Join(", ", a.DnsServers.Take(4)) : L("none");
        _current.Text = manual.Count == 0
            ? L("Current: automatic (provided by the network) — {0}", servers)
            : provider is not null
                ? L("Current: {0} — {1}", provider.Name, servers)
                : L("Current: custom servers — {0}", servers);

        // Présélectionne l'option qui correspond à la configuration actuelle.
        if (manual.Count == 0) Select(DnsProviders.Auto);
        else if (DnsProviders.Identify(manual) is { } known) Select(known.Key);
        else
        {
            _custom.Text = string.Join(", ", manual);
            Select(DnsProviders.Custom);
        }
        _apply.Content = BuildApplyContent(L("Apply to “{0}”", a.Name));
        ValidateInput();
    }

    private static object BuildApplyContent(string text) =>
        NetUi.Row(new TextBlock { Text = "", FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("Pp.IconFont"), FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                  new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

    private void OnProviderChanged()
    {
        var key = SelectedKey;
        _custom.IsEnabled = key == DnsProviders.Custom;
        var provider = DnsProviders.Get(key);
        _familyInfo.Visibility = provider?.IsFamily == true ? Visibility.Visible : Visibility.Collapsed;
        ValidateInput();
    }

    private void ValidateInput()
    {
        var key = SelectedKey;
        var provider = DnsProviders.Get(key);
        var win11 = AppHost.Profile.IsWindows11;

        _doh.IsEnabled = win11 && provider?.DohTemplate is not null;
        if (!_doh.IsEnabled) _doh.IsChecked = false;
        _dohHelp.Text = !win11
            ? L("Windows' built-in encrypted DNS requires Windows 11.")
            : provider is null
                ? L("Only available for providers in the list (known encryption template).")
                : L("Windows registers the DoH template for {0} and encrypts queries to its servers. If encryption fails, Windows falls back to regular DNS so your internet doesn't cut out (use the “Encrypted DNS” setting in the Settings tab to require it).", provider.Name);

        string? error = null;
        if (key == DnsProviders.Custom)
        {
            try { DnsProviders.ParseCustom(_custom.Text); }
            catch (ValidationException ex) { error = _custom.Text.Trim().Length == 0 ? L("Enter at least one address, for example 192.168.1.10.") : ex.Message; }
        }
        _customError.Text = error ?? "";
        _customError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is not null) _customError.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Danger");
        _apply.IsEnabled = !_busyApplying && error is null && SelectedAdapter is not null;
    }

    private async Task ApplyAsync()
    {
        var adapter = SelectedAdapter;
        if (adapter is null) return;
        var key = SelectedKey;
        var parameters = new Dictionary<string, string>
        {
            ["ifIndex"] = adapter.IfIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["provider"] = key,
            ["ipv6"] = adapter.SupportsIPv6 ? "true" : "false",
            ["doh"] = _doh.IsChecked == true ? "true" : "false",
        };
        if (key == DnsProviders.Custom)
        {
            var (v4, v6) = DnsProviders.ParseCustom(_custom.Text);
            parameters["servers"] = string.Join(",", v4.Concat(v6));
        }

        _busyApplying = true;
        var outcome = await NetworkUiActions.RunAsync(NetworkActionIds.SetDns, parameters,
            [_apply, _adapterBox, .. _options.Values, _custom, _doh], _busy, showOutcome: false);
        _busyApplying = false;
        ValidateInput();

        if (outcome.Success)
        {
            var undo = BuildUndo(outcome.Data);
            AppHost.Toasts.Show(outcome.Message, ToastKind.Success, undo is null ? null : L("Undo"), undo is null ? null : () => _ = RestoreAsync(undo));
            await _page.RefreshAsync();
        }
        else if (!outcome.Cancelled)
        {
            AppHost.Toasts.ShowOutcome(outcome);
        }
    }

    /// <summary>Paramètres qui rétablissent la configuration précédente (renvoyée par l'action).</summary>
    private static Dictionary<string, string>? BuildUndo(Dictionary<string, string>? data)
    {
        if (data is null || !data.TryGetValue("ifIndex", out var ifIndex) || !data.TryGetValue("prevProvider", out var prev)) return null;
        var servers = data.GetValueOrDefault("prevServers") ?? "";
        if (prev == DnsProviders.Auto || servers.Length == 0)
            return new() { ["ifIndex"] = ifIndex, ["provider"] = DnsProviders.Auto };
        // Restaure les adresses exactes (2 par famille au plus, limite de l'action).
        var list = servers.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var v4 = list.Where(s => !s.Contains(':')).Take(2);
        var v6 = list.Where(s => s.Contains(':')).Take(2);
        return new() { ["ifIndex"] = ifIndex, ["provider"] = DnsProviders.Custom, ["servers"] = string.Join(",", v4.Concat(v6)) };
    }

    private async Task RestoreAsync(Dictionary<string, string> parameters)
    {
        var outcome = await AppHost.Engine.RunActionAsync(NetworkActionIds.SetDns, parameters);
        AppHost.Toasts.Show(outcome.Success ? L("Previous DNS configuration restored.") : outcome.Message,
            outcome.Success ? ToastKind.Success : ToastKind.Error);
        await _page.RefreshAsync();
    }
}
