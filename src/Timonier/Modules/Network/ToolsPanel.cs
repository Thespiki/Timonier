using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Network;

/// <summary>Onglet « Outils » : dépannage (cache DNS, adresse IP, réinitialisation) et raccourcis vers Windows.</summary>
internal sealed class ToolsPanel : UserControl
{
    public ToolsPanel()
    {
        Focusable = false;
        var root = new StackPanel();

        root.Children.Add(NetUi.Text(L("From gentlest to most drastic: try the tools in order if a site or the connection stops responding."),
            "Pp.Caption", new Thickness(2, 0, 0, 4)));
        root.Children.Add(NetUi.Section(L("Troubleshooting")));
        var repair = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -8, 0) };
        repair.Children.Add(ToolCard("", L("Flush DNS cache"),
            L("Forgets saved site addresses. Useful when a site has moved to a different server, or after a change to DNS or the hosts file."),
            L("Clear"), false, "Pp.Button", FlushAsync));
        repair.Children.Add(ToolCard("", L("Renew IP address"),
            L("Releases the IP address, then requests a new one from the router (DHCP). The connection drops for a few seconds. No effect on a static address."),
            L("Renew"), true, "Pp.Button", RenewAsync));
        repair.Children.Add(ToolCard("", L("Reset network stack"),
            L("Last resort for corruption (Winsock, TCP/IP): erases manual IP and DNS settings. Restart required."),
            L("Reset…"), true, "Pp.DangerButton", ResetAsync));
        root.Children.Add(repair);

        root.Children.Add(NetUi.Section(LC("section header", "Windows Settings")));
        var links = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -8, 0) };
        links.Children.Add(LinkCard("", L("Network status"), L("Overview, data usage and Windows Network reset."),
            () => ConnectionsPanel.OpenSettings("ms-settings:network-status")));
        links.Children.Add(LinkCard("", "Wi-Fi", L("Available networks, random hardware addresses, known networks."),
            () => ConnectionsPanel.OpenSettings("ms-settings:network-wifi")));
        links.Children.Add(LinkCard("", L("Network Connections"), L("Classic adapter panel (ncpa.cpl): IPv4/IPv6 properties, disabling an adapter."),
            OpenNcpa));
        links.Children.Add(LinkCard("", "Proxy", L("Manual proxy address, automatic setup script."),
            () => ConnectionsPanel.OpenSettings("ms-settings:network-proxy")));
        links.Children.Add(LinkCard("", "VPN", L("Add or manage the VPN connections built into Windows."),
            () => ConnectionsPanel.OpenSettings("ms-settings:network-vpn")));
        links.Children.Add(LinkCard("", L("Mobile hotspot"), L("Share this PC's connection with other devices."),
            () => ConnectionsPanel.OpenSettings("ms-settings:network-mobilehotspot")));
        root.Children.Add(links);

        Content = root;
    }

    private static Border ToolCard(string glyph, string title, string description, string buttonText, bool admin, string buttonStyle, Func<Button, Task> run)
    {
        var stack = new DockPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var tile = NetUi.IconTile(glyph);
        DockPanel.SetDock(tile, Dock.Left);
        head.Children.Add(tile);
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }.Styled("Pp.CardTitle"));
        if (admin)
        {
            var adminRow = NetUi.Row(NetUi.Icon("", 11, "Pp.TextSecondary"), NetUi.Text(L("Administrator"), "Pp.Caption", new Thickness(4, 0, 0, 0)));
            titleStack.Children.Add(adminRow);
        }
        head.Children.Add(titleStack);
        DockPanel.SetDock(head, Dock.Top);
        stack.Children.Add(head);

        var button = NetUi.Button(buttonText, null, buttonStyle);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(0, 12, 0, 0);
        button.Click += async (_, _) => await run(button);
        DockPanel.SetDock(button, Dock.Bottom);
        stack.Children.Add(button);
        stack.Children.Add(NetUi.Text(description, "Pp.Caption"));
        var card = NetUi.Card(stack, new Thickness(0, 0, 8, 8));
        card.Padding = new Thickness(16, 14, 16, 14);
        return card;
    }

    private static Border LinkCard(string glyph, string title, string description, Action open)
    {
        var dock = new DockPanel();
        var tile = NetUi.IconTile(glyph, "Pp.NeutralBackground", "Pp.TextPrimary", 32);
        DockPanel.SetDock(tile, Dock.Left);
        dock.Children.Add(tile);
        var chevron = NetUi.Icon("", 12, "Pp.TextSecondary");
        chevron.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(chevron, Dock.Right);
        dock.Children.Add(chevron);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }.Styled("Pp.CardTitle"));
        text.Children.Add(NetUi.Text(description, "Pp.Caption"));
        dock.Children.Add(text);
        var card = NetUi.Card(dock, new Thickness(0, 0, 8, 8)).Styled("Pp.CardInteractive");
        card.Margin = new Thickness(0, 0, 8, 8);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, _) => open();
        System.Windows.Automation.AutomationProperties.SetName(card, title);
        return card;
    }

    private static async Task FlushAsync(Button b) =>
        await NetworkUiActions.RunAsync(NetworkActionIds.FlushDns, null, [b]);

    private static async Task RenewAsync(Button b)
    {
        if (!await AppHost.Dialogs.ConfirmAsync(L("Renew IP address"),
                L("All network adapters will release their IP address and then request a new one. The connection (and any video call or download in progress) will be interrupted for a few seconds. Administrator permission is requested."), L("Renew")))
            return;
        await NetworkUiActions.RunAsync(NetworkActionIds.RenewIp, null, [b]);
    }

    internal static async Task ResetAsync(Button? b)
    {
        if (!await AppHost.Dialogs.ConfirmAsync(L("Reset network stack"),
                L("Only use this if the internet still doesn't work after trying the other tools (Winsock errors, persistent “no connection”).\n\n• The Winsock catalog and TCP/IP configuration are reset (netsh winsock reset, netsh int ip reset).\n• Manually entered IP and DNS addresses are erased: write them down first if you use any.\n• Some VPNs, third-party firewalls or filtering software may need to be reinstalled.\n• Saved Wi-Fi networks and their passwords are kept.\n\nA restart is required. A second confirmation will appear in Timonier's administrator window."),
                L("Reset"), danger: true))
            return;
        await NetworkUiActions.RunAsync(NetworkActionIds.Reset, null, b is null ? [] : [b]);
    }

    private static void OpenNcpa()
    {
        try { ProcessRunner.Launch(SystemTool.Control, "ncpa.cpl"); }
        catch (Exception ex)
        {
            Log.Warn("Network", "ncpa.cpl : " + ex.Message);
            AppHost.Toasts.Show(L("Couldn't open Network Connections."), ToastKind.Error);
        }
    }
}
