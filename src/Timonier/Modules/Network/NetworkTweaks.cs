using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Network;

/// <summary>
/// Réglages déclaratifs de la catégorie « network ». Toutes les valeurs proviennent des modèles d'administration
/// (ADMX) ou de la documentation Microsoft Learn citée en commentaire.
/// </summary>
internal static class NetworkTweaks
{
    public static readonly string GroupConnections = L("Connections");
    public static readonly string GroupDns = L("DNS");
    public static readonly string GroupUpdates = L("Updates and bandwidth");
    public static readonly string GroupAdvanced = L("Protocols (advanced)");

    // wcm.admx — « Réduire le nombre de connexions simultanées à Internet ou à un domaine Windows ».
    private const string Wcm = @"SOFTWARE\Policies\Microsoft\Windows\WcmSvc\GroupPolicy";
    // ICM.admx — « Désactiver les tests actifs de l'indicateur de statut de connectivité réseau Windows ».
    private const string Ncsi = @"SOFTWARE\Policies\Microsoft\Windows\NetworkConnectivityStatusIndicator";
    // wlansvc.admx (stratégie WiFiSense) — écrit hors de la branche Policies.
    private const string WifiSense = @"SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config";
    // Paramètres Internet de l'utilisateur (WinINet).
    // DeliveryOptimization.admx.
    private const string DeliveryOpt = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    // DnsClient.admx — « Configurer la résolution de noms DNS over HTTPS (DoH) » (Windows 11).
    private const string DnsClient = @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient";
    // « Guidance for configuring IPv6 in Windows for advanced users » (Microsoft Learn).
    private const string Tcpip6 = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";

