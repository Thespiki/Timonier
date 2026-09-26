using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Network;

/// <summary>
/// Réglages déclaratifs de la catégorie « network ». Toutes les valeurs proviennent des modèles d'administration
/// (ADMX) ou de la documentation Microsoft Learn citée en commentaire.
/// </summary>
internal static class NetworkTweaks
{
    public static readonly string GroupConnections = L("Connexions");
    public static readonly string GroupDns = L("DNS");
    public static readonly string GroupUpdates = L("Mises à jour et bande passante");
    public static readonly string GroupAdvanced = L("Protocoles (avancé)");

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
        yield return Tweak.Choice("network.wifi.minimize-connections", L("Wi-Fi quand un câble Ethernet est branché"),
                L("Indique à Windows ce qu'il doit faire du Wi-Fi lorsqu'une connexion filaire est disponible. « Couper le Wi-Fi » déconnecte automatiquement le Wi-Fi dès que l'Ethernet est actif (moins d'interférences, batterie économisée), puis le réactive quand vous débranchez le câble."))
            .In(NetworkModule.Category, GroupConnections)
            .Keywords(L("ethernet, câble, wifi, double connexion, simultané, minimize connections, fMinimizeConnections"))
            .OptionWithHelp("default", L("Par défaut de Windows"),
                L("Windows limite les connexions simultanées mais peut garder le Wi-Fi associé."),
                Reg.LmDel(Wcm, "fMinimizeConnections"))
            .OptionWithHelp("prevent-wifi", L("Couper le Wi-Fi sur Ethernet"),
                L("Le Wi-Fi se déconnecte tant qu'un câble Ethernet est connecté (Windows 10 1703 et plus récent)."),
                Reg.LmDword(Wcm, "fMinimizeConnections", 3))
            .OptionWithHelp("simultaneous", L("Autoriser les connexions simultanées"),
                L("Wi-Fi et Ethernet restent connectés en même temps (utile pour accéder à deux réseaux distincts)."),
                Reg.LmDword(Wcm, "fMinimizeConnections", 0))
            .Requires(Requires.Wifi)
            .WindowsDefault("default")
            .Tags("office")
            .Build();

        yield return Tweak.Toggle("network.wifi.hotspot-autoconnect", L("Connexion automatique aux points d'accès suggérés"),
                L("Fonction héritée de « Wi-Fi Sense » : permet à Windows de se connecter seul à des points d'accès ouverts suggérés et à des hotspots payants partenaires. Le partage des réseaux avec vos contacts a été retiré en 2016, mais la stratégie reste documentée : la désactiver garantit que Windows ne rejoint jamais un réseau ouvert sans votre accord."))
            .In(NetworkModule.Category, GroupConnections)
            .Keywords(L("wifi sense, hotspot, point d'accès, réseau ouvert, wifi public, auto connect, partage wifi"))
            .WhenOn(Reg.LmDel(WifiSense, "AutoConnectAllowedOEM"))
            .WhenOff(Reg.LmDword(WifiSense, "AutoConnectAllowedOEM", 0))
            .Labels(L("Autorisée"), L("Bloquée"))
            .Requires(Requires.Wifi)
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Tags("privacy-max", "security", "family")
            .Build();

        yield return Tweak.Toggle("network.ncsi.active-probe", L("Test de connectivité Internet de Windows"),
                L("Pour afficher « Internet » ou « Pas d'accès Internet », Windows contacte régulièrement un serveur de Microsoft (www.msftconnecttest.com). Le désactiver supprime ces requêtes, mais Windows se fie alors à des indices passifs."))
            .In(NetworkModule.Category, GroupConnections)
            .Keywords(L("ncsi, msftconnecttest, connectivité, pas d'accès internet, portail captif, active probe, icône réseau"))
            .WhenOn(Reg.LmDel(Ncsi, "NoActiveProbe"))
            .WhenOff(Reg.LmDword(Ncsi, "NoActiveProbe", 1))
            .Risk(RiskLevel.Moderate)
            .Warning(L("Sans ce test, l'icône réseau peut afficher « Pas d'accès Internet » alors que tout fonctionne, la page de connexion des Wi-Fi d'hôtel, de gare ou d'entreprise (portail captif) ne s'ouvre plus d'elle-même, et certaines applications (Store, Office, Outlook) peuvent se croire hors ligne."))
            .WindowsDefault(TweakDefinition.On)
            .Tags("privacy-max")
            .Build();

        // Pas de réglage « Proxy manuel » : Windows 10/11 gardent l'état du proxy dans DefaultConnectionSettings (binaire),
        // qui fait foi sur la seule valeur ProxyEnable ; un interrupteur sur ProxyEnable pourrait rester sans effet.
        // La page Proxy des Paramètres reste accessible (entrée de recherche et page Réseau › Outils).

