using Microsoft.Win32;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Performance;

/// <summary>
/// Réglages de performances. Aucun « placebo » : chaque réglage agit sur un paramètre réel de Windows,
/// dont l'effet et les limites sont décrits honnêtement. Les recommandations dépendent du matériel détecté.
/// </summary>
public static class PerformanceTweaks
{
    private const string C = PerformanceModule.Category;

    public static readonly string GroupVisual = L("Effets visuels");
    public static readonly string GroupGames = L("Jeux");
    public static readonly string GroupEnergy = L("Énergie");
    public static readonly string GroupBackground = L("Arrière-plan et démarrage");
    public static readonly string GroupServices = L("Services");
    public static readonly string GroupStorage = L("Stockage");

    public static readonly string[] Groups = [GroupVisual, GroupGames, GroupEnergy, GroupBackground, GroupServices, GroupStorage];

    // Clés de registre
    private const string Desktop = @"Control Panel\Desktop";
    private const string WindowMetrics = @"Control Panel\Desktop\WindowMetrics";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string VisualEffects = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string GameConfigStore = @"System\GameConfigStore";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string GameDvrPolicy = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
    private const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string SessionPower = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    private const string PowerThrottling = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string BackgroundApps = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    private const string AppPrivacyPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
    private const string Serialize = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";
    private const string FileSystem = @"SYSTEM\CurrentControlSet\Control\FileSystem";

    // Masques UserPreferencesMask écrits par la boîte « Options de performances » de Windows 10/11.
    private static readonly byte[] MaskDefault = [0x9E, 0x1E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00];
    private static readonly byte[] MaskAppearance = [0x9E, 0x3E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00];
    private static readonly byte[] MaskPerformance = [0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00];