    public static IEnumerable<TweakDefinition> All()
    {
        // ------------------------------------------------------------------ Connexions
        yield return Tweak.Choice("network.wifi.minimize-connections", L("Wi-Fi when an Ethernet cable is plugged in"),
                L("Tells Windows what to do with Wi-Fi when a wired connection is available. “Turn off Wi-Fi” automatically disconnects Wi-Fi as soon as Ethernet is active (less interference, battery saved), then turns it back on when you unplug the cable."))
            .In(NetworkModule.Category, GroupConnections)
            .Keywords(L("ethernet, cable, wifi, dual connection, simultaneous, minimize connections, fMinimizeConnections"))
            .OptionWithHelp("default", L("Windows default"),
                L("Windows limits simultaneous connections but may keep Wi-Fi associated."),
                Reg.LmDel(Wcm, "fMinimizeConnections"))
            .OptionWithHelp("prevent-wifi", L("Turn off Wi-Fi on Ethernet"),
                L("Wi-Fi disconnects while an Ethernet cable is connected (Windows 10 1703 and later)."),
                Reg.LmDword(Wcm, "fMinimizeConnections", 3))
            .OptionWithHelp("simultaneous", L("Allow simultaneous connections"),
                L("Wi-Fi and Ethernet stay connected at the same time (useful for reaching two separate networks)."),
                Reg.LmDword(Wcm, "fMinimizeConnections", 0))
            .Requires(Requires.Wifi)
            .WindowsDefault("default")
            .Tags("office")
            .Build();

        yield return Tweak.Toggle("network.wifi.hotspot-autoconnect", L("Automatic connection to suggested hotspots"),
                L("Legacy feature from “Wi-Fi Sense”: lets Windows connect on its own to suggested open hotspots and partner paid hotspots. Sharing networks with your contacts was removed in 2016, but the policy is still documented: turning it off ensures Windows never joins an open network without your consent."))
            .In(NetworkModule.Category, GroupConnections)
            .Keywords(L("wifi sense, hotspot, access point, open network, public wifi, auto connect, wifi sharing"))
            .WhenOn(Reg.LmDel(WifiSense, "AutoConnectAllowedOEM"))
            .WhenOff(Reg.LmDword(WifiSense, "AutoConnectAllowedOEM", 0))
            .Labels(LC("feminine", "Allowed"), LC("feminine", "Blocked"))
            .Requires(Requires.Wifi)
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Tags("privacy-max", "security", "family")
            .Build();

        yield return Tweak.Toggle("network.ncsi.active-probe", L("Windows internet connectivity test"),
                L("To show “Internet” or “No internet access”, Windows regularly contacts a Microsoft server (www.msftconnecttest.com). Turning it off stops these requests, but Windows then relies on passive clues."))
            .In(NetworkModule.Category, GroupConnections)
            .Keywords(L("ncsi, msftconnecttest, connectivity, no internet access, captive portal, active probe, network icon"))
            .WhenOn(Reg.LmDel(Ncsi, "NoActiveProbe"))
            .WhenOff(Reg.LmDword(Ncsi, "NoActiveProbe", 1))
            .Risk(RiskLevel.Moderate)
            .Warning(L("Without this test, the network icon may show “No internet access” even though everything works, the sign-in page of hotel, station or company Wi-Fi (captive portal) no longer opens by itself, and some apps (Store, Office, Outlook) may think they're offline."))
            .WindowsDefault(TweakDefinition.On)
            .Tags("privacy-max")
            .Build();

        // Pas de réglage « Proxy manuel » : Windows 10/11 gardent l'état du proxy dans DefaultConnectionSettings (binaire),
        // qui fait foi sur la seule valeur ProxyEnable ; un interrupteur sur ProxyEnable pourrait rester sans effet.
        // La page Proxy des Paramètres reste accessible (entrée de recherche et page Réseau › Outils).

        // ------------------------------------------------------------------ DNS
        yield return Tweak.Choice("network.dns.doh-policy", L("Encrypted DNS (DNS over HTTPS)"),
                L("Windows 11 policy for encrypting DNS queries: prevents your internet provider or a public Wi-Fi network from seeing (and changing) the names of the sites you visit. Encryption only works with compatible servers (Cloudflare, Google, Quad9…: choose them in the DNS tab)."))
            .In(NetworkModule.Category, GroupDns)
            .Keywords(L("doh, dns over https, encrypted dns, secure dns, DoHPolicy"))
            .OptionWithHelp("default", L("Windows default"), L("Encryption follows each adapter's setting in Settings."),
                Reg.LmDel(DnsClient, "DoHPolicy"))
            .OptionWithHelp("allow", L("Allow"), L("Encrypts queries when the DNS server supports it, otherwise uses regular resolution."),
                Reg.LmDword(DnsClient, "DoHPolicy", 2))
            .OptionWithHelp("require", L("Require"), L("Encrypted queries only: without a compatible server, no site will open at all."),
                Reg.LmDword(DnsClient, "DoHPolicy", 3))
            .OptionWithHelp("prohibit", L("Prohibit"), L("Never use encrypted DNS (only useful for corporate DNS filtering)."),
                Reg.LmDword(DnsClient, "DoHPolicy", 1))
            .Risk(RiskLevel.Moderate)
            .Warning(L("“Require” cuts off internet access if your DNS servers aren't DoH-compatible (as with DNS provided by a router). An enforced policy grays out the matching option in Windows Settings."))
            .Requires(Requires.Windows11)
            .WindowsDefault("default")
            .Recommend("allow")
            .Tags("privacy-max")
            .Build();

        // ------------------------------------------------------------------ Mises à jour et bande passante
        yield return Tweak.Choice("network.do.mode", L("Update sharing between PCs"),
                L("Delivery Optimization lets you download Windows and Microsoft Store updates from other PCs instead of from Microsoft, and send updates to them. “Local network” saves bandwidth at home or at the office without sending anything to strangers on the internet."))
            .In(NetworkModule.Category, GroupUpdates)
            .Keywords(L("delivery optimization, p2p, peer to peer, peer, DODownloadMode, bandwidth, upload, update sharing"))
            .OptionWithHelp("default", L("Windows default"), L("Follows the Delivery Optimization page in Settings (local network by default)."),
                Reg.LmDel(DeliveryOpt, "DODownloadMode"))
            .OptionWithHelp("http", L("Microsoft servers only"), L("No exchanges with other PCs (HTTP only mode)."),
                Reg.LmDword(DeliveryOpt, "DODownloadMode", 0))
            .OptionWithHelp("lan", L("PCs on my local network"), L("Exchanges limited to PCs on the same network (behind the same router)."),
                Reg.LmDword(DeliveryOpt, "DODownloadMode", 1))
            .OptionWithHelp("internet", L("Local network and internet"), L("Also exchanges with unknown PCs on the internet (sends data in the background)."),
                Reg.LmDword(DeliveryOpt, "DODownloadMode", 3))
            .WindowsDefault("default")
            .RecommendWhen(p => p.IsManaged ? null : "lan")
            .Tags("privacy-max", "office")
            .Build();

        yield return Tweak.Choice("network.do.background-bandwidth", L("Background update bandwidth"),
                L("Caps the share of your connection used by background update downloads. By default, Windows adjusts dynamically; a cap helps on a slow connection (DSL, shared 4G) at the cost of longer updates."))
            .In(NetworkModule.Category, GroupUpdates)
            .Keywords(L("bandwidth, limit, updates, windows update, speed, throughput, DOPercentageMaxBackgroundBandwidth, slow connection"))
            .Option("default", L("Automatic (Windows)"), Reg.LmDel(DeliveryOpt, "DOPercentageMaxBackgroundBandwidth"))
            .Option("50", L("50% maximum"), Reg.LmDword(DeliveryOpt, "DOPercentageMaxBackgroundBandwidth", 50))
            .Option("20", L("20% maximum"), Reg.LmDword(DeliveryOpt, "DOPercentageMaxBackgroundBandwidth", 20))
            .WindowsDefault("default")
            .Build();

        // ------------------------------------------------------------------ Protocoles (avancé)
        yield return Tweak.Choice("network.ipv6.components", L("IPv6 protocol"),
                L("Sets IPv6 priority through the DisabledComponents value documented by Microsoft. “Prefer IPv4” is the only option Microsoft recommends to work around a network issue: IPv6 remains available. Disabling IPv6 doesn't speed up the internet."))
            .In(NetworkModule.Category, GroupAdvanced)
            .Keywords(L("ipv6, ipv4, DisabledComponents, prefer ipv4, disable ipv6, tcpip6"))
            .OptionWithHelp("default", L("Default (IPv6 preferred)"), L("Windows' original configuration."),
                Reg.LmDel(Tcpip6, "DisabledComponents"))
            .OptionWithHelp("prefer-ipv4", L("Prefer IPv4"), L("IPv4 is used first when a site is reachable both ways (value 0x20)."),
                Reg.LmDword(Tcpip6, "DisabledComponents", 0x20))
            .OptionWithHelp("disabled", L("Disable IPv6"), L("Disables all IPv6 components except loopback (value 0xFF)."),
                Reg.LmDword(Tcpip6, "DisabledComponents", 0xFF))
            .Risk(RiskLevel.Advanced)
            .Effect(ApplyEffect.Reboot)
            .Warning(L("Microsoft advises against disabling IPv6: Windows is tested with IPv6 enabled, and some features (Remote Assistance, DirectAccess, some VPNs and online games, IPv6-only networks of some mobile carriers) may stop working. Use for troubleshooting only; restart required."))
            .WindowsDefault("default")
            .Build();
    }
}
