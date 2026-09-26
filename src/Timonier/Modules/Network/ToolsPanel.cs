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

        root.Children.Add(NetUi.Text(L("Du plus doux au plus radical : essayez les outils dans l'ordre si un site ou la connexion ne répond plus."),
            "Pp.Caption", new Thickness(2, 0, 0, 4)));
        root.Children.Add(NetUi.Section(L("Dépannage")));
        var repair = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -8, 0) };
        repair.Children.Add(ToolCard("", L("Vider le cache DNS"),
            L("Oublie les adresses mémorisées des sites. Utile quand un site a changé de serveur ou après une modification des DNS ou du fichier hosts."),
            L("Vider"), false, "Pp.Button", FlushAsync));
        repair.Children.Add(ToolCard("", L("Renouveler l'adresse IP"),
            L("Rend l'adresse IP puis en redemande une à la box (DHCP). La connexion est coupée quelques secondes. Sans effet sur une adresse fixe."),
            L("Renouveler"), true, "Pp.Button", RenewAsync));
        repair.Children.Add(ToolCard("", L("Réinitialiser la pile réseau"),
            L("Dernier recours en cas de corruption (Winsock, TCP/IP) : efface les réglages IP et DNS manuels. Redémarrage requis."),
            L("Réinitialiser…"), true, "Pp.DangerButton", ResetAsync));
        root.Children.Add(repair);

        root.Children.Add(NetUi.Section(L("Paramètres de Windows")));
        var links = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -8, 0) };
        links.Children.Add(LinkCard("", L("État du réseau"), L("Vue d'ensemble, utilisation des données et Réinitialisation du réseau de Windows."),
            () => ConnectionsPanel.OpenSettings("ms-settings:network-status")));
        links.Children.Add(LinkCard("", "Wi-Fi", L("Réseaux disponibles, adresses matérielles aléatoires, réseaux connus."),
            () => ConnectionsPanel.OpenSettings("ms-settings:network-wifi")));
        links.Children.Add(LinkCard("", L("Connexions réseau"), L("Panneau classique des cartes (ncpa.cpl) : propriétés IPv4/IPv6, désactivation d'une carte."),
            OpenNcpa));
        links.Children.Add(LinkCard("", "Proxy", L("Adresse du proxy manuel, script de configuration automatique."),
            () => ConnectionsPanel.OpenSettings("ms-settings:network-proxy")));
        links.Children.Add(LinkCard("", "VPN", L("Ajouter ou gérer les connexions VPN intégrées à Windows."),
            () => ConnectionsPanel.OpenSettings("ms-settings:network-vpn")));
        links.Children.Add(LinkCard("", L("Point d'accès sans fil mobile"), L("Partager la connexion de ce PC avec d'autres appareils."),
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
            var adminRow = NetUi.Row(NetUi.Icon("", 11, "Pp.TextSecondary"), NetUi.Text(L("Administrateur"), "Pp.Caption", new Thickness(4, 0, 0, 0)));
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
        if (!await AppHost.Dialogs.ConfirmAsync(L("Renouveler l'adresse IP"),
                L("Toutes les cartes réseau vont rendre leur adresse IP puis en redemander une. La connexion (et un éventuel appel vidéo ou téléchargement) sera interrompue quelques secondes. Une autorisation administrateur est demandée."), L("Renouveler")))
            return;
        await NetworkUiActions.RunAsync(NetworkActionIds.RenewIp, null, [b]);
    }

    internal static async Task ResetAsync(Button? b)
    {
        if (!await AppHost.Dialogs.ConfirmAsync(L("Réinitialiser la pile réseau"),
                L("À n'utiliser que si Internet ne fonctionne plus malgré les autres outils (erreurs Winsock, « aucune connexion » persistante).\n\n• Le catalogue Winsock et la configuration TCP/IP sont remis à zéro (netsh winsock reset, netsh int ip reset).\n• Les adresses IP et DNS saisies manuellement sont effacées : notez-les avant si vous en utilisez.\n• Certains VPN, pare-feu tiers ou logiciels de filtrage devront peut-être être réinstallés.\n• Les réseaux Wi-Fi enregistrés et leurs mots de passe sont conservés.\n\nUn redémarrage est nécessaire. Une seconde confirmation s'affichera dans la fenêtre administrateur de Timonier."),
                L("Réinitialiser"), danger: true))
            return;
        await NetworkUiActions.RunAsync(NetworkActionIds.Reset, null, b is null ? [] : [b]);
    }

    private static void OpenNcpa()
    {
        try { ProcessRunner.Launch(SystemTool.Control, "ncpa.cpl"); }
        catch (Exception ex)
        {
            Log.Warn("Network", "ncpa.cpl : " + ex.Message);
            AppHost.Toasts.Show(L("Impossible d'ouvrir les connexions réseau."), ToastKind.Error);
        }
    }
}