    private static bool LowEnd(SystemProfile p) => p.HardwareLoaded && p.Tier == PerformanceTier.Low;
    private static bool HasDedicatedGpu(SystemProfile p) =>
        p.Gpus.Any(g => (!g.Integrated && g.Vendor is HardwareVendor.Nvidia or HardwareVendor.Amd)
                        || g.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase) && g.Vendor == HardwareVendor.Intel);

    public static IEnumerable<TweakDefinition> All() =>
    [
        .. Visual(), .. Games(), .. Energy(), .. Background(), .. Services(), .. Storage(),
    ];

    // ================================================================== Effets visuels

    private static IEnumerable<TweakDefinition> Visual()
    {
        yield return Tweak.Choice("perf.fx.preset", L("Effets visuels de Windows"),
                L("Équivalent des « Options de performances » de Windows (sysdm.cpl > Avancé > Performances). « Performances » coupe les animations, fondus, ombres et aperçus : l'interface paraît plus vive sur un PC modeste, sans accélérer les applications. Contrairement au bouton de Windows, le lissage des polices est conservé. Pris en compte après une déconnexion."))
            .In(C, GroupVisual)
            .Keywords(L("effets visuels, visual effects, animations, options de performances, apparence, fluidité, ombres, fondu"))
            .Tags("lowend")
            .OptionWithHelp("auto", L("Laisser Windows choisir"), L("Réglage d'origine : tous les effets activés sur la plupart des PC."),
                VisualSet(0, MaskDefault, true))
            .OptionWithHelp("appearance", L("Meilleure apparence"), L("Tous les effets, y compris l'ombre sous le pointeur."),
                VisualSet(1, MaskAppearance, true))
            .OptionWithHelp("performance", L("Meilleures performances"), L("Coupe animations, fondus, ombres et aperçus (lissage des polices conservé)."),
                VisualSet(2, MaskPerformance, false))
            .OptionWithHelp("custom", L("Personnalisé"), L("Conserve vos réglages individuels (ceux ci-dessous)."),
                Reg.CuDword(VisualEffects, "VisualFXSetting", 3))
            .Detect(() => RegistryAccess.ReadDword(RegHive.CurrentUser, VisualEffects, "VisualFXSetting") switch
            {
                null or 0 => "auto",
                1 => "appearance",
                2 => "performance",
                3 => "custom",
                _ => null,
            })
            .WindowsDefault("auto")
            .Effect(ApplyEffect.SignOut)
            .RecommendWhen(p => LowEnd(p) ? "performance" : null)
            .Build();

        yield return Tweak.Toggle("perf.fx.minanimate", L("Animation des fenêtres (réduire et agrandir)"),
                L("Effet de zoom lorsqu'une fenêtre est réduite dans la barre des tâches ou agrandie. Le couper rend ces gestes instantanés ; aucun effet sur la vitesse des applications."))
            .In(C, GroupVisual)
            .Keywords(L("animation, réduire, agrandir, minimize, maximize, zoom fenêtre"))
            .Tags("lowend")
            .WhenOn(Reg.CuString(WindowMetrics, "MinAnimate", "1"))
            .WhenOff(Reg.CuString(WindowMetrics, "MinAnimate", "0"))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.SignOut)
            .RecommendWhen(p => LowEnd(p) ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Toggle("perf.fx.taskbaranim", L("Animations de la barre des tâches"),
                L("Animations des boutons et des aperçus de la barre des tâches. Les couper allège légèrement l'Explorateur sur un PC modeste."))
            .In(C, GroupVisual)
            .Keywords(L("barre des tâches, taskbar, animation, aperçu"))
            .Tags("lowend")
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "TaskbarAnimations", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "TaskbarAnimations", 0))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.SignOut)
            .RecommendWhen(p => LowEnd(p) ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Choice("perf.fx.menudelay", L("Délai d'ouverture des sous-menus"),
                L("Temps d'attente avant l'ouverture d'un sous-menu classique au survol (menus contextuels, barres de menus). Un délai plus court donne une impression de réactivité, sans rien changer à la puissance du PC."))
            .In(C, GroupVisual)
            .Keywords(L("menu, délai, MenuShowDelay, sous-menu, réactivité, survol"))
            .Option("400", L("Standard (400 ms)"), Reg.CuString(Desktop, "MenuShowDelay", "400"))
            .Option("200", L("Rapide (200 ms)"), Reg.CuString(Desktop, "MenuShowDelay", "200"))
            .Option("50", L("Très rapide (50 ms)"), Reg.CuString(Desktop, "MenuShowDelay", "50"))
            .WindowsDefault("400")
            .Effect(ApplyEffect.SignOut)
            .Build();

        yield return Tweak.Toggle("perf.fx.dragfull", L("Contenu des fenêtres pendant le déplacement"),
                L("Affiche la fenêtre entière pendant qu'on la déplace ou la redimensionne. Désactivé, seul un cadre suit la souris : utile surtout sur une carte graphique très faible ou en bureau à distance."))
            .In(C, GroupVisual)
            .Keywords(L("déplacer, glisser, drag, redimensionner, contenu fenêtre"))
            .Tags("lowend")
            .WhenOn(Reg.CuString(Desktop, "DragFullWindows", "1"))
            .WhenOff(Reg.CuString(Desktop, "DragFullWindows", "0"))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.SignOut)
            .Build();

        yield return Tweak.Toggle("perf.fx.peek", L("Aperçu du Bureau (Peek)"),
                L("Rend les fenêtres transparentes pour montrer le Bureau quand on survole le coin droit de la barre des tâches. Le désactiver n'a qu'un effet minime sur les performances."))
            .In(C, GroupVisual)
            .Keywords(L("peek, aero peek, aperçu bureau, coin barre des tâches"))
            .WhenOn(Reg.CuDword(Dwm, "EnableAeroPeek", 1))
            .WhenOff(Reg.CuDword(Dwm, "EnableAeroPeek", 0))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.SignOut)
            .Build();

        yield return Tweak.Toggle("perf.fx.iconshadow", L("Ombre sous le nom des icônes du Bureau"),
                L("Ombre portée derrière le texte des icônes pour le rendre lisible sur tous les fonds d'écran. Impact négligeable sur les performances."))
            .In(C, GroupVisual)
            .Keywords(L("ombre, icônes, bureau, shadow, texte icône"))
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "ListviewShadow", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "ListviewShadow", 0))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.RestartExplorer)
            .Build();

        yield return Tweak.Toggle("perf.fx.alphaselect", L("Rectangle de sélection translucide"),
                L("Rectangle bleu semi-transparent lorsqu'on sélectionne des fichiers à la souris. Désactivé, un simple cadre en pointillés est affiché."))
            .In(C, GroupVisual)
            .Keywords(L("sélection, rectangle, translucide, explorateur"))
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "ListviewAlphaSelect", 1))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "ListviewAlphaSelect", 0))
            .WindowsDefault(TweakDefinition.On)
            .Effect(ApplyEffect.RestartExplorer)
            .Build();
    }

    /// <summary>Préréglage d'effets : case de la boîte Windows + masque des effets + réglages individuels cohérents.</summary>
    private static Operation[] VisualSet(int fxSetting, byte[] mask, bool effects) =>
    [
        Reg.CuDword(VisualEffects, "VisualFXSetting", fxSetting),
        Reg.Cu(Desktop, "UserPreferencesMask", RegistryValueKind.Binary, mask),
        Reg.CuString(WindowMetrics, "MinAnimate", effects ? "1" : "0"),
        Reg.CuDword(ExplorerAdvanced, "TaskbarAnimations", effects ? 1 : 0),
        Reg.CuDword(Dwm, "EnableAeroPeek", effects ? 1 : 0),
        Reg.CuDword(ExplorerAdvanced, "ListviewAlphaSelect", effects ? 1 : 0),
        Reg.CuDword(ExplorerAdvanced, "ListviewShadow", effects ? 1 : 0),
        Reg.CuString(Desktop, "DragFullWindows", effects ? "1" : "0"),
    ];

    // ================================================================== Jeux

    private static IEnumerable<TweakDefinition> Games()
    {
        yield return Tweak.Toggle("perf.game.mode", L("Mode Jeu"),
                L("Quand un jeu est détecté, Windows lui donne la priorité (processeur, carte graphique) et suspend l'installation des pilotes et les notifications de redémarrage de Windows Update. Sans effet hors des jeux ; activé par défaut."))
            .In(C, GroupGames)
            .Keywords(L("mode jeu, game mode, gaming, jeux, fps"))
            .Tags("gaming")
            .WhenOn(Reg.CuDword(GameBar, "AutoGameModeEnabled", 1))
            .WhenOff(Reg.CuDword(GameBar, "AutoGameModeEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("perf.game.capture", L("Captures de jeu (Xbox Game Bar)"),
                L("Captures d'écran et enregistrements vidéo des jeux avec la Xbox Game Bar (Win + Alt + Impr. écran). Désactivé, la Game Bar ne peut plus enregistrer et Windows ne prépare plus la capture au lancement des jeux."))
            .In(C, GroupGames)
            .Keywords(L("game dvr, capture, enregistrement, xbox game bar, vidéo jeu, clip"))
            .Tags("gaming", "lowend")
            .WhenOn(Reg.CuDword(GameConfigStore, "GameDVR_Enabled", 1), Reg.CuDword(GameDvr, "AppCaptureEnabled", 1),
                Reg.LmDel(GameDvrPolicy, "AllowGameDVR"))
            .WhenOff(Reg.CuDword(GameConfigStore, "GameDVR_Enabled", 0), Reg.CuDword(GameDvr, "AppCaptureEnabled", 0),
                Reg.LmDword(GameDvrPolicy, "AllowGameDVR", 0))
            .WindowsDefault(TweakDefinition.On)
            .RecommendWhen(p => LowEnd(p) ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Toggle("perf.game.bgrecord", L("Enregistrement en arrière-plan"),
                L("Filme en continu les dernières minutes de jeu pour pouvoir « enregistrer ce qui vient de se passer ». L'encodage vidéo permanent coûte des images par seconde et de l'autonomie : désactivé par défaut dans Windows."))
            .In(C, GroupGames)
            .Keywords(L("enregistrer ce qui vient de se passer, background recording, replay, dvr, enregistrement continu"))
            .Tags("gaming", "lowend", "battery")
            .WhenOn(Reg.CuDword(GameDvr, "HistoricalCaptureEnabled", 1))
            .WhenOff(Reg.CuDword(GameDvr, "HistoricalCaptureEnabled", 0))
            .WindowsDefault(TweakDefinition.Off)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("perf.game.nexus", L("Ouvrir la Game Bar avec le bouton Xbox"),
                L("Le bouton Xbox d'une manette ouvre la Xbox Game Bar. À couper si vous l'ouvrez par erreur en jouant ou si un autre lanceur (Steam) utilise ce bouton."))
            .In(C, GroupGames)
            .Keywords(L("manette, bouton xbox, controller, game bar, nexus"))
            .Tags("gaming")
            .WhenOn(Reg.CuDword(GameBar, "UseNexusForGameBarEnabled", 1))
            .WhenOff(Reg.CuDword(GameBar, "UseNexusForGameBarEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Choice("perf.game.hags", L("Planification GPU à accélération matérielle"),
                L("La carte graphique gère elle-même sa file de travail au lieu de Windows, ce qui peut réduire légèrement la latence et la charge du processeur dans les jeux. Nécessite une carte récente avec un pilote WDDM 2.7 ou plus récent (NVIDIA GTX 10xx et ultérieures, AMD RX 5000 et ultérieures, Intel Arc) ; les gains varient selon les jeux."))
            .In(C, GroupGames)
            .Keywords(L("hags, gpu scheduling, planification gpu, carte graphique, latence, wddm, dlss 3, génération d'images"))
            .Tags("gaming")
            .Option("default", L("Choix du pilote"), Reg.LmDel(GraphicsDrivers, "HwSchMode"))
            .Option("on", L("Activée"), Reg.LmDword(GraphicsDrivers, "HwSchMode", 2))
            .Option("off", L("Désactivée"), Reg.LmDword(GraphicsDrivers, "HwSchMode", 1))
            .WindowsDefault("default")
            .Requires(new Requirement { MinBuild = 19041 }.And(Requires.When(HasDedicatedGpu,
                L("Nécessite une carte graphique dédiée récente (NVIDIA, AMD ou Intel Arc) avec un pilote WDDM 2.7 ou plus récent."))))
            .Effect(ApplyEffect.Reboot)
            .RecommendWhen(p => HasDedicatedGpu(p) && p.Tier != PerformanceTier.Low ? "on" : null)
            .Build();
    }

    // ================================================================== Énergie

    private static IEnumerable<TweakDefinition> Energy()
    {
        yield return Tweak.Toggle("perf.power.hibernate", L("Veille prolongée"),
                L("Enregistre la session sur le disque puis éteint complètement le PC : aucune consommation, reprise là où vous étiez. Le fichier hiberfil.sys occupe environ 40 % de la mémoire vive. La désactiver libère cet espace mais supprime aussi le démarrage rapide et la veille prolongée automatique (un portable en veille pourra alors se vider complètement)."))
            .In(C, GroupEnergy)
            .Keywords(L("hibernation, veille prolongée, hiberfil, hibernate, espace disque"))
            .Tags("battery")
            .WhenOn(Sys.Tool(SystemTool.PowerCfg, true, L("Active la veille prolongée (powercfg /hibernate on)"), "/hibernate", "on"))
            .WhenOff(Sys.Tool(SystemTool.PowerCfg, true, L("Désactive la veille prolongée et supprime hiberfil.sys (powercfg /hibernate off)"), "/hibernate", "off"))
            .Detect(() => PowerApi.HibernationEnabled() ? TweakDefinition.On : TweakDefinition.Off)
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Désactiver la veille prolongée désactive aussi le démarrage rapide."))
            .RecommendWhen(p => p.HasBattery ? TweakDefinition.On : null)
            .Build();

        yield return Tweak.Toggle("perf.power.faststartup", L("Démarrage rapide"),
                L("À l'arrêt, Windows ferme votre session mais met le noyau en veille prolongée : le démarrage suivant est plus court, surtout sur disque dur. Inconvénients : le PC n'est pas vraiment « neuf » après un arrêt (seul « Redémarrer » le fait), certaines mises à jour et pilotes attendent un redémarrage, et les disques Windows restent verrouillés en double démarrage (Linux)."))
            .In(C, GroupEnergy)
            .Keywords(L("démarrage rapide, fast startup, hiberboot, arrêt, boot, dual boot"))
            .WhenOn(Reg.LmDword(SessionPower, "HiberbootEnabled", 1))
            .WhenOff(Reg.LmDword(SessionPower, "HiberbootEnabled", 0))
            .WindowsDefault(TweakDefinition.On)
            .Requires(Requires.When(_ => PowerApi.HibernationEnabled(),
                L("Nécessite la veille prolongée : activez-la d'abord (réglage ci-dessus).")))
            .RecommendWhen(p => p.SystemDiskIsHdd ? TweakDefinition.On : null)
            .Build();

        yield return Tweak.Toggle("perf.power.throttling", L("Limitation de l'énergie des tâches en arrière-plan"),
                L("Power Throttling : Windows fait tourner les applications en arrière-plan sur les cœurs et fréquences les plus économes. C'est ce qui préserve l'autonomie d'un portable. La désactiver ne sert qu'à un PC fixe sur secteur dont un traitement en arrière-plan (encodage, calcul) est anormalement ralenti."))
            .In(C, GroupEnergy)
            .Keywords(L("power throttling, ecoqos, limitation, arrière-plan, efficacité, autonomie"))
            .Tags("battery")
            .WhenOn(Reg.LmDel(PowerThrottling, "PowerThrottlingOff"))
            .WhenOff(Reg.LmDword(PowerThrottling, "PowerThrottlingOff", 1))
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning(L("Sur un portable, la désactiver réduit l'autonomie."))
            .RecommendWhen(p => p.HasBattery ? TweakDefinition.On : null)
            .Build();
    }

    // ================================================================== Arrière-plan et démarrage

    private static IEnumerable<TweakDefinition> Background()
    {
        yield return Tweak.Toggle("perf.bg.apps", L("Applications en arrière-plan"),
                L("Autorise les applications du Microsoft Store à s'exécuter en arrière-plan (réception de notifications, mises à jour de vignettes, synchronisation). Les bloquer économise un peu de mémoire et de batterie."))
            .In(C, GroupBackground)
            .Keywords(L("arrière-plan, background apps, applications en arrière-plan, batterie, notifications"))
            .Tags("battery", "lowend")
            .WhenOn(Reg.CuDword(BackgroundApps, "GlobalUserDisabled", 0))
            .WhenOff(Reg.CuDword(BackgroundApps, "GlobalUserDisabled", 1))
            .WindowsDefault(TweakDefinition.On)
            .Requires(Requires.Windows10Only)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Certaines applications (Courrier, Calendrier, Téléphone…) ne recevront plus de notifications tant qu'elles sont fermées."))
            .RecommendWhen(p => LowEnd(p) ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Toggle("perf.bg.apps.policy", L("Applications du Store en arrière-plan"),
                L("Stratégie « Autoriser les applications Windows à s'exécuter en arrière-plan » (Windows 11 n'a plus d'interrupteur global). Bloquée, aucune application du Microsoft Store ne tourne tant qu'elle n'est pas ouverte : un peu de mémoire et de batterie économisées. Les applications classiques (Win32) ne sont pas concernées."))
            .In(C, GroupBackground)
            .Keywords(L("arrière-plan, background apps, LetAppsRunInBackground, applications store, batterie"))
            .Tags("battery", "lowend")
            .Labels(L("Autorisées"), L("Bloquées"))
            .WhenOn(Reg.LmDel(AppPrivacyPolicy, "LetAppsRunInBackground"))
            .WhenOff(Reg.LmDword(AppPrivacyPolicy, "LetAppsRunInBackground", 2))
            .WindowsDefault(TweakDefinition.On)
            .Requires(Requires.Windows11)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Les applications du Store (Teams, WhatsApp, Téléphone, Courrier…) ne recevront plus de notifications tant qu'elles sont fermées."))
            .Build();

        yield return Tweak.Toggle("perf.boot.startupdelay", L("Délai avant les applications de démarrage"),
                L("Après l'ouverture de session, l'Explorateur attend une dizaine de secondes avant de lancer les programmes de démarrage, pour que le Bureau soit utilisable plus vite. Sans ce délai, ils démarrent immédiatement : intéressant sur SSD rapide, contre-productif sur un disque dur ou un PC modeste."))
            .In(C, GroupBackground)
            .Keywords(L("démarrage, startup delay, ouverture de session, programmes au démarrage, StartupDelayInMSec"))
            .Tags("office")
            .Labels(L("Délai actif"), L("Lancement immédiat"))
            .WhenOn(Reg.CuDel(Serialize, "StartupDelayInMSec"))
            .WhenOff(Reg.CuDword(Serialize, "StartupDelayInMSec", 0))
            .WindowsDefault(TweakDefinition.On)
            .RecommendWhen(p => !p.HardwareLoaded ? null
                : p.SystemDiskIsHdd || p.Tier == PerformanceTier.Low ? TweakDefinition.On
                : p.Tier == PerformanceTier.High && p.SystemDisk?.Media is DiskMedia.Ssd or DiskMedia.Nvme ? TweakDefinition.Off
                : null)
            .Build();
    }

    // ================================================================== Services

    private static IEnumerable<TweakDefinition> Services()
    {
        yield return Tweak.Toggle("perf.svc.sysmain", L("SysMain (préchargement des applications)"),
                L("Anciennement SuperFetch : garde en mémoire les applications que vous utilisez souvent pour les lancer plus vite. Indispensable sur disque dur. Sur SSD le gain est plus faible ; ne le désactivez que si vous constatez une activité disque anormale et durable."))
            .In(C, GroupServices)
            .Keywords(L("sysmain, superfetch, prefetch, préchargement, disque 100 %, service"))
            .Labels(L("Actif"), L("Désactivé"))
            .WhenOn(Sys.Service("SysMain", ServiceStartKind.Automatic))
            .WhenOff(Sys.Service("SysMain", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .RecommendWhen(p => p.SystemDiskIsHdd ? TweakDefinition.On : null)
            .Build();

        yield return Tweak.Toggle("perf.svc.wsearch", L("Indexation de la recherche Windows"),
                L("Service Windows Search : indexe le nom et le contenu des fichiers, courriers Outlook et paramètres pour des résultats instantanés. Désactivé, la recherche du menu Démarrer et de l'Explorateur fonctionne encore mais devient lente et ne cherche plus dans le contenu des documents."))
            .In(C, GroupServices)
            .Keywords(L("indexation, windows search, wsearch, recherche, indexer, service"))
            .Tags("lowend")
            .Labels(L("Actif"), L("Désactivé"))
            .WhenOn(Sys.Service("WSearch", ServiceStartKind.AutomaticDelayed))
            .WhenOff(Sys.Service("WSearch", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .Warning(L("La recherche dans Outlook, le menu Démarrer et l'Explorateur sera nettement plus lente."))
            .RecommendWhen(p => p.SystemDiskIsHdd && p.Tier == PerformanceTier.Low ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Toggle("perf.svc.xbox", L("Services Xbox"),
                L("Authentification Xbox Live, sauvegardes de jeux dans le cloud, réseau Xbox et accessoires Xbox (XblAuthManager, XblGameSave, XboxNetApiSvc, XboxGipSvc). Ils ne démarrent qu'à la demande : les désactiver ne fait gagner presque rien si vous ne jouez pas, mais casse le Game Pass et les jeux du Microsoft Store si vous jouez."))
            .In(C, GroupServices)
            .Keywords(L("xbox, xbox live, game pass, services xbox, sauvegarde jeux, manette xbox"))
            .Tags("office")
            .Labels(L("À la demande"), L("Désactivés"))
            .WhenOn(Sys.Service("XblAuthManager", ServiceStartKind.Manual), Sys.Service("XblGameSave", ServiceStartKind.Manual),
                Sys.Service("XboxNetApiSvc", ServiceStartKind.Manual), Sys.Service("XboxGipSvc", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("XblAuthManager", ServiceStartKind.Disabled), Sys.Service("XblGameSave", ServiceStartKind.Disabled),
                Sys.Service("XboxNetApiSvc", ServiceStartKind.Disabled), Sys.Service("XboxGipSvc", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .Warning(L("Game Pass, jeux du Microsoft Store, sauvegardes Xbox et mises à jour des manettes Xbox ne fonctionneront plus."))
            .Build();

        yield return Tweak.Toggle("perf.svc.maps", L("Gestionnaire des cartes téléchargées (démarrage automatique)"),
                L("Service MapsBroker : met à jour les cartes hors connexion de l'application Cartes. En démarrage manuel, il ne se lance que si une application en a besoin ; gain faible mais sans inconvénient."))
            .In(C, GroupServices)
            .Keywords(L("cartes, maps, mapsbroker, cartes hors connexion, service"))
            .Tags("lowend")
            .Labels(L("Automatique"), L("Manuel"))
            .WhenOn(Sys.Service("MapsBroker", ServiceStartKind.AutomaticDelayed))
            .WhenOff(Sys.Service("MapsBroker", ServiceStartKind.Manual))
            .WindowsDefault(TweakDefinition.On)
            .RecommendWhen(p => LowEnd(p) ? TweakDefinition.Off : null)
            .Build();

        yield return Tweak.Toggle("perf.svc.fax", L("Service de télécopie (Fax)"),
                L("Nécessaire uniquement pour envoyer ou recevoir des télécopies avec un modem fax. Il ne démarre qu'à la demande : le désactiver ne libère presque rien. Absent sur les installations récentes."))
            .In(C, GroupServices)
            .Keywords(L("fax, télécopie, service"))
            .Labels(L("À la demande"), L("Désactivé"))
            .WhenOn(Sys.Service("Fax", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("Fax", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("perf.svc.retaildemo", L("Service de démonstration en magasin"),
                L("Sert uniquement au mode « démo » des PC exposés en magasin. Inutile sur un PC personnel ; le désactiver est sans risque."))
            .In(C, GroupServices)
            .Keywords(L("retaildemo, démo magasin, retail demo, service"))
            .Labels(L("À la demande"), L("Désactivé"))
            .WhenOn(Sys.Service("RetailDemo", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("RetailDemo", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Build();

        yield return Tweak.Toggle("perf.svc.wmpnetwork", L("Partage réseau du Lecteur Windows Media"),
                L("Partage la bibliothèque de l'ancien Lecteur Windows Media avec d'autres appareils (DLNA). Il ne démarre qu'à la demande : le désactiver ne fait rien gagner tant que le partage n'est pas utilisé, mais réduit la surface réseau."))
            .In(C, GroupServices)
            .Keywords(L("wmpnetworksvc, windows media, dlna, partage multimédia, service"))
            .Labels(L("À la demande"), L("Désactivé"))
            .WhenOn(Sys.Service("WMPNetworkSvc", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("WMPNetworkSvc", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("perf.svc.wisvc", L("Service du programme Windows Insider"),
                L("Nécessaire uniquement pour recevoir les versions préliminaires de Windows (programme Insider). Démarre à la demande."))
            .In(C, GroupServices)
            .Keywords(L("insider, wisvc, préversion, service"))
            .Labels(L("À la demande"), L("Désactivé"))
            .WhenOn(Sys.Service("wisvc", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("wisvc", ServiceStartKind.Disabled))
            .WindowsDefault(TweakDefinition.On)
            .Warning(L("Le programme Windows Insider ne fonctionnera plus sur ce PC."))
            .Build();
    }

    // ================================================================== Stockage

    private static IEnumerable<TweakDefinition> Storage()
    {
        yield return Tweak.Choice("perf.storage.lastaccess", L("Horodatage du dernier accès NTFS"),
                L("NTFS peut noter la date du dernier accès à chaque fichier lu, ce qui ajoute des écritures. Depuis Windows 10 1803, Windows gère ce réglage seul (activé uniquement sur les petits volumes système) : n'y touchez que si un logiciel de sauvegarde ou d'archivage a besoin de cette date. Pris en compte au redémarrage."))
            .In(C, GroupStorage)
            .Keywords(L("ntfs, last access, dernier accès, horodatage, fsutil, disque"))
            .OptionWithHelp("system", L("Géré par Windows"), L("Valeur d'origine (0x80000002)."),
                Reg.LmDword(FileSystem, "NtfsDisableLastAccessUpdate", unchecked((int)0x80000002)))
            .OptionWithHelp("off", L("Toujours désactivé"), L("Aucune date de dernier accès n'est enregistrée (0x80000001)."),
                Reg.LmDword(FileSystem, "NtfsDisableLastAccessUpdate", unchecked((int)0x80000001)))
            .OptionWithHelp("on", L("Toujours activé"), L("Nécessaire à certains outils d'archivage (0x80000000)."),
                Reg.LmDword(FileSystem, "NtfsDisableLastAccessUpdate", unchecked((int)0x80000000)))
            .Detect(() => RegistryAccess.ReadDword(RegHive.LocalMachine, FileSystem, "NtfsDisableLastAccessUpdate") switch
            {
                null => "system",
                int v when (v & 2) != 0 => "system",
                int v when (v & 1) != 0 => "off",
                _ => "on",
            })
            .WindowsDefault("system")
            .Risk(RiskLevel.Advanced)
            .Effect(ApplyEffect.Reboot)
            .Build();

        yield return Tweak.Toggle("perf.storage.trim", L("TRIM des SSD"),
                L("Windows signale au SSD les blocs libérés (fsutil behavior DisableDeleteNotify). Indispensable pour conserver les performances et la durée de vie d'un SSD ; activé par défaut. À ne désactiver que sur demande d'un fabricant."))
            .In(C, GroupStorage)
            .Keywords(L("trim, ssd, nvme, DisableDeleteNotify, optimisation ssd, fsutil"))
            .WhenOn(Reg.LmDword(FileSystem, "DisableDeleteNotification", 0))
            .WhenOff(Reg.LmDword(FileSystem, "DisableDeleteNotification", 1))
            .WindowsDefault(TweakDefinition.On)
            .Requires(Requires.When(p => !p.HardwareLoaded || p.Disks.Count == 0 || p.Disks.Any(d => d.Media is DiskMedia.Ssd or DiskMedia.Nvme),
                L("Aucun SSD détecté sur ce PC.")))
            .Risk(RiskLevel.Moderate)
            .Warning(L("Sans TRIM, un SSD ralentit progressivement et s'use davantage."))
            .Recommend(TweakDefinition.On)
            .Effect(ApplyEffect.Reboot)
            .Build();

        yield return Tweak.Toggle("perf.storage.thumbnails", L("Miniatures des fichiers dans l'Explorateur"),
                L("Aperçu des images, vidéos et documents à la place des icônes. Désactivées, l'Explorateur ouvre plus vite les dossiers contenant beaucoup de photos, surtout sur disque dur ou clé USB lente."))
            .In(C, GroupStorage)
            .Keywords(L("miniatures, thumbnails, aperçu, icônes, explorateur, photos"))
            .Tags("lowend")
            .WhenOn(Reg.CuDword(ExplorerAdvanced, "IconsOnly", 0))
            .WhenOff(Reg.CuDword(ExplorerAdvanced, "IconsOnly", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();
    }
}
