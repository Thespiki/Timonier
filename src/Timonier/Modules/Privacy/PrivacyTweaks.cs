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

    private static readonly Requirement Build1903 = Requires.When(p => p.Build >= 18362, L("Requires Windows 10 version 1903 or later."));
    private static readonly Requirement Build21H2 = Requires.When(p => p.Build >= 19044, L("Requires Windows 10 21H2 or later."));
    private static readonly Requirement LegacyCopilot = Requires.When(p => p.Build < 26100,
        L("No effect since Windows 11 24H2: Copilot is a regular app there, which you can uninstall from the Apps page."));

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

        yield return Tweak.Choice("privacy.telemetry.level", L("Diagnostic data level"),
                L("Amount of diagnostic data sent to Microsoft. “User's choice” lets the “Optional diagnostic data” option in Settings decide; “Required data only” enforces the minimum accepted by the Home and Pro editions (device state, security, updates working properly) and locks that option."))
            .In(Category, g)
            .Keywords(L("telemetry, diagnostic data, allowtelemetry, required data, optional data, optional diagnostic data"))
            .Tags("privacy-max")
            .Option("user", L("User's choice (default)"), Reg.LmDel(DataCollectionPolicy, "AllowTelemetry"))
            .Option("required", L("Required data only"), Reg.LmDword(DataCollectionPolicy, "AllowTelemetry", 1))
            .Option("optional", L("Optional data included"), Reg.LmDword(DataCollectionPolicy, "AllowTelemetry", 3))
            // Pas de WindowsDefault : une valeur 0 (niveau désactivé, réglage suivant) doit apparaître comme « personnalisée ».
            .RecommendWhen(p => p.SupportsTelemetryOff || p.IsManaged ? null : "required")
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.off", L("Diagnostic data off (level 0)"),
                L("Windows stops sending diagnostic data to Microsoft (formerly the “Security” level). Accepted only by the Enterprise, Education, IoT and Server editions; other editions treat it as “Required data”. Turning this setting off removes the policy: the level goes back to the one chosen in Settings."))
            .In(Category, g)
            .Keywords(L("telemetry, security, level 0, zero, diagnostic off, allowtelemetry"))
            .Tags("privacy-max")
            .RequiresAndRecommends(Requires.EnterpriseOrEducation, TweakDefinition.On, p => !p.IsManaged)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Services that rely on diagnostic data (Windows Insider Program, Windows Update for Business reports, Windows Autopatch) will stop working."))
            .WhenOn(Reg.LmDword(DataCollectionPolicy, "AllowTelemetry", 0))
            .WhenOff(Reg.LmDel(DataCollectionPolicy, "AllowTelemetry"))
            .WindowsDefault(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.diagtrack", L("Telemetry service (DiagTrack)"),
                L("The “Connected User Experiences and Telemetry” service, which collects and sends diagnostic data. When off, this service no longer transmits anything, whatever level is chosen (error reporting has its own setting)."))
            .In(Category, g)
            .Keywords(L("diagtrack, utc, connected user experiences, telemetry service"))
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning(L("Windows may turn this service back on during a major update. The Windows Insider Program and some enterprise management tools need it."))
            .WhenOn(Sys.Service("DiagTrack", ServiceStartKind.Automatic))
            .WhenOff(Sys.Service("DiagTrack", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .RecommendWhen(p => p.IsManaged ? null : TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.dmwappush", L("WAP Push Message Routing Service (dmwappushservice)"),
                L("Routes remote device management messages (MDM, Intune). On an unmanaged personal PC, it almost never starts: turning it off is more an extra precaution than a real gain."))
            .In(Category, g)
            .Keywords(L("dmwappushservice, wap push, mdm, intune, device management"))
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning(L("Required to enroll or manage this PC with a work or school account (Intune/MDM)."))
            .WhenOn(Sys.Service("dmwappushservice", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("dmwappushservice", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.tailored", L("Tailored experiences"),
                L("Allows Microsoft to use your diagnostic data to personalize the tips, ads and recommendations shown in Windows. When off, you still see suggestions, but less targeted ones."))
            .In(Category, g)
            .Keywords(L("tailored experiences, personalization, targeted recommendations"))
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

        yield return Tweak.Toggle("privacy.telemetry.feedback", L("Feedback requests"),
                L("Windows occasionally asks for your opinion in a notification (“Feedback”). When off, the frequency is set to “Never” and a policy blocks these requests."))
            .In(Category, g)
            .Keywords(L("feedback, opinion, siuf, feedback frequency, feedback hub"))
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

        yield return Tweak.Toggle("privacy.telemetry.limits", L("Limit memory dumps and logs sent"),
                L("If sending optional data is allowed, Windows can attach full memory dumps (which may contain open documents) and diagnostic logs. When on, it's limited to small dumps and no longer sends these logs."))
            .In(Category, g)
            .Keywords(L("dump, memory dump, diagnostic logs, limitdumpcollection, limitdiagnosticlogcollection"))
            .Tags("privacy-max")
            .RequiresAndRecommends(Build1903, TweakDefinition.On)
            .WhenOn(Reg.LmDword(DataCollectionPolicy, "LimitDumpCollection", 1), Reg.LmDword(DataCollectionPolicy, "LimitDiagnosticLogCollection", 1))
            .WhenOff(Reg.LmDel(DataCollectionPolicy, "LimitDumpCollection"), Reg.LmDel(DataCollectionPolicy, "LimitDiagnosticLogCollection"))
            .WindowsDefault(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.wer", L("Windows Error Reporting"),
                L("When an app or Windows crashes, a report (sometimes with a copy of the program's memory) is sent to Microsoft to look for a solution and inform the publisher."))
            .In(Category, g)
            .Keywords(L("wer, error reporting, error report, crash, werfault"))
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning(L("No more crash reports are sent: publishers no longer receive information to fix bugs and Windows no longer offers solutions."))
            .WhenOn(Reg.LmDel(WerPolicy, "Disabled"))
            .WhenOff(Reg.LmDword(WerPolicy, "Disabled", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.ceip", L("Customer Experience Improvement Program"),
                L("Legacy program that collects usage statistics (CEIP/SQM). Largely replaced by diagnostic data, it's still checked by some Windows components."))
            .In(Category, g)
            .Keywords(L("ceip, sqm, customer experience improvement program, experience improvement"))
            .Tags("privacy-max")
            .WhenOn(Reg.LmDel(SqmPolicy, "CEIPEnable"))
            .WhenOff(Reg.LmDword(SqmPolicy, "CEIPEnable", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.telemetry.appcompat", L("Application compatibility data collection"),
                L("Inventory of installed programs, application telemetry and Steps Recorder (psr.exe), used to assess compatibility. When off, Steps Recorder can no longer be used; the compatibility engine itself stays active."))
            .In(Category, g)
            .Keywords(L("appcompat, inventory, application telemetry, steps recorder, psr"))
            .Tags("privacy-max")
            .WhenOn(Reg.LmDel(AppCompatPolicy, "DisableInventory"), Reg.LmDel(AppCompatPolicy, "AITEnable"), Reg.LmDel(AppCompatPolicy, "DisableUAR"))
            .WhenOff(Reg.LmDword(AppCompatPolicy, "DisableInventory", 1), Reg.LmDword(AppCompatPolicy, "AITEnable", 0), Reg.LmDword(AppCompatPolicy, "DisableUAR", 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        var tasks = PrivacyDetect.ExistingTasks(TelemetryTaskCandidates);
        if (tasks.Length > 0)
        {
            yield return Tweak.Toggle("privacy.telemetry.tasks", L("Telemetry scheduled tasks"),
                    LP(tasks.Length, "Compatibility Appraiser, improvement program (Consolidator, UsbCeip), disk diagnostics, device census (DeviceCensus) and feedback (DmClient). {0} task affected on this PC; those missing from your Windows version are skipped.",
                        "Compatibility Appraiser, improvement program (Consolidator, UsbCeip), disk diagnostics, device census (DeviceCensus) and feedback (DmClient). {0} tasks affected on this PC; those missing from your Windows version are skipped."))
                .In(Category, g)
                .Keywords(L("scheduled task, compatibility appraiser, consolidator, devicecensus, dmclient, ceip"))
                .Tags("privacy-max")
                .Warning(L("Windows may turn these tasks back on during a major update. Windows Update runs its own compatibility checks before an upgrade anyway."))
                .WhenOn([.. tasks.Select(t => (Operation)Sys.EnableTask(t))])
                .WhenOff([.. tasks.Select(t => (Operation)Sys.DisableTask(t))])
                .Detect(() => PrivacyDetect.TasksState(tasks))
                .WindowsDefault(TweakDefinition.On)
                .Recommend(TweakDefinition.Off)
                .Build();
        }

        yield return Tweak.Toggle("privacy.telemetry.devtools", L("PowerShell 7 and .NET SDK telemetry"),
                L("These development tools send anonymous usage statistics. When off, the POWERSHELL_TELEMETRY_OPTOUT and DOTNET_CLI_TELEMETRY_OPTOUT variables are set for the whole PC (picked up by programs started afterward). No effect if these tools aren't installed; Windows PowerShell 5.1 isn't affected."))
            .In(Category, g)
            .Keywords(L("powershell, pwsh, dotnet, .net, sdk, developer, telemetry optout"))
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

        yield return Tweak.Toggle("privacy.ads.id", L("Advertising ID"),
                L("A unique ID that lets apps and ad networks recognize you from one app to another to target ads. When off, apps can no longer use it; if it's turned back on, a new ID is created."))
            .In(Category, g)
            .Keywords(L("ads, advertising, advertising id, tracking, targeting, ad id"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(AdvertisingInfo, "Enabled", 1), Reg.LmDel(AdvertisingPolicy, "DisabledByGroupPolicy"))
            .WhenOff(Reg.CuDword(AdvertisingInfo, "Enabled", 0), Reg.LmDword(AdvertisingPolicy, "DisabledByGroupPolicy", 1))
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Cu(AdvertisingInfo, "Enabled") == 0,
                PrivacyDetect.Lm(AdvertisingPolicy, "DisabledByGroupPolicy") == 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.start-suggestions", L("Suggestions in the Start menu"),
                L("Suggested apps, often sponsored, shown from time to time in the Windows 10 Start menu."))
            .In(Category, g)
            .Keywords(L("suggestion, start menu, suggested apps, sponsored, start suggestions"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows10Only, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-338388Enabled", 1), Reg.CuDword(ContentDelivery, "SystemPaneSuggestionsEnabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-338388Enabled", 0), Reg.CuDword(ContentDelivery, "SystemPaneSuggestionsEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.tips", L("Windows tips and suggestions"),
                L("Notifications with tips and suggestions while you use Windows (feature discovery, Microsoft offers)."))
            .In(Category, g)
            .Keywords(L("tip, advice, tips, suggestion, notification, soft landing"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-338389Enabled", 1), Reg.CuDword(ContentDelivery, "SoftLandingEnabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-338389Enabled", 0), Reg.CuDword(ContentDelivery, "SoftLandingEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.settings-content", L("Suggested content in Settings"),
                L("Suggestions and offers (Microsoft 365, OneDrive, Game Pass…) shown in the Settings app."))
            .In(Category, g)
            .Keywords(L("settings, suggested content, offer, ads"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-338393Enabled", 1), Reg.CuDword(ContentDelivery, "SubscribedContent-353694Enabled", 1),
                    Reg.CuDword(ContentDelivery, "SubscribedContent-353696Enabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-338393Enabled", 0), Reg.CuDword(ContentDelivery, "SubscribedContent-353694Enabled", 0),
                     Reg.CuDword(ContentDelivery, "SubscribedContent-353696Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.settings-notifications", L("Notifications in the Settings app"),
                L("Reminders about your Microsoft account (backup, subscription, account security) shown in Settings."))
            .In(Category, g)
            .Keywords(L("notification, settings, microsoft account, account notifications"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_22H2, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(AccountNotifications, "EnableAccountNotifications", 1))
            .WhenOff(Reg.CuDword(AccountNotifications, "EnableAccountNotifications", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.lockscreen", L("Fun facts and tips on the lock screen"),
                L("Text (fun facts, tips, links to offers) overlaid on the lock screen image."))
            .In(Category, g)
            .Keywords(L("lock screen, fun facts, tips, trivia"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "RotatingLockScreenOverlayEnabled", 1), Reg.CuDword(ContentDelivery, "SubscribedContent-338387Enabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "RotatingLockScreenOverlayEnabled", 0), Reg.CuDword(ContentDelivery, "SubscribedContent-338387Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.welcome", L("What's new after updates"),
                L("Welcome screens that show what's new and Microsoft suggestions after an update or when you sign in."))
            .In(Category, g)
            .Keywords(L("welcome experience, welcome, what's new, after update"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SubscribedContent-310093Enabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SubscribedContent-310093Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.silent-install", L("Automatic installation of suggested apps"),
                L("Windows installs or pins promoted apps (games, partner apps) on its own, especially when an account is created or after a major update. When off, these silent installations are blocked; apps already present stay installed."))
            .In(Category, g)
            .Keywords(L("bloatware, candy crush, silent install, promoted apps, preinstalled"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ContentDelivery, "SilentInstalledAppsEnabled", 1), Reg.CuDword(ContentDelivery, "OemPreInstalledAppsEnabled", 1),
                    Reg.CuDword(ContentDelivery, "PreInstalledAppsEnabled", 1), Reg.CuDword(ContentDelivery, "PreInstalledAppsEverEnabled", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "SilentInstalledAppsEnabled", 0), Reg.CuDword(ContentDelivery, "OemPreInstalledAppsEnabled", 0),
                     Reg.CuDword(ContentDelivery, "PreInstalledAppsEnabled", 0), Reg.CuDword(ContentDelivery, "PreInstalledAppsEverEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.content-delivery", L("Microsoft content delivery (master switch)"),
                L("Allows the Windows content manager (ContentDeliveryManager) to download suggestions, promotions and images. When off, all this delivery stops at once, including useful content."))
            .In(Category, g)
            .Keywords(L("contentdeliverymanager, content delivery, promotion"))
            .Tags("privacy-max")
            .Risk(RiskLevel.Moderate)
            .Warning(L("The “Windows spotlight” lock screen images may stop refreshing."))
            .WhenOn(Reg.CuDword(ContentDelivery, "ContentDeliveryAllowed", 1))
            .WhenOff(Reg.CuDword(ContentDelivery, "ContentDeliveryAllowed", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.scoobe", L("“Finish setting up your device” reminders"),
                L("Full-screen page that comes back after some updates to offer OneDrive, Microsoft 365, Phone Link or a Microsoft account."))
            .In(Category, g)
            .Keywords(L("scoobe, finish setup, get the most out of windows, reminder"))
            .Tags("privacy-max", "family")
            .WhenOn(Reg.CuDword(ProfileEngagement, "ScoobeSystemSettingEnabled", 1))
            .WhenOff(Reg.CuDword(ProfileEngagement, "ScoobeSystemSettingEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.ads.start-iris", L("Tips and app recommendations in Start"),
                L("Tips, shortcuts and new apps (some promoted) offered in the “Recommended” section of the Windows 11 Start menu."))
            .In(Category, g)
            .Keywords(L("start menu, recommended, iris, recommendations, start recommendations"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_22H2, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_IrisRecommendations", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_IrisRecommendations", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.start-account", L("Account notifications in Start"),
                L("Reminders about your Microsoft account (backup, OneDrive storage, account security) flagged on your profile picture in the Start menu."))
            .In(Category, g)
            .Keywords(L("start menu, account, account notifications, profile badge"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_22H2, TweakDefinition.Off)
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_AccountNotifications", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_AccountNotifications", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.consumer-features", L("Microsoft consumer experiences (policy)"),
                L("Official policy that blocks personalized recommendations, promoted app installations and Microsoft account notifications for the whole PC. Windows only honors it on the Enterprise and Education editions; on other editions, use the settings in this section."))
            .In(Category, g)
            .Keywords(L("consumer features, consumer experiences, cloud content, promoted apps"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.EnterpriseOrEducation, TweakDefinition.Off)
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.LmDel(CloudContentPolicy, "DisableWindowsConsumerFeatures"))
            .WhenOff(Reg.LmDword(CloudContentPolicy, "DisableWindowsConsumerFeatures", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ads.spotlight", L("“Windows spotlight” features (policy)"),
                L("Turns off Windows spotlight all at once (lock screen images and suggestions, tips, promoted content) as well as third-party suggestions. Honored only by the Enterprise and Education editions."))
            .In(Category, g)
            .Keywords(L("spotlight, windows spotlight, lock screen, third party suggestions"))
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

        yield return Tweak.Toggle("privacy.search.web", L("Web results (Bing) in search"),
                L("What you type in Start menu or taskbar search is also sent to Bing to show web results. When off, search stays local (apps, files, settings). Side effect: File Explorer no longer shows your recent searches."))
            .In(Category, g)
            .Keywords(L("bing, web search, internet results, disablesearchboxsuggestions"))
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

        yield return Tweak.Toggle("privacy.search.cloud", L("Search your cloud content"),
                L("Includes results from OneDrive, Outlook and other services linked to your Microsoft or work account in Windows search: your searches are then also sent to these services."))
            .In(Category, g)
            .Keywords(L("cloud search, onedrive, outlook, microsoft account, work account"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(SearchSettings, "IsMSACloudSearchEnabled", 1), Reg.CuDword(SearchSettings, "IsAADCloudSearchEnabled", 1))
            .WhenOff(Reg.CuDword(SearchSettings, "IsMSACloudSearchEnabled", 0), Reg.CuDword(SearchSettings, "IsAADCloudSearchEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.search.history", L("Search history on this device"),
                L("Remembers your searches to suggest them again. This data stays on the PC."))
            .In(Category, g)
            .Keywords(L("search history, recent searches"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(SearchSettings, "IsDeviceSearchHistoryEnabled", 1))
            .WhenOff(Reg.CuDword(SearchSettings, "IsDeviceSearchHistoryEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.search.highlights", L("Search highlights"),
                L("Illustrations, events of the day and trending content provided by Bing in the search box and search pane."))
            .In(Category, g)
            .Keywords(L("search highlights, highlights, trending, bing, search illustration"))
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

        yield return Tweak.Choice("privacy.search.safesearch", L("SafeSearch"),
                L("Filters adult content in Windows search web results. Not relevant if web results are turned off."))
            .In(Category, g)
            .Keywords(L("safesearch, safe search, adult content, filtering, parental controls"))
            .Tags("family")
            .Option("strict", L("Strict"), Reg.CuDword(SearchSettings, "SafeSearchMode", 2))
            .Option("moderate", L("Moderate (default)"), Reg.CuDword(SearchSettings, "SafeSearchMode", 1))
            .Option("off", L("Disabled"), Reg.CuDword(SearchSettings, "SafeSearchMode", 0))
            .WindowsDefault("moderate")
            .Build();

        yield return Tweak.Toggle("privacy.ai.copilot", L("Built-in Copilot (legacy version)"),
                L("“Turn off Windows Copilot” policy: hides and blocks the Copilot pane built into Windows 11 23H2 and Windows 10 (including the taskbar button)."))
            .In(Category, g)
            .Keywords(L("copilot, ai, assistant, turnoffwindowscopilot"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(LegacyCopilot, TweakDefinition.Off)
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDel(CopilotUserPolicy, "TurnOffWindowsCopilot"))
            .WhenOff(Reg.CuDword(CopilotUserPolicy, "TurnOffWindowsCopilot", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ai.recall", L("Recall snapshots"),
                L("On Copilot+ PCs, Recall regularly saves snapshots of your screen so you can find what you've seen. The policy prevents these snapshots from being saved; it has no effect on other PCs."))
            .In(Category, g)
            .Keywords(L("recall, screenshot, snapshot, copilot+, disableaidataanalysis"))
            .Tags("privacy-max", "family")
            .RequiresAndRecommends(Requires.Windows11_24H2, TweakDefinition.Off)
            .Warning(L("Snapshots already saved by Recall are deleted when saving is turned off."))
            .WhenOn(Reg.LmDel(WindowsAiPolicy, "DisableAIDataAnalysis"), Reg.CuDel(WindowsAiUserPolicy, "DisableAIDataAnalysis"))
            .WhenOff(Reg.LmDword(WindowsAiPolicy, "DisableAIDataAnalysis", 1), Reg.CuDword(WindowsAiUserPolicy, "DisableAIDataAnalysis", 1))
            .Detect(() => PrivacyDetect.OffWhenAny(
                PrivacyDetect.Lm(WindowsAiPolicy, "DisableAIDataAnalysis") == 1,
                PrivacyDetect.Cu(WindowsAiUserPolicy, "DisableAIDataAnalysis") == 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ai.recall-component", L("Recall component"),
                L("“Allow Recall to be enabled” policy. “Removed”: the optional Recall component is disabled and removed from Windows after a restart. “Available” (default): it stays off until you turn it on yourself."))
            .In(Category, g)
            .Keywords(L("recall, optional component, optional feature, allowrecallenablement, copilot+"))
            .Labels(L("Available"), L("Removed"))
            .Requires(Requires.Windows11_24H2)
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning(L("Existing Recall snapshots are deleted. To get it back, set this setting to “Available” again, then restart the PC."))
            .WhenOn(Reg.LmDel(WindowsAiPolicy, "AllowRecallEnablement"))
            .WhenOff(Reg.LmDword(WindowsAiPolicy, "AllowRecallEnablement", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.ai.clicktodo", L("Click to Do"),
                L("Analyzes a screenshot, on the device, to suggest actions on the text or images displayed. Mostly available on Copilot+ PCs."))
            .In(Category, g)
            .Keywords(L("click to do, ai, screenshot, disableclicktodo"))
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
                L("Windows 10 voice assistant. The policy turns it off for all users of the PC."))
            .In(Category, g)
            .Keywords(L("cortana, voice assistant, allowcortana"))
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

        yield return Tweak.Toggle("privacy.activity.history", L("Activity history"),
                L("Windows records the apps, files and pages you open (to pick up where you left off) and can send them to your Microsoft account. The policy stops recording and sending for all accounts on the PC."))
            .In(Category, g)
            .Keywords(L("activity history, timeline, publishuseractivities"))
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

        yield return Tweak.Toggle("privacy.activity.clipboard-sync", L("Clipboard sync across devices"),
                L("“Share across devices” option: copied content goes through the Microsoft cloud to your other devices. When blocked, the option is no longer offered."))
            .In(Category, g)
            .Keywords(L("clipboard, sync, cloud clipboard, cross device"))
            .Tags("privacy-max", "family")
            .Labels(L("Allowed"), L("Blocked"))
            .WhenOn(Reg.LmDel(SystemPolicy, "AllowCrossDeviceClipboard"))
            .WhenOff(Reg.LmDword(SystemPolicy, "AllowCrossDeviceClipboard", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.activity.clipboard-history", L("Clipboard history"),
                L("Keeps the last items you copied (Windows + V), including any passwords. The history stays on the PC and is cleared when it restarts, except pinned items."))
            .In(Category, g)
            .Keywords(L("clipboard, clipboard history, windows v, copy history"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ClipboardKey, "EnableClipboardHistory", 1))
            .WhenOff(Reg.CuDword(ClipboardKey, "EnableClipboardHistory", 0))
            .WindowsDefault(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.activity.track-progs", L("App launch tracking"),
                L("Windows remembers the apps you launch to improve the Start menu and search (“Most used” list). This data stays on the PC."))
            .In(Category, g)
            .Keywords(L("start_trackprogs, most used apps, app tracking, track app launches"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_TrackProgs", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_TrackProgs", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.track-docs", L("Recent items in Start and jump lists"),
                L("Shows recently opened files in the Start menu, jump lists (right-click a taskbar icon) and File Explorer."))
            .In(Category, g)
            .Keywords(L("start_trackdocs, recent items, jump list, recent files"))
            .Tags("privacy-max")
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "Start_TrackDocs", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "Start_TrackDocs", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.explorer-recent", L("Recent files in File Explorer"),
                L("“Recent” section of File Explorer Home (Quick access on Windows 10). Applies to new File Explorer windows."))
            .In(Category, g)
            .Keywords(L("recent files, showrecent, quick access, file explorer home"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ExplorerKey, "ShowRecent", 1))
            .WhenOff(Reg.CuDword(ExplorerKey, "ShowRecent", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.explorer-frequent", L("Frequent folders in File Explorer"),
                L("Folders you open often, automatically added to Quick access. Applies to new windows."))
            .In(Category, g)
            .Keywords(L("frequent folders, showfrequent, quick access"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(ExplorerKey, "ShowFrequent", 1))
            .WhenOff(Reg.CuDword(ExplorerKey, "ShowFrequent", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.explorer-cloud", L("Office.com files in File Explorer"),
                L("Shows recent documents from your Microsoft 365 and OneDrive account, fetched online, in File Explorer Home."))
            .In(Category, g)
            .Keywords(L("office.com, microsoft 365, onedrive, cloud files, showcloudfilesinquickaccess"))
            .Tags("privacy-max")
            .Requires(Requires.Windows11_22H2)
            .WhenOn(Reg.CuDword(ExplorerKey, "ShowCloudFilesInQuickAccess", 1))
            .WhenOff(Reg.CuDword(ExplorerKey, "ShowCloudFilesInQuickAccess", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.activity.online-tips", L("Online tips in Settings"),
                L("Allows the Settings app to download tips and help content from Microsoft servers."))
            .In(Category, g)
            .Keywords(L("online tips, online help, allowonlinetips"))
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

        yield return Tweak.Toggle("privacy.input.personalization", L("Personal inking and typing dictionary"),
                L("Windows learns from what you type and handwrite (as well as from your contacts) to improve suggestions and recognition. When off, Windows stops this learning."))
            .In(Category, g)
            .Keywords(L("typing, keyboard, handwriting, inking typing, personal dictionary, input personalization"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(PersonalizationSettings, "AcceptedPrivacyPolicy", 1), Reg.CuDword(InputPersonalization, "RestrictImplicitInkCollection", 0),
                    Reg.CuDword(InputPersonalization, "RestrictImplicitTextCollection", 0), Reg.CuDword(TrainedDataStore, "HarvestContacts", 1))
            .WhenOff(Reg.CuDword(PersonalizationSettings, "AcceptedPrivacyPolicy", 0), Reg.CuDword(InputPersonalization, "RestrictImplicitInkCollection", 1),
                     Reg.CuDword(InputPersonalization, "RestrictImplicitTextCollection", 1), Reg.CuDword(TrainedDataStore, "HarvestContacts", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.tipc", L("Improve inking and typing"),
                L("Sends typing and handwriting data to Microsoft to improve recognition and suggestions. Only applies if sending optional diagnostic data is turned on."))
            .In(Category, g)
            .Keywords(L("tipc, improve inking typing, typing, handwriting, optional data"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(InputTipc, "Enabled", 1))
            .WhenOff(Reg.CuDword(InputTipc, "Enabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.speech", L("Online speech recognition"),
                L("Uses Microsoft servers for dictation and voice commands: more accurate, but your voice is sent online. When off, local voice features (Voice access, Windows Speech Recognition) remain available."))
            .In(Category, g)
            .Keywords(L("voice, speech, dictation, speech recognition, online speech"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDword(OnlineSpeech, "HasAccepted", 1))
            .WhenOff(Reg.CuDword(OnlineSpeech, "HasAccepted", 0))
            .WindowsDefault(TweakDefinition.Off)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.voice-activation", L("Voice activation for apps"),
                L("Allows voice assistants to constantly listen for their keyword to activate, including on the lock screen. The policy applies to all accounts on the PC."))
            .In(Category, g)
            .Keywords(L("voice activation, keyword, wake word, voice assistant, listening"))
            .Tags("privacy-max")
            .Labels(L("Allowed"), L("Blocked"))
            .WhenOn(Reg.LmDel(AppPrivacyPolicy, "LetAppsActivateWithVoice"), Reg.LmDel(AppPrivacyPolicy, "LetAppsActivateWithVoiceAboveLock"))
            .WhenOff(Reg.LmDword(AppPrivacyPolicy, "LetAppsActivateWithVoice", 2), Reg.LmDword(AppPrivacyPolicy, "LetAppsActivateWithVoiceAboveLock", 2))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("privacy.input.language-list", L("Website access to your language list"),
                L("Websites can read the list of languages configured in Windows to show content relevant to your region."))
            .In(Category, g)
            .Keywords(L("language, language list, httpacceptlanguageoptout, websites, local content"))
            .Tags("privacy-max")
            .WhenOn(Reg.CuDel(InternationalProfile, "HttpAcceptLanguageOptOut"))
            .WhenOff(Reg.CuDword(InternationalProfile, "HttpAcceptLanguageOptOut", 1))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.input.handwriting", L("Handwriting data sharing"),
                L("Sends handwriting samples and recognition error reports to Microsoft to improve the service. Mostly affects touch PCs with a pen."))
            .In(Category, g)
            .Keywords(L("handwriting, pen, stylus, handwriting recognition"))
            .Tags("privacy-max")
            .Labels(L("Allowed"), L("Blocked"))
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

        yield return Tweak.Toggle("privacy.location.search", L("Location use by search"),
                L("Allows Windows search to use the device's location to suggest local results. The policy applies to all accounts on the PC."))
            .In(Category, g)
            .Keywords(L("location, position, search, allowsearchtouselocation, local results"))
            .Tags("privacy-max")
            .Labels(L("Allowed"), L("Blocked"))
            .WhenOn(Reg.LmDel(WindowsSearchPolicy, "AllowSearchToUseLocation"))
            .WhenOff(Reg.LmDword(WindowsSearchPolicy, "AllowSearchToUseLocation", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("privacy.location.findmydevice", L("Find my device"),
                L("Lets you locate this PC on a map from account.microsoft.com if it's lost or stolen. Once the user turns the feature on, the PC's location is regularly sent to Microsoft. The policy prevents it for the whole PC."))
            .In(Category, g)
            .Keywords(L("find my device, locate device, theft, lost, location, anti-theft"))
            .Labels(L("Allowed"), L("Blocked"))
            .Risk(RiskLevel.Moderate)
            .Warning(L("You'll no longer be able to locate or lock this PC remotely if it's lost or stolen."))
            .WhenOn(Reg.LmDel(FindMyDevicePolicy, "AllowFindMyDevice"))
            .WhenOff(Reg.LmDword(FindMyDevicePolicy, "AllowFindMyDevice", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();
    }

    // ================================================================== Autorisations des applications (compte courant)

    private static IEnumerable<TweakDefinition> Permissions()
    {
        yield return Permission("webcam", "webcam", L("App access to the camera"),
            L("Camera access for your account. No effect if the camera is blocked for the whole PC (Devices page)."),
            [L("camera, webcam, video")], recommendDeny: false, Requires.Camera);
        yield return Permission("microphone", "microphone", L("App access to the microphone"),
            L("Microphone access for your account. No effect if the microphone is blocked for the whole PC (Devices page)."),
            [L("mic, microphone, audio, recording")], recommendDeny: false);
        yield return Permission("location", "location", L("App access to your location"),
            L("Access to the device's location for your account. No effect if location is turned off for the whole PC (Devices page)."),
            [L("location, position, gps, geolocation")], recommendDeny: false);
        yield return Permission("userNotificationListener", "notifications", L("App access to your notifications"),
            L("Allows apps to read all the notifications you receive (useful for some smartwatches)."),
            [L("notification, notification listener")], recommendDeny: false);
        yield return Permission("userAccountInformation", "account-info", L("App access to your account info"),
            L("Name, picture and address of your Windows account, readable by apps that request them."),
            [L("account, account info, user name, profile picture")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("contacts", "contacts", L("App access to your contacts"),
            L("Reading contacts saved in Windows (People app, email accounts)."),
            [L("contacts, address book, people")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("appointments", "calendar", L("App access to your calendar"),
            L("Reading and changing appointments saved in Windows."),
            [L("calendar, schedule, appointments, agenda")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("phoneCall", "phone-calls", L("App access to phone calls"),
            L("Making calls from the PC (for example through Phone Link and a linked phone)."),
            [L("call, phone, phone call, phone link")], recommendDeny: false);
        yield return Permission("phoneCallHistory", "call-history", L("App access to call history"),
            L("Reading the call history synced to the PC."),
            [L("call history, phone")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("email", "email", L("App access to your email"),
            L("Reading and sending email from accounts configured in Windows."),
            [L("email, e-mail, mail, messaging")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("userDataTasks", "tasks", L("App access to your tasks"),
            L("Reading and changing task lists saved in Windows."),
            [L("tasks, to do, task list")], recommendDeny: true, null, "privacy-max");
        yield return Permission("chat", "messaging", L("App access to messaging (SMS and MMS)"),
            L("Reading and sending SMS or MMS messages from the PC."),
            [L("sms, mms, messaging, messages, text messages, chat")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("radios", "radios", L("Radio control by apps"),
            L("Allows apps to turn Bluetooth or Wi-Fi on or off."),
            [L("radio, bluetooth, wifi, wireless")], recommendDeny: false);
        yield return Permission("bluetoothSync", "other-devices", L("Communication with unpaired devices"),
            L("Allows apps to automatically exchange information with nearby wireless devices that aren't paired with the PC (beacons, smart devices)."),
            [L("unpaired devices, beacon, smart device, iot, other devices")], recommendDeny: true, null, "privacy-max");
        yield return Permission("appDiagnostics", "app-diagnostics", L("App access to diagnostics of other apps"),
            L("Allows an app to see which other apps are running and their diagnostic information."),
            [L("app diagnostics, diagnostics, processes")], recommendDeny: true, null, "privacy-max", "family");
        yield return Permission("documentsLibrary", "documents", L("App access to the Documents folder"),
            L("Applies to Microsoft Store apps; classic desktop programs aren't subject to this control."),
            [L("documents, files, library")], recommendDeny: false);
        yield return Permission("picturesLibrary", "pictures", L("App access to the Pictures folder"),
            L("Applies to Microsoft Store apps; classic desktop programs aren't subject to this control."),
            [L("pictures, images, photos, library")], recommendDeny: false);
        yield return Permission("videosLibrary", "videos", L("App access to the Videos folder"),
            L("Applies to Microsoft Store apps; classic desktop programs aren't subject to this control."),
            [L("videos, movies, library")], recommendDeny: false);
        yield return Permission("musicLibrary", "music", L("App access to the Music library"),
            L("Applies to Microsoft Store apps; classic desktop programs aren't subject to this control."),
            [L("music, audio, library")], recommendDeny: false, Requires.Windows11);
        yield return Permission("broadFileSystemAccess", "file-system", L("App access to the entire file system"),
            L("Access to all your files for the few Microsoft Store apps that request it."),
            [L("file system, all files, broad file system access")], recommendDeny: false, null, "privacy-max");
        yield return Permission("graphicsCaptureProgrammatic", "screenshots", L("Screenshots by apps"),
            L("Allows apps to capture the screen or other windows."),
            [L("screenshot, screen capture, screen recording")], recommendDeny: false, Requires.Windows11);
        yield return Permission("graphicsCaptureWithoutBorder", "screenshot-borders", L("Borderless screenshots"),
            L("Allows apps to turn off the border that shows a window is being captured."),
            [L("capture border, screenshot, screenshot border")], recommendDeny: false, Requires.Windows11);
        yield return Permission("systemAIModels", "ai-models", L("App access to Windows AI models"),
            L("Allows apps to use the artificial intelligence models built into Windows (text and image generation, on the device)."),
            [L("ai, artificial intelligence, text generation, image generation, ai models")], recommendDeny: false, Requires.Windows11_24H2);
        yield return Permission("activity", "motion", L("App access to motion data"),
            L("Activity data from motion sensors (walking, running…), found on some tablets."),
            [L("motion, sensor, activity")], recommendDeny: false, null, "privacy-max");
        yield return Permission("cellularData", "cellular", L("App access to cellular data"),
            L("Use of the mobile connection (4G/5G). Only applies to PCs with a cellular modem."),
            [L("cellular, 4g, 5g, mobile data, lte")], recommendDeny: false);
        yield return Permission("gazeInput", "eye-tracker", L("App access to eye tracking"),
            L("Use of an eye-tracking device. Only applies to PCs with such a device."),
            [L("eye tracking, eye tracker, gaze")], recommendDeny: false);
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
            .Keywords([.. keywords, capability, L("authorization, permission, app access, app permissions")])
            .Labels(L("Allowed"), L("Blocked"))
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
