using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Network;

/// <summary>
/// Réglages déclaratifs de la catégorie « network ». Toutes les valeurs proviennent des modèles d'administration
/// (ADMX) ou de la documentation Microsoft Learn citée en commentaire.
/// </summary>
internal static class NetworkTweaks
{
    public const string GroupConnections = "Connexions";
    public const string GroupDns = "DNS";
    public const string GroupUpdates = "Mises à jour et bande passante";
    public const string GroupAdvanced = "Protocoles (avancé)";

    // wcm.admx — « Réduire le nombre de connexions simultanées à Internet ou à un domaine Windows ».
    private const string Wcm = @"SOFTWARE\Policies\Microsoft\Windows\WcmSvc\GroupPolicy";
    // ICM.admx — « Désactiver les tests actifs de l'indicateur de statut de connectivité réseau Windows ».
    private const string Ncsi = @"SOFTWARE\Policies\Microsoft\Windows\NetworkConnectivityStatusIndicator";
    // wlansvc.admx (stratégie WiFiSense) — écrit hors de la branche Policies.
    private const string WifiSense = @"SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config";
    // Paramètres Internet de l'utilisateur (WinINet).
    private const string InetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    // DeliveryOptimization.admx.
    private const string DeliveryOpt = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    // DnsClient.admx — « Configurer la résolution de noms DNS over HTTPS (DoH) » (Windows 11).
    private const string DnsClient = @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient";
    // « Guidance for configuring IPv6 in Windows for advanced users » (Microsoft Learn).
    private const string Tcpip6 = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";

