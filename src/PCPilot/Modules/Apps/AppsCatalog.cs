using PcPilot.Core.Platform;

namespace PcPilot.Modules.Apps;

/// <summary>
/// Application du catalogue d'installation (source « winget » communautaire).
/// <para><see cref="Tags"/> : essentials, office, gaming, dev, family, media, security, vendor, lowend (légère), heavy (exigeante).</para>
/// </summary>
public sealed record CatalogApp(string WingetId, string Name, string Category, string Description, string[] Tags)
{
    /// <summary>Constructeur du PC concerné (outils constructeur) : même libellé que <see cref="SystemProfile.Manufacturer"/>.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Fabricant de carte graphique concerné (pilotes graphiques).</summary>
    public HardwareVendor? GpuVendor { get; init; }

    /// <summary>Débuts possibles du nom affiché dans « Programmes et fonctionnalités » (détection « déjà installée »).</summary>
    public string[] Match { get; init; } = [];

    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    /// <summary>Vrai si ce programme semble déjà installé (comparaison avec les noms de la liste des programmes).</summary>
    public bool MatchesInstalledName(string displayName)
    {
        var prefixes = Match.Length > 0 ? Match : [Name];
        return prefixes.Any(p => displayName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Pertinent pour ce PC (outil du constructeur, pilote de la carte graphique présente).</summary>
    public bool IsRelevantFor(SystemProfile profile) =>
        (Manufacturer is not null && string.Equals(Manufacturer, profile.Manufacturer, StringComparison.OrdinalIgnoreCase))
        || (GpuVendor is { } v && profile.Gpus.Any(g => g.Vendor == v));
}

/// <summary>
/// Catalogue d'applications installables en un clic (identifiants vérifiés dans la source « winget » communautaire).
/// Utilisé par la page Applications, l'action <c>apps.winget.install</c> (liste blanche) et les profils (étiquettes).
/// </summary>
public static class AppsCatalog
{
    public const string Browsers = "Navigateurs";
    public const string Office = "Bureautique et PDF";
    public const string Media = "Multimédia";
    public const string Creation = "Création";
    public const string Utilities = "Utilitaires";
    public const string Security = "Sécurité et mots de passe";
    public const string Communication = "Communication";
    public const string Development = "Développement";
    public const string Games = "Jeux";
    public const string Vendor = "Outils constructeur";
    public const string Drivers = "Pilotes graphiques";
    public const string Runtimes = "Runtimes";

    /// <summary>Catégories dans l'ordre d'affichage, avec leur icône (Segoe Fluent Icons).</summary>
    public static IReadOnlyList<(string Name, string Glyph)> Categories { get; } =
    [
        (Browsers, ""), (Office, ""), (Media, ""), (Creation, ""), (Utilities, ""),
        (Security, ""), (Communication, ""), (Development, ""), (Games, ""),
        (Vendor, ""), (Drivers, ""), (Runtimes, ""),
    ];

    public static IReadOnlyList<CatalogApp> All { get; } = Build();

    private static readonly Dictionary<string, CatalogApp> ById =
        All.ToDictionary(a => a.WingetId, StringComparer.OrdinalIgnoreCase);

    public static CatalogApp? Find(string wingetId) => ById.GetValueOrDefault(wingetId);

    /// <summary>Identifiant présent dans le catalogue (comparaison insensible à la casse, comme winget).</summary>
    public static bool Contains(string wingetId) => ById.ContainsKey(wingetId);

    public static IEnumerable<CatalogApp> WithTag(string tag) => All.Where(a => a.HasTag(tag));

    public static string GlyphFor(string category) =>
        Categories.FirstOrDefault(c => c.Name == category).Glyph ?? "";

    private static CatalogApp A(string id, string name, string category, string description, params string[] tags) =>
        new(id, name, category, description, tags);

    private static List<CatalogApp> Build() =>
    [
        // ------------------------------------------------------------------ Navigateurs
        A("Mozilla.Firefox.fr", "Firefox", Browsers,
            "Navigateur libre de la fondation Mozilla, attentif à la vie privée. Version française.", "essentials", "family")
            with { Match = ["Mozilla Firefox"] },
        A("Google.Chrome", "Google Chrome", Browsers,
            "Le navigateur de Google, synchronisé avec votre compte Google.", "essentials"),
        A("Brave.Brave", "Brave", Browsers,
            "Navigateur basé sur Chromium avec bloqueur de publicités et de pisteurs intégré.", "security"),
        A("Opera.Opera", "Opera", Browsers,
            "Navigateur basé sur Chromium avec VPN de navigation intégré et messageries dans la barre latérale.")
            with { Match = ["Opera Stable"] },
        A("Opera.OperaGX", "Opera GX", Browsers,
            "Variante d'Opera pour les joueurs : limites de mémoire, de processeur et de bande passante réglables.", "gaming"),
        A("Vivaldi.Vivaldi", "Vivaldi", Browsers,
            "Navigateur très personnalisable : onglets empilés, panneaux latéraux, messagerie et agenda intégrés."),
        A("LibreWolf.LibreWolf", "LibreWolf", Browsers,
            "Variante de Firefox orientée confidentialité : sans télémétrie, avec uBlock Origin préinstallé.", "security"),

        // ------------------------------------------------------------------ Bureautique et PDF
        A("TheDocumentFoundation.LibreOffice", "LibreOffice", Office,
            "Suite bureautique libre et gratuite (texte, tableur, présentations), compatible avec les fichiers Word, Excel et PowerPoint.",
            "essentials", "office", "family"),
        A("ONLYOFFICE.DesktopEditors", "ONLYOFFICE", Office,
            "Suite bureautique gratuite à l'interface proche de Microsoft Office, très fidèle aux formats DOCX, XLSX et PPTX.", "office"),
        A("SumatraPDF.SumatraPDF", "SumatraPDF", Office,
            "Lecteur PDF, ePub et bandes dessinées ultra léger et rapide : idéal sur les PC modestes.", "essentials", "office", "lowend"),
        A("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", Office,
            "Le lecteur PDF d'Adobe : lecture, annotations, remplissage et signature de formulaires.", "office")
            with { Match = ["Adobe Acrobat"] },
        A("geeksoftwareGmbH.PDF24Creator", "PDF24 Creator", Office,
            "Boîte à outils PDF gratuite et hors ligne : fusionner, compresser, convertir, imprimante PDF.", "office"),
        A("PDFsam.PDFsam", "PDFsam Basic", Office,
            "Fusionner, découper, extraire ou faire pivoter des pages PDF, gratuitement et hors ligne.", "office"),
        A("Obsidian.Obsidian", "Obsidian", Office,
            "Prise de notes en Markdown dans des fichiers locaux, avec liens entre les notes.", "office"),
        A("Notion.Notion", "Notion", Office,
            "Espace de travail en ligne : notes, bases de données, wikis et projets (compte requis).", "office"),
        A("Joplin.Joplin", "Joplin", Office,
            "Application de notes libre, chiffrée de bout en bout, synchronisable (Nextcloud, Dropbox, OneDrive…).", "office"),
        A("DigitalScholar.Zotero", "Zotero", Office,
            "Gestionnaire de références bibliographiques pour les étudiants et les chercheurs.", "office", "family"),
        A("calibre.calibre", "calibre", Office,
            "Gérer une bibliothèque de livres numériques, convertir les formats et les envoyer vers une liseuse.", "family"),
        A("Anki.Anki", "Anki", Office,
            "Cartes de révision à répétition espacée pour mémoriser durablement (langues, examens…).", "family"),
        A("GeoGebra.Classic", "GeoGebra Classic", Office,
            "Mathématiques dynamiques : géométrie, fonctions, tableur et calcul formel. Très utilisé au collège et au lycée.", "family"),

        // ------------------------------------------------------------------ Multimédia
        A("VideoLAN.VLC", "VLC", Media,
            "Lecteur multimédia libre qui lit presque tous les formats audio et vidéo, sans codec supplémentaire.",
            "essentials", "media", "family")
            with { Match = ["VLC media player"] },
        A("clsid2.mpc-hc", "MPC-HC", Media,
            "Lecteur vidéo léger et rapide, parfait pour les PC modestes.", "media", "lowend")
            with { Match = ["MPC-HC"] },
        A("Daum.PotPlayer", "PotPlayer", Media,
            "Lecteur vidéo très complet, avec accélération matérielle poussée et nombreux réglages.", "media")
            with { Match = ["PotPlayer"] },
        A("CodecGuide.K-LiteCodecPack.Standard", "K-Lite Codec Pack Standard", Media,
            "Codecs et filtres pour lire dans les anciens lecteurs les formats que Windows ne gère pas (inclut MPC-HC).", "media")
            with { Match = ["K-Lite"] },
        A("PeterPawlowski.foobar2000", "foobar2000", Media,
            "Lecteur audio léger et puissant pour les grandes bibliothèques musicales.", "media", "lowend"),
        A("AIMP.AIMP", "AIMP", Media,
            "Lecteur audio gratuit à l'interface soignée, avec égaliseur et radios en ligne.", "media"),
        A("Audacity.Audacity", "Audacity", Media,
            "Enregistrer et éditer du son : couper, mixer, réduire le bruit.", "media"),
        A("HandBrake.HandBrake", "HandBrake", Media,
            "Convertir et compresser des vidéos (MP4, MKV) avec des préréglages simples.", "media"),
        A("OBSProject.OBSStudio", "OBS Studio", Media,
            "Enregistrer l'écran et diffuser en direct (Twitch, YouTube) avec scènes et sources multiples.", "media", "gaming"),
        A("XBMCFoundation.Kodi", "Kodi", Media,
            "Centre multimédia pour films, séries, musique et photos, idéal sur un PC relié au téléviseur.", "media", "family"),
        A("MusicBrainz.Picard", "MusicBrainz Picard", Media,
            "Identifier et étiqueter automatiquement vos fichiers musicaux (titres, albums, pochettes).", "media"),
        A("IrfanSkiljan.IrfanView", "IrfanView", Media,
            "Visionneuse d'images ultra rapide, avec retouches simples et conversion par lots.", "media", "lowend"),
        A("DuongDieuPhap.ImageGlass", "ImageGlass", Media,
            "Visionneuse d'images moderne et légère, compatible WebP, HEIC, AVIF, RAW…", "media"),
        A("Stellarium.Stellarium", "Stellarium", Media,
            "Planétarium : le ciel en temps réel depuis votre position, étoiles, planètes et constellations.", "family"),

        // ------------------------------------------------------------------ Création
        A("GIMP.GIMP.3", "GIMP", Creation,
            "Retouche photo et création graphique avancées, alternative libre à Photoshop.", "media"),
        A("KDE.Krita", "Krita", Creation,
            "Peinture numérique et illustration, très apprécié avec une tablette graphique.", "media", "family"),
        A("Inkscape.Inkscape", "Inkscape", Creation,
            "Dessin vectoriel (SVG) : logos, illustrations, schémas.", "media"),
        A("dotPDN.PaintDotNet", "Paint.NET", Creation,
            "Retouche d'images simple et rapide, avec calques et effets.", "media")
            with { Match = ["paint.net"] },
        A("darktable.darktable", "darktable", Creation,
            "Développement de photos RAW et photothèque, alternative libre à Lightroom.", "media"),
        A("BlenderFoundation.Blender", "Blender", Creation,
            "Modélisation 3D, animation, rendu et montage vidéo. Exigeant pour la carte graphique.", "media", "heavy"),
        A("KDE.Kdenlive", "Kdenlive", Creation,
            "Montage vidéo multipiste libre, avec effets et transitions.", "media", "heavy"),
        A("Meltytech.Shotcut", "Shotcut", Creation,
            "Montage vidéo libre, simple à prendre en main.", "media"),
        A("Figma.Figma", "Figma", Creation,
            "Conception d'interfaces collaborative (compte requis).", "dev"),

        // ------------------------------------------------------------------ Utilitaires
        A("7zip.7zip", "7-Zip", Utilities,
            "Compresser et extraire les archives (ZIP, 7z, RAR…), libre et léger.", "essentials", "lowend"),
        A("RARLab.WinRAR", "WinRAR", Utilities,
            "Archiveur RAR et ZIP. Version d'essai : une licence payante est prévue pour un usage prolongé."),
        A("voidtools.Everything", "Everything", Utilities,
            "Recherche instantanée de fichiers par leur nom sur tous les disques.", "essentials", "lowend"),
        A("Microsoft.PowerToys", "PowerToys", Utilities,
            "Utilitaires de Microsoft : disposition des fenêtres, renommage en masse, pipette de couleur, lanceur…", "essentials", "dev"),
        A("ShareX.ShareX", "ShareX", Utilities,
            "Captures d'écran, enregistrements GIF ou vidéo, annotations et partage.", "media"),
        A("Greenshot.Greenshot", "Greenshot", Utilities,
            "Captures d'écran rapides avec annotations et masquage de zones.", "office", "lowend"),
        A("CPUID.CPU-Z", "CPU-Z", Utilities,
            "Informations détaillées sur le processeur, la carte mère et la mémoire.")
            with { Match = ["CPUID CPU-Z"] },
        A("TechPowerUp.GPU-Z", "GPU-Z", Utilities,
            "Informations et capteurs de la carte graphique.", "gaming")
            with { Match = ["TechPowerUp GPU-Z", "GPU-Z"] },
        A("REALiX.HWiNFO", "HWiNFO", Utilities,
            "Inventaire matériel complet et surveillance des capteurs (températures, tensions, ventilateurs)."),
        A("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", Utilities,
            "Santé (S.M.A.R.T.) et température des disques durs et SSD."),
        A("CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark", Utilities,
            "Mesure des débits de lecture et d'écriture des disques."),
        A("AntibodySoftware.WizTree", "WizTree", Utilities,
            "Voir en quelques secondes ce qui occupe l'espace disque.", "lowend"),
        A("WinDirStat.WinDirStat", "WinDirStat", Utilities,
            "Carte visuelle de l'occupation du disque, libre."),
        A("Rufus.Rufus", "Rufus", Utilities,
            "Créer une clé USB amorçable pour installer Windows ou Linux."),
        A("Ventoy.Ventoy", "Ventoy", Utilities,
            "Clé USB multi-démarrage : il suffit d'y copier des fichiers ISO."),
        A("BleachBit.BleachBit", "BleachBit", Utilities,
            "Supprimer les fichiers temporaires et les traces de navigation, libre."),
        A("Klocman.BulkCrapUninstaller", "Bulk Crap Uninstaller", Utilities,
            "Désinstaller de nombreux programmes à la fois et supprimer leurs restes.")
            with { Match = ["BCUninstaller"] },
        A("File-New-Project.EarTrumpet", "EarTrumpet", Utilities,
            "Volume de chaque application depuis la zone de notification."),
        A("Microsoft.Sysinternals.Autoruns", "Autoruns", Utilities,
            "Tout ce qui démarre avec Windows, en détail (outil Sysinternals de Microsoft).", "dev"),
        A("Microsoft.Sysinternals.ProcessExplorer", "Process Explorer", Utilities,
            "Gestionnaire des tâches avancé (outil Sysinternals de Microsoft).", "dev"),
        A("AutoHotkey.AutoHotkey", "AutoHotkey", Utilities,
            "Automatiser des tâches et créer des raccourcis clavier à l'aide de scripts.", "dev"),
        A("Flow-Launcher.Flow-Launcher", "Flow Launcher", Utilities,
            "Lanceur au clavier : applications, fichiers, calculs, recherche web.")
            with { Match = ["Flow Launcher", "Flow-Launcher"] },
        A("QL-Win.QuickLook", "QuickLook", Utilities,
            "Aperçu d'un fichier en appuyant sur Espace dans l'Explorateur, comme sur macOS."),
        A("FilesCommunity.Files", "Files", Utilities,
            "Gestionnaire de fichiers moderne avec onglets et volets, alternative à l'Explorateur.")
            with { Match = ["Files - Stable", "Files App"] },
        A("LocalSend.LocalSend", "LocalSend", Utilities,
            "Envoyer des fichiers entre appareils du même réseau local, sans Internet ni compte.", "family"),
        A("TeamViewer.TeamViewer", "TeamViewer", Utilities,
            "Assistance et prise de main à distance (gratuit pour un usage personnel).", "family"),
        A("AnyDesk.AnyDesk", "AnyDesk", Utilities,
            "Prise de main à distance légère et rapide."),
        A("Google.GoogleDrive", "Google Drive", Utilities,
            "Accéder à vos fichiers Google Drive depuis l'Explorateur et les synchroniser."),
        A("Dropbox.Dropbox", "Dropbox", Utilities,
            "Synchroniser vos fichiers Dropbox avec ce PC."),
        A("Nextcloud.NextcloudDesktop", "Nextcloud", Utilities,
            "Client de synchronisation pour un serveur Nextcloud (personnel ou hébergé)."),
        A("qBittorrent.qBittorrent", "qBittorrent", Utilities,
            "Client BitTorrent libre et sans publicité."),
        A("Logitech.OptionsPlus", "Logi Options+", Utilities,
            "Personnaliser les boutons et gestes des souris et claviers Logitech.")
            with { Match = ["Logi Options+", "Logitech Options+"] },

        // ------------------------------------------------------------------ Sécurité et mots de passe
        A("Bitwarden.Bitwarden", "Bitwarden", Security,
            "Gestionnaire de mots de passe libre et chiffré, synchronisé sur tous vos appareils.", "essentials", "security", "family"),
        A("KeePassXCTeam.KeePassXC", "KeePassXC", Security,
            "Gestionnaire de mots de passe hors ligne : un fichier chiffré qui reste chez vous.", "security"),
        A("DominikReichl.KeePass", "KeePass", Security,
            "Gestionnaire de mots de passe hors ligne historique, extensible par greffons.", "security"),
        A("AgileBits.1Password", "1Password", Security,
            "Gestionnaire de mots de passe familial et professionnel (abonnement).", "security"),
        A("Proton.ProtonPass", "Proton Pass", Security,
            "Gestionnaire de mots de passe et d'alias de messagerie chiffré, par Proton.", "security"),
        A("Proton.ProtonVPN", "Proton VPN", Security,
            "VPN avec offre gratuite sans limite de données, sans journalisation.", "security"),
        A("WireGuard.WireGuard", "WireGuard", Security,
            "Client VPN WireGuard pour votre propre serveur ou votre fournisseur de VPN.", "security", "dev"),
        A("Malwarebytes.Malwarebytes", "Malwarebytes", Security,
            "Analyse anti-logiciels malveillants à la demande, en complément de Microsoft Defender. L'essai Premium peut s'inscrire " +
            "comme antivirus principal : vérifiez ensuite dans Sécurité Windows que la protection reste active.", "security"),
        A("IDRIX.VeraCrypt", "VeraCrypt", Security,
            "Chiffrer des disques, des clés USB ou des conteneurs de fichiers.", "security"),
        A("Cryptomator.Cryptomator", "Cryptomator", Security,
            "Chiffrer vos fichiers avant de les déposer dans le cloud (OneDrive, Google Drive, Dropbox…).", "security"),

        // ------------------------------------------------------------------ Communication
        A("Mozilla.Thunderbird.fr", "Thunderbird", Communication,
            "Client de messagerie libre : plusieurs comptes, agenda et contacts. Version française.", "essentials", "office", "family")
            with { Match = ["Mozilla Thunderbird"] },
        A("Discord.Discord", "Discord", Communication,
            "Discussions vocales, vidéo et texte, très utilisé par les joueurs et les communautés.", "gaming"),
        A("Zoom.Zoom", "Zoom Workplace", Communication,
            "Visioconférences et réunions en ligne.", "office")
            with { Match = ["Zoom"] },
        A("Microsoft.Teams", "Microsoft Teams", Communication,
            "Réunions et messagerie d'équipe pour comptes professionnels, scolaires et personnels.", "office"),
        A("SlackTechnologies.Slack", "Slack", Communication,
            "Messagerie d'équipe organisée en canaux.", "office"),
        A("OpenWhisperSystems.Signal", "Signal", Communication,
            "Messagerie chiffrée de bout en bout ; nécessite Signal sur votre téléphone.", "security", "family"),
        A("Telegram.TelegramDesktop", "Telegram", Communication,
            "Messagerie rapide avec groupes, canaux et synchronisation dans le cloud.")
            with { Match = ["Telegram Desktop"] },
        A("Element.Element", "Element", Communication,
            "Messagerie décentralisée et chiffrée basée sur le protocole Matrix.", "security"),

        // ------------------------------------------------------------------ Développement
        A("Microsoft.VisualStudioCode", "Visual Studio Code", Development,
            "Éditeur de code léger et extensible de Microsoft.", "dev")
            with { Match = ["Microsoft Visual Studio Code"] },
        A("Notepad++.Notepad++", "Notepad++", Development,
            "Éditeur de texte et de code rapide, avec coloration syntaxique et onglets.", "dev", "essentials", "lowend"),
        A("Git.Git", "Git", Development,
            "Gestion de versions Git, avec Git Bash.", "dev"),
        A("GitHub.GitHubDesktop", "GitHub Desktop", Development,
            "Interface graphique simple pour Git et GitHub.", "dev"),
        A("Python.Python.3.14", "Python 3.14", Development,
            "Langage Python et son lanceur « py ».", "dev", "family"),
        A("OpenJS.NodeJS.LTS", "Node.js LTS", Development,
            "Environnement JavaScript côté serveur, version à support long.", "dev")
            with { Match = ["Node.js"] },
        A("Microsoft.DotNet.SDK.10", "SDK .NET 10", Development,
            "Outils pour développer des applications .NET 10 (C#, F#).", "dev")
            with { Match = ["Microsoft .NET SDK 10"] },
        A("Microsoft.PowerShell", "PowerShell 7", Development,
            "Version moderne et multiplateforme de PowerShell, installée à côté de Windows PowerShell 5.1.", "dev")
            with { Match = ["PowerShell 7"] },
        A("Microsoft.WindowsTerminal", "Terminal Windows", Development,
            "Terminal à onglets pour PowerShell, l'invite de commandes et WSL. Déjà inclus dans Windows 11.", "dev"),
        A("Microsoft.VisualStudio.2022.Community", "Visual Studio Community 2022", Development,
            "Environnement de développement complet de Microsoft, installé sans charge de travail : ajoutez-les ensuite avec " +
            "Visual Studio Installer. Gratuit pour les particuliers, l'enseignement et les petites équipes.", "dev", "heavy"),
        A("JetBrains.Toolbox", "JetBrains Toolbox", Development,
            "Installe et met à jour les IDE JetBrains (IntelliJ IDEA, PyCharm, Rider…).", "dev"),
        A("Docker.DockerDesktop", "Docker Desktop", Development,
            "Conteneurs Docker sous Windows ; nécessite WSL 2 et la virtualisation. Gratuit pour un usage personnel et les petites entreprises.",
            "dev", "heavy"),
        A("Oracle.VirtualBox", "VirtualBox", Development,
            "Machines virtuelles gratuites pour essayer d'autres systèmes.", "dev", "heavy")
            with { Match = ["Oracle VirtualBox", "Oracle VM VirtualBox"] },
        A("Postman.Postman", "Postman", Development,
            "Tester et documenter des API web.", "dev"),
        A("WinSCP.WinSCP", "WinSCP", Development,
            "Transferts de fichiers SFTP, FTP et SCP.", "dev"),
        A("PuTTY.PuTTY", "PuTTY", Development,
            "Client SSH et Telnet historique.", "dev"),
        A("WinMerge.WinMerge", "WinMerge", Development,
            "Comparer et fusionner des fichiers et des dossiers.", "dev"),
        A("DBBrowserForSQLite.DBBrowserForSQLite", "DB Browser for SQLite", Development,
            "Explorer et modifier des bases de données SQLite.", "dev"),
        A("Rustlang.Rustup", "Rust (rustup)", Development,
            "Installateur de la chaîne d'outils Rust ; nécessite les outils de compilation C++ de Visual Studio.", "dev")
            with { Match = ["Rustup"] },
        A("GoLang.Go", "Go", Development,
            "Langage Go et ses outils.", "dev")
            with { Match = ["Go Programming Language"] },

        // ------------------------------------------------------------------ Jeux
        A("Valve.Steam", "Steam", Games,
            "La plus grande boutique de jeux PC, avec bibliothèque, succès et jeu entre amis.", "gaming"),
        A("EpicGames.EpicGamesLauncher", "Epic Games Launcher", Games,
            "Boutique d'Epic Games (Fortnite, jeux gratuits chaque semaine).", "gaming")
            with { Match = ["Epic Games Launcher"] },
        A("GOG.Galaxy", "GOG Galaxy", Games,
            "Boutique de jeux sans DRM et bibliothèque unifiée de vos différentes plateformes.", "gaming")
            with { Match = ["GOG GALAXY", "GOG Galaxy"] },
        A("Ubisoft.Connect", "Ubisoft Connect", Games,
            "Lanceur et boutique des jeux Ubisoft.", "gaming"),
        A("ElectronicArts.EADesktop", "EA app", Games,
            "Lanceur et boutique des jeux Electronic Arts.", "gaming"),
        A("Playnite.Playnite", "Playnite", Games,
            "Bibliothèque unique pour tous vos jeux (Steam, Epic, GOG, émulateurs…), libre.", "gaming"),
        A("Nvidia.GeForceNow", "GeForce NOW", Games,
            "Jeu en streaming dans le cloud : les jeux tournent sur les serveurs de NVIDIA, idéal pour un PC modeste avec une bonne connexion.",
            "gaming", "lowend")
            with { Match = ["NVIDIA GeForce NOW", "GeForce NOW"] },
        A("Parsec.Parsec", "Parsec", Games,
            "Jouer à distance sur un autre PC ou partager une partie avec des amis, avec une faible latence.", "gaming"),
        A("Mojang.MinecraftLauncher", "Minecraft Launcher", Games,
            "Lanceur officiel de Minecraft : Java Edition (compte Microsoft et jeu acheté requis).", "gaming", "family"),
        A("PrismLauncher.PrismLauncher", "Prism Launcher", Games,
            "Lanceur Minecraft libre pour gérer plusieurs instances et modpacks (compte Minecraft requis).", "gaming"),
        A("Guru3D.Afterburner", "MSI Afterburner", Games,
            "Affichage en jeu (images par seconde, températures) et réglages de la carte graphique.", "gaming"),
        A("Logitech.GHUB", "Logitech G HUB", Games,
            "Réglages, macros et éclairage des périphériques de jeu Logitech G.", "gaming"),

        // ------------------------------------------------------------------ Outils constructeur
        A("Lenovo.SystemUpdate", "Lenovo System Update", Vendor,
            "Pilotes et BIOS à jour pour les ThinkPad, ThinkCentre et ThinkStation. Sur les IdeaPad, Yoga et Legion, " +
            "préférez Lenovo Vantage (Microsoft Store).", "vendor")
            with { Manufacturer = "Lenovo" },
        A("Dell.CommandUpdate.Universal", "Dell Command | Update", Vendor,
            "Pilotes, BIOS et micrologiciels à jour pour les PC Dell (Latitude, OptiPlex, Precision, Vostro, XPS).", "vendor")
            with { Manufacturer = "Dell", Match = ["Dell Command | Update"] },
        A("HP.ImageAssistant", "HP Image Assistant", Vendor,
            "Outil HP pour les PC professionnels (EliteBook, ProBook, ZBook) : détecte et installe pilotes, BIOS et correctifs. " +
            "Sur les PC grand public, utilisez HP Support Assistant.", "vendor")
            with { Manufacturer = "HP" },
        A("Asus.ArmouryCrate", "Armoury Crate", Vendor,
            "Centre de contrôle des PC ASUS ROG et TUF (modes de performance, éclairage, mises à jour). Inutile et lourd sur les autres modèles.",
            "vendor", "gaming")
            with { Manufacturer = "ASUS", Match = ["ARMOURY CRATE", "Armoury Crate"] },
        A("MSI.MSICenter", "MSI Center", Vendor,
            "Centre de contrôle des PC et cartes mères MSI : profils de performance, surveillance, mises à jour.", "vendor")
            with { Manufacturer = "MSI" },
        A("Microsoft.SurfaceApp", "Surface", Vendor,
            "Application des appareils Surface : batterie, stylet, garantie et réglages.", "vendor")
            with { Manufacturer = "Microsoft" },

        // ------------------------------------------------------------------ Pilotes graphiques
        A("Intel.IntelDriverAndSupportAssistant", "Intel Driver & Support Assistant", Drivers,
            "Détecte et installe les derniers pilotes Intel (graphiques, Wi-Fi, Bluetooth). Sur un portable, les pilotes du constructeur " +
            "restent parfois préférables.", "vendor")
            with { GpuVendor = HardwareVendor.Intel, Match = ["Intel® Driver & Support Assistant", "Intel(R) Driver & Support Assistant", "Intel Driver"] },
        A("TechPowerUp.NVCleanstall", "NVCleanstall", Drivers,
            "Télécharge le pilote officiel NVIDIA et l'installe sans les composants superflus. Outil tiers de TechPowerUp : " +
            "l'application NVIDIA officielle n'est pas disponible via winget.", "vendor", "gaming")
            with { GpuVendor = HardwareVendor.Nvidia },

        // ------------------------------------------------------------------ Runtimes
        A("Microsoft.VCRedist.2015+.x64", "Visual C++ 2015-2022 (x64)", Runtimes,
            "Bibliothèques Visual C++ requises par un grand nombre de programmes et de jeux 64 bits.", "essentials", "gaming")
            with { Match = ["Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft Visual C++ v14 Redistributable (x64)"] },
        A("Microsoft.VCRedist.2015+.x86", "Visual C++ 2015-2022 (x86)", Runtimes,
            "Les mêmes bibliothèques pour les programmes 32 bits, encore nombreux.", "essentials", "gaming")
            with { Match = ["Microsoft Visual C++ 2015-2022 Redistributable (x86)", "Microsoft Visual C++ v14 Redistributable (x86)"] },
        A("Microsoft.DotNet.DesktopRuntime.8", ".NET Desktop Runtime 8", Runtimes,
            "Environnement d'exécution des applications de bureau .NET 8.", "essentials")
            with { Match = ["Microsoft Windows Desktop Runtime - 8", "Microsoft .NET Windows Desktop Runtime 8"] },
        A("Microsoft.DotNet.DesktopRuntime.10", ".NET Desktop Runtime 10", Runtimes,
            "Environnement d'exécution des applications de bureau .NET 10.")
            with { Match = ["Microsoft Windows Desktop Runtime - 10", "Microsoft .NET Windows Desktop Runtime 10"] },
        A("Microsoft.DirectX", "DirectX (bibliothèques d'origine)", Runtimes,
            "Anciennes bibliothèques DirectX 9 à 11 (d3dx9, XAudio 2.7…) requises par de nombreux jeux anciens.", "gaming")
            with { Match = ["Microsoft DirectX"] },
        A("Microsoft.XNARedist", "XNA Framework 4.0", Runtimes,
            "Requis par certains jeux indépendants anciens (Terraria, Stardew Valley avant 1.5…).", "gaming")
            with { Match = ["Microsoft XNA Framework Redistributable"] },
        A("EclipseAdoptium.Temurin.21.JRE", "Java 21 (Temurin)", Runtimes,
            "Environnement d'exécution Java libre, pour les programmes qui en ont besoin.")
            with { Match = ["Eclipse Temurin JRE"] },
        A("Microsoft.EdgeWebView2Runtime", "WebView2", Runtimes,
            "Composant web utilisé par de nombreuses applications modernes ; déjà présent sur Windows 11.")
            with { Match = ["Microsoft Edge WebView2"] },
    ];
}
