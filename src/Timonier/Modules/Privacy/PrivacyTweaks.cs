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

    private static readonly Requirement Build1903 = Requires.When(p => p.Build >= 18362, L("Nécessite Windows 10 version 1903 ou plus récent."));
    private static readonly Requirement Build21H2 = Requires.When(p => p.Build >= 19044, L("Nécessite Windows 10 21H2 ou plus récent."));
    private static readonly Requirement LegacyCopilot = Requires.When(p => p.Build < 26100,
        L("Sans effet depuis Windows 11 24H2 : Copilot y est une application ordinaire, à désinstaller depuis la page Applications."));

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
        var g = PrivacyGroups.Telemetry;

        yield return Tweak.Choice("privacy.telemetry.level", L("Niveau des données de diagnostic"),
                L("Quantité de données de diagnostic envoyées à Microsoft. « Au choix de l'utilisateur » laisse décider l'option « Données facultatives » des Paramètres ; « Données requises uniquement » impose le minimum accepté par les éditions Famille et Professionnel (état de l'appareil, sécurité, bon fonctionnement des mises à jour) et verrouille cette option."))
            .In(Category, g)
            .Keywords(L("telemetrie, telemetry, diagnostic, allowtelemetry, donnees requises, donnees facultatives, optional diagnostic data"))
            .Tags("privacy-max")
            .Option("user", L("Au choix de l'utilisateur (par défaut)"), Reg.LmDel(DataCollectionPolicy, "AllowTelemetry"))
            .Option("required", L("Données requises uniquement"), Reg.LmDword(DataCollectionPolicy, "AllowTelemetry", 1))
            .Option("optional", L("Données facultatives incluses"), Reg.LmDword(DataCollectionPolicy, "AllowTelemetry", 3))
            // Pas de WindowsDefault : une valeur 0 (niveau désactivé, réglage suivant) doit apparaître comme « personnalisée ».
            .RecommendWhen(p => p.SupportsTelemetryOff || p.IsManaged ? null : "required")
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.off", L("Données de diagnostic désactivées (niveau 0)"),
                L("Windows cesse d'envoyer des données de diagnostic à Microsoft (ancien niveau « Sécurité »). Accepté uniquement par les éditions Entreprise, Éducation, IoT et Server ; les autres éditions le traitent comme « Données requises ». Désactiver ce réglage supprime la stratégie : le niveau redevient celui choisi dans les Paramètres."))
            .In(Category, g)
            .Keywords(L("telemetrie, securite, niveau 0, zero, diagnostic off, allowtelemetry"))
            .Tags("privacy-max")
            .RequiresAndRecommends(Requires.EnterpriseOrEducation, TweakDefinition.On, p => !p.IsManaged)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Les services qui s'appuient sur les données de diagnostic (programme Windows Insider, rapports Windows Update for Business, Windows Autopatch) ne fonctionneront plus."))
            .WhenOn(Reg.LmDword(DataCollectionPolicy, "AllowTelemetry", 0))
            .WhenOff(Reg.LmDel(DataCollectionPolicy, "AllowTelemetry"))
            .WindowsDefault(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.diagtrack", L("Service de télémétrie (DiagTrack)"),
                L("Service « Expériences des utilisateurs connectés et télémétrie » qui collecte et envoie les données de diagnostic. Désactivé, plus rien n'est transmis par ce service, quel que soit le niveau choisi (le rapport d'erreurs a son propre réglage)."))
            .In(Category, g)
            .Keywords(L("diagtrack, utc, connected user experiences, service telemetrie, experiences utilisateurs connectes"))
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning(L("Windows peut réactiver ce service lors d'une mise à jour majeure. Le programme Windows Insider et certains outils de gestion d'entreprise en ont besoin."))
            .WhenOn(Sys.Service("DiagTrack", ServiceStartKind.Automatic))
            .WhenOff(Sys.Service("DiagTrack", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .RecommendWhen(p => p.IsManaged ? null : TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.dmwappush", L("Service de routage des messages push WAP (dmwappushservice)"),
                L("Achemine les messages de gestion à distance des appareils (MDM, Intune). Sur un PC personnel non géré, il ne démarre pratiquement jamais : le désactiver est une précaution supplémentaire plus qu'un gain réel."))
            .In(Category, g)
            .Keywords(L("dmwappushservice, wap push, mdm, intune, gestion appareil"))
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning(L("Nécessaire pour inscrire ou gérer ce PC avec un compte professionnel ou scolaire (Intune/MDM)."))
            .WhenOn(Sys.Service("dmwappushservice", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("dmwappushservice", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.tailored", L("Expériences personnalisées"),
                L("Autorise Microsoft à exploiter vos données de diagnostic pour personnaliser les conseils, publicités et recommandations affichés dans Windows. Désactivé, vous voyez toujours des suggestions, mais moins ciblées."))
            .In(Category, g)
            .Keywords(L("tailored experiences, experiences personnalisees, personnalisation, recommandations ciblees"))
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

        yield return Tweak.Toggle("privacy.telemetry.feedback", L("Demandes de commentaires"),
                L("Windows vous demande de temps en temps votre avis par une notification (« Commentaires »). Désactivé, la fréquence passe à « Jamais » et une stratégie bloque ces sollicitations."))
            .In(Category, g)
            .Keywords(L("feedback, commentaires, avis, siuf, frequence commentaires, hub commentaires"))
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

        yield return Tweak.Toggle("privacy.telemetry.limits", L("Limitation des vidages mémoire et journaux envoyés"),
                L("Si l'envoi des données facultatives est autorisé, Windows peut joindre des vidages mémoire complets (qui peuvent contenir des documents ouverts) et des journaux de diagnostic. Activé, il se limite à de petits vidages et n'envoie plus ces journaux."))
            .In(Category, g)
            .Keywords(L("dump, vidage memoire, journaux diagnostic, limitdumpcollection, limitdiagnosticlogcollection"))
            .Tags("privacy-max")
            .RequiresAndRecommends(Build1903, TweakDefinition.On)
            .WhenOn(Reg.LmDword(DataCollectionPolicy, "LimitDumpCollection", 1), Reg.LmDword(DataCollectionPolicy, "LimitDiagnosticLogCollection", 1))
            .WhenOff(Reg.LmDel(DataCollectionPolicy, "LimitDumpCollection"), Reg.LmDel(DataCollectionPolicy, "LimitDiagnosticLogCollection"))
            .WindowsDefault(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.wer", L("Rapport d'erreurs Windows"),
                L("Quand une application ou Windows plante, un rapport (parfois avec une copie de la mémoire du programme) est envoyé à Microsoft pour rechercher une solution et informer l'éditeur."))
            .In(Category, g)
            .Keywords(L("wer, error reporting, rapport erreur, plantage, crash, werfault"))
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning(L("Plus aucun rapport de plantage n'est envoyé : les éditeurs ne reçoivent plus d'informations pour corriger les bugs et Windows ne propose plus de solutions."))
            .WhenOn(Reg.LmDel(WerPolicy, "Disabled"))
            .WhenOff(Reg.LmDword(WerPolicy, "Disabled", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.ceip", L("Programme d'amélioration de l'expérience utilisateur"),
                L("Ancien programme de collecte de statistiques d'utilisation (CEIP/SQM). Largement remplacé par les données de diagnostic, il reste consulté par certains composants de Windows."))
            .In(Category, g)
            .Keywords(L("ceip, sqm, customer experience improvement program, amelioration experience"))
            .Tags("privacy-max")
            .WhenOn(Reg.LmDel(SqmPolicy, "CEIPEnable"))
            .WhenOff(Reg.LmDword(SqmPolicy, "CEIPEnable", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.appcompat", L("Collecte de compatibilité des applications"),
                L("Inventaire des programmes installés, télémétrie d'utilisation des applications et Enregistreur d'actions (psr.exe), utilisés pour évaluer la compatibilité. Désactivé, l'Enregistreur d'actions n'est plus utilisable ; le moteur de compatibilité lui-même reste actif."))
            .In(Category, g)
            .Keywords(L("appcompat, inventaire, inventory, application telemetry, enregistreur actions, steps recorder, psr"))
            .Tags("privacy-max")
            .WhenOn(Reg.LmDel(AppCompatPolicy, "DisableInventory"), Reg.LmDel(AppCompatPolicy, "AITEnable"), Reg.LmDel(AppCompatPolicy, "DisableUAR"))
            .WhenOff(Reg.LmDword(AppCompatPolicy, "DisableInventory", 1), Reg.LmDword(AppCompatPolicy, "AITEnable", 0), Reg.LmDword(AppCompatPolicy, "DisableUAR", 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        var tasks = PrivacyDetect.ExistingTasks(TelemetryTaskCandidates);
        if (tasks.Length > 0)
        {
            yield return Tweak.Toggle("privacy.telemetry.tasks", L("Tâches planifiées de télémétrie"),
                    LP(tasks.Length, "Évaluateur de compatibilité, programme d'amélioration (Consolidator, UsbCeip), diagnostics disque, recensement de l'appareil (DeviceCensus) et commentaires (DmClient). {0} tâche concernée sur ce PC ; celles absentes de votre version de Windows sont ignorées.",
                        "Évaluateur de compatibilité, programme d'amélioration (Consolidator, UsbCeip), diagnostics disque, recensement de l'appareil (DeviceCensus) et commentaires (DmClient). {0} tâches concernées sur ce PC ; celles absentes de votre version de Windows sont ignorées."))
                .In(Category, g)
                .Keywords(L("tache planifiee, scheduled task, compatibility appraiser, consolidator, devicecensus, dmclient, ceip"))
                .Tags("privacy-max")
                .Warning(L("Windows peut réactiver ces tâches lors d'une mise à jour majeure. Windows Update effectue de toute façon ses propres vérifications de compatibilité avant une mise à niveau."))
                .WhenOn([.. tasks.Select(t => (Operation)Sys.EnableTask(t))])
                .WhenOff([.. tasks.Select(t => (Operation)Sys.DisableTask(t))])
                .Detect(() => PrivacyDetect.TasksState(tasks))
                .WindowsDefault(TweakDefinition.On)
                .Recommend(TweakDefinition.Off)
                .Build();
        }

        yield return Tweak.Toggle("privacy.telemetry.devtools", L("Télémétrie de PowerShell 7 et du SDK .NET"),
                L("Ces outils de développement envoient des statistiques d'utilisation anonymes. Désactivé, les variables POWERSHELL_TELEMETRY_OPTOUT et DOTNET_CLI_TELEMETRY_OPTOUT sont définies pour tout le PC (prises en compte par les programmes lancés ensuite). Sans effet si ces outils ne sont pas installés ; Windows PowerShell 5.1 n'est pas concerné."))
            .In(Category, g)
            .Keywords(L("powershell, pwsh, dotnet, .net, sdk, developpeur, telemetry optout"))
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
        var g = PrivacyGroups.Ads;

        yield return Tweak.Toggle("privacy.ads.id", L("Identifiant de publicité"),
                L("Identifiant unique qui permet aux applications et aux régies publicitaires de vous reconnaître d'une application à l'autre pour cibler les publicités. Désactivé, les applications ne peuvent plus l'utiliser ; s'il est réactivé, un nouvel identifiant est créé."))
            .In(Category, g)
            .Keywords(L("pub, publicite, ads, advertising id, tracking, ciblage, identifiant publicitaire"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(AdvertisingInfo, "Enabled", 1), Reg.LmDel(AdvertisingPolicy, "DisabledByGroupPolicy"))
            .WhenOff(Reg.CuDword(AdvertisingInfo, "Enabled", 0), Reg.LmDword(AdvertisingPolicy, "DisabledByGroupPolicy", 1))
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Cu(AdvertisingInfo, "Enabled") == 0,
                PrivacyDetect.Lm(AdvertisingPolicy, "DisabledByGroupPolicy") == 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.start-suggestions", L("Suggestions dans le menu Démarrer"),
                L("Applications suggérées, souvent sponsorisées, affichées de temps en temps dans le menu Démarrer de Windows 10."))
            .In(Category, g)
            .Keywords(L("suggestion, menu demarrer, applications suggerees, sponsorise, start suggestions"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows10Only, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-338388Enabled", 1), Reg.CuDword(ContentDelivery, "SystemPaneSuggestionsEnabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-338388Enabled", 0), Reg.CuDword(ContentDelivery, "SystemPaneSuggestionsEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.tips", L("Astuces et suggestions de Windows"),
                L("Notifications de conseils et de suggestions pendant l'utilisation de Windows (découverte de fonctions, offres Microsoft)."))
            .In(Category, g)
            .Keywords(L("astuce, conseil, tips, suggestion, notification, soft landing"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-338389Enabled", 1), Reg.CuDword(ContentDelivery, "SoftLandingEnabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-338389Enabled", 0), Reg.CuDword(ContentDelivery, "SoftLandingEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.settings-content", L("Contenu suggéré dans les Paramètres"),
                L("Suggestions et offres (Microsoft 365, OneDrive, Game Pass…) affichées dans l'application Paramètres."))
            .In(Category, g)
            .Keywords(L("parametres, settings, contenu suggere, suggested content, offre"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-338393Enabled", 1), Reg.CuDword(ContentDelivery, "SubscribedContent-353694Enabled", 1),
                    Reg.CuDword(ContentDelivery, "SubscribedContent-353696Enabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-338393Enabled", 0), Reg.CuDword(ContentDelivery, "SubscribedContent-353694Enabled", 0),
                     Reg.CuDword(ContentDelivery, "SubscribedContent-353696Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.settings-notifications", L("Notifications dans l'application Paramètres"),
                L("Rappels liés à votre compte Microsoft (sauvegarde, abonnement, sécurité du compte) affichés dans les Paramètres."))
            .In(Category, g)
            .Keywords(L("notification, parametres, compte microsoft, account notifications"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_22H2, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(AccountNotifications, "EnableAccountNotifications", 1))
            .WhenOff(Reg.CuDword(AccountNotifications, "EnableAccountNotifications", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.lockscreen", L("Anecdotes et conseils sur l'écran de verrouillage"),
                L("Textes (anecdotes, astuces, liens vers des offres) superposés à l'image de l'écran de verrouillage."))
            .In(Category, g)
            .Keywords(L("ecran verrouillage, lock screen, anecdote, fun facts, astuce"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "RotatingLockScreenOverlayEnabled", 1), Reg.CuDword(ContentDelivery, "SubscribedContent-338387Enabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "RotatingLockScreenOverlayEnabled", 0), Reg.CuDword(ContentDelivery, "SubscribedContent-338387Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.welcome", L("Présentation des nouveautés après les mises à jour"),
                L("Écrans d'accueil qui présentent les nouveautés et des suggestions Microsoft après une mise à jour ou à la connexion."))
            .In(Category, g)
            .Keywords(L("welcome experience, accueil, nouveautes, apres mise jour"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-310093Enabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-310093Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.silent-install", L("Installation automatique d'applications suggérées"),
                L("Windows installe ou épingle de lui-même des applications promues (jeux, applications partenaires), notamment à la création d'un compte ou après une mise à jour majeure. Désactivé, ces installations silencieuses sont bloquées ; les applications déjà présentes restent installées."))
            .In(Category, g)
            .Keywords(L("bloatware, candy crush, installation silencieuse, applications promues, silent install, preinstalle"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SilentInstalledAppsEnabled", 1), Reg.CuDword(ContentDelivery, "OemPreInstalledAppsEnabled", 1),
                    Reg.CuDword(ContentDelivery, "PreInstalledAppsEnabled", 1), Reg.CuDword(ContentDelivery, "PreInstalledAppsEverEnabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SilentInstalledAppsEnabled", 0), Reg.CuDword(ContentDelivery, "OemPreInstalledAppsEnabled", 0),
                     Reg.CuDword(ContentDelivery, "PreInstalledAppsEnabled", 0), Reg.CuDword(ContentDelivery, "PreInstalledAppsEverEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.content-delivery", L("Diffusion de contenu Microsoft (interrupteur général)"),
                L("Autorise le gestionnaire de contenu de Windows (ContentDeliveryManager) à télécharger suggestions, promotions et images. Désactivé, toute cette diffusion s'arrête d'un coup, y compris les contenus utiles."))
            .In(Category, g)
            .Keywords(L("contentdeliverymanager, content delivery, diffusion contenu, promotion"))
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning(L("Les images « Windows à la une » de l'écran de verrouillage risquent de ne plus se renouveler."))
            .WhenOn(Reg.CuDword(ContentDelivery, "ContentDeliveryAllowed", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "ContentDeliveryAllowed", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.scoobe", L("Rappels « Terminer la configuration de l'appareil »"),
                L("Écran plein qui revient après certaines mises à jour pour proposer OneDrive, Microsoft 365, Lien avec Windows ou un compte Microsoft."))
            .In(Category, g)
            .Keywords(L("scoobe, terminer configuration, finish setup, tirer le meilleur parti, rappel"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ProfileEngagement, "ScoobeSystemSettingEnabled", 1))
            .WhenOff(Reg.CuDword(ProfileEngagement, "ScoobeSystemSettingEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.start-iris", L("Recommandations d'astuces et d'applications dans Démarrer"),
                L("Conseils, raccourcis et nouvelles applications (certaines promues) proposés dans la section « Recommandé » du menu Démarrer de Windows 11."))
            .In(Category, g)
            .Keywords(L("menu demarrer, recommande, iris, recommandations, start recommendations"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_22H2, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_IrisRecommendations", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_IrisRecommendations", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.start-account", L("Notifications de compte dans Démarrer"),
                L("Rappels liés au compte Microsoft (sauvegarde, stockage OneDrive, sécurité du compte) signalés sur votre photo de profil dans le menu Démarrer."))
            .In(Category, g)
            .Keywords(L("menu demarrer, compte, account notifications, badge profil"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_22H2, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_AccountNotifications", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_AccountNotifications", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.consumer-features", L("Expériences Microsoft grand public (stratégie)"),
                L("Stratégie officielle qui bloque pour tout le PC les recommandations personnalisées, les installations d'applications promues et les notifications liées au compte Microsoft. Windows ne la respecte que sur les éditions Entreprise et Éducation ; sur les autres, utilisez les réglages de cette section."))
            .In(Category, g)
            .Keywords(L("consumer features, experiences grand public, cloud content, applications promues"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.EnterpriseOrEducation, TweakDefinition.Off)
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.LmDel(CloudContentPolicy, "DisableWindowsConsumerFeatures"))
            .WhenOff(Reg.LmDword(CloudContentPolicy, "DisableWindowsConsumerFeatures", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.spotlight", L("Fonctionnalités « Windows à la une » (stratégie)"),
                L("Désactive en bloc Windows à la une (images et suggestions de l'écran de verrouillage, conseils, contenus promus) ainsi que les suggestions d'éditeurs tiers. Respecté uniquement par les éditions Entreprise et Éducation."))
            .In(Category, g)
            .Keywords(L("spotlight, windows a la une, ecran verrouillage, third party suggestions"))
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
        var g = PrivacyGroups.Search;

        yield return Tweak.Toggle("privacy.search.web", L("Résultats web (Bing) dans la recherche"),
                L("Ce que vous tapez dans la recherche du menu Démarrer ou de la barre des tâches est aussi envoyé à Bing pour afficher des résultats web. Désactivé, la recherche reste locale (applications, fichiers, paramètres). Effet secondaire : l'Explorateur n'affiche plus vos recherches récentes."))
            .In(Category, g)
            .Keywords(L("bing, recherche web, web search, resultats internet, disablesearchboxsuggestions"))
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

        yield return Tweak.Toggle("privacy.search.cloud", L("Recherche dans vos contenus cloud"),
                L("Inclut dans la recherche Windows des résultats issus de OneDrive, Outlook et des autres services liés à votre compte Microsoft ou professionnel : vos recherches sont alors aussi transmises à ces services."))
            .In(Category, g)
            .Keywords(L("cloud search, recherche cloud, onedrive, outlook, compte microsoft, compte professionnel"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(SearchSettings, "IsMSACloudSearchEnabled", 1), Reg.CuDword(SearchSettings, "IsAADCloudSearchEnabled", 1))
            .WhenOff(Reg.CuDword(SearchSettings, "IsMSACloudSearchEnabled", 0), Reg.CuDword(SearchSettings, "IsAADCloudSearchEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.search.history", L("Historique des recherches sur cet appareil"),
                L("Mémorise vos recherches pour vous les proposer à nouveau. Ces données restent sur le PC."))
            .In(Category, g)
            .Keywords(L("historique recherche, search history, recherches recentes"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(SearchSettings, "IsDeviceSearchHistoryEnabled", 1))
            .WhenOff(Reg.CuDword(SearchSettings, "IsDeviceSearchHistoryEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.search.highlights", L("Points forts de la recherche"),
                L("Illustrations, événements du jour et contenus tendance fournis par Bing dans la zone et le panneau de recherche."))
            .In(Category, g)
            .Keywords(L("search highlights, points forts, tendances, bing, illustration recherche"))
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

        yield return Tweak.Choice("privacy.search.safesearch", L("Recherche sécurisée (SafeSearch)"),
                L("Filtrage des contenus pour adultes dans les résultats web de la recherche Windows. Sans objet si les résultats web sont désactivés."))
            .In(Category, g)
            .Keywords(L("safesearch, recherche securisee, contenu adulte, filtrage, controle parental"))
            .Tags("family")
            .Option("strict", L("Stricte"), Reg.CuDword(SearchSettings, "SafeSearchMode", 2))
            .Option("moderate", L("Modérée (par défaut)"), Reg.CuDword(SearchSettings, "SafeSearchMode", 1))
            .Option("off", L("Désactivée"), Reg.CuDword(SearchSettings, "SafeSearchMode", 0))
            .WindowsDefault("moderate")
            .Build();

        yield return Tweak.Toggle("privacy.ai.copilot", L("Copilot intégré (ancienne version)"),
                L("Stratégie « Désactiver Windows Copilot » : masque et bloque le volet Copilot intégré à Windows 11 23H2 et à Windows 10 (bouton de la barre des tâches compris)."))
            .In(Category, g)
            .Keywords(L("copilot, ia, ai, assistant, turnoffwindowscopilot"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(LegacyCopilot, TweakDefinition.Off)
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDel(CopilotUserPolicy, "TurnOffWindowsCopilot"))
            .WhenOff(Reg.CuDword(CopilotUserPolicy, "TurnOffWindowsCopilot", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ai.recall", L("Captures d'écran de Recall (Retrouver)"),
                L("Sur les PC Copilot+, Recall enregistre régulièrement des captures de votre écran pour vous permettre de retrouver ce que vous avez vu. La stratégie interdit l'enregistrement de ces captures ; elle est sans effet sur les autres PC."))
            .In(Category, g)
            .Keywords(L("recall, retrouver, capture ecran, instantane, snapshot, copilot+, disableaidataanalysis"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_24H2, TweakDefinition.Off)
            .Warning(L("Les captures déjà enregistrées par Recall sont supprimées quand l'enregistrement est désactivé."))
            .WhenOn(Reg.LmDel(WindowsAiPolicy, "DisableAIDataAnalysis"), Reg.CuDel(WindowsAiUserPolicy, "DisableAIDataAnalysis"))
            .WhenOff(Reg.LmDword(WindowsAiPolicy, "DisableAIDataAnalysis", 1), Reg.CuDword(WindowsAiUserPolicy, "DisableAIDataAnalysis", 1))
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Lm(WindowsAiPolicy, "DisableAIDataAnalysis") == 1,
                PrivacyDetect.Cu(WindowsAiUserPolicy, "DisableAIDataAnalysis") == 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ai.recall-component", L("Composant Recall"),
                L("Stratégie « Autoriser l'activation de Recall ». « Retiré » : le composant facultatif Recall est désactivé et supprimé de Windows après redémarrage. « Disponible » (par défaut) : il reste désactivé tant que vous ne l'activez pas vous-même."))
            .In(Category, g)
            .Keywords(L("recall, retrouver, composant facultatif, allowrecallenablement, copilot+"))
            .Labels(L("Disponible"), L("Retiré"))
            .Requires(Requires.Windows11_24H2)
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning(L("Les captures Recall existantes sont supprimées. Pour le récupérer, remettez ce réglage sur « Disponible » puis redémarrez le PC."))
            .WhenOn(Reg.LmDel(WindowsAiPolicy, "AllowRecallEnablement"))
            .WhenOff(Reg.LmDword(WindowsAiPolicy, "AllowRecallEnablement", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ai.clicktodo", L("Click to Do (Actions par clic)"),
                L("Analyse une capture de l'écran, sur l'appareil, pour proposer des actions sur le texte ou les images affichés. Présent surtout sur les PC Copilot+."))
            .In(Category, g)
            .Keywords(L("click to do, actions par clic, ia, capture, disableclicktodo"))
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
                L("Assistant vocal de Windows 10. La stratégie le désactive pour tous les utilisateurs du PC."))
            .In(Category, g)
            .Keywords(L("cortana, assistant vocal, allowcortana"))
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
        var g = PrivacyGroups.Activity;

        yield return Tweak.Toggle("privacy.activity.history", L("Historique des activités"),
                L("Windows enregistre les applications, fichiers et pages que vous ouvrez (reprise d'activité) et peut les envoyer à votre compte Microsoft. La stratégie coupe l'enregistrement et l'envoi pour tous les comptes du PC."))
            .In(Category, g)
            .Keywords(L("historique activite, activity history, timeline, chronologie, publishuseractivities"))
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

        yield return Tweak.Toggle("privacy.activity.clipboard-sync", L("Synchronisation du presse-papiers entre appareils"),
                L("Option « Partager entre vos appareils » : le contenu copié transite par le cloud Microsoft vers vos autres appareils. Bloqué, l'option n'est plus proposée."))
            .In(Category, g)
            .Keywords(L("presse papier, clipboard, synchronisation, cloud clipboard, cross device"))
            .Tags("privacy-max", "family")
            .Labels(L("Autorisé"), L("Bloqué"))
            .WhenOn(Reg.LmDel(SystemPolicy, "AllowCrossDeviceClipboard"))
            .WhenOff(Reg.LmDword(SystemPolicy, "AllowCrossDeviceClipboard", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.activity.clipboard-history", L("Historique du presse-papiers"),
                L("Conserve les derniers éléments copiés (Windows + V), y compris d'éventuels mots de passe. L'historique reste sur le PC et se vide au redémarrage, sauf les éléments épinglés."))
            .In(Category, g)
            .Keywords(L("presse papier, clipboard history, windows v, historique copier"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ClipboardKey, "EnableClipboardHistory", 1))
            .WhenOff(Reg.CuDword(ClipboardKey, "EnableClipboardHistory", 0))
            .WindowsDefault(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.activity.track-progs", L("Suivi du lancement des applications"),
                L("Windows mémorise les applications que vous lancez pour améliorer le menu Démarrer et la recherche (liste « Les plus utilisées »). Ces données restent sur le PC."))
            .In(Category, g)
            .Keywords(L("start_trackprogs, applications plus utilisees, suivi applications, track app launches"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_TrackProgs", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_TrackProgs", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.track-docs", L("Éléments récents dans Démarrer et les listes de raccourcis"),
                L("Affiche les fichiers ouverts récemment dans le menu Démarrer, les listes de raccourcis (clic droit sur une icône de la barre des tâches) et l'Explorateur."))
            .In(Category, g)
            .Keywords(L("start_trackdocs, elements recents, jump list, liste raccourcis, fichiers recents"))
            .Tags("privacy-max")
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_TrackDocs", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_TrackDocs", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.explorer-recent", L("Fichiers récents dans l'Explorateur"),
                L("Section « Récent » de l'Accueil de l'Explorateur (Accès rapide sous Windows 10). Pris en compte dans les nouvelles fenêtres de l'Explorateur."))
            .In(Category, g)
            .Keywords(L("fichiers recents, showrecent, acces rapide, quick access, accueil explorateur"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ExplorerKey, "ShowRecent", 1))
            .WhenOff(Reg.CuDword(ExplorerKey, "ShowRecent", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.explorer-frequent", L("Dossiers fréquents dans l'Explorateur"),
                L("Dossiers que vous ouvrez souvent, ajoutés automatiquement à l'Accès rapide. Pris en compte dans les nouvelles fenêtres."))
            .In(Category, g)
            .Keywords(L("dossiers frequents, showfrequent, acces rapide, quick access"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ExplorerKey, "ShowFrequent", 1))
            .WhenOff(Reg.CuDword(ExplorerKey, "ShowFrequent", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.explorer-cloud", L("Fichiers d'Office.com dans l'Explorateur"),
                L("Affiche dans l'Accueil de l'Explorateur les documents récents de votre compte Microsoft 365 et OneDrive, récupérés en ligne."))
            .In(Category, g)
            .Keywords(L("office.com, microsoft 365, onedrive, fichiers cloud, showcloudfilesinquickaccess"))
            .Tags("privacy-max")
            .Requires(Requires.Windows11_22H2)
            .WhenOn(Reg.CuDword(ExplorerKey, "ShowCloudFilesInQuickAccess", 1))
            .WhenOff(Reg.CuDword(ExplorerKey, "ShowCloudFilesInQuickAccess", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.online-tips", L("Conseils en ligne dans les Paramètres"),
                L("Autorise l'application Paramètres à télécharger des conseils et des contenus d'aide depuis les serveurs de Microsoft."))
            .In(Category, g)
            .Keywords(L("conseils en ligne, online tips, aide en ligne, allowonlinetips"))
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
        var g = PrivacyGroups.Input;

        yield return Tweak.Toggle("privacy.input.personalization", L("Dictionnaire personnel de saisie et d'écriture manuscrite"),
                L("Windows apprend de ce que vous tapez et écrivez à la main (ainsi que de vos contacts) pour améliorer les suggestions et la reconnaissance. Désactivé, Windows cesse cet apprentissage."))
            .In(Category, g)
            .Keywords(L("saisie, clavier, ecriture manuscrite, inking typing, dictionnaire personnel, input personalization"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(PersonalizationSettings, "AcceptedPrivacyPolicy", 1), Reg.CuDword(InputPersonalization, "RestrictImplicitInkCollection", 0),
                    Reg.CuDword(InputPersonalization, "RestrictImplicitTextCollection", 0), Reg.CuDword(TrainedDataStore, "HarvestContacts", 1))
            .WhenOff(Reg.CuDword(PersonalizationSettings, "AcceptedPrivacyPolicy", 0), Reg.CuDword(InputPersonalization, "RestrictImplicitInkCollection", 1),
                     Reg.CuDword(InputPersonalization, "RestrictImplicitTextCollection", 1), Reg.CuDword(TrainedDataStore, "HarvestContacts", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.tipc", L("Amélioration de la saisie et de l'écriture manuscrite"),
                L("Envoie à Microsoft des données de saisie et d'écriture manuscrite pour améliorer la reconnaissance et les suggestions. Ne s'applique que si l'envoi des données de diagnostic facultatives est activé."))
            .In(Category, g)
            .Keywords(L("tipc, improve inking typing, saisie, ecriture manuscrite, donnees facultatives"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(InputTipc, "Enabled", 1))
            .WhenOff(Reg.CuDword(InputTipc, "Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.speech", L("Reconnaissance vocale en ligne"),
                L("Utilise les serveurs de Microsoft pour la dictée et les commandes vocales : plus précis, mais votre voix est envoyée en ligne. Désactivé, les fonctions vocales locales (Accès vocal, reconnaissance vocale Windows) restent utilisables."))
            .In(Category, g)
            .Keywords(L("voix, vocal, speech, dictee, reconnaissance vocale, online speech"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(OnlineSpeech, "HasAccepted", 1))
            .WhenOff(Reg.CuDword(OnlineSpeech, "HasAccepted", 0))
            .WindowsDefault(TweakDefinition.Off)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.voice-activation", L("Activation vocale des applications"),
                L("Autorise les assistants vocaux à écouter en permanence leur mot-clé pour se déclencher, y compris sur l'écran de verrouillage. La stratégie s'applique à tous les comptes du PC."))
            .In(Category, g)
            .Keywords(L("activation vocale, voice activation, mot cle, assistant vocal, ecoute"))
            .Tags("privacy-max")
            .Labels(L("Autorisé"), L("Bloqué"))
            .WhenOn(Reg.LmDel(AppPrivacyPolicy, "LetAppsActivateWithVoice"), Reg.LmDel(AppPrivacyPolicy, "LetAppsActivateWithVoiceAboveLock"))
            .WhenOff(Reg.LmDword(AppPrivacyPolicy, "LetAppsActivateWithVoice", 2), Reg.LmDword(AppPrivacyPolicy, "LetAppsActivateWithVoiceAboveLock", 2))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.input.language-list", L("Accès des sites web à votre liste de langues"),
                L("Les sites web peuvent lire la liste des langues configurées dans Windows pour afficher un contenu adapté à votre région."))
            .In(Category, g)
            .Keywords(L("langue, language list, httpacceptlanguageoptout, sites web, contenu local"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDel(InternationalProfile, "HttpAcceptLanguageOptOut"))
            .WhenOff(Reg.CuDword(InternationalProfile, "HttpAcceptLanguageOptOut", 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.handwriting", L("Partage des données d'écriture manuscrite"),
                L("Envoi à Microsoft d'échantillons d'écriture manuscrite et de rapports d'erreurs de reconnaissance pour améliorer le service. Concerne surtout les PC tactiles avec stylet."))
            .In(Category, g)
            .Keywords(L("ecriture manuscrite, handwriting, stylet, pen, reconnaissance ecriture"))
            .Tags("privacy-max")
            .Labels(L("Autorisé"), L("Bloqué"))
            .WhenOn(Reg.LmDel(TabletPcPolicy, "PreventHandwritingDataSharing"), Reg.LmDel(HandwritingErrorsPolicy, "PreventHandwritingErrorReports"))
            .WhenOff(Reg.LmDword(TabletPcPolicy, "PreventHandwritingDataSharing", 1), Reg.LmDword(HandwritingErrorsPolicy, "PreventHandwritingErrorReports", 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();
    }

    // ================================================================== Localisation

    private static IEnumerable<TweakDefinition> Location()
    {
        var g = PrivacyGroups.Location;

        yield return Tweak.Toggle("privacy.location.search", L("Utilisation de la position par la recherche"),
                L("Autorise la recherche Windows à utiliser la position de l'appareil pour proposer des résultats locaux. La stratégie s'applique à tous les comptes du PC."))
            .In(Category, g)
            .Keywords(L("localisation, position, recherche, allowsearchtouselocation, resultats locaux"))
            .Tags("privacy-max")
            .Labels(L("Autorisé"), L("Bloqué"))
            .WhenOn(Reg.LmDel(WindowsSearchPolicy, "AllowSearchToUseLocation"))
            .WhenOff(Reg.LmDword(WindowsSearchPolicy, "AllowSearchToUseLocation", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.location.findmydevice", L("Localiser mon appareil"),
                L("Permet de retrouver ce PC sur une carte depuis account.microsoft.com en cas de perte ou de vol. Une fois la fonction activée par l'utilisateur, la position du PC est envoyée régulièrement à Microsoft. La stratégie l'interdit pour tout le PC."))
            .In(Category, g)
            .Keywords(L("localiser appareil, find my device, vol, perte, position, antivol"))
            .Labels(L("Autorisé"), L("Bloqué"))
            .Risk(RiskLevel.Moderate)
            .Warning(L("Vous ne pourrez plus localiser ni verrouiller ce PC à distance s'il est perdu ou volé."))
            .WhenOn(Reg.LmDel(FindMyDevicePolicy, "AllowFindMyDevice"))
            .WhenOff(Reg.LmDword(FindMyDevicePolicy, "AllowFindMyDevice", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();
    }

    // ================================================================== Autorisations des applications (compte courant)

    private static IEnumerable<TweakDefinition> Permissions()
    {
        yield return Permission("webcam", "webcam", L("Accès des applications à la caméra"),
            L("Accès à la caméra pour votre compte. Sans effet si la caméra est bloquée pour tout le PC (page Périphériques)."),
            [L("camera, webcam, video")], recommendDeny: false, Requires.Camera);
        yield return Permission("microphone", "microphone", L("Accès des applications au microphone"),
            L("Accès au micro pour votre compte. Sans effet si le micro est bloqué pour tout le PC (page Périphériques)."),
            [L("micro, microphone, audio, enregistrement")], recommendDeny: false);
        yield return Permission("location", "location", L("Accès des applications à votre position"),
            L("Accès à la position de l'appareil pour votre compte. Sans effet si la localisation est désactivée pour tout le PC (page Périphériques)."),
            [L("localisation, position, gps, location")], recommendDeny: false);
        yield return Permission("userNotificationListener", "notifications", L("Accès des applications à vos notifications"),
            L("Permet à des applications de lire toutes les notifications que vous recevez (utile pour certaines montres connectées)."),
            [L("notification, notification listener")], recommendDeny: false);
        yield return Permission("userAccountInformation", "account-info", L("Accès des applications aux informations de votre compte"),
            L("Nom, photo et adresse de votre compte Windows, lisibles par les applications qui le demandent."),
            [L("compte, account info, nom utilisateur, photo profil")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("contacts", "contacts", L("Accès des applications à vos contacts"),
            L("Lecture des contacts enregistrés dans Windows (application Contacts, comptes de messagerie)."),
            [L("contacts, carnet adresses, people")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("appointments", "calendar", L("Accès des applications à votre calendrier"),
            L("Lecture et modification des rendez-vous enregistrés dans Windows."),
            [L("calendrier, agenda, rendez vous, calendar, appointments")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("phoneCall", "phone-calls", L("Accès des applications aux appels téléphoniques"),
            L("Passer des appels depuis le PC (par exemple via Lien avec Windows et un téléphone associé)."),
            [L("appel, telephone, phone call, lien avec windows")], recommendDeny: false);
        yield return Permission("phoneCallHistory", "call-history", L("Accès des applications à l'historique des appels"),
            L("Lecture de l'historique des appels synchronisé sur le PC."),
            [L("historique appels, call history, telephone")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("email", "email", L("Accès des applications à vos e-mails"),
            L("Lecture et envoi des e-mails des comptes configurés dans Windows."),
            [L("email, e mail, courriel, mail, messagerie electronique")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("userDataTasks", "tasks", L("Accès des applications à vos tâches"),
            L("Lecture et modification des listes de tâches enregistrées dans Windows."),
            [L("taches, to do, tasks, liste de taches")], recommendDeny: true, null, "privacy-max");
        yield return Permission("chat", "messaging", L("Accès des applications à la messagerie (SMS et MMS)"),
            L("Lecture et envoi de SMS ou de MMS depuis le PC."),
            [L("sms, mms, messagerie, messages, chat")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("radios", "radios", L("Contrôle des radios par les applications"),
            L("Permet aux applications d'activer ou de couper le Bluetooth ou le Wi-Fi."),
            [L("radio, bluetooth, wifi, sans fil")], recommendDeny: false);
        yield return Permission("bluetoothSync", "other-devices", L("Communication avec des appareils non appairés"),
            L("Permet aux applications d'échanger automatiquement des informations avec des appareils sans fil proches qui ne sont pas appairés au PC (balises, objets connectés)."),
            [L("appareils non appaires, balise, beacon, objet connecte, other devices")], recommendDeny: true, null, "privacy-max");
        yield return Permission("appDiagnostics", "app-diagnostics", L("Accès des applications aux diagnostics des autres applications"),
            L("Permet à une application de connaître les autres applications en cours d'exécution et leurs informations de diagnostic."),
            [L("diagnostic application, app diagnostics, processus")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("documentsLibrary", "documents", L("Accès des applications au dossier Documents"),
            L("Concerne les applications du Microsoft Store ; les programmes de bureau classiques ne sont pas soumis à ce contrôle."),
            [L("documents, fichiers, bibliotheque")], recommendDeny: false);
        yield return Permission("picturesLibrary", "pictures", L("Accès des applications au dossier Images"),
            L("Concerne les applications du Microsoft Store ; les programmes de bureau classiques ne sont pas soumis à ce contrôle."),
            [L("images, photos, pictures, bibliotheque")], recommendDeny: false);
        yield return Permission("videosLibrary", "videos", L("Accès des applications au dossier Vidéos"),
            L("Concerne les applications du Microsoft Store ; les programmes de bureau classiques ne sont pas soumis à ce contrôle."),
            [L("videos, films, bibliotheque")], recommendDeny: false);
        yield return Permission("musicLibrary", "music", L("Accès des applications à la bibliothèque Musique"),
            L("Concerne les applications du Microsoft Store ; les programmes de bureau classiques ne sont pas soumis à ce contrôle."),
            [L("musique, music, audio, bibliotheque")], recommendDeny: false, Requires.Windows11);
        yield return Permission("broadFileSystemAccess", "file-system", L("Accès des applications à tout le système de fichiers"),
            L("Accès à tous vos fichiers pour les rares applications du Microsoft Store qui le demandent."),
            [L("systeme fichiers, file system, tous les fichiers")], recommendDeny: false, null, "privacy-max");
        yield return Permission("graphicsCaptureProgrammatic", "screenshots", L("Captures d'écran par les applications"),
            L("Permet aux applications de capturer l'écran ou d'autres fenêtres."),
            [L("capture ecran, screenshot, enregistrement ecran")], recommendDeny: false, Requires.Windows11);
        yield return Permission("graphicsCaptureWithoutBorder", "screenshot-borders", L("Captures d'écran sans bordure"),
            L("Permet aux applications de désactiver la bordure qui signale qu'une fenêtre est en cours de capture."),
            [L("bordure capture, capture ecran, screenshot border")], recommendDeny: false, Requires.Windows11);
        yield return Permission("systemAIModels", "ai-models", L("Accès des applications aux modèles d'IA de Windows"),
            L("Permet aux applications d'utiliser les modèles d'intelligence artificielle intégrés à Windows (génération de texte et d'images, sur l'appareil)."),
            [L("ia, intelligence artificielle, generation texte, generation image, modeles ia")], recommendDeny: false, Requires.Windows11_24H2);
        yield return Permission("activity", "motion", L("Accès des applications aux données de mouvement"),
            L("Données d'activité issues des capteurs de mouvement (marche, course…), présentes sur certaines tablettes."),
            [L("mouvement, motion, capteur, activite")], recommendDeny: false, null, "privacy-max");
        yield return Permission("cellularData", "cellular", L("Accès des applications aux données cellulaires"),
            L("Utilisation de la connexion mobile (4G/5G). Ne concerne que les PC équipés d'un modem cellulaire."),
            [L("cellulaire, 4g, 5g, donnees mobiles, lte")], recommendDeny: false);
        yield return Permission("gazeInput", "eye-tracker", L("Accès des applications au suivi oculaire"),
            L("Utilisation d'un dispositif de suivi du regard. Ne concerne que les PC équipés d'un tel périphérique."),
            [L("suivi oculaire, eye tracker, regard")], recommendDeny: false);
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
            .Keywords([.. keywords, capability, L("autorisation, permission, acces application")])
            .Labels(L("Autorisé"), L("Bloqué"))
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
