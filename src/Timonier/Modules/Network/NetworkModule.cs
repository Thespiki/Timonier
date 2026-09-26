using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Network;

/// <summary>
/// Réseau : état des connexions, DNS (familial, chiffré, personnalisé), fichier hosts, dépannage, réseaux Wi-Fi
/// enregistrés et réglages (Optimisation de la distribution, proxy, IPv6…). Déclarations uniquement : appelé aussi dans le broker.
/// Timonier n'initie aucune connexion réseau : les états affichés sont ceux que Windows connaît déjà.
/// </summary>
public sealed class NetworkModule : IModule
{
    public const string Category = "network";
    public const string PageId = "network";
    private const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, L("Réseau"), Glyph,
            L("Connexions, DNS, fichier hosts, Wi-Fi enregistrés, partage des mises à jour et dépannage réseau.")));

        r.AddTweaks(NetworkTweaks.All());

        r.AddAction(new SetDnsAction());
        r.AddAction(new FlushDnsAction());
        r.AddAction(new HostsAddAction());
        r.AddAction(new HostsRemoveAction());
        r.AddAction(new HostsClearAction());
        r.AddAction(new RenewIpAction());
        r.AddAction(new NetworkResetAction());
        r.AddAction(new WifiForgetAction());
        r.AddAction(new WifiForgetAdminAction());

        r.AddPage(new PageInfo(PageId, L("Réseau"), Glyph, NavSection.Settings, 40, () => new NetworkPage())
        {
            CategoryId = Category,
            Description = L("Cartes réseau, DNS familial ou chiffré, blocage de sites (hosts), dépannage et Wi-Fi enregistrés."),
            Keywords = [L("réseau, internet, wifi, wi-fi, ethernet, dns, ip, connexion, hosts, network, adresse ip, contrôle parental, bloquer un site")],
        });

        // Contrôles de santé : lecture locale uniquement (aucune requête vers Internet).
        r.AddHealthCheck(HealthCheck.Sync("network.connectivity", L("Connexion réseau"), Glyph, PageId, CheckConnectivity));
        r.AddHealthCheck(HealthCheck.Sync("network.dns", L("Serveurs DNS"), "", PageId, CheckDns));

        r.AddQuickAction(new QuickAction("network.flush-dns", L("Vider le cache DNS"), "",
            L("Oublie les adresses mémorisées des sites (utile quand un site ne s'ouvre plus après un changement). Sans droits administrateur."),
            async () =>
            {
                var outcome = await AppHost.Engine.RunActionAsync(NetworkActionIds.FlushDns);
                AppHost.Toasts.ShowOutcome(outcome);
            })
        {
            Keywords = [L("flushdns, ipconfig, cache dns, vider dns, site ne s'ouvre pas, dns cache")],
            Order = 40,
        });

        r.AddQuickAction(new QuickAction("network.reset", L("Réinitialiser la pile réseau"), "",
            L("Dernier recours quand Internet ne fonctionne plus : réinitialise Winsock et TCP/IP (redémarrage requis, confirmation demandée)."),
            async () => await ToolsPanel.ResetAsync(null))
        {
            Keywords = [L("winsock, netsh, reset réseau, réparer internet, plus d'internet, network reset, tcp/ip")],
            Order = 90,
            RequiresAdmin = true,
        });

        RegisterSearchEntries(r);

        Synonyms.AddGroup("dns", "serveur dns", "resolution de noms", "name server");
        Synonyms.AddGroup("hosts", "fichier hosts", "hosts file", "bloquer site", "bloquer un site", "blocage site");
        Synonyms.AddGroup("dns familial", "filtre familial", "controle parental", "filtre adulte", "family dns", "safe search");
        Synonyms.AddGroup("doh", "dns over https", "dns chiffre", "dns securise", "encrypted dns");
        Synonyms.AddGroup("wifi", "wi-fi", "sans fil", "wlan", "wireless");
        Synonyms.AddGroup("optimisation de la distribution", "delivery optimization", "partage mises a jour", "p2p windows update");
        Synonyms.AddGroup("reinitialiser reseau", "network reset", "winsock reset", "reparer internet");
    }

    private static HealthResult CheckConnectivity()
    {
        var adapters = NetworkInfo.ReadAdapters();
        var up = adapters.Where(a => a.IsUp).ToList();
        var primary = NetworkInfo.Primary(adapters);
        if (up.Count == 0)
            return new HealthResult(HealthStatus.Warning, L("Aucune connexion réseau active"), L("Vérifiez le Wi-Fi, le câble ou le mode Avion."));
        if (primary is null || !primary.HasGateway)
            return new HealthResult(HealthStatus.Warning, L("Connecté sans passerelle"),
                L("Une carte est active mais aucune passerelle n'est configurée : l'accès à Internet est peu probable."));
        return new HealthResult(HealthStatus.Good, L("Connecté ({0})", primary.KindLabel),
            L("« {0} », passerelle {1}.", primary.Name, primary.Gateways[0]));
    }

    private static HealthResult CheckDns()
    {
        var primary = NetworkInfo.Primary(NetworkInfo.ReadAdapters());
        if (primary is null) return new HealthResult(HealthStatus.Unknown, L("Aucune carte active"));
        var provider = DnsProviders.Identify(primary.DnsServers);
        var servers = primary.DnsServers.Count > 0 ? string.Join(", ", primary.DnsServers.Take(2)) : L("aucun");
        if (!primary.DnsIsManual)
            return new HealthResult(HealthStatus.Info, L("DNS automatiques (fournis par le réseau)"), servers);
        return new HealthResult(HealthStatus.Info,
            provider is null ? L("DNS personnalisés") : provider.IsFamily ? L("DNS : {0} (filtre familial)", provider.Name) : L("DNS : {0}", provider.Name),
            servers);
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        AddSection(r, "network.section.dns", L("Changer de serveur DNS"), L("Cloudflare, Quad9, Google, AdGuard, DNS familial, personnalisé, DNS chiffré"),
            "", "dns", [L("changer dns, dns 1.1.1.1, cloudflare, quad9, google dns, adguard, opendns, dns rapide, dns chiffre")]);
        AddSection(r, "network.section.family-dns", L("Filtre familial par DNS"), L("Bloquer les sites pour adultes sur tout le PC (Cloudflare Famille, AdGuard Famille, OpenDNS FamilyShield)"),
            "", "dns", [L("controle parental, filtre adulte, protection enfant, bloquer contenu adulte, family, familyshield")]);
        AddSection(r, "network.section.hosts", L("Bloquer un site (fichier hosts)"), L("Ajouter ou retirer des sites bloqués sur tout le PC"),
            "", "hosts", [L("bloquer site, hosts, fichier hosts, interdire site, block website")]);
        AddSection(r, "network.section.connections", L("Adresse IP et cartes réseau"), L("Adresses IPv4/IPv6, passerelle, DNS, DHCP, adresse MAC"),
            "", "connections", [L("adresse ip, mon ip, ipconfig, passerelle, mac, adresse mac, carte reseau, ethernet")]);
        AddSection(r, "network.section.tools", L("Dépannage réseau"), L("Vider le cache DNS, renouveler l'adresse IP, réinitialiser la pile réseau"),
            "", "tools", [L("depannage, internet ne marche pas, renouveler ip, release renew, reparer reseau")]);
        AddSection(r, "network.section.wifi", L("Réseaux Wi-Fi enregistrés"), L("Voir et oublier les réseaux Wi-Fi mémorisés"),
            "", "wifi", [L("oublier wifi, supprimer reseau wifi, profils wifi, reseaux connus, forget network")]);

        AddWindowsSetting(r, "network.win.status", L("État du réseau (Paramètres Windows)"), L("Vue d'ensemble et réinitialisation du réseau de Windows"),
            "ms-settings:network-status", [L("etat reseau, network status, reinitialisation du reseau")]);
        AddWindowsSetting(r, "network.win.wifi", L("Wi-Fi (Paramètres Windows)"), L("Réseaux disponibles, réseaux connus, adresses matérielles aléatoires"),
            "ms-settings:network-wifi", [L("parametres wifi, wifi settings, adresse aleatoire")]);
        AddWindowsSetting(r, "network.win.proxy", L("Proxy (Paramètres Windows)"), L("Configurer un serveur proxy ou un script de configuration"),
            "ms-settings:network-proxy", [L("proxy, parametres proxy, proxy settings")]);
        AddWindowsSetting(r, "network.win.vpn", L("VPN (Paramètres Windows)"), L("Ajouter ou gérer une connexion VPN"),
            "ms-settings:network-vpn", [L("vpn, reseau prive virtuel")]);
        AddWindowsSetting(r, "network.win.hotspot", L("Point d'accès sans fil mobile"), L("Partager la connexion Internet de ce PC"),
            "ms-settings:network-mobilehotspot", [L("partage connexion, hotspot, point d'acces, tethering")]);
        r.AddSearchEntry(new SearchEntry
        {
            Id = "network.win.ncpa",
            Title = L("Connexions réseau (ncpa.cpl)"),
            Subtitle = L("Panneau classique des cartes réseau et de leurs propriétés"),
            Glyph = "",
            Keywords = [L("ncpa, ncpa.cpl, connexions reseau, proprietes ipv4, carte reseau")],
            Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.Launch(SystemTool.Control, "ncpa.cpl"),
        });
    }

    private static void AddSection(ModuleRegistry r, string id, string title, string subtitle, string glyph, string tab, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id,
            Title = title,
            Subtitle = subtitle,
            Glyph = glyph,
            Keywords = keywords,
            PageId = PageId,
            PageParameter = "section:" + tab,
        });

    private static void AddWindowsSetting(ModuleRegistry r, string id, string title, string subtitle, string uri, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id,
            Title = title,
            Subtitle = subtitle,
            Glyph = "",
            Keywords = keywords,
            Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri(uri),
        });
}
