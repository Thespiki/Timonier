using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Profiles;

/// <summary>
/// Profil d'installation (combinable avec les autres).
/// <para>Règle de calcul, pour chaque réglage retenu : option = <see cref="Targets"/> si elle est définie, sinon la
/// recommandation du réglage pour ce PC (<see cref="TweakDefinition.RecommendationFor"/>) ; sans l'une ni l'autre,
/// le réglage n'est PAS inclus (on ne devine jamais). Une étiquette signifie « concerne cet usage », pas « appliquer ».</para>
/// </summary>
/// <param name="Id">Identifiant stable (fichiers exportés).</param>
/// <param name="Glyph">Icône Segoe Fluent Icons.</param>
public sealed record ProfileDefinition(string Id, string Title, string Description, string Glyph)
{
    /// <summary>Étiquettes de réglages concernées (TweakDefinition.Tags).</summary>
    public string[] IncludeTags { get; init; } = [];

    /// <summary>Choix explicites, relus un par un : identifiant de réglage → option. Les identifiants absents du registre sont ignorés.</summary>
    public IReadOnlyDictionary<string, string> Targets { get; init; } = new Dictionary<string, string>();

    /// <summary>Réglages étiquetés mais volontairement écartés de ce profil.</summary>
    public string[] Excluded { get; init; } = [];

    /// <summary>Réglages proposés mais non cochés par défaut (gêne possible au quotidien).</summary>
    public string[] OptIn { get; init; } = [];

    /// <summary>Conditions matérielles : le réglage n'est retenu par ce profil que si la condition est vraie sur ce PC.</summary>
    public IReadOnlyDictionary<string, Func<SystemProfile, bool>> When { get; init; } = new Dictionary<string, Func<SystemProfile, bool>>();

    /// <summary>Tous les réglages sans risque recommandés pour ce PC (profil « Recommandé »).</summary>
    public bool AllRecommended { get; init; }

    /// <summary>Applications cochées par défaut (identifiants winget du catalogue).</summary>
    public string[] AppIds { get; init; } = [];

    /// <summary>Applications proposées (non cochées) selon leurs étiquettes dans le catalogue.</summary>
    public string[] AppTags { get; init; } = [];

    /// <summary>« Ce que ça change », en quelques points.</summary>
    public string[] Changes { get; init; } = [];

    /// <summary>Limite ou précision affichée sur la carte et dans le plan.</summary>
    public string? Notes { get; init; }

    /// <summary>Page vers laquelle orienter la suite (ex. kiosque) et libellé du lien.</summary>
    public string? LinkPageId { get; init; }
    public string? LinkLabel { get; init; }

    /// <summary>Profil sans objet sur ce PC (il n'est alors pas proposé).</summary>
    public Func<SystemProfile, bool>? Hidden { get; init; }

    /// <summary>Profil présélectionné pour ce PC.</summary>
    public Func<SystemProfile, bool>? Suggested { get; init; }
}

/// <summary>Catalogue des profils, dans l'ordre d'affichage (qui départage aussi les conflits).</summary>
public static class ProfileCatalog
{
    public const string Recommended = "recommended";
    public const string Office = "office";
    public const string Gaming = "gaming";
    public const string Developer = "dev";
    public const string Family = "family";
    public const string PrivacyMax = "privacy-max";
    public const string LowEnd = "lowend";
    public const string Battery = "battery";
    public const string Security = "security";
    public const string Kiosk = "kiosk";

    private const string On = TweakDefinition.On;
    private const string Off = TweakDefinition.Off;

    public static IReadOnlyList<ProfileDefinition> All { get; } = Build();

    public static ProfileDefinition? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    public static int IndexOf(string id)
    {
        for (var i = 0; i < All.Count; i++)
            if (All[i].Id == id) return i;
        return int.MaxValue;
    }

    /// <summary>Autorisations d'accès des applications aux données personnelles (compte, contacts, calendrier, e-mails, SMS…).</summary>
    /// (Propriété calculée et non champ : <see cref="All"/> est initialisé avant les champs déclarés plus bas.)
    private static string[] PersonalDataPermissions =>
    [
        "privacy.perm.account-info", "privacy.perm.contacts", "privacy.perm.calendar", "privacy.perm.email",
        "privacy.perm.call-history", "privacy.perm.messaging", "privacy.perm.tasks",
    ];

