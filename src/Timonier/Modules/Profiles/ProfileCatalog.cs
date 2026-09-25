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
        new(Recommended, "Recommandé pour ce PC",
            "Les réglages sans risque que Timonier recommande pour votre matériel et votre édition de Windows, et deux applications essentielles.",
            "")
        {
            AllRecommended = true,
            // Recommandés « Bloqué » par la page Confidentialité, mais les couper empêche Courrier, Calendrier, Contacts ou
            // Lien avec Windows de fonctionner : proposés, jamais cochés d'office par ce profil généraliste.
            OptIn = PersonalDataPermissions,
            Notes = "Le blocage de l'accès des applications à votre compte, vos contacts, votre calendrier, vos e-mails et vos SMS est " +
                    "proposé sans être coché : il empêcherait Courrier, Calendrier, Outlook ou Lien avec Windows de fonctionner.",
            AppIds = ["7zip.7zip", "VideoLAN.VLC"],
            AppTags = ["essentials"],
            Changes =
            [
                "Tous les réglages marqués « Recommandé » dans les pages, uniquement ceux sans risque",
                "Recommandations adaptées au matériel (portable, disque, gamme de performance)",
                "7-Zip et VLC proposés ; outils du constructeur de ce PC en option",
            ],
            Suggested = _ => true,
        },

        new(Office, "Bureautique",
            "Un poste de travail sobre : moins de distractions dans la barre des tâches, extensions de fichiers visibles, protocoles anciens coupés.",
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
                "Boutons Widgets et Conversation retirés de la barre des tâches",
                "« Fin de tâche » au clic droit, extensions de fichiers visibles, Verr. num activé (PC de bureau)",
                "SMBv1 et assistance à distance coupés ; LibreOffice et SumatraPDF proposés",
            ],
        },

        new(Gaming, "Jeux",
            "Priorité aux jeux : Mode Jeu, enregistrement en arrière-plan coupé, planification GPU matérielle si votre carte graphique s'y prête.",
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
                "Mode Jeu activé, enregistrement en arrière-plan désactivé",
                "Précision du pointeur désactivée (visée plus régulière à la souris)",
                "Services Xbox et Game Bar conservés ; Steam et Visual C++ proposés",
            ],
        },

        new(Developer, "Développeur",
            "L'Explorateur et la barre des tâches pensés pour le code : fichiers cachés et extensions visibles, télémétrie des outils coupée.",
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
                "Fichiers cachés, extensions et chemin complet visibles dans l'Explorateur",
                "« Fin de tâche » au clic droit ; télémétrie de PowerShell 7 et du SDK .NET coupée",
                "Visual Studio Code et Git proposés",
            ],
        },

        new(Family, "Famille et enfants",
            "Un PC partagé plus sûr et moins publicitaire : recherche sécurisée stricte, suggestions et contenus promotionnels coupés, SmartScreen imposé.",
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
            Notes = "Les comptes enfants et le contrôle parental (temps d'écran, filtrage) se gèrent avec Microsoft Family Safety : ce profil ne les configure pas.",
            LinkPageId = "users",
            LinkLabel = "Gérer les comptes",
            Changes =
            [
                "Recherche sécurisée stricte, résultats web et suggestions retirés",
                "Publicités, astuces et installations automatiques d'applications coupées",
                "SmartScreen imposé, Bureau à distance et Assistance à distance coupés",
            ],
        },

        new(PrivacyMax, "Vie privée maximale",
            "Réduit au minimum ce que Windows envoie et affiche : données de diagnostic, publicités, recherche web, Copilot et Recall.",
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
            Notes = "Windows Update et Microsoft Defender continuent de communiquer avec Microsoft : c'est indispensable à la sécurité du PC. " +
                    "L'accès des applications à vos contacts, calendrier et e-mails est bloqué : décochez ces lignes si vous utilisez " +
                    "Courrier, Calendrier, Outlook ou Lien avec Windows.",
            Changes =
            [
                "Données de diagnostic au minimum autorisé par votre édition",
                "Identifiant de publicité, suggestions, recherche web et historique coupés",
                "Copilot, Recall et Click to Do désactivés",
            ],
        },

        new(LowEnd, "PC modeste",
            "Allège Windows sur un PC peu puissant : effets visuels, animations, applications relancées à l'ouverture de session.",
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
            Notes = "Les réglages plus profonds (effets visuels « Performances », indexation) ne sont proposés que si ce PC est détecté comme modeste.",
            Suggested = p => p.HardwareLoaded && p.Tier == PerformanceTier.Low,
            Changes =
            [
                "Transparence et animations des fenêtres désactivées",
                "Captures de jeu et mise à jour automatique des cartes coupées",
                "Applications non relancées à l'ouverture de session",
            ],
        },

        new(Battery, "Portable et autonomie",
            "Économise la batterie : veille prolongée disponible, limitation d'énergie des tâches en arrière-plan, enregistrement de jeu coupé.",
            "")
        {
            IncludeTags = ["battery"],
            OptIn = ["perf.bg.apps", "perf.bg.apps.policy"],
            Hidden = p => p.HardwareLoaded && !p.HasBattery,
            Changes =
            [
                "Veille prolongée activée (reprise sans perte après une longue pause)",
                "Tâches en arrière-plan sur les cœurs les plus économes",
                "Enregistrement de jeu en arrière-plan désactivé",
            ],
        },

        new(Security, "Sécurité renforcée",
            "Durcit Windows sans le rendre pénible : SmartScreen et protections de Defender imposés, accès à distance et protocoles anciens coupés.",
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
                "SmartScreen, blocage des applications indésirables et protection réseau imposés",
                "Bureau à distance, Registre à distance, LLMNR et SMBv1 coupés",
                "Protection LSA et intégrité de la mémoire si le PC s'y prête",
            ],
        },

        new(Kiosk, "Borne / kiosque",
            "Prépare un PC en libre-service : supports amovibles bloqués, exécution automatique et scripts coupés. Le compte et l'application se configurent ensuite dans la page Kiosque.",
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
            Notes = "Ce profil ne crée pas de compte kiosque et ne verrouille pas la session : il prépare seulement le PC.",
            LinkPageId = "kiosk",
            LinkLabel = "Configurer le kiosque",
            Changes =
            [
                "Clés USB, CD/DVD et téléphones bloqués pour tous les comptes",
                "SmartScreen imposé, Windows Script Host coupé",
                "Suite dans la page Kiosque : compte dédié et application affichée",
            ],
        },
    ];
}