    public static IEnumerable<TweakDefinition> All()
    {
        // ------------------------------------------------------------------ Connexions
        yield return Tweak.Choice("network.wifi.minimize-connections", "Wi-Fi quand un câble Ethernet est branché",
                "Indique à Windows ce qu'il doit faire du Wi-Fi lorsqu'une connexion filaire est disponible. « Couper le Wi-Fi » " +
                "déconnecte automatiquement le Wi-Fi dès que l'Ethernet est actif (moins d'interférences, batterie économisée), " +
                "puis le réactive quand vous débranchez le câble.")
            .In(NetworkModule.Category, GroupConnections)
            .Keywords("ethernet", "câble", "wifi", "double connexion", "simultané", "minimize connections", "fMinimizeConnections")
            .OptionWithHelp("default", "Par défaut de Windows",
                "Windows limite les connexions simultanées mais peut garder le Wi-Fi associé.",
                Reg.LmDel(Wcm, "fMinimizeConnections"))
            .OptionWithHelp("prevent-wifi", "Couper le Wi-Fi sur Ethernet",
                "Le Wi-Fi se déconnecte tant qu'un câble Ethernet est connecté (Windows 10 1703 et plus récent).",
                Reg.LmDword(Wcm, "fMinimizeConnections", 3))
            .OptionWithHelp("simultaneous", "Autoriser les connexions simultanées",
                "Wi-Fi et Ethernet restent connectés en même temps (utile pour accéder à deux réseaux distincts).",
                Reg.LmDword(Wcm, "fMinimizeConnections", 0))
            .Requires(Requires.Wifi)
            .WindowsDefault("default")
            .Tags("office")
            .Build();

        yield return Tweak.Toggle("network.wifi.hotspot-autoconnect", "Connexion automatique aux points d'accès suggérés",
                "Fonction héritée de « Wi-Fi Sense » : permet à Windows de se connecter seul à des points d'accès ouverts suggérés " +
                "et à des hotspots payants partenaires. Le partage des réseaux avec vos contacts a été retiré en 2016, mais la " +
                "stratégie reste documentée : la désactiver garantit que Windows ne rejoint jamais un réseau ouvert sans votre accord.")
            .In(NetworkModule.Category, GroupConnections)
            .Keywords("wifi sense", "hotspot", "point d'accès", "réseau ouvert", "wifi public", "auto connect", "partage wifi")
            .WhenOn(Reg.LmDel(WifiSense, "AutoConnectAllowedOEM"))
            .WhenOff(Reg.LmDword(WifiSense, "AutoConnectAllowedOEM", 0))
            .Labels("Autorisée", "Bloquée")
            .Requires(Requires.Wifi)
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Tags("privacy-max", "security", "family")
            .Build();

        yield return Tweak.Toggle("network.ncsi.active-probe", "Test de connectivité Internet de Windows",
                "Pour afficher « Internet » ou « Pas d'accès Internet », Windows contacte régulièrement un serveur de Microsoft " +
                "(www.msftconnecttest.com). Le désactiver supprime ces requêtes, mais Windows se fie alors à des indices passifs.")
            .In(NetworkModule.Category, GroupConnections)
            .Keywords("ncsi", "msftconnecttest", "connectivité", "pas d'accès internet", "portail captif", "active probe", "icône réseau")
            .WhenOn(Reg.LmDel(Ncsi, "NoActiveProbe"))
            .WhenOff(Reg.LmDword(Ncsi, "NoActiveProbe", 1))
            .Risk(RiskLevel.Moderate)
            .Warning("Sans ce test, l'icône réseau peut afficher « Pas d'accès Internet » alors que tout fonctionne, la page de " +
                     "connexion des Wi-Fi d'hôtel, de gare ou d'entreprise (portail captif) ne s'ouvre plus d'elle-même, et certaines " +
                     "applications (Store, Office, Outlook) peuvent se croire hors ligne.")
            .WindowsDefault(TweakDefinition.On)
            .Tags("privacy-max")
            .Build();

        yield return Tweak.Toggle("network.proxy.manual", "Proxy manuel",
                "Active ou désactive le serveur proxy saisi dans Paramètres > Réseau et Internet > Proxy (le trafic web des " +
                "navigateurs et de nombreuses applications passe alors par ce serveur). Timonier ne modifie jamais l'adresse du proxy. " +
                "Un proxy inconnu apparu sans raison est un signe classique de logiciel malveillant ou publicitaire qui détourne votre trafic.")
            .In(NetworkModule.Category, GroupConnections)
            .Keywords("proxy", "serveur proxy", "mandataire", "ProxyEnable", "détournement", "hijack")
            .WhenOn(Reg.CuDword(InetSettings, "ProxyEnable", 1))
            .WhenOff(Reg.CuDword(InetSettings, "ProxyEnable", 0))
            .Labels("Utilisé", "Désactivé")
            .Risk(RiskLevel.Moderate)
            .Warning("Activez un proxy uniquement si vous savez qui l'administre (entreprise, école) : il peut lire ou modifier " +
                     "le trafic non chiffré. Relancez vos navigateurs pour qu'ils prennent en compte le changement.")
            .Requires(Requires.When(_ => ProxyConfigured(), "Aucun proxy manuel n'est configuré. Saisissez d'abord son adresse dans Paramètres > Réseau et Internet > Proxy."))
            .WindowsDefault(TweakDefinition.Off)
            .Build();

        // ------------------------------------------------------------------ DNS
        yield return Tweak.Choice("network.dns.doh-policy", "DNS chiffré (DNS over HTTPS)",
                "Stratégie de Windows 11 pour le chiffrement des requêtes DNS : empêche votre fournisseur d'accès ou un réseau Wi-Fi " +
                "public de voir (et de modifier) les noms des sites consultés. Le chiffrement n'est possible qu'avec des serveurs " +
                "compatibles (Cloudflare, Google, Quad9… : choisissez-les dans l'onglet DNS).")
            .In(NetworkModule.Category, GroupDns)
            .Keywords("doh", "dns over https", "dns chiffré", "dns sécurisé", "encrypted dns", "DoHPolicy")
            .OptionWithHelp("default", "Par défaut de Windows", "Chiffrement selon le réglage de chaque carte dans les Paramètres.",
                Reg.LmDel(DnsClient, "DoHPolicy"))
            .OptionWithHelp("allow", "Autoriser", "Chiffre les requêtes quand le serveur DNS le permet, sinon résolution classique.",
                Reg.LmDword(DnsClient, "DoHPolicy", 2))
            .OptionWithHelp("require", "Exiger", "Uniquement des requêtes chiffrées : sans serveur compatible, plus aucun site ne s'ouvre.",
                Reg.LmDword(DnsClient, "DoHPolicy", 3))
            .OptionWithHelp("prohibit", "Interdire", "Jamais de DNS chiffré (utile seulement pour un filtrage DNS d'entreprise).",
                Reg.LmDword(DnsClient, "DoHPolicy", 1))
            .Risk(RiskLevel.Moderate)
            .Warning("« Exiger » coupe l'accès à Internet si vos serveurs DNS ne sont pas compatibles DoH (cas des DNS fournis par une box). " +
                     "Une stratégie imposée grise l'option correspondante dans les Paramètres de Windows.")
            .Requires(Requires.Windows11)
            .WindowsDefault("default")
            .Recommend("allow")
            .Tags("privacy-max")
            .Build();

        // ------------------------------------------------------------------ Mises à jour et bande passante
        yield return Tweak.Choice("network.do.mode", "Partage des mises à jour entre PC",
                "L'Optimisation de la distribution permet de télécharger les mises à jour Windows et du Store depuis d'autres PC plutôt " +
                "que depuis Microsoft, et d'en envoyer. « Réseau local » économise la bande passante à la maison ou au bureau " +
                "sans rien envoyer à des inconnus sur Internet.")
            .In(NetworkModule.Category, GroupUpdates)
            .Keywords("optimisation de la distribution", "delivery optimization", "p2p", "pair à pair", "peer", "DODownloadMode", "bande passante", "upload")
            .OptionWithHelp("default", "Par défaut de Windows", "Selon la page Optimisation de la distribution des Paramètres (réseau local par défaut).",
                Reg.LmDel(DeliveryOpt, "DODownloadMode"))
            .OptionWithHelp("http", "Serveurs Microsoft uniquement", "Aucun échange avec d'autres PC (mode HTTP seul).",
                Reg.LmDword(DeliveryOpt, "DODownloadMode", 0))
            .OptionWithHelp("lan", "PC de mon réseau local", "Échanges limités aux PC du même réseau (derrière la même box).",
                Reg.LmDword(DeliveryOpt, "DODownloadMode", 1))
            .OptionWithHelp("internet", "Réseau local et Internet", "Échanges aussi avec des PC inconnus sur Internet (envoi de données en arrière-plan).",
                Reg.LmDword(DeliveryOpt, "DODownloadMode", 3))
            .WindowsDefault("default")
            .RecommendWhen(p => p.IsManaged ? null : "lan")
            .Tags("privacy-max", "office")
            .Build();

        yield return Tweak.Choice("network.do.background-bandwidth", "Bande passante des mises à jour en arrière-plan",
                "Plafonne la part de votre connexion utilisée par les téléchargements de mises à jour en arrière-plan. Par défaut, " +
                "Windows ajuste dynamiquement ; un plafond aide sur une connexion lente (ADSL, 4G partagée) au prix de mises à jour plus longues.")
            .In(NetworkModule.Category, GroupUpdates)
            .Keywords("bande passante", "limiter", "mises à jour", "windows update", "débit", "DOPercentageMaxBackgroundBandwidth", "connexion lente")
            .Option("default", "Automatique (Windows)", Reg.LmDel(DeliveryOpt, "DOPercentageMaxBackgroundBandwidth"))
            .Option("50", "50 % au maximum", Reg.LmDword(DeliveryOpt, "DOPercentageMaxBackgroundBandwidth", 50))
            .Option("20", "20 % au maximum", Reg.LmDword(DeliveryOpt, "DOPercentageMaxBackgroundBandwidth", 20))
            .WindowsDefault("default")
            .Build();

        // ------------------------------------------------------------------ Protocoles (avancé)
        yield return Tweak.Choice("network.ipv6.components", "Protocole IPv6",
                "Règle la priorité d'IPv6 via la valeur DisabledComponents documentée par Microsoft. « Préférer IPv4 » est la seule " +
                "option recommandée par Microsoft pour contourner un problème de réseau : IPv6 reste disponible. Désactiver IPv6 " +
                "n'accélère pas Internet.")
            .In(NetworkModule.Category, GroupAdvanced)
            .Keywords("ipv6", "ipv4", "DisabledComponents", "préférer ipv4", "désactiver ipv6", "tcpip6")
            .OptionWithHelp("default", "Par défaut (IPv6 prioritaire)", "Configuration d'origine de Windows.",
                Reg.LmDel(Tcpip6, "DisabledComponents"))
            .OptionWithHelp("prefer-ipv4", "Préférer IPv4", "IPv4 est utilisé en priorité quand un site est joignable des deux façons (valeur 0x20).",
                Reg.LmDword(Tcpip6, "DisabledComponents", 0x20))
            .OptionWithHelp("disabled", "Désactiver IPv6", "Désactive tous les composants IPv6 sauf la boucle locale (valeur 0xFF).",
                Reg.LmDword(Tcpip6, "DisabledComponents", 0xFF))
            .Risk(RiskLevel.Advanced)
            .Effect(ApplyEffect.Reboot)
            .Warning("Microsoft déconseille de désactiver IPv6 : Windows est testé avec IPv6 actif et certaines fonctions (Assistance à distance, " +
                     "DirectAccess, certains VPN et jeux en ligne, réseaux 100 % IPv6 de certains opérateurs mobiles) peuvent cesser de fonctionner. " +
                     "À réserver au diagnostic ; redémarrage nécessaire.")
            .WindowsDefault("default")
            .Build();
    }

    /// <summary>Un proxy manuel est-il saisi (ou déjà actif) pour l'utilisateur courant ? Lecture HKCU, instantanée.</summary>
    private static bool ProxyConfigured()
    {
        try
        {
            if (RegistryAccess.ReadDword(RegHive.CurrentUser, InetSettings, "ProxyEnable") == 1) return true;
            return !string.IsNullOrWhiteSpace(RegistryAccess.ReadString(RegHive.CurrentUser, InetSettings, "ProxyServer"));
        }
        catch (Exception)
        {
            return true; // en cas de doute, on n'empêche pas l'utilisateur de désactiver un proxy
        }
    }
}
