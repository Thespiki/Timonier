using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Customization;

/// <summary>
/// Personnalisation : apparence (mode clair/sombre, accent, transparence), fond d'écran et écran de verrouillage,
/// barre des tâches, menu Démarrer, Explorateur de fichiers, bureau et fenêtres, ouverture de session, souris et clavier.
/// <para>Register ne fait que déclarer (il est aussi appelé dans le broker élevé) : aucune E/S, aucune interface.</para>
/// </summary>
public sealed class CustomizationModule : IModule
{
    public const string Category = "customization";
    public const string PageId = "customization";
    private const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, "Personnalisation", Glyph,
            "Apparence de Windows, fond d'écran, barre des tâches, menu Démarrer, Explorateur de fichiers et ouverture de session."));

        r.AddTweaks(CustomizationTweaks.All());

        r.AddAction(new SetWallpaperAction());
        r.AddAction(new SetSolidColorAction());
        r.AddAction(new SetLockScreenImageAction());

        r.AddPage(new PageInfo(PageId, "Personnalisation", Glyph, NavSection.Settings, 20, () => new CustomizationPage())
        {
            CategoryId = Category,
            Description = "Thème clair ou sombre, couleurs, fond d'écran, barre des tâches, Démarrer, Explorateur.",
            Keywords =
            [
                "personnalisation", "apparence", "theme", "mode sombre", "fond d'ecran", "barre des taches", "menu demarrer",
                "explorateur", "bureau", "ecran de verrouillage", "personalization", "customize", "look",
            ],
        });

        r.AddQuickAction(ThemeSwitcher.CreateQuickAction());
        r.AddSearchEntries(SearchEntries());
        AddSynonyms();
    }

    private static IEnumerable<SearchEntry> SearchEntries()
    {
        // Fonctions de la page.
        yield return new SearchEntry
        {
            Id = "custom.section.wallpaper", Title = "Changer le fond d'écran",
            Subtitle = "Image ou couleur unie, position (remplir, ajuster, mosaïque…), retour au fond précédent",
            Glyph = "", PageId = PageId, PageParameter = "section:wallpaper", Boost = 0.1,
            Keywords = ["fond d'ecran", "wallpaper", "image de fond", "arriere plan", "couleur unie", "background", "photo bureau"],
        };
        yield return new SearchEntry
        {
            Id = "custom.section.lockscreen", Title = "Image de l'écran de verrouillage",
            Subtitle = "Choisir l'image affichée quand la session est verrouillée",
            Glyph = "", PageId = PageId, PageParameter = "section:lockscreen",
            Keywords = ["ecran de verrouillage", "lock screen", "image verrouillage", "windows a la une", "spotlight"],
        };
        yield return new SearchEntry
        {
            Id = "custom.section.appearance", Title = "Mode clair, sombre ou mixte",
            Subtitle = "Changer le thème de Windows et des applications en un clic",
            Glyph = "", PageId = PageId, PageParameter = "section:appearance", Boost = 0.1,
            Keywords = ["mode sombre", "mode clair", "dark mode", "light mode", "theme", "couleur d'accent", "accent"],
        };

        // Pages des Paramètres de Windows (ouvertes directement).
        yield return Settings("custom.settings.colors", "Couleurs (Paramètres Windows)", "Couleur d'accent précise, mode, transparence",
            "ms-settings:colors", "couleur d'accent", "accent color", "couleurs");
        yield return Settings("custom.settings.themes", "Thèmes (Paramètres Windows)", "Thèmes complets, sons, pointeurs, icônes du bureau",
            "ms-settings:themes", "themes", "theme windows", "pack de themes");
        yield return Settings("custom.settings.background", "Arrière-plan (Paramètres Windows)", "Image, diaporama, Windows à la une",
            "ms-settings:personalization-background", "diaporama", "slideshow", "windows a la une", "spotlight");
        yield return Settings("custom.settings.lockscreen", "Écran de verrouillage (Paramètres Windows)", "Image, état détaillé, widgets",
            "ms-settings:lockscreen", "ecran de verrouillage", "lock screen");
        yield return Settings("custom.settings.taskbar", "Barre des tâches (Paramètres Windows)", "Icônes de la zone de notification, comportements",
            "ms-settings:taskbar", "zone de notification", "icones systeme", "system tray", "masquer automatiquement");
        yield return Settings("custom.settings.start", "Démarrer (Paramètres Windows)", "Dossiers à côté du bouton Marche/Arrêt, disposition",
            "ms-settings:personalization-start", "dossiers demarrer", "start folders", "menu demarrer");
        yield return Settings("custom.settings.fonts", "Polices (Paramètres Windows)", "Polices installées, ajout de polices",
            "ms-settings:fonts", "police", "fonts", "typographie");
        yield return Settings("custom.settings.mouse", "Souris (Paramètres Windows)", "Vitesse du pointeur, bouton principal, défilement",
            "ms-settings:mousetouchpad", "vitesse souris", "defilement", "bouton principal", "mouse speed");
        yield return Settings("custom.settings.keyboard", "Clavier et accessibilité (Paramètres Windows)", "Touches rémanentes, filtres, bascules",
            "ms-settings:easeofaccess-keyboard", "touches remanentes", "sticky keys", "clavier visuel");

        yield return new SearchEntry
        {
            Id = "custom.tool.desktopicons", Title = "Paramètres des icônes du bureau",
            Subtitle = "Fenêtre classique : icônes Ce PC, Corbeille, Réseau… et leur apparence",
            Glyph = "", Kind = SearchEntryKind.Tool,
            Keywords = ["icones du bureau", "desktop icons", "desk.cpl", "corbeille", "ce pc"],
            Execute = () => ProcessRunner.Launch(SystemTool.Rundll32, "shell32.dll,Control_RunDLL", "desk.cpl,,0"),
        };
        yield return new SearchEntry
        {
            Id = "custom.tool.folderoptions", Title = "Options des dossiers de l'Explorateur",
            Subtitle = "Fenêtre classique : affichage, fichiers cachés, navigation",
            Glyph = "", Kind = SearchEntryKind.Tool,
            Keywords = ["options des dossiers", "folder options", "options de l'explorateur", "affichage dossiers"],
            Execute = () => ProcessRunner.Launch(SystemTool.Control, "folders"),
        };
    }

    private static SearchEntry Settings(string id, string title, string subtitle, string uri, params string[] keywords) => new()
    {
        Id = id,
        Title = title,
        Subtitle = subtitle,
        Glyph = "",
        Kind = SearchEntryKind.WindowsSetting,
        Keywords = keywords,
        Execute = () => ProcessRunner.OpenSettingsUri(uri),
    };

    private static void AddSynonyms()
    {
        Synonyms.AddGroup("menu contextuel classique", "afficher plus d options", "ancien menu clic droit", "classic context menu");
        Synonyms.AddGroup("fin de tache", "end task", "forcer la fermeture", "tuer application");
        Synonyms.AddGroup("verr num", "numlock", "num lock", "pave numerique");
        Synonyms.AddGroup("touches remanentes", "sticky keys", "cinq fois maj", "maj cinq fois");
        Synonyms.AddGroup("precision du pointeur", "acceleration souris", "mouse acceleration", "enhance pointer precision");
        Synonyms.AddGroup("volet de navigation", "navigation pane", "panneau de gauche");
        Synonyms.AddGroup("icones du bureau", "desktop icons", "icone ce pc", "icone corbeille");
        Synonyms.AddGroup("alignement barre des taches", "icones a gauche", "centrer icones", "taskbar left");
    }
}
