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
    private readonly CheckBox _doh = new() { Content = "Chiffrer les requêtes DNS (DNS over HTTPS)" };
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

        _empty = NetUi.EmptyState("", "Aucune carte réseau active",
            "Connectez-vous à un réseau (Wi-Fi ou câble) pour choisir ses serveurs DNS.");
        _empty.Visibility = Visibility.Collapsed;
        root.Children.Add(_empty);
        root.Children.Add(_body);

        root.Children.Add(NetUi.InfoBar(
            "Le DNS traduit les noms de sites (exemple.com) en adresses. Par défaut, c'est votre box ou votre fournisseur d'accès qui s'en charge. " +
            "Un fournisseur public peut être plus rapide, plus respectueux de la vie privée ou filtrer les sites dangereux. " +
            "Le choix s'applique à la carte sélectionnée, sur tous les réseaux auxquels elle se connecte.",
            "", "Pp.InfoBar"));

        // --- Carte sélectionnée
        var adapterCard = new StackPanel();
        adapterCard.Children.Add(new TextBlock { Text = "Carte réseau", FontWeight = FontWeights.SemiBold }.Styled("Pp.CardTitle"));
        _adapterBox.Margin = new Thickness(0, 8, 0, 0);
        _adapterBox.SelectionChanged += (_, _) => OnAdapterChanged();
        adapterCard.Children.Add(_adapterBox);
        adapterCard.Children.Add(_current);
        _body.Children.Add(NetUi.Card(adapterCard, new Thickness(0, 0, 0, 8)));

        // --- Fournisseurs
        _body.Children.Add(NetUi.Section("Fournisseur DNS"));
        _body.Children.Add(OptionCard(DnsProviders.Auto, "Automatique (fourni par le réseau)",
            "Revient aux serveurs annoncés par la box ou le réseau (DHCP). C'est le réglage d'origine de Windows.", null, null));

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -8, 0) };
        foreach (var p in DnsProviders.Known)
            grid.Children.Add(OptionCard(p.Key, p.Name, p.Description, p.Filtering, p));
        _body.Children.Add(grid);

        var custom = OptionCard(DnsProviders.Custom, "Personnalisé",
            "Vos propres serveurs (Pi-hole, NextDNS, serveur d'entreprise…) : jusqu'à 2 adresses IPv4 et 2 IPv6, séparées par des virgules.", null, null);
        var customStack = (StackPanel)((RadioButton)((Border)custom).Child).Content;
        _custom.TextChanged += (_, _) => ValidateInput();
        System.Windows.Automation.AutomationProperties.SetName(_custom, "Serveurs DNS personnalisés");
        customStack.Children.Add(_custom);
        customStack.Children.Add(_customError);
        _body.Children.Add(custom);

        // --- Options
        _body.Children.Add(NetUi.Section("Options"));
        var opts = new StackPanel();
        _doh.Checked += (_, _) => ValidateInput();
        _doh.Unchecked += (_, _) => ValidateInput();
        opts.Children.Add(_doh);
        opts.Children.Add(_dohHelp);
        opts.Children.Add(NetUi.Text(
            "Les adresses IPv6 du fournisseur sont ajoutées automatiquement si la carte utilise IPv6 : sinon, les DNS IPv6 de la box " +
            "continueraient de répondre et pourraient contourner un filtre.", "Pp.Caption", new Thickness(0, 10, 0, 0)));
        _body.Children.Add(NetUi.Card(opts, new Thickness(0, 0, 0, 8)));

        _familyInfo = NetUi.InfoBar(
            "Un DNS familial bloque les sites pour adultes et dangereux sur tout le PC, sans logiciel à installer. Il a des limites : " +
            "il ne filtre pas le contenu à l'intérieur d'un site autorisé (réseaux sociaux, vidéos), il peut être contourné par un VPN, " +
            "un navigateur réglé sur son propre DNS sécurisé (Chrome, Edge, Firefox) ou un changement de DNS par un compte administrateur. " +
            "Pour un enfant, combinez-le avec un compte standard (non administrateur) et Microsoft Family Safety.",
            "", "Pp.InfoBar.Success", title: "Filtre familial : ce qu'il fait et ne fait pas");
        _familyInfo.Visibility = Visibility.Collapsed;
        _body.Children.Add(_familyInfo);

        // --- Application
        _apply = NetUi.Button("Appliquer", "", "Pp.AccentButton", async (_, _) => await ApplyAsync());
        var applyRow = NetUi.Row(_apply, _busy);
        applyRow.Margin = new Thickness(0, 8, 0, 0);
        _body.Children.Add(applyRow);
        _body.Children.Add(NetUi.Text("Une autorisation administrateur est demandée. Le changement est immédiat et peut être annulé depuis la notification.",
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
        var servers = a.DnsServers.Count > 0 ? string.Join(", ", a.DnsServers.Take(4)) : "aucun";
        _current.Text = manual.Count == 0
            ? $"Actuellement : automatiques (fournis par le réseau) — {servers}"
            : $"Actuellement : {(provider is not null ? provider.Name : "serveurs personnalisés")} — {servers}";

        // Présélectionne l'option qui correspond à la configuration actuelle.
        if (manual.Count == 0) Select(DnsProviders.Auto);
        else if (DnsProviders.Identify(manual) is { } known) Select(known.Key);
        else
        {
            _custom.Text = string.Join(", ", manual);
            Select(DnsProviders.Custom);
        }
        _apply.Content = BuildApplyContent($"Appliquer à « {a.Name} »");
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
            ? "Le DNS chiffré intégré à Windows nécessite Windows 11."
            : provider is null
                ? "Disponible uniquement pour les fournisseurs de la liste (modèle de chiffrement connu)."
                : "Windows enregistre le modèle DoH de " + provider.Name + " et chiffre les requêtes vers ses serveurs. Si le chiffrement échoue, " +
                  "Windows repasse en DNS classique pour ne pas couper Internet (réglage « DNS chiffré » de l'onglet Réglages pour l'exiger).";

        string? error = null;
        if (key == DnsProviders.Custom)
        {
            try { DnsProviders.ParseCustom(_custom.Text); }
            catch (ValidationException ex) { error = _custom.Text.Trim().Length == 0 ? "Saisissez au moins une adresse, par exemple 192.168.1.10." : ex.Message; }
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
            AppHost.Toasts.Show(outcome.Message, ToastKind.Success, undo is null ? null : "Annuler", undo is null ? null : () => _ = RestoreAsync(undo));
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
        AppHost.Toasts.Show(outcome.Success ? "Configuration DNS précédente rétablie." : outcome.Message,
            outcome.Success ? ToastKind.Success : ToastKind.Error);
        await _page.RefreshAsync();
    }
}
