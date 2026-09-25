using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Privacy;

/// <summary>
/// Catalogue des réglages de confidentialité. Chaque chemin et chaque valeur provient des modèles d'administration
/// (ADMX, vérifiés dans %windir%\PolicyDefinitions) ou des valeurs écrites par l'application Paramètres de Windows.
/// Déclaratif uniquement : appelé aussi dans le broker élevé.
/// </summary>
public static class PrivacyTweaks
{
    private const string Category = PrivacyModule.Category;

    // ------------------------------------------------------------------ Clés
    private const string DataCollectionPolicy = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    private const string PrivacyKey = @"Software\Microsoft\Windows\CurrentVersion\Privacy";
    private const string CloudContentUserPolicy = @"Software\Policies\Microsoft\Windows\CloudContent";
    private const string CloudContentPolicy = @"SOFTWARE\Policies\Microsoft\Windows\CloudContent";
    private const string SiufRules = @"Software\Microsoft\Siuf\Rules";
    private const string WerPolicy = @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting";
    private const string SqmPolicy = @"SOFTWARE\Policies\Microsoft\SQMClient\Windows";
    private const string AppCompatPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AppCompat";
    private const string EnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string AdvertisingInfo = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
    private const string AdvertisingPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo";
    private const string ProfileEngagement = @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string AccountNotifications = @"Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications";

    private const string ExplorerUserPolicy = @"Software\Policies\Microsoft\Windows\Explorer";
    private const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string SearchSettings = @"Software\Microsoft\Windows\CurrentVersion\SearchSettings";
    private const string WindowsSearchPolicy = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search";
    private const string CopilotUserPolicy = @"Software\Policies\Microsoft\Windows\WindowsCopilot";
    private const string WindowsAiPolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI";
    private const string WindowsAiUserPolicy = @"Software\Policies\Microsoft\Windows\WindowsAI";

    private const string SystemPolicy = @"SOFTWARE\Policies\Microsoft\Windows\System";
    private const string ClipboardKey = @"Software\Microsoft\Clipboard";
    private const string ExplorerKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string ExplorerMachinePolicy = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    private const string InputPersonalization = @"Software\Microsoft\InputPersonalization";
    private const string TrainedDataStore = @"Software\Microsoft\InputPersonalization\TrainedDataStore";
    private const string PersonalizationSettings = @"Software\Microsoft\Personalization\Settings";
    private const string InputTipc = @"Software\Microsoft\Input\TIPC";
    private const string OnlineSpeech = @"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy";
    private const string InternationalProfile = @"Control Panel\International\User Profile";
    private const string TabletPcPolicy = @"SOFTWARE\Policies\Microsoft\Windows\TabletPC";
    private const string HandwritingErrorsPolicy = @"SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports";
    private const string AppPrivacyPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";

    private const string FindMyDevicePolicy = @"SOFTWARE\Policies\Microsoft\FindMyDevice";
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    /// <summary>Tâches planifiées de collecte (celles absentes de la version de Windows sont écartées à la déclaration).</summary>
    internal static readonly string[] TelemetryTaskCandidates =
    [
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser Exp",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Application Experience\AitAgent",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
        @"\Microsoft\Windows\Autochk\Proxy",
        @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
        @"\Microsoft\Windows\Device Information\Device",
        @"\Microsoft\Windows\Device Information\Device User",
        @"\Microsoft\Windows\Feedback\Siuf\DmClient",
        @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
    ];

    private static readonly Requirement Build1903 = Requires.When(p => p.Build >= 18362, "Nécessite Windows 10 version 1903 ou plus récent.");
    private static readonly Requirement Build21H2 = Requires.When(p => p.Build >= 19044, "Nécessite Windows 10 21H2 ou plus récent.");
    private static readonly Requirement LegacyCopilot = Requires.When(p => p.Build < 26100,
        "Sans effet depuis Windows 11 24H2 : Copilot y est une application ordinaire, à désinstaller depuis la page Applications.");

    public static IEnumerable<TweakDefinition> All() =>
    [
        .. Telemetry(),
        .. Advertising(),
        .. SearchAndAi(),
        .. Activity(),
        .. InputAndSpeech(),
        .. Location(),
        .. Permissions(),
    ];

    // ================================================================== Télémétrie et diagnostics

