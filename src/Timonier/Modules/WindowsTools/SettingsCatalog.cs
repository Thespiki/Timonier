namespace Timonier.Modules.WindowsTools;

/// <summary>Lien direct vers une page de l'application Paramètres (URI ms-settings: constante).</summary>
internal sealed record SettingLink(string Key, string Title, string Glyph, string[] Keywords)
{
    /// <summary>Page parente dans Paramètres (null = page de premier niveau de la section).</summary>
    public string? Parent { get; init; }
    /// <summary>Build minimal (22000 = Windows 11) ; en dessous, le lien n'est ni affiché ni indexé.</summary>
    public int MinBuild { get; init; }
    /// <summary>Faux si un autre module indexe déjà cette page dans la recherche (évite les doublons).</summary>
    public bool InSearch { get; init; } = true;
    public SettingsSection Section { get; set; } = null!;

    public string Uri => "ms-settings:" + Key;
    public string Id => "wintools.ms." + Key;
    public bool Windows11Only => MinBuild >= 22000;

    /// <summary>Chemin affiché : « Paramètres › Section › Page parente ».</summary>
    public string Path => Parent is null ? "Paramètres › " + Section.Title : "Paramètres › " + Section.Title + " › " + Parent;

    public SettingLink Win11() => this with { MinBuild = Math.Max(MinBuild, 22000) };
    public SettingLink Min(int build) => this with { MinBuild = build };
    public SettingLink Under(string parent) => this with { Parent = parent };
    public SettingLink NoSearch() => this with { InSearch = false };
}

internal sealed class SettingsSection(string key, string title, string glyph, SettingLink[] links)
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public IReadOnlyList<SettingLink> Links { get; } = links;
}

/// <summary>
/// Liens vers l'application Paramètres de Windows. Toutes les URI ont été vérifiées dans les ressources de
/// Windows 11 25H2 ; celles propres à Windows 11 (ou à une version précise) portent un build minimal.
/// </summary>
internal static class SettingsCatalog
{
    private static readonly Lazy<IReadOnlyList<SettingsSection>> AvailableSections = new(() =>
    {
        var build = WinInfo.Build;
        var list = new List<SettingsSection>();
        foreach (var s in All())
        {
            var links = s.Links.Where(l => l.MinBuild <= build).ToArray();
            if (links.Length == 0) continue;
            var section = new SettingsSection(s.Key, s.Title, s.Glyph, links);
            foreach (var l in links) l.Section = section;
            list.Add(section);
        }
        return list;
    });

    /// <summary>Sections et liens disponibles sur cette version de Windows.</summary>
    public static IReadOnlyList<SettingsSection> Sections => AvailableSections.Value;

    public static IEnumerable<SettingLink> Links => Sections.SelectMany(s => s.Links);

    private static SettingLink L(string key, string title, string glyph, params string[] keywords) => new(key, title, glyph, keywords);

