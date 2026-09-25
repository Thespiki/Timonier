using System.Globalization;
using Microsoft.Win32;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Customization;

/// <summary>
/// Catalogue des réglages de personnalisation. Chaque valeur de registre correspond à ce qu'écrit l'interface de
/// Windows (Paramètres, Options des dossiers, Panneau de configuration) ou à une stratégie de groupe documentée.
/// Les rares astuces non documentées par Microsoft (menu contextuel classique, suffixe des raccourcis, volet de
/// navigation) sont signalées comme telles dans leur description.
/// </summary>
internal static class CustomizationTweaks
{
    private const string C = CustomizationModule.Category;

    // Groupes (sous-titres de la page, dans cet ordre).
    public const string GroupColors = "Couleurs";
    public const string GroupTaskbar = "Barre des tâches";
    public const string GroupStart = "Menu Démarrer";
    public const string GroupExplorer = "Explorateur de fichiers";
    public const string GroupDesktop = "Bureau et fenêtres";
    public const string GroupLogon = "Connexion et verrouillage";
    public const string GroupInput = "Souris et clavier";

    // Clés de registre (HKCU sauf mention contraire).
    internal const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";
    private const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string TaskbarDev = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings";
    private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string CabinetState = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState";
    private const string NamingTemplates = @"Software\Microsoft\Windows\CurrentVersion\Explorer\NamingTemplates";
    private const string DesktopIcons = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    internal const string DesktopKey = @"Control Panel\Desktop";
    private const string MouseKey = @"Control Panel\Mouse";
    private const string StickyKeys = @"Control Panel\Accessibility\StickyKeys";
    private const string FilterKeys = @"Control Panel\Accessibility\Keyboard Response";
    private const string ToggleKeys = @"Control Panel\Accessibility\ToggleKeys";
    private const string ClassicMenuClsid = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string GalleryClsid = @"Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}";
    private const string HomeClsid = @"Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}";
    private const string PinnedToNavPane = "System.IsPinnedToNameSpaceTree";

    // HKLM / HKU\.DEFAULT (admin).
    private const string ExplorerPolicy = @"SOFTWARE\Policies\Microsoft\Windows\Explorer";
    private const string SystemPolicy = @"SOFTWARE\Policies\Microsoft\Windows\System";
    private const string SystemPoliciesLegacy = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string BootAnimation = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation";
    private const string DefaultKeyboard = @"Control Panel\Keyboard";

    private const string On = TweakDefinition.On;
    private const string Off = TweakDefinition.Off;

    /// <summary>Notification envoyée après un changement de barre des tâches (rafraîchit Explorer quand il le permet).</summary>
    private static BroadcastSettingChangeOp Tray => Sys.Broadcast("TraySettings");
    private static BroadcastSettingChangeOp Colors => Sys.Broadcast("ImmersiveColorSet");

    public static IEnumerable<TweakDefinition> All() =>
    [
        .. ColorTweaks(),
        .. TaskbarTweaks(),
        .. StartTweaks(),
        .. ExplorerTweaks(),
        .. DesktopTweaks(),
        .. LogonTweaks(),
        .. InputTweaks(),
    ];

    /// <summary>Réglages affichés dans la section « Apparence » de la page (et non dans la liste principale).</summary>
    public static readonly string[] AppearanceIds =
    [
        "custom.theme.apps", "custom.theme.system", "custom.colors.autoaccent",
        "custom.colors.accentstart", "custom.colors.accenttitlebars", "custom.colors.transparency",
    ];