    private static IEnumerable<TweakDefinition> Telemetry()
    {
        const string g = PrivacyGroups.Telemetry;

        yield return Tweak.Choice("privacy.telemetry.level", "Niveau des données de diagnostic",
                "Quantité de données de diagnostic envoyées à Microsoft. « Au choix de l'utilisateur » laisse décider l'option « Données " +
                "facultatives » des Paramètres ; « Données requises uniquement » impose le minimum accepté par les éditions Famille et " +
                "Professionnel (état de l'appareil, sécurité, bon fonctionnement des mises à jour) et verrouille cette option.")
            .In(Category, g)
            .Keywords("telemetrie", "telemetry", "diagnostic", "allowtelemetry", "donnees requises", "donnees facultatives", "optional diagnostic data")
            .Tags("privacy-max")
            .Option("user", "Au choix de l'utilisateur (par défaut)", Reg.LmDel(DataCollectionPolicy, "AllowTelemetry"))
            .Option("required", "Données requises uniquement", Reg.LmDword(DataCollectionPolicy, "AllowTelemetry", 1))
            .Option("optional", "Données facultatives incluses", Reg.LmDword(DataCollectionPolicy, "AllowTelemetry", 3))
            // Pas de WindowsDefault : une valeur 0 (niveau désactivé, réglage suivant) doit apparaître comme « personnalisée ».
            .RecommendWhen(p => p.SupportsTelemetryOff || p.IsManaged ? null : "required")
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.off", "Données de diagnostic désactivées (niveau 0)",
                "Windows cesse d'envoyer des données de diagnostic à Microsoft (ancien niveau « Sécurité »). Accepté uniquement par les " +
                "éditions Entreprise, Éducation, IoT et Server ; les autres éditions le traitent comme « Données requises ». " +
                "Désactiver ce réglage supprime la stratégie : le niveau redevient celui choisi dans les Paramètres.")
            .In(Category, g)
            .Keywords("telemetrie", "securite", "niveau 0", "zero", "diagnostic off", "allowtelemetry")
            .Tags("privacy-max")
            .RequiresAndRecommends(Requires.EnterpriseOrEducation, TweakDefinition.On, p => !p.IsManaged)
            .Risk(RiskLevel.Moderate)
            .Warning("Les services qui s'appuient sur les données de diagnostic (programme Windows Insider, rapports Windows Update for Business, Windows Autopatch) ne fonctionneront plus.")
            .WhenOn(Reg.LmDword(DataCollectionPolicy, "AllowTelemetry", 0))
            .WhenOff(Reg.LmDel(DataCollectionPolicy, "AllowTelemetry"))
            .WindowsDefault(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.diagtrack", "Service de télémétrie (DiagTrack)",
                "Service « Expériences des utilisateurs connectés et télémétrie » qui collecte et envoie les données de diagnostic. " +
                "Désactivé, plus rien n'est transmis par ce service, quel que soit le niveau choisi (le rapport d'erreurs a son propre réglage).")
            .In(Category, g)
            .Keywords("diagtrack", "utc", "connected user experiences", "service telemetrie", "experiences utilisateurs connectes")
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning("Windows peut réactiver ce service lors d'une mise à jour majeure. Le programme Windows Insider et certains outils de gestion d'entreprise en ont besoin.")
            .WhenOn(Sys.Service("DiagTrack", ServiceStartKind.Automatic))
            .WhenOff(Sys.Service("DiagTrack", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .RecommendWhen(p => p.IsManaged ? null : TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.dmwappush", "Service de routage des messages push WAP (dmwappushservice)",
                "Achemine les messages de gestion à distance des appareils (MDM, Intune). Sur un PC personnel non géré, il ne démarre " +
                "pratiquement jamais : le désactiver est une précaution supplémentaire plus qu'un gain réel.")
            .In(Category, g)
            .Keywords("dmwappushservice", "wap push", "mdm", "intune", "gestion appareil")
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning("Nécessaire pour inscrire ou gérer ce PC avec un compte professionnel ou scolaire (Intune/MDM).")
            .WhenOn(Sys.Service("dmwappushservice", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("dmwappushservice", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.tailored", "Expériences personnalisées",
                "Autorise Microsoft à exploiter vos données de diagnostic pour personnaliser les conseils, publicités et recommandations " +
                "affichés dans Windows. Désactivé, vous voyez toujours des suggestions, mais moins ciblées.")
            .In(Category, g)
            .Keywords("tailored experiences", "experiences personnalisees", "personnalisation", "recommandations ciblees")
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(PrivacyKey, "TailoredExperiencesWithDiagnosticDataEnabled", 1),
                    Reg.CuDel(CloudContentUserPolicy, "DisableTailoredExperiencesWithDiagnosticData"))
            .WhenOff(Reg.CuDword(PrivacyKey, "TailoredExperiencesWithDiagnosticDataEnabled", 0),
                     Reg.CuDword(CloudContentUserPolicy, "DisableTailoredExperiencesWithDiagnosticData", 1))
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Cu(PrivacyKey, "TailoredExperiencesWithDiagnosticDataEnabled") == 0,
                PrivacyDetect.Cu(CloudContentUserPolicy, "DisableTailoredExperiencesWithDiagnosticData") == 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.feedback", "Demandes de commentaires",
                "Windows vous demande de temps en temps votre avis par une notification (« Commentaires »). " +
                "Désactivé, la fréquence passe à « Jamais » et une stratégie bloque ces sollicitations.")
            .In(Category, g)
            .Keywords("feedback", "commentaires", "avis", "siuf", "frequence commentaires", "hub commentaires")
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDel(SiufRules, "NumberOfSIUFInPeriod"), Reg.CuDel(SiufRules, "PeriodInNanoSeconds"),
                    Reg.LmDel(DataCollectionPolicy, "DoNotShowFeedbackNotifications"))
            .WhenOff(Reg.CuDword(SiufRules, "NumberOfSIUFInPeriod", 0), Reg.CuDel(SiufRules, "PeriodInNanoSeconds"),
                     Reg.LmDword(DataCollectionPolicy, "DoNotShowFeedbackNotifications", 1))
            // « Jamais » choisi dans les Paramètres (sans la stratégie) compte aussi comme désactivé.
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Lm(DataCollectionPolicy, "DoNotShowFeedbackNotifications") == 1,
                PrivacyDetect.Cu(SiufRules, "NumberOfSIUFInPeriod") == 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.limits", "Limitation des vidages mémoire et journaux envoyés",
                "Si l'envoi des données facultatives est autorisé, Windows peut joindre des vidages mémoire complets (qui peuvent contenir " +
                "des documents ouverts) et des journaux de diagnostic. Activé, il se limite à de petits vidages et n'envoie plus ces journaux.")
            .In(Category, g)
            .Keywords("dump", "vidage memoire", "journaux diagnostic", "limitdumpcollection", "limitdiagnosticlogcollection")
            .Tags("privacy-max")
            .RequiresAndRecommends(Build1903, TweakDefinition.On)
            .WhenOn(Reg.LmDword(DataCollectionPolicy, "LimitDumpCollection", 1), Reg.LmDword(DataCollectionPolicy, "LimitDiagnosticLogCollection", 1))
            .WhenOff(Reg.LmDel(DataCollectionPolicy, "LimitDumpCollection"), Reg.LmDel(DataCollectionPolicy, "LimitDiagnosticLogCollection"))
            .WindowsDefault(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.wer", "Rapport d'erreurs Windows",
                "Quand une application ou Windows plante, un rapport (parfois avec une copie de la mémoire du programme) est envoyé à " +
                "Microsoft pour rechercher une solution et informer l'éditeur.")
            .In(Category, g)
            .Keywords("wer", "error reporting", "rapport erreur", "plantage", "crash", "werfault")
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning("Plus aucun rapport de plantage n'est envoyé : les éditeurs ne reçoivent plus d'informations pour corriger les bugs et Windows ne propose plus de solutions.")
            .WhenOn(Reg.LmDel(WerPolicy, "Disabled"))
            .WhenOff(Reg.LmDword(WerPolicy, "Disabled", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.ceip", "Programme d'amélioration de l'expérience utilisateur",
                "Ancien programme de collecte de statistiques d'utilisation (CEIP/SQM). Largement remplacé par les données de diagnostic, " +
                "il reste consulté par certains composants de Windows.")
            .In(Category, g)
            .Keywords("ceip", "sqm", "customer experience improvement program", "amelioration experience")
            .Tags("privacy-max")
            .WhenOn(Reg.LmDel(SqmPolicy, "CEIPEnable"))
            .WhenOff(Reg.LmDword(SqmPolicy, "CEIPEnable", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.appcompat", "Collecte de compatibilité des applications",
                "Inventaire des programmes installés, télémétrie d'utilisation des applications et Enregistreur d'actions (psr.exe), " +
                "utilisés pour évaluer la compatibilité. Désactivé, l'Enregistreur d'actions n'est plus utilisable ; " +
                "le moteur de compatibilité lui-même reste actif.")
            .In(Category, g)
            .Keywords("appcompat", "inventaire", "inventory", "application telemetry", "enregistreur actions", "steps recorder", "psr")
            .Tags("privacy-max")
            .WhenOn(Reg.LmDel(AppCompatPolicy, "DisableInventory"), Reg.LmDel(AppCompatPolicy, "AITEnable"), Reg.LmDel(AppCompatPolicy, "DisableUAR"))
            .WhenOff(Reg.LmDword(AppCompatPolicy, "DisableInventory", 1), Reg.LmDword(AppCompatPolicy, "AITEnable", 0), Reg.LmDword(AppCompatPolicy, "DisableUAR", 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        var tasks = PrivacyDetect.ExistingTasks(TelemetryTaskCandidates);
        if (tasks.Length > 0)
        {
            yield return Tweak.Toggle("privacy.telemetry.tasks", "Tâches planifiées de télémétrie",
                    "Évaluateur de compatibilité, programme d'amélioration (Consolidator, UsbCeip), diagnostics disque, recensement de " +
                    $"l'appareil (DeviceCensus) et commentaires (DmClient). {tasks.Length} tâche(s) concernée(s) sur ce PC ; " +
                    "celles absentes de votre version de Windows sont ignorées.")
                .In(Category, g)
                .Keywords("tache planifiee", "scheduled task", "compatibility appraiser", "consolidator", "devicecensus", "dmclient", "ceip")
                .Tags("privacy-max")
                .Warning("Windows peut réactiver ces tâches lors d'une mise à jour majeure. Windows Update effectue de toute façon ses propres vérifications de compatibilité avant une mise à niveau.")
                .WhenOn([.. tasks.Select(t => (Operation)Sys.EnableTask(t))])
                .WhenOff([.. tasks.Select(t => (Operation)Sys.DisableTask(t))])
                .Detect(() => PrivacyDetect.TasksState(tasks))
                .WindowsDefault(TweakDefinition.On)
                .Recommend(TweakDefinition.Off)
                .Build();
        }

        yield return Tweak.Toggle("privacy.telemetry.devtools", "Télémétrie de PowerShell 7 et du SDK .NET",
                "Ces outils de développement envoient des statistiques d'utilisation anonymes. Désactivé, les variables " +
                "POWERSHELL_TELEMETRY_OPTOUT et DOTNET_CLI_TELEMETRY_OPTOUT sont définies pour tout le PC (prises en compte par les " +
                "programmes lancés ensuite). Sans effet si ces outils ne sont pas installés ; Windows PowerShell 5.1 n'est pas concerné.")
            .In(Category, g)
            .Keywords("powershell", "pwsh", "dotnet", ".net", "sdk", "developpeur", "telemetry optout")
            .Tags("privacy-max", "dev")
            .WhenOn(Reg.LmDel(EnvironmentKey, "POWERSHELL_TELEMETRY_OPTOUT"), Reg.LmDel(EnvironmentKey, "DOTNET_CLI_TELEMETRY_OPTOUT"),
                    Sys.Broadcast("Environment"))
            .WhenOff(Reg.LmString(EnvironmentKey, "POWERSHELL_TELEMETRY_OPTOUT", "1"), Reg.LmString(EnvironmentKey, "DOTNET_CLI_TELEMETRY_OPTOUT", "1"),
                     Sys.Broadcast("Environment"))
            .WindowsDefault(TweakDefinition.On)
            .Build();
    }

    // ================================================================== Publicité et suggestions

    private static IEnumerable<TweakDefinition> Advertising()
    {
        const string g = PrivacyGroups.Ads;

        yield return Tweak.Toggle("privacy.ads.id", "Identifiant de publicité",
                "Identifiant unique qui permet aux applications et aux régies publicitaires de vous reconnaître d'une application à " +
                "l'autre pour cibler les publicités. Désactivé, les applications ne peuvent plus l'utiliser ; s'il est réactivé, " +
                "un nouvel identifiant est créé.")
            .In(Category, g)
            .Keywords("pub", "publicite", "ads", "advertising id", "tracking", "ciblage", "identifiant publicitaire")
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(AdvertisingInfo, "Enabled", 1), Reg.LmDel(AdvertisingPolicy, "DisabledByGroupPolicy"))
            .WhenOff(Reg.CuDword(AdvertisingInfo, "Enabled", 0), Reg.LmDword(AdvertisingPolicy, "DisabledByGroupPolicy", 1))
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Cu(AdvertisingInfo, "Enabled") == 0,
                PrivacyDetect.Lm(AdvertisingPolicy, "DisabledByGroupPolicy") == 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.start-suggestions", "Suggestions dans le menu Démarrer",
                "Applications suggérées, souvent sponsorisées, affichées de temps en temps dans le menu Démarrer de Windows 10.")
            .In(Category, g)
            .Keywords("suggestion", "menu demarrer", "applications suggerees", "sponsorise", "start suggestions")
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows10Only, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-338388Enabled", 1), Reg.CuDword(ContentDelivery, "SystemPaneSuggestionsEnabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-338388Enabled", 0), Reg.CuDword(ContentDelivery, "SystemPaneSuggestionsEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.tips", "Astuces et suggestions de Windows",
                "Notifications de conseils et de suggestions pendant l'utilisation de Windows (découverte de fonctions, offres Microsoft).")
            .In(Category, g)
            .Keywords("astuce", "conseil", "tips", "suggestion", "notification", "soft landing")
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-338389Enabled", 1), Reg.CuDword(ContentDelivery, "SoftLandingEnabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-338389Enabled", 0), Reg.CuDword(ContentDelivery, "SoftLandingEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.settings-content", "Contenu suggéré dans les Paramètres",
                "Suggestions et offres (Microsoft 365, OneDrive, Game Pass…) affichées dans l'application Paramètres.")
            .In(Category, g)
            .Keywords("parametres", "settings", "contenu suggere", "suggested content", "offre")
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-338393Enabled", 1), Reg.CuDword(ContentDelivery, "SubscribedContent-353694Enabled", 1),
                    Reg.CuDword(ContentDelivery, "SubscribedContent-353696Enabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-338393Enabled", 0), Reg.CuDword(ContentDelivery, "SubscribedContent-353694Enabled", 0),
                     Reg.CuDword(ContentDelivery, "SubscribedContent-353696Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.settings-notifications", "Notifications dans l'application Paramètres",
                "Rappels liés à votre compte Microsoft (sauvegarde, abonnement, sécurité du compte) affichés dans les Paramètres.")
            .In(Category, g)
            .Keywords("notification", "parametres", "compte microsoft", "account notifications")
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_22H2, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(AccountNotifications, "EnableAccountNotifications", 1))
            .WhenOff(Reg.CuDword(AccountNotifications, "EnableAccountNotifications", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.lockscreen", "Anecdotes et conseils sur l'écran de verrouillage",
                "Textes (anecdotes, astuces, liens vers des offres) superposés à l'image de l'écran de verrouillage.")
            .In(Category, g)
            .Keywords("ecran verrouillage", "lock screen", "anecdote", "fun facts", "astuce")
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "RotatingLockScreenOverlayEnabled", 1), Reg.CuDword(ContentDelivery, "SubscribedContent-338387Enabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "RotatingLockScreenOverlayEnabled", 0), Reg.CuDword(ContentDelivery, "SubscribedContent-338387Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.welcome", "Présentation des nouveautés après les mises à jour",
                "Écrans d'accueil qui présentent les nouveautés et des suggestions Microsoft après une mise à jour ou à la connexion.")
            .In(Category, g)
            .Keywords("welcome experience", "accueil", "nouveautes", "apres mise jour")
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-310093Enabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-310093Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.silent-install", "Installation automatique d'applications suggérées",
                "Windows installe ou épingle de lui-même des applications promues (jeux, applications partenaires), notamment à la " +
                "création d'un compte ou après une mise à jour majeure. Désactivé, ces installations silencieuses sont bloquées ; " +
                "les applications déjà présentes restent installées.")
            .In(Category, g)
            .Keywords("bloatware", "candy crush", "installation silencieuse", "applications promues", "silent install", "preinstalle")
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SilentInstalledAppsEnabled", 1), Reg.CuDword(ContentDelivery, "OemPreInstalledAppsEnabled", 1),
                    Reg.CuDword(ContentDelivery, "PreInstalledAppsEnabled", 1), Reg.CuDword(ContentDelivery, "PreInstalledAppsEverEnabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SilentInstalledAppsEnabled", 0), Reg.CuDword(ContentDelivery, "OemPreInstalledAppsEnabled", 0),
                     Reg.CuDword(ContentDelivery, "PreInstalledAppsEnabled", 0), Reg.CuDword(ContentDelivery, "PreInstalledAppsEverEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.content-delivery", "Diffusion de contenu Microsoft (interrupteur général)",
                "Autorise le gestionnaire de contenu de Windows (ContentDeliveryManager) à télécharger suggestions, promotions et images. " +
                "Désactivé, toute cette diffusion s'arrête d'un coup, y compris les contenus utiles.")
            .In(Category, g)
            .Keywords("contentdeliverymanager", "content delivery", "diffusion contenu", "promotion")
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning("Les images « Windows à la une » de l'écran de verrouillage risquent de ne plus se renouveler.")
            .WhenOn(Reg.CuDword(ContentDelivery, "ContentDeliveryAllowed", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "ContentDeliveryAllowed", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.scoobe", "Rappels « Terminer la configuration de l'appareil »",
                "Écran plein qui revient après certaines mises à jour pour proposer OneDrive, Microsoft 365, Lien avec Windows ou un compte Microsoft.")
            .In(Category, g)
            .Keywords("scoobe", "terminer configuration", "finish setup", "tirer le meilleur parti", "rappel")
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ProfileEngagement, "ScoobeSystemSettingEnabled", 1))
            .WhenOff(Reg.CuDword(ProfileEngagement, "ScoobeSystemSettingEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.start-iris", "Recommandations d'astuces et d'applications dans Démarrer",
                "Conseils, raccourcis et nouvelles applications (certaines promues) proposés dans la section « Recommandé » du menu Démarrer de Windows 11.")
            .In(Category, g)
            .Keywords("menu demarrer", "recommande", "iris", "recommandations", "start recommendations")
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_22H2, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_IrisRecommendations", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_IrisRecommendations", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.start-account", "Notifications de compte dans Démarrer",
                "Rappels liés au compte Microsoft (sauvegarde, stockage OneDrive, sécurité du compte) signalés sur votre photo de profil dans le menu Démarrer.")
            .In(Category, g)
            .Keywords("menu demarrer", "compte", "account notifications", "badge profil")
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_22H2, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_AccountNotifications", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_AccountNotifications", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.consumer-features", "Expériences Microsoft grand public (stratégie)",
                "Stratégie officielle qui bloque pour tout le PC les recommandations personnalisées, les installations d'applications " +
                "promues et les notifications liées au compte Microsoft. Windows ne la respecte que sur les éditions Entreprise et Éducation ; " +
                "sur les autres, utilisez les réglages de cette section.")
            .In(Category, g)
            .Keywords("consumer features", "experiences grand public", "cloud content", "applications promues")
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.EnterpriseOrEducation, TweakDefinition.Off)
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.LmDel(CloudContentPolicy, "DisableWindowsConsumerFeatures"))
            .WhenOff(Reg.LmDword(CloudContentPolicy, "DisableWindowsConsumerFeatures", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.spotlight", "Fonctionnalités « Windows à la une » (stratégie)",
                "Désactive en bloc Windows à la une (images et suggestions de l'écran de verrouillage, conseils, contenus promus) ainsi que " +
                "les suggestions d'éditeurs tiers. Respecté uniquement par les éditions Entreprise et Éducation.")
            .In(Category, g)
            .Keywords("spotlight", "windows a la une", "ecran verrouillage", "third party suggestions")
            .Tags("privacy-max")
            .Requires(Requires.EnterpriseOrEducation)
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.CuDel(CloudContentUserPolicy, "DisableWindowsSpotlightFeatures"), Reg.CuDel(CloudContentUserPolicy, "DisableThirdPartySuggestions"))
            .WhenOff(Reg.CuDword(CloudContentUserPolicy, "DisableWindowsSpotlightFeatures", 1), Reg.CuDword(CloudContentUserPolicy, "DisableThirdPartySuggestions", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();
    }

    // ================================================================== Recherche et IA

    private static IEnumerable<TweakDefinition> SearchAndAi()
    {
        const string g = PrivacyGroups.Search;

        yield return Tweak.Toggle("privacy.search.web", "Résultats web (Bing) dans la recherche",
                "Ce que vous tapez dans la recherche du menu Démarrer ou de la barre des tâches est aussi envoyé à Bing pour afficher des " +
                "résultats web. Désactivé, la recherche reste locale (applications, fichiers, paramètres). Effet secondaire : " +
                "l'Explorateur n'affiche plus vos recherches récentes.")
            .In(Category, g)
            .Keywords("bing", "recherche web", "web search", "resultats internet", "disablesearchboxsuggestions")
            .Tags("privacy-max", "family")
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDel(ExplorerUserPolicy, "DisableSearchBoxSuggestions"), Reg.CuDel(SearchKey, "BingSearchEnabled"))
            .WhenOff(Reg.CuDword(ExplorerUserPolicy, "DisableSearchBoxSuggestions", 1), Reg.CuDword(SearchKey, "BingSearchEnabled", 0))
            // Windows 11 n'utilise plus BingSearchEnabled : seule la stratégie compte.
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Cu(ExplorerUserPolicy, "DisableSearchBoxSuggestions") == 1,
                PrivacyDetect.WindowsBuild < 22000 && PrivacyDetect.Cu(SearchKey, "BingSearchEnabled") == 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.search.cloud", "Recherche dans vos contenus cloud",
                "Inclut dans la recherche Windows des résultats issus de OneDrive, Outlook et des autres services liés à votre compte " +
                "Microsoft ou professionnel : vos recherches sont alors aussi transmises à ces services.")
            .In(Category, g)
            .Keywords("cloud search", "recherche cloud", "onedrive", "outlook", "compte microsoft", "compte professionnel")
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(SearchSettings, "IsMSACloudSearchEnabled", 1), Reg.CuDword(SearchSettings, "IsAADCloudSearchEnabled", 1))
            .WhenOff(Reg.CuDword(SearchSettings, "IsMSACloudSearchEnabled", 0), Reg.CuDword(SearchSettings, "IsAADCloudSearchEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.search.history", "Historique des recherches sur cet appareil",
                "Mémorise vos recherches pour vous les proposer à nouveau. Ces données restent sur le PC.")
            .In(Category, g)
            .Keywords("historique recherche", "search history", "recherches recentes")
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(SearchSettings, "IsDeviceSearchHistoryEnabled", 1))
            .WhenOff(Reg.CuDword(SearchSettings, "IsDeviceSearchHistoryEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.search.highlights", "Points forts de la recherche",
                "Illustrations, événements du jour et contenus tendance fournis par Bing dans la zone et le panneau de recherche.")
            .In(Category, g)
            .Keywords("search highlights", "points forts", "tendances", "bing", "illustration recherche")
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Build21H2, TweakDefinition.Off)
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(SearchSettings, "IsDynamicSearchBoxEnabled", 1), Reg.LmDel(WindowsSearchPolicy, "EnableDynamicContentInWSB"))
            .WhenOff(Reg.CuDword(SearchSettings, "IsDynamicSearchBoxEnabled", 0), Reg.LmDword(WindowsSearchPolicy, "EnableDynamicContentInWSB", 0))
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Cu(SearchSettings, "IsDynamicSearchBoxEnabled") == 0,
                PrivacyDetect.Lm(WindowsSearchPolicy, "EnableDynamicContentInWSB") == 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Choice("privacy.search.safesearch", "Recherche sécurisée (SafeSearch)",
                "Filtrage des contenus pour adultes dans les résultats web de la recherche Windows. Sans objet si les résultats web sont désactivés.")
            .In(Category, g)
            .Keywords("safesearch", "recherche securisee", "contenu adulte", "filtrage", "controle parental")
            .Tags("family")
            .Option("strict", "Stricte", Reg.CuDword(SearchSettings, "SafeSearchMode", 2))
            .Option("moderate", "Modérée (par défaut)", Reg.CuDword(SearchSettings, "SafeSearchMode", 1))
            .Option("off", "Désactivée", Reg.CuDword(SearchSettings, "SafeSearchMode", 0))
            .WindowsDefault("moderate")
            .Build();

        yield return Tweak.Toggle("privacy.ai.copilot", "Copilot intégré (ancienne version)",
                "Stratégie « Désactiver Windows Copilot » : masque et bloque le volet Copilot intégré à Windows 11 23H2 et à Windows 10 " +
                "(bouton de la barre des tâches compris).")
            .In(Category, g)
            .Keywords("copilot", "ia", "ai", "assistant", "turnoffwindowscopilot")
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(LegacyCopilot, TweakDefinition.Off)
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDel(CopilotUserPolicy, "TurnOffWindowsCopilot"))
            .WhenOff(Reg.CuDword(CopilotUserPolicy, "TurnOffWindowsCopilot", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ai.recall", "Captures d'écran de Recall (Retrouver)",
                "Sur les PC Copilot+, Recall enregistre régulièrement des captures de votre écran pour vous permettre de retrouver ce que " +
                "vous avez vu. La stratégie interdit l'enregistrement de ces captures ; elle est sans effet sur les autres PC.")
            .In(Category, g)
            .Keywords("recall", "retrouver", "capture ecran", "instantane", "snapshot", "copilot+", "disableaidataanalysis")
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_24H2, TweakDefinition.Off)
            .Warning("Les captures déjà enregistrées par Recall sont supprimées quand l'enregistrement est désactivé.")
            .WhenOn(Reg.LmDel(WindowsAiPolicy, "DisableAIDataAnalysis"), Reg.CuDel(WindowsAiUserPolicy, "DisableAIDataAnalysis"))
            .WhenOff(Reg.LmDword(WindowsAiPolicy, "DisableAIDataAnalysis", 1), Reg.CuDword(WindowsAiUserPolicy, "DisableAIDataAnalysis", 1))
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Lm(WindowsAiPolicy, "DisableAIDataAnalysis") == 1,
                PrivacyDetect.Cu(WindowsAiUserPolicy, "DisableAIDataAnalysis") == 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ai.recall-component", "Composant Recall",
                "Stratégie « Autoriser l'activation de Recall ». « Retiré » : le composant facultatif Recall est désactivé et supprimé " +
                "de Windows après redémarrage. « Disponible » (par défaut) : il reste désactivé tant que vous ne l'activez pas vous-même.")
            .In(Category, g)
            .Keywords("recall", "retrouver", "composant facultatif", "allowrecallenablement", "copilot+")
            .Labels("Disponible", "Retiré")
            .Requires(Requires.Windows11_24H2)
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning("Les captures Recall existantes sont supprimées. Pour le récupérer, remettez ce réglage sur « Disponible » puis redémarrez le PC.")
            .WhenOn(Reg.LmDel(WindowsAiPolicy, "AllowRecallEnablement"))
            .WhenOff(Reg.LmDword(WindowsAiPolicy, "AllowRecallEnablement", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ai.clicktodo", "Click to Do (Actions par clic)",
                "Analyse une capture de l'écran, sur l'appareil, pour proposer des actions sur le texte ou les images affichés. " +
                "Présent surtout sur les PC Copilot+.")
            .In(Category, g)
            .Keywords("click to do", "actions par clic", "ia", "capture", "disableclicktodo")
            .Tags("privacy-max")
            .Requires(Requires.Windows11_24H2)
            .WhenOn(Reg.LmDel(WindowsAiPolicy, "DisableClickToDo"), Reg.CuDel(WindowsAiUserPolicy, "DisableClickToDo"))
            .WhenOff(Reg.LmDword(WindowsAiPolicy, "DisableClickToDo", 1), Reg.CuDword(WindowsAiUserPolicy, "DisableClickToDo", 1))
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Lm(WindowsAiPolicy, "DisableClickToDo") == 1,
                PrivacyDetect.Cu(WindowsAiUserPolicy, "DisableClickToDo") == 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.search.cortana", "Cortana",
                "Assistant vocal de Windows 10. La stratégie le désactive pour tous les utilisateurs du PC.")
            .In(Category, g)
            .Keywords("cortana", "assistant vocal", "allowcortana")
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows10Only, TweakDefinition.Off)
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.LmDel(WindowsSearchPolicy, "AllowCortana"))
            .WhenOff(Reg.LmDword(WindowsSearchPolicy, "AllowCortana", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();
    }

    // ================================================================== Activité et historique

    private static IEnumerable<TweakDefinition> Activity()
    {
        const string g = PrivacyGroups.Activity;

        yield return Tweak.Toggle("privacy.activity.history", "Historique des activités",
                "Windows enregistre les applications, fichiers et pages que vous ouvrez (reprise d'activité) et peut les envoyer à votre " +
                "compte Microsoft. La stratégie coupe l'enregistrement et l'envoi pour tous les comptes du PC.")
            .In(Category, g)
            .Keywords("historique activite", "activity history", "timeline", "chronologie", "publishuseractivities")
            .Tags("privacy-max", "family")
            .WhenOn(Reg.LmDel(SystemPolicy, "EnableActivityFeed"), Reg.LmDel(SystemPolicy, "PublishUserActivities"), Reg.LmDel(SystemPolicy, "UploadUserActivities"))
            .WhenOff(Reg.LmDword(SystemPolicy, "EnableActivityFeed", 0), Reg.LmDword(SystemPolicy, "PublishUserActivities", 0), Reg.LmDword(SystemPolicy, "UploadUserActivities", 0))
            // Sans publication (PublishUserActivities = 0), Windows n'enregistre ni n'envoie plus rien.
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Lm(SystemPolicy, "PublishUserActivities") == 0,
                PrivacyDetect.Lm(SystemPolicy, "EnableActivityFeed") == 0 && PrivacyDetect.Lm(SystemPolicy, "UploadUserActivities") == 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.activity.clipboard-sync", "Synchronisation du presse-papiers entre appareils",
                "Option « Partager entre vos appareils » : le contenu copié transite par le cloud Microsoft vers vos autres appareils. " +
                "Bloqué, l'option n'est plus proposée.")
            .In(Category, g)
            .Keywords("presse papier", "clipboard", "synchronisation", "cloud clipboard", "cross device")
            .Tags("privacy-max", "family")
            .Labels("Autorisé", "Bloqué")
            .WhenOn(Reg.LmDel(SystemPolicy, "AllowCrossDeviceClipboard"))
            .WhenOff(Reg.LmDword(SystemPolicy, "AllowCrossDeviceClipboard", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.activity.clipboard-history", "Historique du presse-papiers",
                "Conserve les derniers éléments copiés (Windows + V), y compris d'éventuels mots de passe. L'historique reste sur le PC " +
                "et se vide au redémarrage, sauf les éléments épinglés.")
            .In(Category, g)
            .Keywords("presse papier", "clipboard history", "windows v", "historique copier")
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ClipboardKey, "EnableClipboardHistory", 1))
            .WhenOff(Reg.CuDword(ClipboardKey, "EnableClipboardHistory", 0))
            .WindowsDefault(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.activity.track-progs", "Suivi du lancement des applications",
                "Windows mémorise les applications que vous lancez pour améliorer le menu Démarrer et la recherche (liste « Les plus " +
                "utilisées »). Ces données restent sur le PC.")
            .In(Category, g)
            .Keywords("start_trackprogs", "applications plus utilisees", "suivi applications", "track app launches")
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_TrackProgs", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_TrackProgs", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.track-docs", "Éléments récents dans Démarrer et les listes de raccourcis",
                "Affiche les fichiers ouverts récemment dans le menu Démarrer, les listes de raccourcis (clic droit sur une icône de la " +
                "barre des tâches) et l'Explorateur.")
            .In(Category, g)
            .Keywords("start_trackdocs", "elements recents", "jump list", "liste raccourcis", "fichiers recents")
            .Tags("privacy-max")
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_TrackDocs", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_TrackDocs", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.explorer-recent", "Fichiers récents dans l'Explorateur",
                "Section « Récent » de l'Accueil de l'Explorateur (Accès rapide sous Windows 10). Pris en compte dans les nouvelles " +
                "fenêtres de l'Explorateur.")
            .In(Category, g)
            .Keywords("fichiers recents", "showrecent", "acces rapide", "quick access", "accueil explorateur")
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ExplorerKey, "ShowRecent", 1))
            .WhenOff(Reg.CuDword(ExplorerKey, "ShowRecent", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.explorer-frequent", "Dossiers fréquents dans l'Explorateur",
                "Dossiers que vous ouvrez souvent, ajoutés automatiquement à l'Accès rapide. Pris en compte dans les nouvelles fenêtres.")
            .In(Category, g)
            .Keywords("dossiers frequents", "showfrequent", "acces rapide", "quick access")
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ExplorerKey, "ShowFrequent", 1))
            .WhenOff(Reg.CuDword(ExplorerKey, "ShowFrequent", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.explorer-cloud", "Fichiers d'Office.com dans l'Explorateur",
                "Affiche dans l'Accueil de l'Explorateur les documents récents de votre compte Microsoft 365 et OneDrive, récupérés en ligne.")
            .In(Category, g)
            .Keywords("office.com", "microsoft 365", "onedrive", "fichiers cloud", "showcloudfilesinquickaccess")
            .Tags("privacy-max")
            .Requires(Requires.Windows11_22H2)
            .WhenOn(Reg.CuDword(ExplorerKey, "ShowCloudFilesInQuickAccess", 1))
            .WhenOff(Reg.CuDword(ExplorerKey, "ShowCloudFilesInQuickAccess", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.online-tips", "Conseils en ligne dans les Paramètres",
                "Autorise l'application Paramètres à télécharger des conseils et des contenus d'aide depuis les serveurs de Microsoft.")
            .In(Category, g)
            .Keywords("conseils en ligne", "online tips", "aide en ligne", "allowonlinetips")
            .Tags("privacy-max")
            .WhenOn(Reg.LmDel(ExplorerMachinePolicy, "AllowOnlineTips"))
            .WhenOff(Reg.LmDword(ExplorerMachinePolicy, "AllowOnlineTips", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();
    }

    // ================================================================== Saisie, voix et langue

    private static IEnumerable<TweakDefinition> InputAndSpeech()
    {
        const string g = PrivacyGroups.Input;

        yield return Tweak.Toggle("privacy.input.personalization", "Dictionnaire personnel de saisie et d'écriture manuscrite",
                "Windows apprend de ce que vous tapez et écrivez à la main (ainsi que de vos contacts) pour améliorer les suggestions " +
                "et la reconnaissance. Désactivé, Windows cesse cet apprentissage.")
            .In(Category, g)
            .Keywords("saisie", "clavier", "ecriture manuscrite", "inking typing", "dictionnaire personnel", "input personalization")
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(PersonalizationSettings, "AcceptedPrivacyPolicy", 1), Reg.CuDword(InputPersonalization, "RestrictImplicitInkCollection", 0),
                    Reg.CuDword(InputPersonalization, "RestrictImplicitTextCollection", 0), Reg.CuDword(TrainedDataStore, "HarvestContacts", 1))
            .WhenOff(Reg.CuDword(PersonalizationSettings, "AcceptedPrivacyPolicy", 0), Reg.CuDword(InputPersonalization, "RestrictImplicitInkCollection", 1),
                     Reg.CuDword(InputPersonalization, "RestrictImplicitTextCollection", 1), Reg.CuDword(TrainedDataStore, "HarvestContacts", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.tipc", "Amélioration de la saisie et de l'écriture manuscrite",
                "Envoie à Microsoft des données de saisie et d'écriture manuscrite pour améliorer la reconnaissance et les suggestions. " +
                "Ne s'applique que si l'envoi des données de diagnostic facultatives est activé.")
            .In(Category, g)
            .Keywords("tipc", "improve inking typing", "saisie", "ecriture manuscrite", "donnees facultatives")
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(InputTipc, "Enabled", 1))
            .WhenOff(Reg.CuDword(InputTipc, "Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.speech", "Reconnaissance vocale en ligne",
                "Utilise les serveurs de Microsoft pour la dictée et les commandes vocales : plus précis, mais votre voix est envoyée " +
                "en ligne. Désactivé, les fonctions vocales locales (Accès vocal, reconnaissance vocale Windows) restent utilisables.")
            .In(Category, g)
            .Keywords("voix", "vocal", "speech", "dictee", "reconnaissance vocale", "online speech")
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(OnlineSpeech, "HasAccepted", 1))
            .WhenOff(Reg.CuDword(OnlineSpeech, "HasAccepted", 0))
            .WindowsDefault(TweakDefinition.Off)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.voice-activation", "Activation vocale des applications",
                "Autorise les assistants vocaux à écouter en permanence leur mot-clé pour se déclencher, y compris sur l'écran de " +
                "verrouillage. La stratégie s'applique à tous les comptes du PC.")
            .In(Category, g)
            .Keywords("activation vocale", "voice activation", "mot cle", "assistant vocal", "ecoute")
            .Tags("privacy-max")
            .Labels("Autorisé", "Bloqué")
            .WhenOn(Reg.LmDel(AppPrivacyPolicy, "LetAppsActivateWithVoice"), Reg.LmDel(AppPrivacyPolicy, "LetAppsActivateWithVoiceAboveLock"))
            .WhenOff(Reg.LmDword(AppPrivacyPolicy, "LetAppsActivateWithVoice", 2), Reg.LmDword(AppPrivacyPolicy, "LetAppsActivateWithVoiceAboveLock", 2))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.input.language-list", "Accès des sites web à votre liste de langues",
                "Les sites web peuvent lire la liste des langues configurées dans Windows pour afficher un contenu adapté à votre région.")
            .In(Category, g)
            .Keywords("langue", "language list", "httpacceptlanguageoptout", "sites web", "contenu local")
            .Tags("privacy-max")
            .WhenOn(Reg.CuDel(InternationalProfile, "HttpAcceptLanguageOptOut"))
            .WhenOff(Reg.CuDword(InternationalProfile, "HttpAcceptLanguageOptOut", 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.handwriting", "Partage des données d'écriture manuscrite",
                "Envoi à Microsoft d'échantillons d'écriture manuscrite et de rapports d'erreurs de reconnaissance pour améliorer le " +
                "service. Concerne surtout les PC tactiles avec stylet.")
            .In(Category, g)
            .Keywords("ecriture manuscrite", "handwriting", "stylet", "pen", "reconnaissance ecriture")
            .Tags("privacy-max")
            .Labels("Autorisé", "Bloqué")
            .WhenOn(Reg.LmDel(TabletPcPolicy, "PreventHandwritingDataSharing"), Reg.LmDel(HandwritingErrorsPolicy, "PreventHandwritingErrorReports"))
            .WhenOff(Reg.LmDword(TabletPcPolicy, "PreventHandwritingDataSharing", 1), Reg.LmDword(HandwritingErrorsPolicy, "PreventHandwritingErrorReports", 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();
    }

    // ================================================================== Localisation

    private static IEnumerable<TweakDefinition> Location()
    {
        const string g = PrivacyGroups.Location;

        yield return Tweak.Toggle("privacy.location.search", "Utilisation de la position par la recherche",
                "Autorise la recherche Windows à utiliser la position de l'appareil pour proposer des résultats locaux. " +
                "La stratégie s'applique à tous les comptes du PC.")
            .In(Category, g)
            .Keywords("localisation", "position", "recherche", "allowsearchtouselocation", "resultats locaux")
            .Tags("privacy-max")
            .Labels("Autorisé", "Bloqué")
            .WhenOn(Reg.LmDel(WindowsSearchPolicy, "AllowSearchToUseLocation"))
            .WhenOff(Reg.LmDword(WindowsSearchPolicy, "AllowSearchToUseLocation", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.location.findmydevice", "Localiser mon appareil",
                "Permet de retrouver ce PC sur une carte depuis account.microsoft.com en cas de perte ou de vol. Une fois la fonction " +
                "activée par l'utilisateur, la position du PC est envoyée régulièrement à Microsoft. La stratégie l'interdit pour tout le PC.")
            .In(Category, g)
            .Keywords("localiser appareil", "find my device", "vol", "perte", "position", "antivol")
            .Labels("Autorisé", "Bloqué")
            .Risk(RiskLevel.Moderate)
            .Warning("Vous ne pourrez plus localiser ni verrouiller ce PC à distance s'il est perdu ou volé.")
            .WhenOn(Reg.LmDel(FindMyDevicePolicy, "AllowFindMyDevice"))
            .WhenOff(Reg.LmDword(FindMyDevicePolicy, "AllowFindMyDevice", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();
    }

    // ================================================================== Autorisations des applications (compte courant)

    private static IEnumerable<TweakDefinition> Permissions()
    {
        const string cam = "Accès à la caméra pour votre compte. Sans effet si la caméra est bloquée pour tout le PC (page Périphériques).";
        yield return Permission("webcam", "webcam", "Accès des applications à la caméra", cam,
            ["camera", "webcam", "video"], recommendDeny: false, Requires.Camera);
        yield return Permission("microphone", "microphone", "Accès des applications au microphone",
            "Accès au micro pour votre compte. Sans effet si le micro est bloqué pour tout le PC (page Périphériques).",
            ["micro", "microphone", "audio", "enregistrement"], recommendDeny: false);
        yield return Permission("location", "location", "Accès des applications à votre position",
            "Accès à la position de l'appareil pour votre compte. Sans effet si la localisation est désactivée pour tout le PC (page Périphériques).",
            ["localisation", "position", "gps", "location"], recommendDeny: false);
        yield return Permission("userNotificationListener", "notifications", "Accès des applications à vos notifications",
            "Permet à des applications de lire toutes les notifications que vous recevez (utile pour certaines montres connectées).",
            ["notification", "notification listener"], recommendDeny: false);
        yield return Permission("userAccountInformation", "account-info", "Accès des applications aux informations de votre compte",
            "Nom, photo et adresse de votre compte Windows, lisibles par les applications qui le demandent.",
            ["compte", "account info", "nom utilisateur", "photo profil"], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("contacts", "contacts", "Accès des applications à vos contacts",
            "Lecture des contacts enregistrés dans Windows (application Contacts, comptes de messagerie).",
            ["contacts", "carnet adresses", "people"], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("appointments", "calendar", "Accès des applications à votre calendrier",
            "Lecture et modification des rendez-vous enregistrés dans Windows.",
            ["calendrier", "agenda", "rendez vous", "calendar", "appointments"], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("phoneCall", "phone-calls", "Accès des applications aux appels téléphoniques",
            "Passer des appels depuis le PC (par exemple via Lien avec Windows et un téléphone associé).",
            ["appel", "telephone", "phone call", "lien avec windows"], recommendDeny: false);
        yield return Permission("phoneCallHistory", "call-history", "Accès des applications à l'historique des appels",
            "Lecture de l'historique des appels synchronisé sur le PC.",
            ["historique appels", "call history", "telephone"], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("email", "email", "Accès des applications à vos e-mails",
            "Lecture et envoi des e-mails des comptes configurés dans Windows.",
            ["email", "e mail", "courriel", "mail", "messagerie electronique"], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("userDataTasks", "tasks", "Accès des applications à vos tâches",
            "Lecture et modification des listes de tâches enregistrées dans Windows.",
            ["taches", "to do", "tasks", "liste de taches"], recommendDeny: true, null, "privacy-max");
        yield return Permission("chat", "messaging", "Accès des applications à la messagerie (SMS et MMS)",
            "Lecture et envoi de SMS ou de MMS depuis le PC.",
            ["sms", "mms", "messagerie", "messages", "chat"], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("radios", "radios", "Contrôle des radios par les applications",
            "Permet aux applications d'activer ou de couper le Bluetooth ou le Wi-Fi.",
            ["radio", "bluetooth", "wifi", "sans fil"], recommendDeny: false);
        yield return Permission("bluetoothSync", "other-devices", "Communication avec des appareils non appairés",
            "Permet aux applications d'échanger automatiquement des informations avec des appareils sans fil proches qui ne sont pas " +
            "appairés au PC (balises, objets connectés).",
            ["appareils non appaires", "balise", "beacon", "objet connecte", "other devices"], recommendDeny: true, null, "privacy-max");
        yield return Permission("appDiagnostics", "app-diagnostics", "Accès des applications aux diagnostics des autres applications",
            "Permet à une application de connaître les autres applications en cours d'exécution et leurs informations de diagnostic.",
            ["diagnostic application", "app diagnostics", "processus"], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("documentsLibrary", "documents", "Accès des applications au dossier Documents",
            "Concerne les applications du Microsoft Store ; les programmes de bureau classiques ne sont pas soumis à ce contrôle.",
            ["documents", "fichiers", "bibliotheque"], recommendDeny: false);
        yield return Permission("picturesLibrary", "pictures", "Accès des applications au dossier Images",
            "Concerne les applications du Microsoft Store ; les programmes de bureau classiques ne sont pas soumis à ce contrôle.",
            ["images", "photos", "pictures", "bibliotheque"], recommendDeny: false);
        yield return Permission("videosLibrary", "videos", "Accès des applications au dossier Vidéos",
            "Concerne les applications du Microsoft Store ; les programmes de bureau classiques ne sont pas soumis à ce contrôle.",
            ["videos", "films", "bibliotheque"], recommendDeny: false);
        yield return Permission("musicLibrary", "music", "Accès des applications à la bibliothèque Musique",
            "Concerne les applications du Microsoft Store ; les programmes de bureau classiques ne sont pas soumis à ce contrôle.",
            ["musique", "music", "audio", "bibliotheque"], recommendDeny: false, Requires.Windows11);
        yield return Permission("broadFileSystemAccess", "file-system", "Accès des applications à tout le système de fichiers",
            "Accès à tous vos fichiers pour les rares applications du Microsoft Store qui le demandent.",
            ["systeme fichiers", "file system", "tous les fichiers"], recommendDeny: false, null, "privacy-max");
        yield return Permission("graphicsCaptureProgrammatic", "screenshots", "Captures d'écran par les applications",
            "Permet aux applications de capturer l'écran ou d'autres fenêtres.",
            ["capture ecran", "screenshot", "enregistrement ecran"], recommendDeny: false, Requires.Windows11);
        yield return Permission("graphicsCaptureWithoutBorder", "screenshot-borders", "Captures d'écran sans bordure",
            "Permet aux applications de désactiver la bordure qui signale qu'une fenêtre est en cours de capture.",
            ["bordure capture", "capture ecran", "screenshot border"], recommendDeny: false, Requires.Windows11);
        yield return Permission("systemAIModels", "ai-models", "Accès des applications aux modèles d'IA de Windows",
            "Permet aux applications d'utiliser les modèles d'intelligence artificielle intégrés à Windows (génération de texte et d'images, sur l'appareil).",
            ["ia", "intelligence artificielle", "generation texte", "generation image", "modeles ia"], recommendDeny: false, Requires.Windows11_24H2);
        yield return Permission("activity", "motion", "Accès des applications aux données de mouvement",
            "Données d'activité issues des capteurs de mouvement (marche, course…), présentes sur certaines tablettes.",
            ["mouvement", "motion", "capteur", "activite"], recommendDeny: false, null, "privacy-max");
        yield return Permission("cellularData", "cellular", "Accès des applications aux données cellulaires",
            "Utilisation de la connexion mobile (4G/5G). Ne concerne que les PC équipés d'un modem cellulaire.",
            ["cellulaire", "4g", "5g", "donnees mobiles", "lte"], recommendDeny: false);
        yield return Permission("gazeInput", "eye-tracker", "Accès des applications au suivi oculaire",
            "Utilisation d'un dispositif de suivi du regard. Ne concerne que les PC équipés d'un tel périphérique.",
            ["suivi oculaire", "eye tracker", "regard"], recommendDeny: false);
    }

    /// <summary>
    /// Condition de disponibilité + recommandation qui n'est proposée que si la condition est remplie
    /// (une carte indisponible n'affiche pas « Recommandé pour ce PC »).
    /// </summary>
    private static TweakBuilder RequiresAndRecommends(this TweakBuilder builder, Requirement requirement, string option,
        Func<SystemProfile, bool>? onlyIf = null) =>
        builder.Requires(requirement)
               .RecommendWhen(p => requirement.Check(p) is null && (onlyIf?.Invoke(p) ?? true) ? option : null);

    /// <summary>Autorisation d'accès d'une capacité pour l'utilisateur courant (HKCU, sans élévation).</summary>
    private static TweakDefinition Permission(string capability, string idSuffix, string title, string description, string[] keywords,
        bool recommendDeny, Requirement? requirement = null, params string[] tags)
    {
        var key = ConsentStore + @"\" + capability;
        var builder = Tweak.Toggle("privacy.perm." + idSuffix, title, description)
            .In(Category, PrivacyGroups.Permissions)
            .Keywords([.. keywords, capability, "autorisation", "permission", "acces application"])
            .Labels("Autorisé", "Bloqué")
            .WhenOn(Reg.CuString(key, "Value", "Allow"))
            .WhenOff(Reg.CuString(key, "Value", "Deny"))
            .WindowsDefault(TweakDefinition.On);
        if (requirement is not null && recommendDeny) builder.RequiresAndRecommends(requirement, TweakDefinition.Off);
        else if (requirement is not null) builder.Requires(requirement);
        else if (recommendDeny) builder.Recommend(TweakDefinition.Off);
        if (tags.Length > 0) builder.Tags(tags);
        return builder.Build();
    }
}
