using System.Text.RegularExpressions;

namespace PcPilot.Modules.Apps;

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
/// PROTÉGÉS que PC Pilot refuse de supprimer (Store, installateur d'applications, bibliothèques, interface de Windows…).
/// </summary>
public static partial class BloatCatalog
{
    private const string Xbox = "Les jeux Xbox et Game Pass s'en servent (connexion, invitations, captures) : à garder si vous jouez sur ce PC.";

    public static IReadOnlyList<BloatEntry> Entries { get; } =
    [
        new("king.com.", "Jeux King (Candy Crush…)", "Jeux gratuits avec achats intégrés, installés automatiquement par Windows.", BloatRisk.Safe, true),
        new("Microsoft.BingNews", "Actualités", "Fil d'actualités Microsoft Start.", BloatRisk.Safe, true),
        new("Microsoft.BingWeather", "Météo", "Prévisions météo de Microsoft (MSN).", BloatRisk.Safe, false),
        new("Microsoft.BingSearch", "Recherche Bing", "Application de recherche web Bing.", BloatRisk.Safe, true),
        new("Microsoft.Getstarted", "Astuces", "Conseils de prise en main de Windows.", BloatRisk.Safe, true),
        new("Microsoft.GetHelp", "Obtenir de l'aide", "Assistance en ligne de Microsoft.", BloatRisk.Moderate, false)
        {
            Warning = "Sous Windows 11, les utilitaires de résolution des problèmes passent par cette application.",
        },
        new("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", "Jeux de cartes avec publicités (abonnement pour les retirer).", BloatRisk.Safe, true),
        new("Microsoft.People", "Contacts", "Ancienne application Contacts de Windows 10.", BloatRisk.Safe, true),
        new("Microsoft.WindowsFeedbackHub", "Hub de commentaires", "Envoyer des avis et signaler des bogues à Microsoft.", BloatRisk.Safe, true),
        new("Microsoft.WindowsMaps", "Cartes", "Cartes et itinéraires Bing (abandonnée par Microsoft).", BloatRisk.Safe, true),
        new("Microsoft.ZuneVideo", "Films et TV", "Ancien lecteur vidéo et boutique de films.", BloatRisk.Safe, true),
        new("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office)", "Portail promotionnel vers Office en ligne et l'abonnement Microsoft 365.", BloatRisk.Safe, true),
        new("Microsoft.SkypeApp", "Skype", "Service arrêté par Microsoft en mai 2025.", BloatRisk.Safe, true),
        new("Clipchamp.Clipchamp", "Clipchamp", "Éditeur vidéo en ligne de Microsoft (compte requis).", BloatRisk.Safe, true),
        new("Microsoft.Todos", "Microsoft To Do", "Listes de tâches synchronisées avec un compte Microsoft.", BloatRisk.Safe, false),
        new("Microsoft.PowerAutomateDesktop", "Power Automate", "Automatisation de tâches pour utilisateurs avancés.", BloatRisk.Safe, true),
        new("MicrosoftTeams", "Teams (Conversation)", "Ancienne messagerie Teams personnelle de Windows 11 (version 21H2/22H2).", BloatRisk.Safe, true),
        new("MSTeams", "Microsoft Teams", "Nouvelle application Teams. À garder si vous l'utilisez pour le travail ou l'école.", BloatRisk.Safe, false),
        new("Microsoft.Copilot", "Copilot", "Assistant d'IA de Microsoft (application).", BloatRisk.Safe, false),
        new("Microsoft.OutlookForWindows", "Outlook (nouveau)", "Nouvelle messagerie Outlook, qui remplace Courrier et Calendrier.", BloatRisk.Safe, false),
        new("microsoft.windowscommunicationsapps", "Courrier et Calendrier", "Anciennes applications retirées par Microsoft fin 2024.", BloatRisk.Safe, true),
        new("Microsoft.549981C3F5F10", "Cortana", "Assistant vocal abandonné par Microsoft.", BloatRisk.Safe, true),
        new("Disney.", "Disney+", "Raccourci promotionnel vers le service de streaming.", BloatRisk.Safe, true),
        new("SpotifyAB.SpotifyMusic", "Spotify", "Musique en streaming. À garder si vous l'utilisez.", BloatRisk.Safe, false),
        new("BytedancePte.Ltd.TikTok", "TikTok", "Application promotionnelle préinstallée.", BloatRisk.Safe, true),
        new("Facebook.", "Facebook, Instagram, Messenger", "Applications promotionnelles préinstallées.", BloatRisk.Safe, true),
        new("7EE7776C.LinkedInforWindows", "LinkedIn", "Application promotionnelle préinstallée.", BloatRisk.Safe, true),
        new("AmazonVideo.PrimeVideo", "Prime Video", "Raccourci promotionnel vers le service de streaming.", BloatRisk.Safe, true),
        new("Microsoft.MixedReality.Portal", "Portail de réalité mixte", "Casques Windows Mixed Reality (plateforme abandonnée).", BloatRisk.Safe, true),
        new("Microsoft.Microsoft3DViewer", "Visionneuse 3D", "Afficher des modèles 3D (retirée des nouvelles versions).", BloatRisk.Safe, true),
        new("Microsoft.MSPaint", "Paint 3D", "Éditeur 3D abandonné (Paint classique n'est pas concerné).", BloatRisk.Safe, true),
        new("Microsoft.Print3D", "Print 3D", "Préparation d'impressions 3D (abandonnée).", BloatRisk.Safe, true),
        new("Microsoft.Wallet", "Portefeuille", "Ancien portefeuille Microsoft, inutilisé.", BloatRisk.Safe, true),
        new("Microsoft.Messaging", "Messages", "Ancienne application SMS de Windows 10.", BloatRisk.Safe, true),
        new("Microsoft.OneConnect", "Forfaits mobiles", "Achat de forfaits de données cellulaires (abandonné).", BloatRisk.Safe, true),
        new("Microsoft.Office.OneNote", "OneNote pour Windows 10", "Ancienne version de OneNote, remplacée par OneNote de Microsoft 365.", BloatRisk.Safe, false),
        new("Microsoft.Windows.DevHome", "Dev Home", "Tableau de bord pour développeurs, abandonné par Microsoft.", BloatRisk.Safe, true),
        new("Microsoft.MicrosoftJournal", "Journal", "Prise de notes manuscrites au stylet.", BloatRisk.Safe, false),
        new("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family Safety", "Contrôle parental en ligne. À garder si vous l'utilisez.", BloatRisk.Safe, false),
        new("Microsoft.MicrosoftStickyNotes", "Pense-bêtes", "Notes adhésives synchronisées avec un compte Microsoft.", BloatRisk.Safe, false),
        new("Microsoft.WindowsAlarms", "Horloge", "Alarmes, minuteur, chronomètre et sessions de concentration.", BloatRisk.Safe, false),
        new("Microsoft.WindowsSoundRecorder", "Enregistreur audio", "Enregistrement rapide au micro.", BloatRisk.Safe, false),
        new("Microsoft.ZuneMusic", "Lecteur multimédia", "Lecteur audio et vidéo par défaut de Windows 11.", BloatRisk.Moderate, false)
        {
            Warning = "C'est le lecteur par défaut : installez-en un autre (VLC…) avant de le supprimer.",
        },
        new("Microsoft.XboxApp", "Compagnon de la console Xbox", "Ancienne application Xbox de Windows 10.", BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.GamingApp", "Xbox", "Boutique Game Pass et bibliothèque de jeux Xbox.", BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.XboxGamingOverlay", "Xbox Game Bar", "Barre de jeu (Win+G) : captures, performances, discussion.", BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.XboxIdentityProvider", "Connexion Xbox", "Connexion au compte Xbox dans les jeux (Minecraft, Game Pass…).", BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.XboxSpeechToTextOverlay", "Transcription vocale Xbox", "Sous-titres des discussions vocales dans les jeux.", BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.Xbox.TCUI", "Interface Xbox des jeux", "Profils, amis et invitations Xbox dans les jeux.", BloatRisk.Moderate, false) { Warning = Xbox },
        new("Microsoft.YourPhone", "Lien avec Windows (Phone Link)", "Relie votre téléphone Android ou iPhone au PC (SMS, appels, photos).", BloatRisk.Moderate, false)
        {
            Warning = "Vous ne pourrez plus recevoir SMS, appels et notifications du téléphone sur ce PC.",
        },
        new("MicrosoftWindows.CrossDevice", "Expérience multi-appareils", "Composant de Lien avec Windows (téléphone dans le menu Démarrer).", BloatRisk.Moderate, false)
        {
            Warning = "Lien avec Windows et l'intégration du téléphone dans le menu Démarrer ne fonctionneront plus.",
        },
        new("MicrosoftWindows.Client.WebExperience", "Widgets", "Panneau des widgets (actualités, météo) de la barre des tâches.", BloatRisk.Moderate, false)
        {
            Warning = "Le panneau des widgets disparaît ; la météo de la barre des tâches aussi.",
        },
        new("Microsoft.WindowsCamera", "Caméra", "Application Caméra de Windows.", BloatRisk.Moderate, false)
        {
            Warning = "Vous n'aurez plus d'application pour prendre des photos ou tester la webcam.",
        },
        new("Microsoft.ScreenSketch", "Outil Capture d'écran", "Captures avec Win+Maj+S et Impr. écran.", BloatRisk.Moderate, false)
        {
            Warning = "Les raccourcis de capture d'écran (Win+Maj+S) ne fonctionneront plus.",
        },
        new("MicrosoftCorporationII.QuickAssist", "Assistance rapide", "Aide à distance entre deux PC Windows.", BloatRisk.Moderate, false)
        {
            Warning = "Vous ne pourrez plus recevoir d'aide à distance avec Assistance rapide.",
        },
        new("Microsoft.Windows.Photos", "Photos", "Visionneuse d'images et de vidéos par défaut.", BloatRisk.Moderate, false)
        {
            Warning = "C'est la visionneuse par défaut : installez-en une autre (ImageGlass, IrfanView…) avant de la supprimer.",
        },
        new("Microsoft.WindowsCalculator", "Calculatrice", "Calculatrice de Windows.", BloatRisk.Moderate, false)
        {
            Warning = "Windows n'aura plus de calculatrice ; la touche Calculatrice du clavier ne fera plus rien.",
        },
        new("Microsoft.WindowsNotepad", "Bloc-notes", "Éditeur de texte de Windows.", BloatRisk.Moderate, false)
        {
            Warning = "Les fichiers texte n'auront plus d'éditeur par défaut.",
        },
        new("Microsoft.Paint", "Paint", "Dessin et retouche simples.", BloatRisk.Moderate, false)
        {
            Warning = "Paint ne sera plus disponible pour les retouches rapides.",
        },
        new("Microsoft.WindowsTerminal", "Terminal Windows", "Terminal par défaut de Windows 11.", BloatRisk.Moderate, false)
        {
            Warning = "Windows se rabattra sur l'ancienne console ; certains scripts et raccourcis s'ouvrent dans le Terminal.",
        },
    ];

    /// <summary>
    /// Composants indispensables : jamais supprimés par PC Pilot (Store, installateur d'applications, bibliothèques,
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
