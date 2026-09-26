using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Timonier.UI.Services;

namespace Timonier.Modules.Network;

/// <summary>
/// Onglet « Wi-Fi enregistrés » : liste des profils (noms uniquement, jamais les mots de passe) et suppression
/// après confirmation. Tentative sans élévation, puis via le broker si Windows l'exige.
/// </summary>
internal sealed class WifiPanel : UserControl
{
    private readonly NetworkPage _page;
    private readonly StackPanel _list = new();
    private readonly TextBox _filter = new() { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _summary = NetUi.Text("", "Pp.Caption");
    private List<WlanProfile> _profiles = [];
    private uint _error;
    private bool _busy;

    public WifiPanel(NetworkPage page)
    {
        _page = page;
        Focusable = false;
        var root = new StackPanel();
        root.Children.Add(NetUi.InfoBar(
            L("Timonier only shows the names of saved networks: passwords are never read or displayed. Forgetting a network also erases its password; Windows will no longer reconnect to it automatically."),
            "", "Pp.InfoBar"));

        var toolbar = new DockPanel { Margin = new Thickness(0, 4, 0, 10) };
        System.Windows.Automation.AutomationProperties.SetName(_filter, L("Filter networks"));
        _filter.SetResourceReference(StyleProperty, "Pp.SearchBox");
        _filter.Tag = L("Filter by name…");
        _filter.TextChanged += (_, _) => Render();
        var refresh = NetUi.Button(L("Refresh"), "", "Pp.Button", async (_, _) => await ReloadAsync());
        DockPanel.SetDock(refresh, Dock.Right);
        toolbar.Children.Add(refresh);
        DockPanel.SetDock(_filter, Dock.Left);
        toolbar.Children.Add(_filter);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        _summary.Margin = new Thickness(14, 0, 0, 0);
        toolbar.Children.Add(_summary);
        root.Children.Add(toolbar);
        root.Children.Add(_list);
        Content = root;

        Loaded += async (_, _) => { _page.SnapshotChanged += OnSnapshot; await ReloadAsync(); };
        Unloaded += (_, _) => _page.SnapshotChanged -= OnSnapshot;
    }

    private void OnSnapshot(object? sender, EventArgs e) => Render();

    private async Task ReloadAsync()
    {
        _list.Children.Clear();
        _list.Children.Add(NetUi.EmptyState("", L("Reading saved networks…")));
        var result = await Task.Run(WlanApi.Profiles);
        _profiles = result.Value ?? [];
        _error = result.Error;
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        if (_error != 0 || (_profiles.Count == 0 && _page.Snapshot is { HasWifiAdapter: false }))
        {
            _summary.Text = "";
            _list.Children.Add(_error == WlanApi.ErrorServiceNotActive || _page.Snapshot is { HasWifiAdapter: false }
                ? NetUi.EmptyState("", L("No Wi-Fi adapter"), L("This PC has no active Wi-Fi adapter, or the “WLAN AutoConfig” service is stopped."))
                : NetUi.EmptyState("", L("Network list unavailable"), L("Windows returned: {0}.", WlanApi.Describe(_error))));
            return;
        }
        if (_profiles.Count == 0)
        {
            _summary.Text = "";
            _list.Children.Add(NetUi.EmptyState("", L("No saved Wi-Fi networks"), L("Networks you connect to will appear here.")));
            return;
        }

        var filter = _filter.Text.Trim();
        var shown = _profiles
            .Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _summary.Text = LP(_profiles.Count, "{0} saved network", "{0} saved networks");
        if (shown.Count == 0)
        {
            _list.Children.Add(NetUi.Text(L("No networks match the filter."), "Pp.Caption", new Thickness(2, 4, 0, 0)));
            return;
        }

        var multipleInterfaces = _profiles.Select(p => p.InterfaceId).Distinct().Count() > 1;
        var current = _page.Snapshot?.Wifi;
        // Nom du profil connecté : lu via WLAN, sinon via le profil de connexion de Windows (quand le SSID est masqué).
        var currentName = current?.Connection?.ProfileName.NullIfEmpty() ??
                          (_page.Snapshot?.Connectivity is { IsWlan: true } cp ? cp.ProfileName : null);
        var card = new StackPanel();
        for (var i = 0; i < shown.Count; i++)
        {
            var p = shown[i];
            if (i > 0) card.Children.Add(NetUi.Divider(new Thickness(0, 6, 0, 6)));
            var row = new DockPanel();
            var forget = NetUi.Button(L("Forget"), "", "Pp.SubtleButton", async (s, _) => await ForgetAsync(p, (Button)s!));
            forget.IsEnabled = !p.IsGroupPolicy && !_busy;
            if (p.IsGroupPolicy) forget.ToolTip = L("Network enforced by an organization policy");
            DockPanel.SetDock(forget, Dock.Right);
            row.Children.Add(forget);

            var icon = NetUi.Icon("", 16, "Pp.TextSecondary");
            icon.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var head = new WrapPanel();
            head.Children.Add(new TextBlock { Text = p.Name, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Body"));
            if (currentName is not null && (current is null || current.Interface.Id == p.InterfaceId) && currentName == p.Name) head.Children.Add(NetUi.Badge(L("Connected"), "Success"));
            head.Children.Add(p.IsGroupPolicy ? NetUi.Badge(L("Enforced by organization"), "Warning")
                : p.IsPerUser ? NetUi.Badge(L("This account only"), "Info")
                : NetUi.Badge(L("All users")));
            text.Children.Add(head);
            if (multipleInterfaces) text.Children.Add(NetUi.Text(p.InterfaceDescription, "Pp.Caption"));
            row.Children.Add(text);
            card.Children.Add(row);
        }
        _list.Children.Add(NetUi.Card(card));
    }

    private async Task ForgetAsync(WlanProfile profile, Button button)
    {
        if (!await AppHost.Dialogs.ConfirmAsync(L("Forget this network"),
                profile.IsPerUser
                    ? L("The network “{0}” and its password will be removed from this PC. To reconnect to it, you'll need to enter the password again.", profile.Name)
                    : L("The network “{0}” and its password will be removed from this PC for all users. To reconnect to it, you'll need to enter the password again.", profile.Name),
                L("Forget"), danger: true))
            return;

        _busy = true;
        var parameters = new Dictionary<string, string> { ["iface"] = profile.InterfaceId.ToString("D"), ["profile"] = profile.Name };
        var outcome = await NetworkUiActions.RunAsync(NetworkActionIds.WifiForget, parameters, [button], showOutcome: false);
        // Profil protégé : Windows exige les droits administrateur, on passe par le broker (une seule invite UAC par session).
        if (!outcome.Success && outcome.Data?.GetValueOrDefault("error") == WlanApi.ErrorAccessDenied.ToString(CultureInfo.InvariantCulture))
            outcome = await NetworkUiActions.RunAsync(NetworkActionIds.WifiForgetAdmin, parameters, [button], showOutcome: false);
        _busy = false;

        if (outcome.Success) AppHost.Toasts.Show(outcome.Message, ToastKind.Success);
        else if (!outcome.Cancelled) AppHost.Toasts.ShowOutcome(outcome);
        await ReloadAsync();
    }
}
