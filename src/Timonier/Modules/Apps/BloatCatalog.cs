using System.Text.RegularExpressions;

namespace Timonier.Modules.Apps;

public enum BloatRisk
{
    /// <summary>Sans conséquence pour Windows : application promotionnelle ou facultative.</summary>
    Safe,
    /// <summary>Supprimable, mais une fonction utile disparaît (lecteur par défaut, jeux Xbox, téléphone…).</summary>
    Moderate,
}

/// <summary>Application préinstallée connue, supprimable pour l'utilisateur courant.</summary>
/// <param name="Pattern">Nom de paquet exact, ou préfixe s'il se termine par « . » ou « * ».</param>
public sealed record BloatEntry(string Pattern, string Title, string Description, BloatRisk Risk, bool Recommended)
{
    public string? Warning { get; init; }

    public bool Matches(string packageName)
    {
        if (Pattern.EndsWith('*')) return packageName.StartsWith(Pattern[..^1], StringComparison.OrdinalIgnoreCase);
        if (Pattern.EndsWith('.')) return packageName.StartsWith(Pattern, StringComparison.OrdinalIgnoreCase);
        return string.Equals(packageName, Pattern, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Table des applications préinstallées supprimables (avec explication et niveau de risque) et liste des composants
/// PROTÉGÉS que Timonier refuse de supprimer (Store, installateur d'applications, bibliothèques, interface de Windows…).
/// </summary>
public static partial class BloatCatalog
{
    private static readonly string Xbox = L("Les jeux Xbox et Game Pass s'en servent (connexion, invitations, captures) : à garder si vous jouez sur ce PC.");

    public static IReadOnlyList<BloatEntry> Entries { get; } =
    [
        new("king.com.", L("Jeux King (Candy Crush…)"), L("Jeux gratuits avec achats intégrés, installés automatiquement par Windows."), BloatRisk.Safe, true),
        new("Microsoft.BingNews", L("Actualités"), L("Fil d'actualités Microsoft Start."), BloatRisk.Safe, true),
        new("Microsoft.BingWeather", L("Météo"), L("Prévisions météo de Microsoft (MSN)."), BloatRisk.Safe, false),
        new("Microsoft.BingSearch", L("Recherche Bing"), L("Application de recherche web Bing."), BloatRisk.Safe, true),
        new("Microsoft.Getstarted", L("Astuces"), L("Conseils de prise en main de Windows."), BloatRisk.Safe, true),
        new("Microsoft.GetHelp", L("Obtenir de l'aide"), L("Assistance en ligne de Microsoft."), BloatRisk.Moderate, false)
        {
            Warning = L("Sous Windows 11, les utilitaires de résolution des problèmes passent par cette application."),
        },
        new("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", L("Jeux de cartes avec publicités (abonnement pour les retirer)."), BloatRisk.Safe, true),
        new("Microsoft.People", L("Contacts"), L("Ancienne application Contacts de Windows 10."), BloatRisk.Safe, true),
        new("Microsoft.WindowsFeedbackHub", L("Hub de commentaires"), L("Envoyer des avis et signaler des bogues à Microsoft."), BloatRisk.Safe, true),
        new("Microsoft.WindowsMaps", L("Cartes"), L("Cartes et itinéraires Bing (abandonnée par Microsoft)."), BloatRisk.Safe, true),
        new("Microsoft.ZuneVideo", L("Films et TV"), L("Ancien lecteur vidéo et boutique de films."), BloatRisk.Safe, true),
        new("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office)", L("Portail promotionnel vers Office en ligne et l'abonnement Microsoft 365."), BloatRisk.Safe, true),
        new("Microsoft.SkypeApp", "Skype", L("Service arrêté par Microsoft en mai 2025."), BloatRisk.Safe, true),
        new("Clipchamp.Clipchamp", "Clipchamp", L("Éditeur vidéo en ligne de Microsoft (compte requis)."), BloatRisk.Safe, true),
        new("Microsoft.Todos", "Microsoft To Do", L("Listes de tâches synchronisées avec un compte Microsoft."), BloatRisk.Safe, false),
        new("Microsoft.PowerAutomateDesktop", "Power Automate", L("Automatisation de tâches pour utilisateurs avancés."), BloatRisk.Safe, true),
        new("MicrosoftTeams", L("Teams (Conversation)"), L("Ancienne messagerie Teams personnelle de Windows 11 (version 21H2/22H2)."), BloatRisk.Safe, true),
        new("MSTeams", "Microsoft Teams", L("Nouvelle application Teams. À garder si vous l'utilisez pour le travail ou l'école."), BloatRisk.Safe, false),
        new("Microsoft.Copilot", "Copilot", L("Assistant d'IA de Microsoft (application)."), BloatRisk.Safe, false),
        new("Microsoft.OutlookForWindows", L("Outlook (nouveau)"), L("Nouvelle messagerie Outlook, qui remplace Courrier et Calendrier."), BloatRisk.Safe, false),
        new("microsoft.windowscommunicationsapps", L("Courrier et Calendrier"), L("Anciennes applications retirées par Microsoft fin 2024."), BloatRisk.Safe, true),
        new("Microsoft.549981C3F5F10", "Cortana", L("Assistant vocal abandonné par Microsoft."), BloatRisk.Safe, true),
        new("Disney.", "Disney+", L("Raccourci promotionnel vers le service de streaming."), BloatRisk.Safe, true),
        new("SpotifyAB.SpotifyMusic", "Spotify", L("Musique en streaming. À garder si vous l'utilisez."), BloatRisk.Safe, false),
        new("BytedancePte.Ltd.TikTok", "TikTok", L("Application promotionnelle préinstallée."), BloatRisk.Safe, true),
        new("Facebook.", "Facebook, Instagram, Messenger", L("Applications promotionnelles préinstallées."), BloatRisk.Safe, true),
        new("7EE7776C.LinkedInforWindows", "LinkedIn", L("Application promotionnelle préinstallée."), BloatRisk.Safe, true),
        new("AmazonVideo.PrimeVideo", "Prime Video", L("Raccourci promotionnel vers le service de streaming."), BloatRisk.Safe, true),
        new("Microsoft.MixedReality.Portal", L("Portail de réalité mixte"), L("Casques Windows Mixed Reality (plateforme abandonnée)."), BloatRisk.Safe, true),
        new("Microsoft.Microsoft3DViewer", L("Visionneuse 3D"), L("Afficher des modèles 3D (retirée des nouvelles versions)."), BloatRisk.Safe, true),
        new("Microsoft.MSPaint", "Paint 3D", L("Éditeur 3D abandonné (Paint classique n'est pas concerné)."), BloatRisk.Safe, true),
        new("Microsoft.Print3D", "Print 3D", L("Préparation d'impressions 3D (abandonnée)."), BloatRisk.Safe, true),
        new("Microsoft.Wallet", L("Portefeuille"), L("Ancien portefeuille Microsoft, inutilisé."), BloatRisk.Safe, true),
        new("Microsoft.Messaging", L("Messages"), L("Ancienne application SMS de Windows 10."), BloatRisk.Safe, true),
        new("Microsoft.OneConnect", L("Forfaits mobiles"), L("Achat de forfaits de données cellulaires (abandonné)."), BloatRisk.Safe, true),
        new("Microsoft.Office.OneNote", L("OneNote pour Windows 10"), L("Ancienne version de OneNote, remplacée par OneNote de Microsoft 365."), BloatRisk.Safe, false),
        new("Microsoft.Windows.DevHome", "Dev Home", L("Tableau de bord pour développeurs, abandonné par Microsoft."), BloatRisk.Safe, true),
        new("Microsoft.MicrosoftJournal", "Journal", L("Prise de notes manuscrites au stylet."), BloatRisk.Safe, false),
        new("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family Safety", L("Contrôle parental en ligne. À garder si vous l'utilisez."), BloatRisk.Safe, false),
        new("Microsoft.MicrosoftStickyNotes", L("Pense-bêtes"), L("Notes adhésives synchronisées avec un compte Microsoft."), BloatRisk.Safe, false),
        new("Microsoft.WindowsAlarms", L("Horloge"), L("Alarmes, minuteur, chronomètre et sessions de concentration."), BloatRisk.Safe, false),
        new("Microsoft.WindowsSoundRecorder", L("Enregistreur audio"), L("Enregistrement rapide au micro."), BloatRisk.Safe, false),
        new("Microsoft.ZuneMusic", L("Lecteur multimédia"), L("Lecteur audio et vidéo par défaut de Windows 11."), BloatRisk.Moderate, false)
        {
            Warning = L("C'est le lecteur par défaut : installez-en un autre (VLC…) avant de le supprimer."),
        },
        new("Microsoft.XboxApp", L("Compagnon de la console Xbox"), L("Ancienne application Xbox de Windows 10."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.GamingApp", "Xbox", L("Boutique Game Pass et bibliothèque de jeux Xbox."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.XboxGamingOverlay", "Xbox Game Bar", L("Barre de jeu (Win+G) : captures, performances, discussion."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.XboxIdentityProvider", L("Connexion Xbox"), L("Connexion au compte Xbox dans les jeux (Minecraft, Game Pass…)."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.XboxSpeechToTextOverlay", L("Transcription vocale Xbox"), L("Sous-titres des discussions vocales dans les jeux."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.Xbox.TCUI", L("Interface Xbox des jeux"), L("Profils, amis et invitations Xbox dans les jeux."), BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.YourPhone", L("Lien avec Windows (Phone Link)"), L("Relie votre téléphone Android ou iPhone au PC (SMS, appels, photos)."), BloatRisk.Moderate, false)
        {
            Warning = L("Vous ne pourrez plus recevoir SMS, appels et notifications du téléphone sur ce PC."),
        },
        new("MicrosoftWindows.CrossDevice", L("Expérience multi-appareils"), L("Composant de Lien avec Windows (téléphone dans le menu Démarrer)."), BloatRisk.Moderate, false)
        {
            Warning = L("Lien avec Windows et l'intégration du téléphone dans le menu Démarrer ne fonctionneront plus."),
        },
        new("MicrosoftWindows.Client.WebExperience", "Widgets", L("Panneau des widgets (actualités, météo) de la barre des tâches."), BloatRisk.Moderate, false)
        {
            Warning = L("Le panneau des widgets disparaît ; la météo de la barre des tâches aussi."),
        },
        new("Microsoft.WindowsCamera", L("Caméra"), L("Application Caméra de Windows."), BloatRisk.Moderate, false)
        {
            Warning = L("Vous n'aurez plus d'application pour prendre des photos ou tester la webcam."),
        },
        new("Microsoft.ScreenSketch", L("Outil Capture d'écran"), L("Captures avec Win+Maj+S et Impr. écran."), BloatRisk.Moderate, false)
        {
            Warning = L("Les raccourcis de capture d'écran (Win+Maj+S) ne fonctionneront plus."),
        },
        new("MicrosoftCorporationII.QuickAssist", L("Assistance rapide"), L("Aide à distance entre deux PC Windows."), BloatRisk.Moderate, false)
        {
            Warning = L("Vous ne pourrez plus recevoir d'aide à distance avec Assistance rapide."),
        },
        new("Microsoft.Windows.Photos", L("Photos"), L("Visionneuse d'images et de vidéos par défaut."), BloatRisk.Moderate, false)
        {
            Warning = L("C'est la visionneuse par défaut : installez-en une autre (ImageGlass, IrfanView…) avant de la supprimer."),
        },
        new("Microsoft.WindowsCalculator", L("Calculatrice"), L("Calculatrice de Windows."), BloatRisk.Moderate, false)
        {
            Warning = L("Windows n'aura plus de calculatrice ; la touche Calculatrice du clavier ne fera plus rien."),
        },
        new("Microsoft.WindowsNotepad", L("Bloc-notes"), L("Éditeur de texte de Windows."), BloatRisk.Moderate, false)
        {
            Warning = L("Les fichiers texte n'auront plus d'éditeur par défaut."),
        },
        new("Microsoft.Paint", "Paint", L("Dessin et retouche simples."), BloatRisk.Moderate, false)
        {
            Warning = L("Paint ne sera plus disponible pour les retouches rapides."),
        },
        new("Microsoft.WindowsTerminal", L("Terminal Windows"), L("Terminal par défaut de Windows 11."), BloatRisk.Moderate, false)
        {
            Warning = L("Windows se rabattra sur l'ancienne console ; certains scripts et raccourcis s'ouvrent dans le Terminal."),
        },
    ];

    /// <summary>
    /// Composants indispensables : jamais supprimés par Timonier (Store, installateur d'applications, bibliothèques,
    /// interface de Windows, sécurité, comptes, extensions multimédias, packs de langue…). Préfixe si terminé par « . » ou « * ».
    /// </summary>
    public static IReadOnlyList<string> Protected { get; } =
    [
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.Winget.Source",
        "Microsoft.VCLibs*", "Microsoft.UI.Xaml*", "Microsoft.NET.Native*", "Microsoft.WindowsAppRuntime*", "Microsoft.WinAppRuntime*",
        "Microsoft.Services.Store.Engagement", "Microsoft.Windows.ShellExperienceHost", "Microsoft.Windows.StartMenuExperienceHost",
        "Microsoft.SecHealthUI", "Microsoft.AAD.BrokerPlugin", "Microsoft.AccountsControl", "Microsoft.Windows.CloudExperienceHost",
        "Microsoft.LockApp", "Microsoft.Windows.ContentDeliveryManager", "Microsoft.Windows.SecureAssessmentBrowser",
        "Microsoft.Windows.ParentalControls", "Microsoft.Windows.PeopleExperienceHost", "Microsoft.Windows.Search",
        "Microsoft.Windows.AssignedAccessLockApp", "Microsoft.BioEnrollment", "Microsoft.CredDialogHost", "Microsoft.ECApp",
        "Microsoft.Win32WebViewHost", "Microsoft.AsyncTextService", "Microsoft.Windows.Apprep.ChxApp", "Microsoft.Windows.OOBE*",
        "Microsoft.Windows.CapturePicker", "Microsoft.Windows.PinningConfirmationDialog", "Microsoft.Windows.PrintQueueActionCenter",
        "Microsoft.Windows.XGpuEjectDialog", "Microsoft.Windows.NarratorQuickStart", "Microsoft.XboxGameCallableUI",
        "Microsoft.MicrosoftEdge*", "Microsoft.OneDriveSync", "Microsoft.ApplicationCompatibilityEnhancements",
        "Microsoft.LanguageExperiencePack*", "Microsoft.*Extension*", "Microsoft.WebMediaExtensions", "Microsoft.HEVCVideoExtension",
        "Microsoft.MPEG2VideoExtension", "Microsoft.Windows.DevicesFlowHost", "Microsoft.WindowsAppSDK*",
        "MicrosoftWindows.Client.*", "MicrosoftWindows.UndockedDevKit", "MicrosoftWindows.Speech*", "MicrosoftWindows.Voice*",
        "windows.immersivecontrolpanel", "Windows.PrintDialog", "Windows.CBSPreview", "Microsoft.MicrosoftEdgeDevToolsClient",
        "NcsiUwpApp", "Microsoft.Windows.FileExplorer*", "Microsoft.Windows.FilePicker*", "MicrosoftWindows.*",
    ];

    /// <summary>Correspondance dans la table des applications supprimables (null si inconnue).</summary>
    public static BloatEntry? Find(string packageName) => Entries.FirstOrDefault(e => e.Matches(packageName));

    /// <summary>
    /// Vrai si le paquet est un composant protégé. Les entrées explicitement listées comme supprimables
    /// (ex. Widgets, multi-appareils) l'emportent sur les motifs génériques « MicrosoftWindows.* ».
    /// </summary>
    public static bool IsProtected(string packageName)
    {
        if (Find(packageName) is not null) return false;
        return Protected.Any(p => WildcardMatch(packageName, p));
    }

    private static bool WildcardMatch(string value, string pattern)
    {
        if (!pattern.Contains('*')) return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
        var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(value, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // ------------------------------------------------------------------ Validation des identifiants de paquets

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]{1,99}_[a-z0-9]{13}$")]
    private static partial Regex FamilyRx();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]{1,99}_[0-9]{1,5}(\.[0-9]{1,5}){3}_[A-Za-z0-9]{0,10}_[A-Za-z0-9.\-~]{0,60}_[a-z0-9]{13}$")]
    private static partial Regex FullNameRx();

    public static bool IsValidFamilyName(string value) => FamilyRx().IsMatch(value);

    public static bool IsValidFullName(string value) => FullNameRx().IsMatch(value);

    /// <summary>Nom du paquet (partie avant le « _ ») d'un nom de famille ou d'un nom complet.</summary>
    public static string NameOf(string familyOrFullName) => familyOrFullName.Split('_')[0];
}