    /// <summary>Autorisation dont le blocage empêche des applications de messagerie ou d'agenda de fonctionner.</summary>
    public static bool IsPersonalDataPermission(string tweakId) => PersonalDataPermissions.Contains(tweakId, StringComparer.Ordinal);

    /// <summary>
    /// Options « connues » : la recommandation pour ce PC ou une cible explicite d'un profil du catalogue. Sert à repérer,
    /// dans un fichier importé (non fiable), un réglage de sécurité réglé autrement que Timonier ne le proposerait.
    /// </summary>
    public static bool IsKnownTarget(string tweakId, string option) =>
        All.Any(p => p.Targets.TryGetValue(tweakId, out var o) && string.Equals(o, option, StringComparison.Ordinal));

    private static Dictionary<string, string> T(params (string Id, string Option)[] items) =>
        items.ToDictionary(i => i.Id, i => i.Option, StringComparer.Ordinal);

    private static List<ProfileDefinition> Build() =>
    [
        new(Recommended, L("Recommended for this PC"),
            L("The safe settings Timonier recommends for your hardware and your edition of Windows, plus two essential apps."),
            "")
        {
            AllRecommended = true,
            // Recommandés « Bloqué » par la page Confidentialité, mais les couper empêche Courrier, Calendrier, Contacts ou
            // Lien avec Windows de fonctionner : proposés, jamais cochés d'office par ce profil généraliste.
            OptIn = PersonalDataPermissions,
            Notes = L("Blocking app access to your account, contacts, calendar, email and SMS is offered but not checked: it would stop Mail, Calendar, Outlook or Phone Link from working."),
            AppIds = ["7zip.7zip", "VideoLAN.VLC"],
            AppTags = ["essentials"],
            Changes =
            [
                L("All settings marked “Recommended” in the pages, only the safe ones"),
                L("Recommendations tailored to the hardware (laptop, drive, performance tier)"),
                L("7-Zip and VLC offered; this PC manufacturer's tools optional"),
            ],
            Suggested = _ => true,
        },

        new(Office, L("Office work"),
            L("A clean workstation: fewer distractions in the taskbar, visible file extensions, legacy protocols turned off."),
            "")
        {
            IncludeTags = ["office"],
            Targets = T(
                ("custom.taskbar.widgets", Off),
                ("custom.taskbar.chat", Off),
                ("custom.taskbar.endtask", On),
                ("custom.taskbar.combine", "whenfull"),
                ("custom.logon.numlock", On)),
            // Sur un portable sans pavé numérique séparé, Verr. num transformerait des lettres en chiffres (cf. le réglage).
            When = new Dictionary<string, Func<SystemProfile, bool>> { ["custom.logon.numlock"] = p => p.HardwareLoaded && !p.IsLaptopLike },
            // Étiquetés « office » mais trop contraignants pour un poste personnel, ou à décider au cas par cas.
            Excluded =
            [
                "devices.block.usbstorage", "devices.block.usbwrite", "devices.block.wpd", "devices.install.metadata",
                "perf.svc.xbox", "security.lsa.ppl", "security.hvci", "security.llmnr", "security.logon.cad",
                "network.wifi.minimize-connections", "custom.explorer.launchto", "custom.explorer.classicmenu", "custom.explorer.fullpath",
            ],
            AppIds = ["TheDocumentFoundation.LibreOffice", "SumatraPDF.SumatraPDF"],
            AppTags = ["office"],
            Changes =
            [
                L("Widgets and Chat buttons removed from the taskbar"),
                L("“End task” on right-click, visible file extensions, Num Lock on (desktop PCs)"),
                L("SMBv1 and Remote Assistance turned off; LibreOffice and SumatraPDF offered"),
            ],
        },

        new(Gaming, L("Games"),
            L("Games first: Game Mode, background recording turned off, hardware-accelerated GPU scheduling if your graphics card supports it."),
            "")
        {
            IncludeTags = ["gaming"],
            Targets = T(("custom.mouse.precision", Off)),
            // Les services Xbox, la Game Bar et ses captures servent à beaucoup de joueurs : ce profil n'y touche pas.
            Excluded = ["perf.game.capture", "perf.game.nexus", "perf.svc.xbox"],
            AppIds = ["Valve.Steam", "Microsoft.VCRedist.2015+.x64"],
            AppTags = ["gaming"],
            Changes =
            [
                L("Game Mode on, background recording off"),
                L("Pointer precision off (more consistent mouse aiming)"),
                L("Xbox services and Game Bar kept; Steam and Visual C++ offered"),
            ],
        },

        new(Developer, L("Developer"),
            L("File Explorer and the taskbar set up for coding: hidden files and extensions visible, tool telemetry turned off."),
            "")
        {
            IncludeTags = ["dev"],
            Targets = T(
                ("custom.explorer.hidden", On),
                ("custom.explorer.extensions", On),
                ("custom.explorer.fullpath", On),
                ("custom.taskbar.endtask", On),
                ("privacy.telemetry.devtools", Off)),
            Excluded = ["custom.explorer.superhidden", "custom.logon.verbose", "custom.taskbar.seconds"],
            AppIds = ["Microsoft.VisualStudioCode", "Git.Git"],
            AppTags = ["dev"],
            Changes =
            [
                L("Hidden files, extensions and full path visible in File Explorer"),
                L("“End task” on right-click; PowerShell 7 and .NET SDK telemetry turned off"),
                L("Visual Studio Code and Git offered"),
            ],
        },

        new(Family, L("Family and kids"),
            L("A safer shared PC with fewer ads: strict SafeSearch, suggestions and promotional content turned off, SmartScreen enforced."),
            "")
        {
            IncludeTags = ["family"],
            Targets = T(
                ("custom.taskbar.widgets", Off),
                ("custom.taskbar.chat", Off),
                ("privacy.search.safesearch", "strict"),
                ("privacy.ads.start-suggestions", Off),
                ("privacy.ads.start-iris", Off),
                ("privacy.ads.consumer-features", Off),
                ("privacy.search.highlights", Off),
                ("security.smartscreen.apps", On)),
            // Bloquer caméra, micro, localisation ou clés USB pour tout le PC est une décision à prendre dans Périphériques.
            Excluded =
            [
                "devices.block.usbstorage", "devices.block.wpd", "devices.block.camera", "devices.block.microphone",
                "devices.block.location", "security.defender.cfa", "security.uac.always", "security.uac.recommended",
            ],
            // Bloquer l'accès aux contacts, au calendrier ou aux e-mails casse Courrier, Calendrier et Lien avec Windows.
            OptIn = PersonalDataPermissions,
            AppTags = ["family"],
            Notes = L("Child accounts and parental controls (screen time, filtering) are managed with Microsoft Family Safety: this profile doesn't configure them."),
            LinkPageId = "users",
            LinkLabel = L("Manage accounts"),
            Changes =
            [
                L("Strict SafeSearch, web results and suggestions removed"),
                L("Ads, tips and automatic app installations turned off"),
                L("SmartScreen enforced, Remote Desktop and Remote Assistance turned off"),
            ],
        },

        new(PrivacyMax, L("Maximum privacy"),
            L("Minimizes what Windows sends and shows: diagnostic data, ads, web search, Copilot and Recall."),
            "")
        {
            IncludeTags = ["privacy-max"],
            Targets = T(
                ("privacy.telemetry.limits", On),
                ("privacy.telemetry.devtools", Off),
                ("privacy.telemetry.wer", Off),
                ("privacy.ads.start-suggestions", Off),
                ("privacy.ads.settings-notifications", Off),
                ("privacy.ads.start-iris", Off),
                ("privacy.ads.start-account", Off),
                ("privacy.ads.consumer-features", Off),
                ("privacy.search.history", Off),
                ("privacy.search.highlights", Off),
                ("privacy.ai.copilot", Off),
                ("privacy.ai.recall", Off),
                ("privacy.ai.clicktodo", Off),
                ("privacy.search.cortana", Off),
                ("privacy.activity.explorer-cloud", Off),
                ("privacy.input.voice-activation", Off)),
            // Casse la détection d'Internet et des portails Wi-Fi, ou fait doublon avec des réglages plus précis.
            // (privacy.telemetry.off reste inclus : c'est « le minimum autorisé » sur Entreprise et Éducation, où
            // privacy.telemetry.level n'a pas de recommandation.)
            Excluded =
            [
                "network.ncsi.active-probe", "privacy.telemetry.dmwappush",
                "privacy.ads.content-delivery", "privacy.ads.spotlight",
            ],
            OptIn = ["privacy.telemetry.wer"],
            Notes = L("Windows Update and Microsoft Defender keep communicating with Microsoft: this is essential to the PC's security. App access to your contacts, calendar and email is blocked: uncheck these lines if you use Mail, Calendar, Outlook or Phone Link."),
            Changes =
            [
                L("Diagnostic data at the minimum allowed by your edition"),
                L("Advertising ID, suggestions, web search and history turned off"),
                L("Copilot, Recall and Click to Do turned off"),
            ],
        },

        new(LowEnd, L("Low-end PC"),
            L("Lightens Windows on a low-powered PC: visual effects, animations, apps reopened when you sign in."),
            "")
        {
            IncludeTags = ["lowend"],
            Targets = T(
                ("custom.colors.transparency", Off),
                ("perf.fx.minanimate", Off),
                ("perf.fx.taskbaranim", Off),
                ("perf.game.capture", Off),
                ("perf.svc.maps", Off),
                ("startup.restartapps", Off)),
            OptIn = ["perf.bg.apps", "perf.bg.apps.policy", "perf.svc.wsearch"],
            AppTags = ["lowend"],
            Notes = L("Deeper settings (“Performance” visual effects, indexing) are only offered if this PC is detected as low-end."),
            Suggested = p => p.HardwareLoaded && p.Tier == PerformanceTier.Low,
            Changes =
            [
                L("Window transparency and animations turned off"),
                L("Game captures and automatic map updates turned off"),
                L("Apps not reopened when you sign in"),
            ],
        },

        new(Battery, L("Laptop and battery life"),
            L("Saves battery: hibernation available, power throttling for background tasks, game recording turned off."),
            "")
        {
            IncludeTags = ["battery"],
            OptIn = ["perf.bg.apps", "perf.bg.apps.policy"],
            Hidden = p => p.HardwareLoaded && !p.HasBattery,
            Changes =
            [
                L("Hibernation on (resume without losing anything after a long break)"),
                L("Background tasks on the most power-efficient cores"),
                L("Background game recording off"),
            ],
        },

        new(Security, L("Enhanced security"),
            L("Hardens Windows without making it a hassle: SmartScreen and Defender protections enforced, remote access and legacy protocols turned off."),
            "")
        {
            IncludeTags = ["security"],
            Targets = T(
                ("security.smartscreen.apps", On),
                ("security.defender.networkprotection", On),
                ("security.defender.cfa", On),
                ("security.wsh", Off)),
            // Blocages matériels et UAC : à décider dans leurs pages, pas dans un profil général.
            Excluded =
            [
                "devices.block.usbstorage", "devices.block.usbwrite", "devices.block.optical", "devices.block.wpd",
                "devices.block.allremovable", "devices.block.camera", "devices.block.microphone", "devices.block.location",
                "devices.install.restrict", "security.logon.cad", "security.adminshares",
                "security.uac.always", "security.uac.recommended",
            ],
            // Utiles mais susceptibles de bloquer des logiciels légitimes : à cocher en connaissance de cause.
            OptIn = ["security.defender.cfa", "security.wsh"],
            AppTags = ["security"],
            Changes =
            [
                L("SmartScreen, potentially unwanted app blocking and network protection enforced"),
                L("Remote Desktop, Remote Registry, LLMNR and SMBv1 turned off"),
                L("LSA protection and memory integrity if the PC supports them"),
            ],
        },

        new(Kiosk, L("Kiosk / public terminal"),
            L("Prepares a self-service PC: removable media blocked, AutoPlay and scripts turned off. The account and the app are then configured on the Kiosk mode page."),
            "")
        {
            IncludeTags = ["kiosk"],
            Targets = T(
                ("devices.block.usbstorage", Off),
                ("devices.block.optical", Off),
                ("devices.block.wpd", Off),
                ("devices.block.allremovable", Off),
                ("devices.block.camera", Off),
                ("devices.block.microphone", Off),
                ("devices.block.location", Off),
                ("devices.install.metadata", Off),
                ("devices.install.restrict", "removable"),
                ("security.smartscreen.apps", On),
                ("security.wsh", Off)),
            Excluded = ["security.logon.cad", "security.adminshares", "security.uac.always", "security.uac.recommended"],
            OptIn = ["devices.block.allremovable", "devices.block.camera", "devices.block.microphone", "devices.block.location"],
            Notes = L("This profile doesn't create a kiosk account or lock the session: it only prepares the PC."),
            LinkPageId = "kiosk",
            LinkLabel = L("Set up the kiosk"),
            Changes =
            [
                L("USB drives, CDs/DVDs and phones blocked for all accounts"),
                L("SmartScreen enforced, Windows Script Host turned off"),
                L("Next steps on the Kiosk mode page: dedicated account and displayed app"),
            ],
        },
    ];
}
