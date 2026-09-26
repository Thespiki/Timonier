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
        r.AddCategory(new CategoryInfo(Category, L("Network"), Glyph,
            L("Connections, DNS, hosts file, saved Wi-Fi, update sharing and network troubleshooting.")));

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

        r.AddPage(new PageInfo(PageId, L("Network"), Glyph, NavSection.Settings, 40, () => new NetworkPage())
        {
            CategoryId = Category,
            Description = L("Network adapters, family or encrypted DNS, site blocking (hosts), troubleshooting and saved Wi-Fi."),
            Keywords = [L("network, internet, wifi, wi-fi, ethernet, dns, ip, connection, hosts, ip address, parental controls, block a site")],
        });

        // Contrôles de santé : lecture locale uniquement (aucune requête vers Internet).
        r.AddHealthCheck(HealthCheck.Sync("network.connectivity", L("Network connection"), Glyph, PageId, CheckConnectivity));
        r.AddHealthCheck(HealthCheck.Sync("network.dns", L("DNS servers"), "", PageId, CheckDns));

        r.AddQuickAction(new QuickAction("network.flush-dns", L("Flush DNS cache"), "",
            L("Forgets saved site addresses (useful when a site stops opening after a change). No administrator rights needed."),
            async () =>
            {
                var outcome = await AppHost.Engine.RunActionAsync(NetworkActionIds.FlushDns);
                AppHost.Toasts.ShowOutcome(outcome);
            })
        {
            Keywords = [L("flushdns, ipconfig, dns cache, clear dns, flush dns, site won't open, site not loading")],
            Order = 40,
        });

        r.AddQuickAction(new QuickAction("network.reset", L("Reset network stack"), "",
            L("Last resort when the internet stops working: resets Winsock and TCP/IP (restart required, confirmation requested)."),
            async () => await ToolsPanel.ResetAsync(null))
        {
            Keywords = [L("winsock, netsh, network reset, fix internet, repair internet, no internet, tcp/ip")],
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
            return new HealthResult(HealthStatus.Warning, L("No active network connection"), L("Check the Wi-Fi, the cable or Airplane mode."));
        if (primary is null || !primary.HasGateway)
            return new HealthResult(HealthStatus.Warning, L("Connected without a gateway"),
                L("An adapter is active but no gateway is configured: internet access is unlikely."));
        return new HealthResult(HealthStatus.Good, L("Connected ({0})", primary.KindLabel),
            L("“{0}”, gateway {1}.", primary.Name, primary.Gateways[0]));
    }

    private static HealthResult CheckDns()
    {
        var primary = NetworkInfo.Primary(NetworkInfo.ReadAdapters());
        if (primary is null) return new HealthResult(HealthStatus.Unknown, L("No active adapter"));
        var provider = DnsProviders.Identify(primary.DnsServers);
        var servers = primary.DnsServers.Count > 0 ? string.Join(", ", primary.DnsServers.Take(2)) : L("none");
        if (!primary.DnsIsManual)
            return new HealthResult(HealthStatus.Info, L("Automatic DNS (provided by the network)"), servers);
        return new HealthResult(HealthStatus.Info,
            provider is null ? L("Custom DNS") : provider.IsFamily ? L("DNS: {0} (family filter)", provider.Name) : L("DNS: {0}", provider.Name),
            servers);
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        AddSection(r, "network.section.dns", L("Change DNS server"), L("Cloudflare, Quad9, Google, AdGuard, family DNS, custom, encrypted DNS"),
            "", "dns", [L("change dns, dns 1.1.1.1, cloudflare, quad9, google dns, adguard, opendns, fast dns, encrypted dns, doh")]);
        AddSection(r, "network.section.family-dns", L("Family filter via DNS"), L("Block adult sites across the whole PC (Cloudflare Family, AdGuard Family, OpenDNS FamilyShield)"),
            "", "dns", [L("parental controls, adult filter, child protection, block adult content, family, familyshield, kids safety")]);
        AddSection(r, "network.section.hosts", L("Block a site (hosts file)"), L("Add or remove sites blocked across the whole PC"),
            "", "hosts", [L("block site, hosts, hosts file, ban site, block website, website blocker")]);
        AddSection(r, "network.section.connections", L("IP address and network adapters"), L("IPv4/IPv6 addresses, gateway, DNS, DHCP, MAC address"),
            "", "connections", [L("ip address, my ip, ipconfig, gateway, mac, mac address, network adapter, network card, ethernet")]);
        AddSection(r, "network.section.tools", L("Network troubleshooting"), L("Flush DNS cache, renew IP address, reset network stack"),
            "", "tools", [L("troubleshooting, internet not working, renew ip, release renew, repair network, fix network")]);
        AddSection(r, "network.section.wifi", L("Saved Wi-Fi networks"), L("View and forget saved Wi-Fi networks"),
            "", "wifi", [L("forget wifi, delete wifi network, wifi profiles, known networks, forget network")]);

        AddWindowsSetting(r, "network.win.status", L("Network status (Windows Settings)"), L("Windows network overview and reset"),
            "ms-settings:network-status", [L("network status, network reset, network overview")]);
        AddWindowsSetting(r, "network.win.wifi", L("Wi-Fi (Windows Settings)"), L("Available networks, known networks, random hardware addresses"),
            "ms-settings:network-wifi", [L("wifi settings, wi-fi, random address, random hardware address")]);
        AddWindowsSetting(r, "network.win.proxy", L("Proxy (Windows Settings)"), L("Set up a proxy server or a setup script"),
            "ms-settings:network-proxy", [L("proxy, proxy settings, proxy server")]);
        AddWindowsSetting(r, "network.win.vpn", L("VPN (Windows Settings)"), L("Add or manage a VPN connection"),
            "ms-settings:network-vpn", [L("vpn, virtual private network")]);
        AddWindowsSetting(r, "network.win.hotspot", L("Mobile hotspot"), L("Share this PC's internet connection"),
            "ms-settings:network-mobilehotspot", [L("connection sharing, hotspot, access point, tethering, share internet")]);
        r.AddSearchEntry(new SearchEntry
        {
            Id = "network.win.ncpa",
            Title = L("Network Connections (ncpa.cpl)"),
            Subtitle = L("Classic panel for network adapters and their properties"),
            Glyph = "",
            Keywords = [L("ncpa, ncpa.cpl, network connections, ipv4 properties, network adapter")],
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
