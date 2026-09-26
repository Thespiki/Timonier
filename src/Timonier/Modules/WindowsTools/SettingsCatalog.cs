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
    public string Path => Parent is null ? L("Paramètres › {0}", Section.Title) : L("Paramètres › {0} › {1}", Section.Title, Parent);

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

    private static SettingLink Link(string key, string title, string glyph, params string[] keywords) => new(key, title, glyph, keywords);

    private static IEnumerable<SettingsSection> All()
    {
        yield return new SettingsSection("system", L("Système"), "",
        [
            Link("display", L("Écran"), "", L("affichage, resolution, mise a l echelle, echelle, plusieurs ecrans, display")).NoSearch(),
            Link("nightlight", L("Éclairage nocturne"), "", L("lumiere bleue, night light, filtre lumiere bleue")).Under(L("Écran")),
            Link("display-advanced", L("Affichage avancé"), "", L("frequence d actualisation, taux de rafraichissement, hz, refresh rate, informations ecran")).Under(L("Écran")),
            Link("display-advancedgraphics", L("Graphiques"), "", L("gpu par application, preference graphique")).Under(L("Écran")).NoSearch(),
            Link("display-hdr", "HDR", "", L("hdr, high dynamic range, video hdr, auto hdr")).Under(L("Écran")).Win11(),
            Link("sound", L("Son"), "", L("audio, sortie audio, entree audio, sound")),
            Link("sound-devices", L("Tous les appareils audio"), "", L("peripheriques audio, haut parleurs, casque, micro")).Under(L("Son")),
            Link("apps-volume", L("Mélangeur de volume"), "", L("volume par application, volume mixer, sortie par application")).Under(L("Son")),
            Link("notifications", L("Notifications"), "", L("notifications, bannieres, ne pas deranger, centre de notifications")),
            Link("quiethours", L("Concentration"), "", L("assistant de concentration, focus, focus assist, session de concentration, minuteur")),
            Link("powersleep", L("Alimentation"), "", L("alimentation, mise en veille, ecran eteint")).NoSearch(),
            Link("batterysaver", L("Économiseur d'énergie"), "", L("economiseur de batterie, battery saver, energy saver")).NoSearch(),
            Link("energyrecommendations", L("Recommandations énergétiques"), "", L("economie d energie, energy recommendations, empreinte carbone")).Under(L("Alimentation")).Min(22621),
            Link("storagesense", L("Stockage"), "", L("espace disque, stockage")).NoSearch(),
            Link("storagepolicies", L("Assistant Stockage"), "", L("storage sense, nettoyage automatique, vider la corbeille automatiquement")).Under(L("Stockage")),
            Link("storagerecommendations", L("Recommandations de nettoyage"), "", L("liberer de l espace, gros fichiers, fichiers inutilises")).Under(L("Stockage")).Win11(),
            Link("disksandvolumes", L("Disques et volumes"), "", L("partitions, volumes, sante du disque, lettre de lecteur")).Under(L("Stockage")).Win11(),
            Link("savelocations", L("Emplacement d'enregistrement du nouveau contenu"), "", L("emplacement par defaut, enregistrer sur un autre disque, save locations")).Under(L("Stockage")),
            Link("multitasking", L("Multitâche"), "", L("ancrer les fenetres, snap, alt tab, bureaux virtuels, secouer la barre de titre")),
            Link("activation", L("Activation"), "", L("activer windows, cle de produit, licence, product key, edition")),
            Link("troubleshoot", L("Résolution des problèmes"), "", L("depannage, utilitaires de resolution, troubleshooter")),
            Link("recovery", L("Récupération"), "", L("reinitialiser ce pc, demarrage avance")).NoSearch(),
            Link("project", L("Projection sur ce PC"), "", L("miracast, projeter, ecran sans fil, affichage sans fil")),
            Link("remotedesktop", L("Bureau à distance"), "", L("rdp, bureau a distance, remote desktop, prise de controle")),
            Link("clipboard", L("Presse-papiers"), "", L("historique du presse papiers, windows v, clipboard, copier coller")),
            Link("crossdevice", L("Partage de proximité"), "", L("partage a proximite, nearby sharing, envoyer a un pc proche")).Win11(),
            Link("optionalfeatures", L("Fonctionnalités facultatives"), "", L("fonctionnalites facultatives, optional features, ajouter une fonctionnalite, rsat, openssh")),
            Link("systemcomponents", L("Composants système"), "", L("composants systeme, system components, applications systeme")).Min(26100),
            Link("developers", L("Pour les développeurs"), "", L("mode developpeur, developer mode, sudo, powershell execution, developpeur")),
            Link("about", L("Informations système"), "", L("a propos, about, specifications, nom du pc, caracteristiques, version de windows, renommer ce pc")),
        ]);

        yield return new SettingsSection("devices", L("Bluetooth et appareils"), "",
        [
            Link("bluetooth", L("Bluetooth et appareils"), "", L("bluetooth, appairer")).NoSearch(),
            Link("devices", L("Appareils"), "", L("ajouter un appareil, peripheriques connectes, devices")),
            Link("printers", L("Imprimantes et scanners"), "", L("imprimante, scanner")).NoSearch(),
            Link("mobile-devices", L("Appareils mobiles"), "", L("lien avec le telephone, phone link, telephone android, iphone, smartphone")).Min(22631),
            Link("camera", L("Caméras"), "", L("webcam, parametres camera, luminosite camera, cameras reseau")).Win11(),
            Link("mousetouchpad", L("Souris"), "", L("souris, vitesse du pointeur")).NoSearch(),
            Link("devices-touchpad", L("Pavé tactile"), "", L("touchpad, gestes")).NoSearch(),
            Link("pen", L("Stylet et Windows Ink"), "", L("stylet, pen, windows ink, ecriture manuscrite, tablette graphique")),
            Link("autoplay", L("Exécution automatique"), "", L("autoplay, lecture automatique, cle usb inseree, carte memoire")),
            Link("usb", "USB", "", L("usb, notifications usb")).NoSearch(),
        ]);

        yield return new SettingsSection("network", L("Réseau et Internet"), "",
        [
            Link("network-status", L("Réseau et Internet"), "", L("etat du reseau")).NoSearch(),
            Link("network-wifi", "Wi-Fi", "", L("wifi")).NoSearch(),
            Link("network-wifisettings", L("Gérer les réseaux connus"), "", L("reseaux connus, oublier un reseau, known networks, wifi enregistres")).Under(L("Wi-Fi")),
            Link("network-ethernet", "Ethernet", "", L("cable reseau, rj45, ethernet, connexion filaire, connexion limitee")),
            Link("network-vpn", "VPN", "", L("vpn")).NoSearch(),
            Link("network-mobilehotspot", L("Point d'accès sans fil mobile"), "", L("hotspot")).NoSearch(),
            Link("network-airplanemode", L("Mode avion"), "", L("mode avion")).NoSearch(),
            Link("network-proxy", "Proxy", "", L("proxy")).NoSearch(),
            Link("network-dialup", L("Accès à distance"), "", L("numerotation, dial up, pppoe, connexion haut debit, modem")),
            Link("network-advancedsettings", L("Paramètres réseau avancés"), "", L("cartes reseau, reinitialisation du reseau, network reset, desactiver une carte reseau")).Win11(),
            Link("network-advancedsharing", L("Paramètres de partage avancés"), "", L("partage de fichiers, decouverte du reseau, partage d imprimantes, network discovery")).Under(L("Paramètres réseau avancés")).Min(22621),
            Link("datausage", L("Utilisation des données"), "", L("consommation de donnees, data usage, limite de donnees, connexion limitee")).Under(L("Paramètres réseau avancés")),
        ]);

        yield return new SettingsSection("personalization", L("Personnalisation"), "",
        [
            Link("personalization", L("Personnalisation"), "", L("personnaliser, apparence, personalization")),
            Link("personalization-background", L("Arrière-plan"), "", L("fond d ecran")).NoSearch(),
            Link("colors", L("Couleurs"), "", L("mode sombre")).NoSearch(),
            Link("themes", L("Thèmes"), "", L("theme")).NoSearch(),
            Link("lockscreen", L("Écran de verrouillage"), "", L("ecran de verrouillage")).NoSearch(),
            Link("personalization-textinput", L("Saisie de texte"), "", L("clavier tactile, touch keyboard, taille du clavier tactile, theme du clavier, indicateur de saisie")).Win11(),
            Link("personalization-start", LC("start menu", "Démarrer"), "", L("menu demarrer")).NoSearch(),
            Link("personalization-start-places", L("Dossiers"), "", L("dossiers a cote du bouton marche arret, raccourcis du menu demarrer, telechargements, parametres dans demarrer")).Under(LC("start menu", "Démarrer")).Win11(),
            Link("taskbar", L("Barre des tâches"), "", L("barre des taches")).NoSearch(),
            Link("fonts", L("Polices"), "", L("polices")).NoSearch(),
            Link("deviceusage", L("Utilisation de l'appareil"), "", L("usage de l appareil, device usage, jeux, etudes, creativite, suggestions personnalisees")).Win11(),
            Link("personalization-lighting", L("Éclairage dynamique"), "", L("eclairage rgb, dynamic lighting, led, clavier retroeclaire")).Min(22631),
        ]);

        yield return new SettingsSection("apps", L("Applications"), "",
        [
            Link("appsfeatures", L("Applications installées"), "", L("applications installees")).NoSearch(),
            Link("advanced-apps", L("Paramètres avancés des applications"), "", L("installer des applications de partout, choisir ou obtenir des applications, alias d execution, archiver les applications")).Win11(),
            Link("defaultapps", L("Applications par défaut"), "", L("navigateur par defaut, ouvrir avec, default apps, associations de fichiers, lecteur pdf par defaut")),
            Link("startupapps", LC("settings page", "Démarrage"), "", L("applications au demarrage")).NoSearch(),
            Link("maps", L("Cartes hors connexion"), "", L("cartes, offline maps, cartes telechargees")),
            Link("maps-downloadmaps", L("Télécharger des cartes"), "", L("telecharger une carte, download maps")).Under(L("Cartes hors connexion")),
            Link("appsforwebsites", L("Applications pour les sites web"), "", L("ouvrir les liens dans l application, apps for websites, liens web")),
            Link("videoplayback", L("Lecture vidéo"), "", L("video, hdr video, lecture video, economie de batterie video")),
        ]);

        yield return new SettingsSection("accounts", L("Comptes"), "",
        [
            Link("accounts", L("Comptes"), "", L("comptes, compte microsoft, accounts")).Win11(),
            Link("yourinfo", L("Vos informations"), "", L("photo de profil")).NoSearch(),
            Link("emailandaccounts", L("E-mail et comptes"), "", L("comptes e mail, comptes utilises par d autres applications, compte google, outlook")),
            Link("signinoptions", L("Options de connexion"), "", L("windows hello")).NoSearch(),
            Link("signinoptions-dynamiclock", L("Verrouillage dynamique"), "", L("verrouillage dynamique, dynamic lock, verrouiller quand je m eloigne, telephone bluetooth")).Under(L("Options de connexion")),
            Link("savedpasskeys", L("Clés d'accès"), "", L("passkey, passkeys, cle d acces, connexion sans mot de passe")).Min(26100),
            Link("family-group", L("Famille"), "", L("famille")).NoSearch(),
            Link("otherusers", L("Autres utilisateurs"), "", L("ajouter un utilisateur")).NoSearch(),
            Link("backup", L("Sauvegarde Windows"), "", L("sauvegarde, backup, onedrive, sauvegarder mes applications, memoriser mes preferences")).Min(22621),
            Link("workplace", L("Accès professionnel ou scolaire"), "", L("compte professionnel, compte scolaire, entreprise, intune, mdm, azure ad, entra")),
        ]);

        yield return new SettingsSection("time", L("Heure et langue"), "",
        [
            Link("dateandtime", L("Date et heure"), "", L("heure, fuseau horaire, synchroniser l heure, heure automatique, time zone")),
            Link("regionlanguage", L("Langue et région"), "", L("langue d affichage, ajouter une langue, disposition du clavier, azerty, qwerty, pays, format regional")),
            Link("typing", L("Saisie"), "", L("correction automatique, suggestions de texte, saisie tactile, autocorrect, typing")),
            Link("speech", L("Voix"), "", L("reconnaissance vocale, voix de synthese, speech, microphone")),
        ]);

        yield return new SettingsSection("gaming", L("Jeux"), "",
        [
            Link("gaming-gamebar", "Game Bar", "", L("xbox game bar, barre de jeu, windows g, manette")),
            Link("gaming-gamedvr", L("Captures"), "", L("enregistrer un jeu, capture de jeu, game dvr, dossier captures, enregistrer ce qui s est passe")),
            Link("gaming-gamemode", L("Mode Jeu"), "", L("mode jeu")).NoSearch(),
        ]);

        yield return new SettingsSection("accessibility", L("Accessibilité"), "",
        [
            Link("easeofaccess-display", L("Taille du texte"), "", L("agrandir le texte, texte plus grand, text size, police plus grande")),
            Link("easeofaccess-visualeffects", L("Effets visuels"), "", L("barres de defilement toujours visibles, effets de transparence, effets d animation, fermer les notifications")).Win11(),
            Link("easeofaccess-mousepointer", L("Pointeur de souris et toucher"), "", L("taille du pointeur, couleur du pointeur, curseur plus grand, mouse pointer")),
            Link("easeofaccess-cursor", L("Curseur de texte"), "", L("curseur de texte, indicateur de curseur, epaisseur du curseur, text cursor")),
            Link("easeofaccess-magnifier", L("Loupe"), "", L("loupe, magnifier, zoom, agrandir l ecran")),
            Link("easeofaccess-colorfilter", L("Filtres de couleur"), "", L("daltonien, daltonisme, niveaux de gris, inverser les couleurs, color filter")),
            Link("easeofaccess-highcontrast", L("Thèmes de contraste"), "", L("contraste eleve, high contrast, contrast themes")),
            Link("easeofaccess-narrator", L("Narrateur"), "", L("narrateur, lecteur d ecran, screen reader, narrator, voix naturelles")),
            Link("easeofaccess-audio", L("Audio"), "", L("audio mono, clignotement de l ecran, notifications visuelles, mono audio")),
            Link("easeofaccess-closedcaptioning", L("Sous-titres"), "", L("sous titres, sous titres en direct, live captions, closed captions")),
            Link("easeofaccess-speechrecognition", L("Voix (accessibilité)"), "", L("acces vocal, voice access, saisie vocale, reconnaissance vocale windows, commandes vocales")),
            Link("easeofaccess-keyboard", L("Clavier"), "", L("touches remanentes")).NoSearch(),
            Link("easeofaccess-mouse", L("Souris"), "", L("touches de la souris, mouse keys, pave numerique souris")),
            Link("easeofaccess-eyecontrol", L("Suivi oculaire"), "", L("controle oculaire, eye control, eye tracking, commande par le regard")),
        ]);

        yield return new SettingsSection("privacy", L("Confidentialité et sécurité"), "",
        [
            Link("windowsdefender", L("Sécurité Windows"), "", L("securite windows, antivirus, defender, protection contre les virus, windows security")),
            Link("findmydevice", L("Localiser mon appareil"), "", L("localiser mon appareil, find my device, pc perdu, pc vole, retrouver mon pc")),
            Link("deviceencryption", L("Chiffrement de l'appareil"), "", L("chiffrement de l appareil, device encryption, chiffrer le disque, bitlocker")),
            Link("privacy-general", L("Général"), "", L("confidentialite generale, identifiant de publicite, contenu suggere")).Under(L("Autorisations Windows")),
            Link("privacy-speech", L("Voix"), "", L("reconnaissance vocale en ligne, online speech recognition")).Under(L("Autorisations Windows")),
            Link("privacy-speechtyping", L("Personnalisation de l'écriture manuscrite et de la saisie"), "", L("dictionnaire personnel, ecriture manuscrite, inking and typing")).Under(L("Autorisations Windows")),
            Link("privacy-feedback", L("Diagnostics et commentaires"), "", L("diagnostics")).Under(L("Autorisations Windows")).NoSearch(),
            Link("privacy-activityhistory", L("Historique des activités"), "", L("historique")).Under(L("Autorisations Windows")).NoSearch(),
            Link("search-permissions", L("Autorisations de recherche"), "", L("safesearch, recherche securisee, recherche dans le contenu cloud, historique de recherche")).Under(L("Autorisations Windows")),
            Link("cortana-windowssearch", L("Recherche dans Windows"), "", L("indexation, rechercher mes fichiers, recherche amelioree, emplacements exclus, indexer")).Under(L("Autorisations Windows")),
            Link("privacy-location", L("Localisation"), "", L("services de localisation, position, gps, localisation des applications")).Under(L("Autorisations des applications")),
            Link("privacy-webcam", L("Caméra"), "", L("acces a la camera, autoriser la camera, webcam")).Under(L("Autorisations des applications")),
            Link("privacy-microphone", L("Microphone"), "", L("acces au micro, autoriser le microphone, micro")).Under(L("Autorisations des applications")),
            Link("privacy-voiceactivation", L("Activation vocale"), "", L("mot d activation, voice activation, assistant vocal")).Under(L("Autorisations des applications")),
            Link("privacy-notifications", L("Notifications"), "", L("acces aux notifications, lire mes notifications")).Under(L("Autorisations des applications")),
            Link("privacy-accountinfo", L("Informations sur le compte"), "", L("acces au nom, photo du compte, account info")).Under(L("Autorisations des applications")),
            Link("privacy-contacts", L("Contacts"), "", L("acces aux contacts, carnet d adresses")).Under(L("Autorisations des applications")),
            Link("privacy-calendar", L("Calendrier"), "", L("acces au calendrier, agenda")).Under(L("Autorisations des applications")),
            Link("privacy-phonecalls", L("Appels téléphoniques"), "", L("passer des appels, phone calls")).Under(L("Autorisations des applications")),
            Link("privacy-callhistory", L("Historique des appels"), "", L("journal des appels, call history")).Under(L("Autorisations des applications")),
            Link("privacy-email", L("E-mail"), "", L("acces aux e mails, courrier")).Under(L("Autorisations des applications")),
            Link("privacy-tasks", L("Tâches"), "", L("acces aux taches, to do")).Under(L("Autorisations des applications")),
            Link("privacy-messaging", L("Messagerie"), "", L("sms, mms, messages texte")).Under(L("Autorisations des applications")),
            Link("privacy-radios", L("Radios"), "", L("controle du bluetooth par les applications, radios, controle wifi")).Under(L("Autorisations des applications")),
            Link("privacy-customdevices", L("Autres appareils"), "", L("appareils non apparies, balises, beacons")).Under(L("Autorisations des applications")),
            Link("privacy-appdiagnostics", L("Diagnostics d'application"), "", L("informations de diagnostic des applications, app diagnostics")).Under(L("Autorisations des applications")),
            Link("privacy-automaticfiledownloads", L("Téléchargements automatiques de fichiers"), "", L("telechargement automatique, fichiers a la demande, onedrive")).Under(L("Autorisations des applications")),
            Link("privacy-documents", L("Documents"), "", L("acces aux documents, bibliotheque documents")).Under(L("Autorisations des applications")),
            Link("privacy-downloadsfolder", L("Dossier Téléchargements"), "", L("acces aux telechargements, downloads folder")).Under(L("Autorisations des applications")).Min(22621),
            Link("privacy-musiclibrary", L("Bibliothèque musicale"), "", L("acces a la musique, music library")).Under(L("Autorisations des applications")),
            Link("privacy-pictures", L("Images"), "", L("acces aux images, photos, bibliotheque images")).Under(L("Autorisations des applications")),
            Link("privacy-videos", L("Vidéos"), "", L("acces aux videos, bibliotheque videos")).Under(L("Autorisations des applications")),
            Link("privacy-broadfilesystemaccess", L("Système de fichiers"), "", L("acces au systeme de fichiers, tous les fichiers, file system")).Under(L("Autorisations des applications")),
        ]);

        yield return new SettingsSection("update", "Windows Update", "",
        [
            Link("windowsupdate", L("Suspendre les mises à jour"), "", L("suspendre les mises a jour, mettre en pause, pause, reporter les mises a jour, windows update")),
            Link("windowsupdate-options", L("Options avancées"), "", L("options avancees windows update, mises a jour d autres produits microsoft, redemarrage, notifications de redemarrage")),
            Link("windowsupdate-history", L("Historique des mises à jour"), "", L("historique des mises a jour, update history, kb, mises a jour installees")),
            Link("windowsupdate-uninstallupdates", L("Désinstaller des mises à jour"), "", L("desinstaller une mise a jour, supprimer une mise a jour, uninstall update, kb")).Under(L("Historique des mises à jour")).Win11(),
            Link("windowsupdate-optionalupdates", L("Mises à jour facultatives"), "", L("mises a jour de pilotes, pilotes facultatifs, optional updates")).Under(L("Options avancées")),
            Link("windowsupdate-activehours", L("Heures d'activité"), "", L("heures d activite, active hours, ne pas redemarrer pendant")).Under(L("Options avancées")),
            Link("delivery-optimization", L("Optimisation de la distribution"), "", L("delivery optimization, p2p, bande passante windows update")).Under(L("Options avancées")),
            Link("windowsinsider", L("Programme Windows Insider"), "", L("insider, preversion, canal beta, canal dev, canary, preview builds")),
        ]);
    }
}