    // =====================================================================================================
    // Couleurs
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> ColorTweaks()
    {
        yield return Tweak.Choice("custom.theme.apps", "Mode des applications",
                "Clair ou sombre pour les applications : Paramètres, Explorateur de fichiers, applications modernes. " +
                "Certains logiciels plus anciens ignorent ce réglage.")
            .In(C, GroupColors)
            .Keywords("theme", "sombre", "clair", "dark mode", "light mode", "mode nuit", "applications")
            .Option("light", "Clair", Reg.CuDword(Personalize, "AppsUseLightTheme", 1), Colors)
            .Option("dark", "Sombre", Reg.CuDword(Personalize, "AppsUseLightTheme", 0), Colors)
            .WindowsDefault("light")
            .Build();

        yield return Tweak.Choice("custom.theme.system", "Mode de Windows",
                "Couleurs de la barre des tâches, du menu Démarrer et du centre de notifications.")
            .In(C, GroupColors)
            .Keywords("theme", "sombre", "clair", "barre des taches", "dark mode", "menu demarrer")
            .Option("light", "Clair", Reg.CuDword(Personalize, "SystemUsesLightTheme", 1), Colors)
            .Option("dark", "Sombre", Reg.CuDword(Personalize, "SystemUsesLightTheme", 0), Colors)
            // Absent du registre : Windows 11 est clair par défaut, Windows 10 sombre.
            .Detect(() => RegistryAccess.ReadDword(RegHive.CurrentUser, Personalize, "SystemUsesLightTheme") switch
            {
                1 => "light",
                0 => "dark",
                _ => OsInfo.Build >= 22000 ? "light" : "dark",
            })
            .Build();

        yield return Tweak.Toggle("custom.colors.autoaccent", "Couleur d'accent automatique",
                "Windows choisit la couleur d'accent d'après votre fond d'écran et la fait évoluer avec lui. " +
                "La couleur est recalculée au prochain changement de fond d'écran (ou à la prochaine ouverture de session).")
            .In(C, GroupColors)
            .Keywords("accent", "couleur", "fond d'ecran", "automatique", "accent color", "wallpaper")
            .WhenOn(Reg.CuDword(DesktopKey, "AutoColorization", 1), Colors)
            .WhenOff(Reg.CuDword(DesktopKey, "AutoColorization", 0), Colors)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.colors.accentstart", "Couleur d'accent sur Démarrer et la barre des tâches",
                "Colore le menu Démarrer, la barre des tâches et le centre de notifications avec la couleur d'accent. " +
                "Sous Windows 11, n'a d'effet que si le mode de Windows est sombre.")
            .In(C, GroupColors)
            .Keywords("accent", "couleur", "barre des taches", "menu demarrer", "taskbar color")
            .WhenOn(Reg.CuDword(Personalize, "ColorPrevalence", 1), Colors)
            .WhenOff(Reg.CuDword(Personalize, "ColorPrevalence", 0), Colors)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.colors.accenttitlebars", "Couleur d'accent sur les barres de titre",
                "Affiche la couleur d'accent sur la barre de titre et la bordure des fenêtres. " +
                "Si le changement n'apparaît pas tout de suite, il sera visible à la prochaine ouverture de session.")
            .In(C, GroupColors)
            .Keywords("accent", "barre de titre", "bordure", "fenetre", "title bar", "border")
            .WhenOn(Reg.CuDword(Dwm, "ColorPrevalence", 1), Colors)
            .WhenOff(Reg.CuDword(Dwm, "ColorPrevalence", 0), Colors)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.colors.transparency", "Effets de transparence",
                "Effet translucide (Mica, acrylique) de la barre des tâches, du menu Démarrer et de certaines fenêtres. " +
                "Le désactiver soulage légèrement les PC modestes.")
            .In(C, GroupColors)
            .Keywords("transparence", "transparency", "acrylique", "mica", "flou", "blur", "translucide")
            .Tags("lowend", "battery")
            .WhenOn(Reg.CuDword(Personalize, "EnableTransparency", 1), Colors)
            .WhenOff(Reg.CuDword(Personalize, "EnableTransparency", 0), Colors)
            .WindowsDefault(On)
            .RecommendWhen(p => p.HardwareLoaded && p.Tier == PerformanceTier.Low ? Off : null)
            .Build();
    }

    // =====================================================================================================
    // Barre des tâches
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> TaskbarTweaks()
    {
        yield return Tweak.Choice("custom.taskbar.alignment", "Alignement de la barre des tâches",
                "Position du bouton Démarrer et des applications épinglées : au centre (Windows 11) ou à gauche comme sous Windows 10.")
            .In(C, GroupTaskbar)
            .Keywords("alignement", "gauche", "centre", "icones", "taskbar alignment", "left", "center", "bouton demarrer")
            .Requires(Requires.Windows11)
            .Option("left", "À gauche", Reg.CuDword(Adv, "TaskbarAl", 0), Tray)
            .Option("center", "Au centre", Reg.CuDword(Adv, "TaskbarAl", 1), Tray)
            .WindowsDefault("center")
            .Build();

        yield return Tweak.Choice("custom.taskbar.search", "Recherche dans la barre des tâches",
                "Forme de l'accès à la recherche Windows sur la barre des tâches. Même masquée, la recherche reste " +
                "accessible : appuyez sur la touche Windows puis tapez votre recherche.")
            .In(C, GroupTaskbar)
            .Keywords("recherche", "loupe", "zone de recherche", "search box", "search icon", "masquer recherche")
            .Requires(Requires.Windows11_22H2)
            .Option("hidden", "Masquée", Reg.CuDword(Search, "SearchboxTaskbarMode", 0), Tray)
            .Option("icon", "Icône seule", Reg.CuDword(Search, "SearchboxTaskbarMode", 1), Tray)
            .Option("iconlabel", "Icône et libellé", Reg.CuDword(Search, "SearchboxTaskbarMode", 3), Tray)
            .Option("box", "Zone de recherche", Reg.CuDword(Search, "SearchboxTaskbarMode", 2), Tray)
            .WindowsDefault("box")
            .Build();

        yield return Tweak.Choice("custom.taskbar.search.win10", "Recherche dans la barre des tâches (Windows 10)",
                "Forme de l'accès à la recherche Windows sur la barre des tâches de Windows 10. Même masquée, la recherche " +
                "reste accessible : appuyez sur la touche Windows puis tapez votre recherche.")
            .In(C, GroupTaskbar)
            .Keywords("recherche", "loupe", "zone de recherche", "search box", "cortana")
            .Requires(Requires.Windows10Only)
            .Option("hidden", "Masquée", Reg.CuDword(Search, "SearchboxTaskbarMode", 0), Tray)
            .Option("icon", "Icône seule", Reg.CuDword(Search, "SearchboxTaskbarMode", 1), Tray)
            .Option("box", "Zone de recherche", Reg.CuDword(Search, "SearchboxTaskbarMode", 2), Tray)
            .WindowsDefault("box")
            .Build();

        yield return Tweak.Toggle("custom.taskbar.taskview", "Bouton Vue des tâches",
                "Bouton qui affiche les fenêtres ouvertes et les bureaux virtuels. Le raccourci Windows + Tab reste disponible.")
            .In(C, GroupTaskbar)
            .Keywords("vue des taches", "task view", "bureaux virtuels", "virtual desktop", "timeline")
            .WhenOn(Reg.CuDword(Adv, "ShowTaskViewButton", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "ShowTaskViewButton", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.widgets", "Bouton Widgets",
                "Bouton météo et actualités de la barre des tâches (seul le bouton est concerné : Windows + W reste disponible). " +
                "Sur certaines versions récentes, Windows protège ce réglage et peut refuser qu'une autre application que " +
                "Paramètres le modifie : utilisez alors Paramètres › Personnalisation › Barre des tâches.")
            .In(C, GroupTaskbar)
            .Keywords("widgets", "meteo", "actualites", "news", "weather", "bouton widgets")
            .Tags("family", "office")
            .Requires(Requires.Windows11)
            .WhenOn(Reg.CuDword(Adv, "TaskbarDa", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarDa", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.chat", "Bouton Conversation (Microsoft Teams)",
                "Bouton Conversation de Windows 11 21H2 et 22H2, qui ouvre la version personnelle de Microsoft Teams.")
            .In(C, GroupTaskbar)
            .Keywords("chat", "conversation", "teams", "bouton teams", "discussion")
            .Tags("family", "office")
            .Requires(Requires.When(p => p.Build is >= 22000 and < 22631,
                "Le bouton Conversation n'existe que sur Windows 11 21H2 et 22H2."))
            .WhenOn(Reg.CuDword(Adv, "TaskbarMn", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarMn", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.copilot", "Bouton Copilot",
                "Bouton Copilot de la barre des tâches de Windows 11 22H2 et 23H2. Seul le bouton est concerné : " +
                "la désactivation de Copilot lui-même se trouve dans la page Confidentialité.")
            .In(C, GroupTaskbar)
            .Keywords("copilot", "ia", "assistant", "bouton copilot", "ai")
            .Requires(Requires.When(p => p.Build is >= 22621 and < 26100,
                "Depuis Windows 11 24H2, Copilot est une application : détachez-la de la barre des tâches."))
            .WhenOn(Reg.CuDword(Adv, "ShowCopilotButton", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "ShowCopilotButton", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.endtask", "« Fin de tâche » au clic droit",
                "Ajoute « Fin de tâche » au menu contextuel des applications de la barre des tâches, pour fermer de force " +
                "une application bloquée sans ouvrir le Gestionnaire des tâches. Les données non enregistrées sont perdues.")
            .In(C, GroupTaskbar)
            .Keywords("fin de tache", "end task", "forcer fermeture", "application bloquee", "kill", "tuer")
            .Tags("dev", "office")
            .Requires(Requires.When(p => p.Build >= 22631, "Nécessite Windows 11 23H2 ou plus récent."))
            .WhenOn(Reg.CuDword(TaskbarDev, "TaskbarEndTask", 1), Tray)
            .WhenOff(Reg.CuDword(TaskbarDev, "TaskbarEndTask", 0), Tray)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.seconds", "Secondes dans l'horloge",
                "Affiche les secondes dans l'horloge de la barre des tâches. Microsoft indique que cela consomme un peu plus d'énergie.")
            .In(C, GroupTaskbar)
            .Keywords("secondes", "horloge", "heure", "clock", "seconds")
            .Tags("dev")
            .Requires(Requires.When(p => p.Build < 22000 || p.Build >= 22621,
                "Sur Windows 11, l'affichage des secondes nécessite la version 22H2 ou plus récente."))
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(Adv, "ShowSecondsInSystemClock", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "ShowSecondsInSystemClock", 0), Tray)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Choice("custom.taskbar.combine", "Regroupement des boutons",
                "Regroupe les fenêtres d'une même application sous un seul bouton et masque ou non leur nom.")
            .In(C, GroupTaskbar)
            .Keywords("regrouper", "combiner", "libelles", "etiquettes", "combine", "labels", "never combine", "jamais")
            .Tags("office")
            .Requires(Requires.When(p => p.Build < 22000 || p.Build >= 22631,
                "Sous Windows 11, ce choix n'est disponible qu'à partir de la version 23H2."))
            .Option("always", "Toujours, sans libellés", Reg.CuDword(Adv, "TaskbarGlomLevel", 0), Tray)
            .Option("whenfull", "Quand la barre est pleine", Reg.CuDword(Adv, "TaskbarGlomLevel", 1), Tray)
            .Option("never", "Jamais", Reg.CuDword(Adv, "TaskbarGlomLevel", 2), Tray)
            .WindowsDefault("always")
            .Build();

        yield return Tweak.Toggle("custom.taskbar.badges", "Badges sur les applications",
                "Pastilles de notification (messages non lus, etc.) sur les icônes de la barre des tâches.")
            .In(C, GroupTaskbar)
            .Keywords("badge", "pastille", "compteur", "non lus", "notification")
            .WhenOn(Reg.CuDword(Adv, "TaskbarBadges", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarBadges", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.flashing", "Clignotement des applications",
                "Une application qui demande votre attention fait clignoter son bouton dans la barre des tâches.")
            .In(C, GroupTaskbar)
            .Keywords("clignoter", "clignotement", "flash", "flashing", "attention")
            .Requires(Requires.Windows11_22H2)
            .WhenOn(Reg.CuDword(Adv, "TaskbarFlashing", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarFlashing", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.multimonitor", "Barre des tâches sur tous les écrans",
                "Avec plusieurs écrans, affiche une barre des tâches sur chacun d'eux (sinon seulement sur l'écran principal).")
            .In(C, GroupTaskbar)
            .Keywords("plusieurs ecrans", "multi ecran", "second ecran", "multiple displays", "dual screen", "moniteur")
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(Adv, "MMTaskbarEnabled", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "MMTaskbarEnabled", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Choice("custom.taskbar.multimonitor.buttons", "Boutons des fenêtres avec plusieurs écrans",
                "Barres des tâches sur lesquelles apparaissent les boutons des fenêtres ouvertes, quand la barre est affichée sur tous les écrans.")
            .In(C, GroupTaskbar)
            .Keywords("plusieurs ecrans", "multi ecran", "second ecran", "boutons", "multiple displays")
            .Effect(ApplyEffect.RestartExplorer)
            .Option("all", "Sur toutes les barres", Reg.CuDword(Adv, "MMTaskbarMode", 0), Tray)
            .Option("mainandwhere", "Barre principale et écran de la fenêtre", Reg.CuDword(Adv, "MMTaskbarMode", 1), Tray)
            .Option("where", "Écran de la fenêtre uniquement", Reg.CuDword(Adv, "MMTaskbarMode", 2), Tray)
            .WindowsDefault("all")
            .Build();

        yield return Tweak.Toggle("custom.taskbar.showdesktop", "Coin « Afficher le bureau »",
                "Cliquer à l'extrémité droite de la barre des tâches réduit toutes les fenêtres pour afficher le bureau.")
            .In(C, GroupTaskbar)
            .Keywords("afficher le bureau", "show desktop", "coin", "reduire tout", "peek")
            .Requires(Requires.Windows11_22H2)
            .WhenOn(Reg.CuDword(Adv, "TaskbarSd", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarSd", 0), Tray)
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.taskbar.smallicons", "Petits boutons de la barre des tâches",
                "Réduit la hauteur de la barre des tâches de Windows 10 en utilisant de petites icônes.")
            .In(C, GroupTaskbar)
            .Keywords("petites icones", "small icons", "taille", "hauteur", "compacte")
            .Requires(Requires.Windows10Only)
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(Adv, "TaskbarSmallIcons", 1), Tray)
            .WhenOff(Reg.CuDword(Adv, "TaskbarSmallIcons", 0), Tray)
            .WindowsDefault(Off)
            .Build();
    }

    // =====================================================================================================
    // Menu Démarrer
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> StartTweaks()
    {
        yield return Tweak.Choice("custom.start.layout", "Disposition du menu Démarrer",
                "Répartition de l'espace entre les applications épinglées et la section « Recommandé ». " +
                "Peut rester sans effet sur le menu Démarrer remanié diffusé fin 2025, qui n'a plus ce choix.")
            .In(C, GroupStart)
            .Keywords("menu demarrer", "epingles", "recommande", "disposition", "start layout", "more pins")
            .Requires(Requires.Windows11_22H2)
            .Effect(ApplyEffect.SignOut)
            .Option("default", "Par défaut", Reg.CuDword(Adv, "Start_Layout", 0))
            .Option("pins", "Plus d'épingles", Reg.CuDword(Adv, "Start_Layout", 1))
            .Option("recommendations", "Plus de recommandations", Reg.CuDword(Adv, "Start_Layout", 2))
            .WindowsDefault("default")
            .Build();

        yield return Tweak.Choice("custom.start.mostused", "Liste « Plus utilisées » de Démarrer",
                "Impose l'affichage ou le masquage des applications les plus utilisées dans le menu Démarrer, pour tous " +
                "les utilisateurs du PC (stratégie Windows). « Selon les Paramètres » laisse chacun choisir.")
            .In(C, GroupStart)
            .Keywords("plus utilisees", "most used", "applications frequentes", "menu demarrer", "liste")
            .Requires(Requires.Windows11_22H2)
            .Effect(ApplyEffect.SignOut)
            .Option("user", "Selon les Paramètres", Reg.LmDel(ExplorerPolicy, "ShowOrHideMostUsedApps"))
            .Option("show", "Toujours affichée", Reg.LmDword(ExplorerPolicy, "ShowOrHideMostUsedApps", 1))
            .Option("hide", "Toujours masquée", Reg.LmDword(ExplorerPolicy, "ShowOrHideMostUsedApps", 2))
            .WindowsDefault("user")
            .Build();

        yield return Tweak.Toggle("custom.start.recentlyadded", "Applications récemment ajoutées dans Démarrer",
                "Liste des applications installées récemment dans le menu Démarrer. « Masquées » l'impose pour tous les " +
                "utilisateurs (stratégie Windows) ; « Autorisées » laisse le choix dans Paramètres.")
            .In(C, GroupStart)
            .Keywords("recemment ajoutees", "recently added", "nouvelles applications", "menu demarrer")
            .Labels("Autorisées", "Masquées")
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.LmDel(ExplorerPolicy, "HideRecentlyAddedApps"))
            .WhenOff(Reg.LmDword(ExplorerPolicy, "HideRecentlyAddedApps", 1))
            .WindowsDefault(On)
            .Build();
    }

    // =====================================================================================================
    // Explorateur de fichiers
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> ExplorerTweaks()
    {
        yield return Tweak.Toggle("custom.explorer.extensions", "Afficher les extensions de fichiers",
                "Montre « .pdf », « .exe »… à la fin des noms de fichiers. Recommandé : aide à repérer les fichiers piégés (ex. « facture.pdf.exe »).")
            .In(C, GroupExplorer)
            .Keywords("extension", "fichier", "exe", "type de fichier")
            .Tags("security", "office")
            .WhenOn(Reg.CuDword(Adv, "HideFileExt", 0))
            .WhenOff(Reg.CuDword(Adv, "HideFileExt", 1))
            .WindowsDefault(Off)
            .Recommend(On)
            .Effect(ApplyEffect.RestartExplorer)
            .Build();

        yield return Tweak.Toggle("custom.explorer.hidden", "Fichiers et dossiers cachés",
                "Affiche les éléments marqués « caché » (AppData, dossiers de configuration…), en semi-transparence. " +
                "Appuyez sur F5 dans les fenêtres déjà ouvertes.")
            .In(C, GroupExplorer)
            .Keywords("fichiers caches", "hidden files", "appdata", "masques", "invisibles")
            .Tags("dev")
            .WhenOn(Reg.CuDword(Adv, "Hidden", 1))
            .WhenOff(Reg.CuDword(Adv, "Hidden", 2))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.explorer.superhidden", "Fichiers protégés du système",
                "Affiche les fichiers système protégés (desktop.ini, pagefile.sys…). Inutile au quotidien. " +
                "Appuyez sur F5 dans les fenêtres déjà ouvertes.")
            .In(C, GroupExplorer)
            .Keywords("fichiers systeme", "proteges", "super hidden", "desktop.ini", "systeme")
            .Tags("dev")
            .Risk(RiskLevel.Advanced)
            .Warning("Supprimer ou modifier ces fichiers peut empêcher Windows de fonctionner ou de démarrer.")
            .WhenOn(Reg.CuDword(Adv, "ShowSuperHidden", 1))
            .WhenOff(Reg.CuDword(Adv, "ShowSuperHidden", 0))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Choice("custom.explorer.launchto", "Ouvrir l'Explorateur sur",
                "Page affichée à l'ouverture de l'Explorateur de fichiers (Windows + E).")
            .In(C, GroupExplorer)
            .Keywords("ce pc", "this pc", "acces rapide", "accueil", "quick access", "home", "ouverture", "poste de travail")
            .Tags("office")
            .Option("home", "Accueil (Accès rapide)", Reg.CuDword(Adv, "LaunchTo", 2))
            .Option("thispc", "Ce PC", Reg.CuDword(Adv, "LaunchTo", 1))
            .WindowsDefault("home")
            .Build();

        yield return Tweak.Toggle("custom.explorer.compact", "Affichage compact",
                "Réduit l'espacement entre les éléments de l'Explorateur, que Windows 11 agrandit pour le tactile. " +
                "S'applique aux nouvelles fenêtres.")
            .In(C, GroupExplorer)
            .Keywords("compact", "espacement", "densite", "compact view", "padding")
            .Requires(Requires.Windows11)
            .WhenOn(Reg.CuDword(Adv, "UseCompactMode", 1))
            .WhenOff(Reg.CuDword(Adv, "UseCompactMode", 0))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.explorer.checkboxes", "Cases à cocher des éléments",
                "Ajoute une case à cocher sur chaque fichier pour en sélectionner plusieurs sans maintenir Ctrl : " +
                "pratique au doigt ou au pavé tactile.")
            .In(C, GroupExplorer)
            .Keywords("cases a cocher", "checkbox", "selection", "tactile", "selectionner")
            .WhenOn(Reg.CuDword(Adv, "AutoCheckSelect", 1))
            .WhenOff(Reg.CuDword(Adv, "AutoCheckSelect", 0))
            .WindowsDefault(Off)
            .RecommendWhen(p => p.HardwareLoaded && p.HasTouch ? On : null)
            .Build();

        yield return Tweak.Toggle("custom.explorer.fullpath", "Chemin complet dans la barre de titre",
                "Affiche le chemin complet du dossier (ex. C:\\Users\\…\\Documents) dans le titre de la fenêtre ou de l'onglet.")
            .In(C, GroupExplorer)
            .Keywords("chemin complet", "full path", "barre de titre", "adresse", "titre")
            .Tags("dev", "office")
            .WhenOn(Reg.CuDword(CabinetState, "FullPath", 1))
            .WhenOff(Reg.CuDword(CabinetState, "FullPath", 0))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.explorer.syncnotifications", "Notifications des services de synchronisation",
                "Messages de OneDrive ou d'autres services de synchronisation affichés dans l'Explorateur, " +
                "souvent des suggestions d'abonnement ou de fonctionnalités.")
            .In(C, GroupExplorer)
            .Keywords("onedrive", "notifications", "synchronisation", "sync provider", "publicite explorateur")
            .WhenOn(Reg.CuDword(Adv, "ShowSyncProviderNotifications", 1))
            .WhenOff(Reg.CuDword(Adv, "ShowSyncProviderNotifications", 0))
            .WindowsDefault(On)
            .Recommend(Off)
            .Build();

        yield return Tweak.Toggle("custom.explorer.classicmenu", "Menu contextuel complet (style Windows 10)",
                "Le clic droit affiche directement toutes les options, sans passer par « Afficher plus d'options » (Maj + F10). " +
                "Astuce non documentée par Microsoft mais très répandue : une mise à jour de Windows pourrait la rendre inopérante.")
            .In(C, GroupExplorer)
            .Keywords("menu contextuel", "clic droit", "afficher plus d'options", "classic context menu", "ancien menu", "windows 10")
            .Tags("office")
            .Requires(Requires.Windows11)
            .Effect(ApplyEffect.RestartExplorer)
            // La clé est d'abord remise à zéro : l'annulation supprime ainsi toute la clé CLSID (sinon une clé vide resterait).
            .WhenOn(Reg.CuDelKey(ClassicMenuClsid), Reg.Cu(ClassicMenuClsid + @"\InprocServer32", "", RegistryValueKind.String, ""))
            .WhenOff(Reg.CuDelKey(ClassicMenuClsid))
            .Detect(() => RegistryAccess.KeyExists(RegHive.CurrentUser, ClassicMenuClsid + @"\InprocServer32") ? On : Off)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.explorer.gallery", "Galerie dans le volet de navigation",
                "Raccourci « Galerie » (vue chronologique de vos photos) dans la colonne de gauche de l'Explorateur. " +
                "Méthode non documentée par Microsoft, sans risque et annulable.")
            .In(C, GroupExplorer)
            .Keywords("galerie", "gallery", "volet de navigation", "navigation pane", "photos")
            .Requires(Requires.When(p => p.Build >= 22631, "La Galerie n'existe qu'à partir de Windows 11 23H2."))
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDelKey(GalleryClsid))
            .WhenOff(Reg.CuDelKey(GalleryClsid), Reg.CuDword(GalleryClsid, PinnedToNavPane, 0))
            .Detect(() => NavPaneDetect(GalleryClsid))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.explorer.home", "Accueil dans le volet de navigation",
                "Raccourci « Accueil » (fichiers récents et favoris) dans la colonne de gauche de l'Explorateur. " +
                "Si vous le masquez, choisissez « Ce PC » comme page d'ouverture. Méthode non documentée par Microsoft, annulable.")
            .In(C, GroupExplorer)
            .Keywords("accueil", "home", "acces rapide", "quick access", "volet de navigation", "navigation pane")
            .Requires(Requires.Windows11_22H2)
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDelKey(HomeClsid))
            .WhenOff(Reg.CuDelKey(HomeClsid), Reg.CuDword(HomeClsid, PinnedToNavPane, 0))
            .Detect(() => NavPaneDetect(HomeClsid))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Choice("custom.explorer.driveletters", "Lettres des lecteurs",
                "Position de la lettre (C:, D:…) dans le nom des lecteurs affichés par l'Explorateur.")
            .In(C, GroupExplorer)
            .Keywords("lettre de lecteur", "drive letter", "c:", "disque", "lecteur")
            .Effect(ApplyEffect.RestartExplorer)
            .Option("after", "Après le nom", Reg.CuDel(Explorer, "ShowDriveLettersFirst"))
            .Option("before", "Avant le nom", Reg.CuDword(Explorer, "ShowDriveLettersFirst", 4))
            .Option("network", "Avant le nom (réseau uniquement)", Reg.CuDword(Explorer, "ShowDriveLettersFirst", 1))
            .Option("hidden", "Masquées", Reg.CuDword(Explorer, "ShowDriveLettersFirst", 2))
            .WindowsDefault("after")
            .Build();

        yield return Tweak.Toggle("custom.explorer.shortcutsuffix", "Suffixe « - Raccourci » des nouveaux raccourcis",
                "Windows ajoute « - Raccourci » au nom des raccourcis que vous créez. Désactivé, le raccourci porte simplement " +
                "le nom de l'élément. Les raccourcis existants ne sont pas renommés.")
            .In(C, GroupExplorer)
            .Keywords("raccourci", "suffixe", "shortcut", "- raccourci", "nom du raccourci")
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDel(NamingTemplates, "ShortcutNameTemplate"))
            .WhenOff(Reg.CuString(NamingTemplates, "ShortcutNameTemplate", "\"%s.lnk\""))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.explorer.statusbar", "Barre d'état",
                "Ligne en bas des fenêtres de l'Explorateur indiquant le nombre d'éléments et la taille de la sélection.")
            .In(C, GroupExplorer)
            .Keywords("barre d'etat", "status bar", "nombre d'elements", "selection")
            .WhenOn(Reg.CuDword(Adv, "ShowStatusBar", 1))
            .WhenOff(Reg.CuDword(Adv, "ShowStatusBar", 0))
            .WindowsDefault(On)
            .Build();
    }

    // =====================================================================================================
    // Bureau et fenêtres
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> DesktopTweaks()
    {
        yield return DesktopIcon("custom.desktop.thispc", "Icône « Ce PC » sur le bureau",
            "Raccourci vers les lecteurs et les dossiers de l'ordinateur.",
            "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", visibleByDefault: false, "ce pc", "poste de travail", "this pc", "ordinateur");
        yield return DesktopIcon("custom.desktop.recyclebin", "Icône « Corbeille » sur le bureau",
            "Accès à la Corbeille depuis le bureau (elle reste accessible dans l'Explorateur).",
            "{645FF040-5081-101B-9F08-00AA002F954E}", visibleByDefault: true, "corbeille", "recycle bin", "poubelle");
        yield return DesktopIcon("custom.desktop.userfolder", "Icône du dossier personnel sur le bureau",
            "Raccourci vers votre dossier utilisateur (Documents, Images, Téléchargements…).",
            "{59031a47-3f72-44a7-89c5-5595fe6b30ee}", visibleByDefault: false, "dossier utilisateur", "fichiers de l'utilisateur", "user folder", "profil");
        yield return DesktopIcon("custom.desktop.network", "Icône « Réseau » sur le bureau",
            "Raccourci vers les ordinateurs et appareils partagés du réseau local.",
            "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", visibleByDefault: false, "reseau", "network", "voisinage reseau");
        yield return DesktopIcon("custom.desktop.controlpanel", "Icône « Panneau de configuration » sur le bureau",
            "Raccourci vers le Panneau de configuration classique.",
            "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", visibleByDefault: false, "panneau de configuration", "control panel");

        yield return Tweak.Toggle("custom.windows.snap", "Ancrage des fenêtres (Snap)",
                "Faire glisser une fenêtre vers un bord ou un coin de l'écran la redimensionne (moitié, quart de l'écran). " +
                "Désactivé, les dispositions d'ancrage et l'assistance à l'ancrage sont aussi indisponibles.")
            .In(C, GroupDesktop)
            .Keywords("ancrage", "snap", "aero snap", "cote a cote", "redimensionner", "moitie ecran")
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.CuString(DesktopKey, "WindowArrangementActive", "1"))
            .WhenOff(Reg.CuString(DesktopKey, "WindowArrangementActive", "0"))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.windows.snaplayouts", "Dispositions d'ancrage au survol",
                "Survoler le bouton Agrandir d'une fenêtre propose des dispositions (deux colonnes, grille…). " +
                "Le raccourci Windows + Z reste disponible.")
            .In(C, GroupDesktop)
            .Keywords("snap layouts", "dispositions", "agrandir", "survol", "ancrage", "grille")
            .Requires(Requires.Windows11)
            .WhenOn(Reg.CuDword(Adv, "EnableSnapAssistFlyout", 1))
            .WhenOff(Reg.CuDword(Adv, "EnableSnapAssistFlyout", 0))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.windows.snapassist", "Assistance à l'ancrage",
                "Après avoir ancré une fenêtre, Windows propose vos autres fenêtres ouvertes pour remplir l'espace restant.")
            .In(C, GroupDesktop)
            .Keywords("snap assist", "assistance", "ancrage", "suggestions fenetres")
            .WhenOn(Reg.CuDword(Adv, "SnapAssist", 1))
            .WhenOff(Reg.CuDword(Adv, "SnapAssist", 0))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.windows.shake", "Secouer pour réduire les autres fenêtres",
                "Secouer la barre de titre d'une fenêtre réduit toutes les autres (« Aero Shake »). " +
                "Désactivé par défaut sous Windows 11, activé sous Windows 10.")
            .In(C, GroupDesktop)
            .Keywords("secouer", "aero shake", "shake", "reduire fenetres", "barre de titre")
            .WhenOn(Reg.CuDword(Adv, "DisallowShaking", 0))
            .WhenOff(Reg.CuDword(Adv, "DisallowShaking", 1))
            .Detect(() => RegistryAccess.ReadDword(RegHive.CurrentUser, Adv, "DisallowShaking") switch
            {
                1 => Off,
                0 => On,
                _ => OsInfo.Build >= 22000 ? Off : On,
            })
            .Build();

        yield return Tweak.Choice("custom.windows.alttabtabs", "Onglets Microsoft Edge dans Alt+Tab",
                "Onglets des applications compatibles (Microsoft Edge) proposés par Alt+Tab et l'ancrage des fenêtres. " +
                "Selon la version de Windows, « Tous les onglets » peut être limité aux plus récents.")
            .In(C, GroupDesktop)
            .Keywords("alt tab", "onglets", "edge", "tabs", "basculer fenetres")
            .Requires(Requires.When(p => p.Build >= 19042, "Nécessite Windows 10 20H2 ou plus récent."))
            .Option("default", "Réglage par défaut", Reg.CuDel(Adv, "MultiTaskingAltTabFilter"))
            .Option("none", "Fenêtres uniquement", Reg.CuDword(Adv, "MultiTaskingAltTabFilter", 3))
            .Option("three", "3 onglets récents", Reg.CuDword(Adv, "MultiTaskingAltTabFilter", 2))
            .Option("five", "5 onglets récents", Reg.CuDword(Adv, "MultiTaskingAltTabFilter", 1))
            .Option("all", "Tous les onglets", Reg.CuDword(Adv, "MultiTaskingAltTabFilter", 0))
            .WindowsDefault("default")
            .Build();

        yield return Tweak.Toggle("custom.desktop.jpegquality", "Qualité maximale des fonds d'écran JPEG",
                "Windows recompresse les fonds d'écran JPEG à 85 % de qualité, ce qui peut créer des artefacts visibles. " +
                "À 100 %, l'image est conservée sans perte visible. S'applique au prochain changement de fond d'écran.")
            .In(C, GroupDesktop)
            .Keywords("qualite fond d'ecran", "jpeg", "compression", "wallpaper quality", "artefacts", "flou fond d'ecran")
            .WhenOn(Reg.CuDword(DesktopKey, "JPEGImportQuality", 100))
            .WhenOff(Reg.CuDel(DesktopKey, "JPEGImportQuality"))
            .WindowsDefault(Off)
            .Build();
    }

    private static TweakDefinition DesktopIcon(string id, string title, string description, string clsid, bool visibleByDefault, params string[] keywords) =>
        Tweak.Toggle(id, title, description + " Visible après actualisation du bureau (F5) ou redémarrage de l'Explorateur.")
            .In(C, GroupDesktop)
            .Keywords([.. keywords, "icones du bureau", "desktop icons", "bureau"])
            .Labels("Affichée", "Masquée")
            .Effect(ApplyEffect.RestartExplorer)
            .WhenOn(Reg.CuDword(DesktopIcons, clsid, 0))
            .WhenOff(Reg.CuDword(DesktopIcons, clsid, 1))
            .WindowsDefault(visibleByDefault ? On : Off)
            .Build();

    // =====================================================================================================
    // Connexion et verrouillage
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> LogonTweaks()
    {
        yield return Tweak.Toggle("custom.logon.acrylic", "Effet de flou sur l'écran de connexion",
                "Flou (acrylique) appliqué à l'image de fond derrière la zone de connexion. Désactivé, l'image reste nette. " +
                "Stratégie Windows : s'applique à tous les utilisateurs.")
            .In(C, GroupLogon)
            .Keywords("flou", "acrylique", "ecran de connexion", "login", "blur", "arriere plan connexion")
            .WhenOn(Reg.LmDel(SystemPolicy, "DisableAcrylicBackgroundOnLogon"))
            .WhenOff(Reg.LmDword(SystemPolicy, "DisableAcrylicBackgroundOnLogon", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.logon.background", "Image de fond sur l'écran de connexion",
                "Affiche l'image de l'écran de verrouillage derrière la zone de connexion. Désactivé, un fond de couleur unie " +
                "est utilisé. S'applique à tous les utilisateurs. Valeur de stratégie absente des modèles d'administration de " +
                "Microsoft mais très répandue : une mise à jour de Windows pourrait la rendre inopérante.")
            .In(C, GroupLogon)
            .Keywords("ecran de connexion", "image de fond", "sign-in screen", "logon background", "arriere plan connexion")
            .WhenOn(Reg.LmDel(SystemPolicy, "DisableLogonBackgroundImage"))
            .WhenOff(Reg.LmDword(SystemPolicy, "DisableLogonBackgroundImage", 1))
            .WindowsDefault(On)
            .Build();

        yield return Tweak.Toggle("custom.logon.verbose", "Messages d'état détaillés",
                "Au démarrage, à l'arrêt et à l'ouverture de session, Windows affiche l'étape en cours " +
                "(« Application des paramètres de stratégie de groupe… ») au lieu de « Veuillez patienter ». Utile pour diagnostiquer une lenteur.")
            .In(C, GroupLogon)
            .Keywords("messages detailles", "verbose", "diagnostic demarrage", "veuillez patienter", "lenteur demarrage")
            .Tags("dev")
            .Risk(RiskLevel.Moderate)
            .WhenOn(Reg.LmDword(SystemPoliciesLegacy, "VerboseStatus", 1))
            .WhenOff(Reg.LmDel(SystemPoliciesLegacy, "VerboseStatus"))
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("custom.logon.numlock", "Verr. num activé au démarrage",
                "Active le pavé numérique dès l'écran de connexion. Sur certains PC, le réglage du BIOS/UEFI l'emporte. " +
                "Sur un portable sans pavé numérique séparé, des lettres taperaient alors des chiffres : n'activez pas ce réglage.")
            .In(C, GroupLogon)
            .Keywords("verr num", "numlock", "pave numerique", "num lock", "chiffres")
            .Tags("office")
            .WhenOn(Reg.DefString(DefaultKeyboard, "InitialKeyboardIndicators", "2147483650"))
            .WhenOff(Reg.DefString(DefaultKeyboard, "InitialKeyboardIndicators", "2147483648"))
            .Detect(() => ParseLong(RegistryAccess.Read(RegHive.DefaultUser, DefaultKeyboard, "InitialKeyboardIndicators")) is { } v
                ? ((v & 2) != 0 ? On : Off)
                : Off)
            .Build();

        yield return Tweak.Toggle("custom.logon.startupsound", "Son de démarrage de Windows",
                "Son joué à l'affichage de l'écran de connexion lors du démarrage du PC. S'applique à tous les utilisateurs.")
            .In(C, GroupLogon)
            .Keywords("son de demarrage", "startup sound", "jingle", "son windows", "musique demarrage")
            .WhenOn(Reg.LmDword(BootAnimation, "DisableStartupSound", 0))
            .WhenOff(Reg.LmDword(BootAnimation, "DisableStartupSound", 1))
            // La stratégie « Désactiver le son de démarrage de Windows » (Logon.admx), si elle est définie, l'emporte.
            .Detect(() => (RegistryAccess.ReadDword(RegHive.LocalMachine, SystemPoliciesLegacy, "DisableStartupSound")
                           ?? RegistryAccess.ReadDword(RegHive.LocalMachine, BootAnimation, "DisableStartupSound")) switch
            {
                1 => Off,
                0 => On,
                _ => OsInfo.Build >= 22000 ? On : Off,
            })
            .Build();
    }

    // =====================================================================================================
    // Souris et clavier
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> InputTweaks()
    {
        yield return Tweak.Toggle("custom.mouse.precision", "Améliorer la précision du pointeur",
                "Accélération de la souris : le pointeur va plus loin quand vous bougez vite. Beaucoup de joueurs et de " +
                "graphistes préfèrent la désactiver pour un déplacement proportionnel et prévisible.")
            .In(C, GroupInput)
            .Keywords("acceleration souris", "precision du pointeur", "mouse acceleration", "enhance pointer precision", "souris", "pointeur")
            .Tags("gaming")
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.CuString(MouseKey, "MouseSpeed", "1"), Reg.CuString(MouseKey, "MouseThreshold1", "6"), Reg.CuString(MouseKey, "MouseThreshold2", "10"))
            .WhenOff(Reg.CuString(MouseKey, "MouseSpeed", "0"), Reg.CuString(MouseKey, "MouseThreshold1", "0"), Reg.CuString(MouseKey, "MouseThreshold2", "0"))
            .WindowsDefault(On)
            .Build();

        yield return AccessibilityShortcut("custom.keyboard.stickykeys", "Raccourci des touches rémanentes (Maj × 5)",
            "Appuyer 5 fois sur Maj propose d'activer les touches rémanentes.",
            StickyKeys, "510", "506", "touches remanentes", "sticky keys", "maj 5 fois", "shift");
        yield return AccessibilityShortcut("custom.keyboard.filterkeys", "Raccourci des touches filtres (Maj droite 8 s)",
            "Maintenir la touche Maj de droite 8 secondes propose d'activer les touches filtres (frappes brèves ou répétées ignorées).",
            FilterKeys, "126", "122", "touches filtres", "filter keys", "maj droite");
        yield return AccessibilityShortcut("custom.keyboard.togglekeys", "Raccourci des touches bascules (Verr. num 5 s)",
            "Maintenir Verr. num 5 secondes active les touches bascules (bip à l'appui de Verr. maj, Verr. num et Arrêt défil).",
            ToggleKeys, "62", "58", "touches bascules", "toggle keys", "bip verr maj");
    }

    /// <summary>
    /// Raccourci clavier d'une fonction d'accessibilité : valeurs « Flags » par défaut de Windows, avec ou sans le bit
    /// « raccourci actif » (0x4, *_HOTKEYACTIVE). La fonction elle-même reste disponible dans Paramètres › Accessibilité.
    /// </summary>
    private static TweakDefinition AccessibilityShortcut(string id, string title, string description, string key,
        string onValue, string offValue, params string[] keywords) =>
        Tweak.Toggle(id, title, description + " Désactiver ce raccourci évite les fenêtres ouvertes par erreur (en jeu notamment) ; " +
                                "la fonction reste disponible dans Paramètres › Accessibilité. Les autres options de la fonction reprennent leur valeur par défaut.")
            .In(C, GroupInput)
            .Keywords([.. keywords, "accessibilite", "raccourci clavier", "clavier"])
            .Tags("gaming")
            .Effect(ApplyEffect.SignOut)
            .WhenOn(Reg.CuString(key, "Flags", onValue))
            .WhenOff(Reg.CuString(key, "Flags", offValue))
            .Detect(() => ParseLong(RegistryAccess.Read(RegHive.CurrentUser, key, "Flags")) is { } v ? ((v & 0x4) != 0 ? On : Off) : On)
            .Build();

    /// <summary>
    /// Élément du volet de navigation masqué par une surcharge utilisateur (HKCU\Software\Classes\CLSID\{…}
    /// System.IsPinnedToNameSpaceTree = 0). « Masqué » remet d'abord la clé à zéro pour que l'annulation la supprime entièrement.
    /// </summary>
    private static string NavPaneDetect(string clsidKey) =>
        RegistryAccess.ReadDword(RegHive.CurrentUser, clsidKey, PinnedToNavPane) == 0 ? Off : On;

    private static long? ParseLong(object? value) => value switch
    {
        int i => i,
        long l => l,
        string s when long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) => r,
        _ => null,
    };
}

/// <summary>Numéro de build de Windows (lecture unique, instantanée) pour les détections qui en dépendent.</summary>
internal static class OsInfo
{
    private static readonly Lazy<int> LazyBuild = new(() =>
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (int.TryParse(k?.GetValue("CurrentBuild") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)) return b;
        }
        catch { /* repli ci-dessous */ }
        return Environment.OSVersion.Version.Build;
    });

    public static int Build => LazyBuild.Value;
}