        // ------------------------------------------------------------------ DNS
        yield return Tweak.Choice("network.dns.doh-policy", L("DNS chiffré (DNS over HTTPS)"),
                L("Stratégie de Windows 11 pour le chiffrement des requêtes DNS : empêche votre fournisseur d'accès ou un réseau Wi-Fi public de voir (et de modifier) les noms des sites consultés. Le chiffrement n'est possible qu'avec des serveurs compatibles (Cloudflare, Google, Quad9… : choisissez-les dans l'onglet DNS)."))
            .In(NetworkModule.Category, GroupDns)
            .Keywords(L("doh, dns over https, dns chiffré, dns sécurisé, encrypted dns, DoHPolicy"))
            .OptionWithHelp("default", L("Par défaut de Windows"), L("Chiffrement selon le réglage de chaque carte dans les Paramètres."),
                Reg.LmDel(DnsClient, "DoHPolicy"))
            .OptionWithHelp("allow", L("Autoriser"), L("Chiffre les requêtes quand le serveur DNS le permet, sinon résolution classique."),
                Reg.LmDword(DnsClient, "DoHPolicy", 2))
            .OptionWithHelp("require", L("Exiger"), L("Uniquement des requêtes chiffrées : sans serveur compatible, plus aucun site ne s'ouvre."),
                Reg.LmDword(DnsClient, "DoHPolicy", 3))
            .OptionWithHelp("prohibit", L("Interdire"), L("Jamais de DNS chiffré (utile seulement pour un filtrage DNS d'entreprise)."),
                Reg.LmDword(DnsClient, "DoHPolicy", 1))
            .Risk(RiskLevel.Moderate)
            .Warning(L("« Exiger » coupe l'accès à Internet si vos serveurs DNS ne sont pas compatibles DoH (cas des DNS fournis par une box). Une stratégie imposée grise l'option correspondante dans les Paramètres de Windows."))
            .Requires(Requires.Windows11)
            .WindowsDefault("default")
            .Recommend("allow")
            .Tags("privacy-max")
            .Build();

        // ------------------------------------------------------------------ Mises à jour et bande passante
        yield return Tweak.Choice("network.do.mode", L("Partage des mises à jour entre PC"),
                L("L'Optimisation de la distribution permet de télécharger les mises à jour Windows et du Store depuis d'autres PC plutôt que depuis Microsoft, et d'en envoyer. « Réseau local » économise la bande passante à la maison ou au bureau sans rien envoyer à des inconnus sur Internet."))
            .In(NetworkModule.Category, GroupUpdates)
            .Keywords(L("optimisation de la distribution, delivery optimization, p2p, pair à pair, peer, DODownloadMode, bande passante, upload"))
            .OptionWithHelp("default", L("Par défaut de Windows"), L("Selon la page Optimisation de la distribution des Paramètres (réseau local par défaut)."),
                Reg.LmDel(DeliveryOpt, "DODownloadMode"))
            .OptionWithHelp("http", L("Serveurs Microsoft uniquement"), L("Aucun échange avec d'autres PC (mode HTTP seul)."),
                Reg.LmDword(DeliveryOpt, "DODownloadMode", 0))
            .OptionWithHelp("lan", L("PC de mon réseau local"), L("Échanges limités aux PC du même réseau (derrière la même box)."),
                Reg.LmDword(DeliveryOpt, "DODownloadMode", 1))
            .OptionWithHelp("internet", L("Réseau local et Internet"), L("Échanges aussi avec des PC inconnus sur Internet (envoi de données en arrière-plan)."),
                Reg.LmDword(DeliveryOpt, "DODownloadMode", 3))
            .WindowsDefault("default")
            .RecommendWhen(p => p.IsManaged ? null : "lan")
            .Tags("privacy-max", "office")
            .Build();

        yield return Tweak.Choice("network.do.background-bandwidth", L("Bande passante des mises à jour en arrière-plan"),
                L("Plafonne la part de votre connexion utilisée par les téléchargements de mises à jour en arrière-plan. Par défaut, Windows ajuste dynamiquement ; un plafond aide sur une connexion lente (ADSL, 4G partagée) au prix de mises à jour plus longues."))
            .In(NetworkModule.Category, GroupUpdates)
            .Keywords(L("bande passante, limiter, mises à jour, windows update, débit, DOPercentageMaxBackgroundBandwidth, connexion lente"))
            .Option("default", L("Automatique (Windows)"), Reg.LmDel(DeliveryOpt, "DOPercentageMaxBackgroundBandwidth"))
            .Option("50", L("50 % au maximum"), Reg.LmDword(DeliveryOpt, "DOPercentageMaxBackgroundBandwidth", 50))
            .Option("20", L("20 % au maximum"), Reg.LmDword(DeliveryOpt, "DOPercentageMaxBackgroundBandwidth", 20))
            .WindowsDefault("default")
            .Build();

        // ------------------------------------------------------------------ Protocoles (avancé)
        yield return Tweak.Choice("network.ipv6.components", L("Protocole IPv6"),
                L("Règle la priorité d'IPv6 via la valeur DisabledComponents documentée par Microsoft. « Préférer IPv4 » est la seule option recommandée par Microsoft pour contourner un problème de réseau : IPv6 reste disponible. Désactiver IPv6 n'accélère pas Internet."))
            .In(NetworkModule.Category, GroupAdvanced)
            .Keywords(L("ipv6, ipv4, DisabledComponents, préférer ipv4, désactiver ipv6, tcpip6"))
            .OptionWithHelp("default", L("Par défaut (IPv6 prioritaire)"), L("Configuration d'origine de Windows."),
                Reg.LmDel(Tcpip6, "DisabledComponents"))
            .OptionWithHelp("prefer-ipv4", L("Préférer IPv4"), L("IPv4 est utilisé en priorité quand un site est joignable des deux façons (valeur 0x20)."),
                Reg.LmDword(Tcpip6, "DisabledComponents", 0x20))
            .OptionWithHelp("disabled", L("Désactiver IPv6"), L("Désactive tous les composants IPv6 sauf la boucle locale (valeur 0xFF)."),
                Reg.LmDword(Tcpip6, "DisabledComponents", 0xFF))
            .Risk(RiskLevel.Advanced)
            .Effect(ApplyEffect.Reboot)
            .Warning(L("Microsoft déconseille de désactiver IPv6 : Windows est testé avec IPv6 actif et certaines fonctions (Assistance à distance, DirectAccess, certains VPN et jeux en ligne, réseaux 100 % IPv6 de certains opérateurs mobiles) peuvent cesser de fonctionner. À réserver au diagnostic ; redémarrage nécessaire."))
            .WindowsDefault("default")
            .Build();
    }
}