    private static IEnumerable<SettingsSection> All()
    {
        yield return new SettingsSection("system", "Système", "",
        [
            L("display", "Écran", "", "affichage", "resolution", "mise a l echelle", "echelle", "plusieurs ecrans", "display").NoSearch(),
            L("nightlight", "Éclairage nocturne", "", "lumiere bleue", "night light", "filtre lumiere bleue").Under("Écran"),
            L("display-advanced", "Affichage avancé", "", "frequence d actualisation", "taux de rafraichissement", "hz", "refresh rate", "informations ecran").Under("Écran"),
            L("display-advancedgraphics", "Graphiques", "", "gpu par application", "preference graphique").Under("Écran").NoSearch(),
            L("display-hdr", "HDR", "", "hdr", "high dynamic range", "video hdr", "auto hdr").Under("Écran").Win11(),
            L("sound", "Son", "", "audio", "sortie audio", "entree audio", "sound"),
            L("sound-devices", "Tous les appareils audio", "", "peripheriques audio", "haut parleurs", "casque", "micro").Under("Son"),
            L("apps-volume", "Mélangeur de volume", "", "volume par application", "volume mixer", "sortie par application").Under("Son"),
            L("notifications", "Notifications", "", "notifications", "bannieres", "ne pas deranger", "centre de notifications"),
            L("quiethours", "Concentration", "", "assistant de concentration", "focus", "focus assist", "session de concentration", "minuteur"),
            L("powersleep", "Alimentation", "", "alimentation", "mise en veille", "ecran eteint").NoSearch(),
            L("batterysaver", "Économiseur d'énergie", "", "economiseur de batterie", "battery saver", "energy saver").NoSearch(),
            L("energyrecommendations", "Recommandations énergétiques", "", "economie d energie", "energy recommendations", "empreinte carbone").Under("Alimentation").Min(22621),
            L("storagesense", "Stockage", "", "espace disque", "stockage").NoSearch(),
            L("storagepolicies", "Assistant Stockage", "", "storage sense", "nettoyage automatique", "vider la corbeille automatiquement").Under("Stockage"),
            L("storagerecommendations", "Recommandations de nettoyage", "", "liberer de l espace", "gros fichiers", "fichiers inutilises").Under("Stockage").Win11(),
            L("disksandvolumes", "Disques et volumes", "", "partitions", "volumes", "sante du disque", "lettre de lecteur").Under("Stockage").Win11(),
            L("savelocations", "Emplacement d'enregistrement du nouveau contenu", "", "emplacement par defaut", "enregistrer sur un autre disque", "save locations").Under("Stockage"),
            L("multitasking", "Multitâche", "", "ancrer les fenetres", "snap", "alt tab", "bureaux virtuels", "secouer la barre de titre"),
            L("activation", "Activation", "", "activer windows", "cle de produit", "licence", "product key", "edition"),
            L("troubleshoot", "Résolution des problèmes", "", "depannage", "utilitaires de resolution", "troubleshooter"),
            L("recovery", "Récupération", "", "reinitialiser ce pc", "demarrage avance").NoSearch(),
            L("project", "Projection sur ce PC", "", "miracast", "projeter", "ecran sans fil", "affichage sans fil"),
            L("remotedesktop", "Bureau à distance", "", "rdp", "bureau a distance", "remote desktop", "prise de controle"),
            L("clipboard", "Presse-papiers", "", "historique du presse papiers", "windows v", "clipboard", "copier coller"),
            L("crossdevice", "Partage de proximité", "", "partage a proximite", "nearby sharing", "envoyer a un pc proche").Win11(),
            L("optionalfeatures", "Fonctionnalités facultatives", "", "fonctionnalites facultatives", "optional features", "ajouter une fonctionnalite", "rsat", "openssh"),
            L("systemcomponents", "Composants système", "", "composants systeme", "system components", "applications systeme").Min(26100),
            L("developers", "Pour les développeurs", "", "mode developpeur", "developer mode", "sudo", "powershell execution", "developpeur"),
            L("about", "Informations système", "", "a propos", "about", "specifications", "nom du pc", "caracteristiques", "version de windows", "renommer ce pc"),
        ]);

        yield return new SettingsSection("devices", "Bluetooth et appareils", "",
        [
            L("bluetooth", "Bluetooth et appareils", "", "bluetooth", "appairer").NoSearch(),
            L("devices", "Appareils", "", "ajouter un appareil", "peripheriques connectes", "devices"),
            L("printers", "Imprimantes et scanners", "", "imprimante", "scanner").NoSearch(),
            L("mobile-devices", "Appareils mobiles", "", "lien avec le telephone", "phone link", "telephone android", "iphone", "smartphone").Min(22631),
            L("camera", "Caméras", "", "webcam", "parametres camera", "luminosite camera", "cameras reseau").Win11(),
            L("mousetouchpad", "Souris", "", "souris", "vitesse du pointeur").NoSearch(),
            L("devices-touchpad", "Pavé tactile", "", "touchpad", "gestes").NoSearch(),
            L("pen", "Stylet et Windows Ink", "", "stylet", "pen", "windows ink", "ecriture manuscrite", "tablette graphique"),
            L("autoplay", "Exécution automatique", "", "autoplay", "lecture automatique", "cle usb inseree", "carte memoire"),
            L("usb", "USB", "", "usb", "notifications usb").NoSearch(),
        ]);

        yield return new SettingsSection("network", "Réseau et Internet", "",
        [
            L("network-status", "Réseau et Internet", "", "etat du reseau").NoSearch(),
            L("network-wifi", "Wi-Fi", "", "wifi").NoSearch(),
            L("network-wifisettings", "Gérer les réseaux connus", "", "reseaux connus", "oublier un reseau", "known networks", "wifi enregistres").Under("Wi-Fi"),
            L("network-ethernet", "Ethernet", "", "cable reseau", "rj45", "ethernet", "connexion filaire", "connexion limitee"),
            L("network-vpn", "VPN", "", "vpn").NoSearch(),
            L("network-mobilehotspot", "Point d'accès sans fil mobile", "", "hotspot").NoSearch(),
            L("network-airplanemode", "Mode avion", "", "mode avion").NoSearch(),
            L("network-proxy", "Proxy", "", "proxy").NoSearch(),
            L("network-dialup", "Accès à distance", "", "numerotation", "dial up", "pppoe", "connexion haut debit", "modem"),
            L("network-advancedsettings", "Paramètres réseau avancés", "", "cartes reseau", "reinitialisation du reseau", "network reset", "desactiver une carte reseau").Win11(),
            L("network-advancedsharing", "Paramètres de partage avancés", "", "partage de fichiers", "decouverte du reseau", "partage d imprimantes", "network discovery").Under("Paramètres réseau avancés").Min(22621),
            L("datausage", "Utilisation des données", "", "consommation de donnees", "data usage", "limite de donnees", "connexion limitee").Under("Paramètres réseau avancés"),
        ]);

        yield return new SettingsSection("personalization", "Personnalisation", "",
        [
            L("personalization", "Personnalisation", "", "personnaliser", "apparence", "personalization"),
            L("personalization-background", "Arrière-plan", "", "fond d ecran").NoSearch(),
            L("colors", "Couleurs", "", "mode sombre").NoSearch(),
            L("themes", "Thèmes", "", "theme").NoSearch(),
            L("lockscreen", "Écran de verrouillage", "", "ecran de verrouillage").NoSearch(),
            L("personalization-textinput", "Saisie de texte", "", "clavier tactile", "touch keyboard", "taille du clavier tactile", "theme du clavier", "indicateur de saisie").Win11(),
            L("personalization-start", "Démarrer", "", "menu demarrer").NoSearch(),
            L("personalization-start-places", "Dossiers", "", "dossiers a cote du bouton marche arret", "raccourcis du menu demarrer", "telechargements", "parametres dans demarrer").Under("Démarrer").Win11(),
            L("taskbar", "Barre des tâches", "", "barre des taches").NoSearch(),
            L("fonts", "Polices", "", "polices").NoSearch(),
            L("deviceusage", "Utilisation de l'appareil", "", "usage de l appareil", "device usage", "jeux", "etudes", "creativite", "suggestions personnalisees").Win11(),
            L("personalization-lighting", "Éclairage dynamique", "", "eclairage rgb", "dynamic lighting", "led", "clavier retroeclaire").Min(22631),
        ]);

        yield return new SettingsSection("apps", "Applications", "",
        [
            L("appsfeatures", "Applications installées", "", "applications installees").NoSearch(),
            L("advanced-apps", "Paramètres avancés des applications", "", "installer des applications de partout", "choisir ou obtenir des applications", "alias d execution", "archiver les applications").Win11(),
            L("defaultapps", "Applications par défaut", "", "navigateur par defaut", "ouvrir avec", "default apps", "associations de fichiers", "lecteur pdf par defaut"),
            L("startupapps", "Démarrage", "", "applications au demarrage").NoSearch(),
            L("maps", "Cartes hors connexion", "", "cartes", "offline maps", "cartes telechargees"),
            L("maps-downloadmaps", "Télécharger des cartes", "", "telecharger une carte", "download maps").Under("Cartes hors connexion"),
            L("appsforwebsites", "Applications pour les sites web", "", "ouvrir les liens dans l application", "apps for websites", "liens web"),
            L("videoplayback", "Lecture vidéo", "", "video", "hdr video", "lecture video", "economie de batterie video"),
        ]);

        yield return new SettingsSection("accounts", "Comptes", "",
        [
            L("accounts", "Comptes", "", "comptes", "compte microsoft", "accounts").Win11(),
            L("yourinfo", "Vos informations", "", "photo de profil").NoSearch(),
            L("emailandaccounts", "E-mail et comptes", "", "comptes e mail", "comptes utilises par d autres applications", "compte google", "outlook"),
            L("signinoptions", "Options de connexion", "", "windows hello").NoSearch(),
            L("signinoptions-dynamiclock", "Verrouillage dynamique", "", "verrouillage dynamique", "dynamic lock", "verrouiller quand je m eloigne", "telephone bluetooth").Under("Options de connexion"),
            L("savedpasskeys", "Clés d'accès", "", "passkey", "passkeys", "cle d acces", "connexion sans mot de passe").Min(26100),
            L("family-group", "Famille", "", "famille").NoSearch(),
            L("otherusers", "Autres utilisateurs", "", "ajouter un utilisateur").NoSearch(),
            L("backup", "Sauvegarde Windows", "", "sauvegarde", "backup", "onedrive", "sauvegarder mes applications", "memoriser mes preferences").Min(22621),
            L("workplace", "Accès professionnel ou scolaire", "", "compte professionnel", "compte scolaire", "entreprise", "intune", "mdm", "azure ad", "entra"),
        ]);

        yield return new SettingsSection("time", "Heure et langue", "",
        [
            L("dateandtime", "Date et heure", "", "heure", "fuseau horaire", "synchroniser l heure", "heure automatique", "time zone"),
            L("regionlanguage", "Langue et région", "", "langue d affichage", "ajouter une langue", "disposition du clavier", "azerty", "qwerty", "pays", "format regional"),
            L("typing", "Saisie", "", "correction automatique", "suggestions de texte", "saisie tactile", "autocorrect", "typing"),
            L("speech", "Voix", "", "reconnaissance vocale", "voix de synthese", "speech", "microphone"),
        ]);

        yield return new SettingsSection("gaming", "Jeux", "",
        [
            L("gaming-gamebar", "Game Bar", "", "xbox game bar", "barre de jeu", "windows g", "manette"),
            L("gaming-gamedvr", "Captures", "", "enregistrer un jeu", "capture de jeu", "game dvr", "dossier captures", "enregistrer ce qui s est passe"),
            L("gaming-gamemode", "Mode Jeu", "", "mode jeu").NoSearch(),
        ]);

        yield return new SettingsSection("accessibility", "Accessibilité", "",
        [
            L("easeofaccess-display", "Taille du texte", "", "agrandir le texte", "texte plus grand", "text size", "police plus grande"),
            L("easeofaccess-visualeffects", "Effets visuels", "", "barres de defilement toujours visibles", "effets de transparence", "effets d animation", "fermer les notifications").Win11(),
            L("easeofaccess-mousepointer", "Pointeur de souris et toucher", "", "taille du pointeur", "couleur du pointeur", "curseur plus grand", "mouse pointer"),
            L("easeofaccess-cursor", "Curseur de texte", "", "curseur de texte", "indicateur de curseur", "epaisseur du curseur", "text cursor"),
            L("easeofaccess-magnifier", "Loupe", "", "loupe", "magnifier", "zoom", "agrandir l ecran"),
            L("easeofaccess-colorfilter", "Filtres de couleur", "", "daltonien", "daltonisme", "niveaux de gris", "inverser les couleurs", "color filter"),
            L("easeofaccess-highcontrast", "Thèmes de contraste", "", "contraste eleve", "high contrast", "contrast themes"),
            L("easeofaccess-narrator", "Narrateur", "", "narrateur", "lecteur d ecran", "screen reader", "narrator", "voix naturelles"),
            L("easeofaccess-audio", "Audio", "", "audio mono", "clignotement de l ecran", "notifications visuelles", "mono audio"),
            L("easeofaccess-closedcaptioning", "Sous-titres", "", "sous titres", "sous titres en direct", "live captions", "closed captions"),
            L("easeofaccess-speechrecognition", "Voix (accessibilité)", "", "acces vocal", "voice access", "saisie vocale", "reconnaissance vocale windows", "commandes vocales"),
            L("easeofaccess-keyboard", "Clavier", "", "touches remanentes").NoSearch(),
            L("easeofaccess-mouse", "Souris", "", "touches de la souris", "mouse keys", "pave numerique souris"),
            L("easeofaccess-eyecontrol", "Suivi oculaire", "", "controle oculaire", "eye control", "eye tracking", "commande par le regard"),
        ]);

        yield return new SettingsSection("privacy", "Confidentialité et sécurité", "",
        [
            L("windowsdefender", "Sécurité Windows", "", "securite windows", "antivirus", "defender", "protection contre les virus", "windows security"),
            L("findmydevice", "Localiser mon appareil", "", "localiser mon appareil", "find my device", "pc perdu", "pc vole", "retrouver mon pc"),
            L("deviceencryption", "Chiffrement de l'appareil", "", "chiffrement de l appareil", "device encryption", "chiffrer le disque", "bitlocker"),
            L("privacy-general", "Général", "", "confidentialite generale", "identifiant de publicite", "contenu suggere").Under("Autorisations Windows"),
            L("privacy-speech", "Voix", "", "reconnaissance vocale en ligne", "online speech recognition").Under("Autorisations Windows"),
            L("privacy-speechtyping", "Personnalisation de l'écriture manuscrite et de la saisie", "", "dictionnaire personnel", "ecriture manuscrite", "inking and typing").Under("Autorisations Windows"),
            L("privacy-feedback", "Diagnostics et commentaires", "", "diagnostics").Under("Autorisations Windows").NoSearch(),
            L("privacy-activityhistory", "Historique des activités", "", "historique").Under("Autorisations Windows").NoSearch(),
            L("search-permissions", "Autorisations de recherche", "", "safesearch", "recherche securisee", "recherche dans le contenu cloud", "historique de recherche").Under("Autorisations Windows"),
            L("cortana-windowssearch", "Recherche dans Windows", "", "indexation", "rechercher mes fichiers", "recherche amelioree", "emplacements exclus", "indexer").Under("Autorisations Windows"),
            L("privacy-location", "Localisation", "", "services de localisation", "position", "gps", "localisation des applications").Under("Autorisations des applications"),
            L("privacy-webcam", "Caméra", "", "acces a la camera", "autoriser la camera", "webcam").Under("Autorisations des applications"),
            L("privacy-microphone", "Microphone", "", "acces au micro", "autoriser le microphone", "micro").Under("Autorisations des applications"),
            L("privacy-voiceactivation", "Activation vocale", "", "mot d activation", "voice activation", "assistant vocal").Under("Autorisations des applications"),
            L("privacy-notifications", "Notifications", "", "acces aux notifications", "lire mes notifications").Under("Autorisations des applications"),
            L("privacy-accountinfo", "Informations sur le compte", "", "acces au nom", "photo du compte", "account info").Under("Autorisations des applications"),
            L("privacy-contacts", "Contacts", "", "acces aux contacts", "carnet d adresses").Under("Autorisations des applications"),
            L("privacy-calendar", "Calendrier", "", "acces au calendrier", "agenda").Under("Autorisations des applications"),
            L("privacy-phonecalls", "Appels téléphoniques", "", "passer des appels", "phone calls").Under("Autorisations des applications"),
            L("privacy-callhistory", "Historique des appels", "", "journal des appels", "call history").Under("Autorisations des applications"),
            L("privacy-email", "E-mail", "", "acces aux e mails", "courrier").Under("Autorisations des applications"),
            L("privacy-tasks", "Tâches", "", "acces aux taches", "to do").Under("Autorisations des applications"),
            L("privacy-messaging", "Messagerie", "", "sms", "mms", "messages texte").Under("Autorisations des applications"),
            L("privacy-radios", "Radios", "", "controle du bluetooth par les applications", "radios", "controle wifi").Under("Autorisations des applications"),
            L("privacy-customdevices", "Autres appareils", "", "appareils non apparies", "balises", "beacons").Under("Autorisations des applications"),
            L("privacy-appdiagnostics", "Diagnostics d'application", "", "informations de diagnostic des applications", "app diagnostics").Under("Autorisations des applications"),
            L("privacy-automaticfiledownloads", "Téléchargements automatiques de fichiers", "", "telechargement automatique", "fichiers a la demande", "onedrive").Under("Autorisations des applications"),
            L("privacy-documents", "Documents", "", "acces aux documents", "bibliotheque documents").Under("Autorisations des applications"),
            L("privacy-downloadsfolder", "Dossier Téléchargements", "", "acces aux telechargements", "downloads folder").Under("Autorisations des applications").Min(22621),
            L("privacy-musiclibrary", "Bibliothèque musicale", "", "acces a la musique", "music library").Under("Autorisations des applications"),
            L("privacy-pictures", "Images", "", "acces aux images", "photos", "bibliotheque images").Under("Autorisations des applications"),
            L("privacy-videos", "Vidéos", "", "acces aux videos", "bibliotheque videos").Under("Autorisations des applications"),
            L("privacy-broadfilesystemaccess", "Système de fichiers", "", "acces au systeme de fichiers", "tous les fichiers", "file system").Under("Autorisations des applications"),
        ]);

        yield return new SettingsSection("update", "Windows Update", "",
        [
            L("windowsupdate", "Suspendre les mises à jour", "", "suspendre les mises a jour", "mettre en pause", "pause", "reporter les mises a jour", "windows update"),
            L("windowsupdate-options", "Options avancées", "", "options avancees windows update", "mises a jour d autres produits microsoft", "redemarrage", "notifications de redemarrage"),
            L("windowsupdate-history", "Historique des mises à jour", "", "historique des mises a jour", "update history", "kb", "mises a jour installees"),
            L("windowsupdate-uninstallupdates", "Désinstaller des mises à jour", "", "desinstaller une mise a jour", "supprimer une mise a jour", "uninstall update", "kb").Under("Historique des mises à jour").Win11(),
            L("windowsupdate-optionalupdates", "Mises à jour facultatives", "", "mises a jour de pilotes", "pilotes facultatifs", "optional updates").Under("Options avancées"),
            L("windowsupdate-activehours", "Heures d'activité", "", "heures d activite", "active hours", "ne pas redemarrer pendant").Under("Options avancées"),
            L("delivery-optimization", "Optimisation de la distribution", "", "delivery optimization", "p2p", "bande passante windows update").Under("Options avancées"),
            L("windowsinsider", "Programme Windows Insider", "", "insider", "preversion", "canal beta", "canal dev", "canary", "preview builds"),
        ]);
    }
}
