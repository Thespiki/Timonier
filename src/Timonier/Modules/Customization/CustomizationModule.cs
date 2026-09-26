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
        r.AddCategory(new CategoryInfo(Category, L("Personnalisation"), Glyph,
            L("Apparence de Windows, fond d'écran, barre des tâches, menu Démarrer, Explorateur de fichiers et ouverture de session.")));

        r.AddTweaks(CustomizationTweaks.All());

        r.AddAction(new SetWallpaperAction());
        r.AddAction(new SetSolidColorAction());
        r.AddAction(new SetLockScreenImageAction());

        r.AddPage(new PageInfo(PageId, L("Personnalisation"), Glyph, NavSection.Settings, 20, () => new CustomizationPage())
        {
            CategoryId = Category,
            Description = L("Thème clair ou sombre, couleurs, fond d'écran, barre des tâches, Démarrer, Explorateur."),
            Keywords =
            [L("personnalisation, apparence, theme, mode sombre, fond d'ecran, barre des taches, menu demarrer, explorateur, bureau, ecran de verrouillage, personalization, customize, look")],
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
            Id = "custom.section.wallpaper", Title = L("Changer le fond d'écran"),
            Subtitle = L("Image ou couleur unie, position (remplir, ajuster, mosaïque…), retour au fond précédent"),
            Glyph = "", PageId = PageId, PageParameter = "section:wallpaper", Boost = 0.1,
            Keywords = [L("fond d'ecran, wallpaper, image de fond, arriere plan, couleur unie, background, photo bureau")],
        };
        yield return new SearchEntry
        {
            Id = "custom.section.lockscreen", Title = L("Image de l'écran de verrouillage"),
            Subtitle = L("Choisir l'image affichée quand la session est verrouillée"),
            Glyph = "", PageId = PageId, PageParameter = "section:lockscreen",
            Keywords = [L("ecran de verrouillage, lock screen, image verrouillage, windows a la une, spotlight")],
        };
        yield return new SearchEntry
        {
            Id = "custom.section.appearance", Title = L("Mode clair, sombre ou mixte"),
            Subtitle = L("Changer le thème de Windows et des applications en un clic"),
            Glyph = "", PageId = PageId, PageParameter = "section:appearance", Boost = 0.1,
            Keywords = [L("mode sombre, mode clair, dark mode, light mode, theme, couleur d'accent, accent")],
        };

        // Pages des Paramètres de Windows (ouvertes directement).
        yield return Settings("custom.settings.colors", L("Couleurs (Paramètres Windows)"), L("Couleur d'accent précise, mode, transparence"),
            "ms-settings:colors", L("couleur d'accent, accent color, couleurs"));
        yield return Settings("custom.settings.themes", L("Thèmes (Paramètres Windows)"), L("Thèmes complets, sons, pointeurs, icônes du bureau"),
            "ms-settings:themes", L("themes, theme windows, pack de themes"));
        yield return Settings("custom.settings.background", L("Arrière-plan (Paramètres Windows)"), L("Image, diaporama, Windows à la une"),
            "ms-settings:personalization-background", L("diaporama, slideshow, windows a la une, spotlight"));
        yield return Settings("custom.settings.lockscreen", L("Écran de verrouillage (Paramètres Windows)"), L("Image, état détaillé, widgets"),
            "ms-settings:lockscreen", L("ecran de verrouillage, lock screen"));
        yield return Settings("custom.settings.taskbar", L("Barre des tâches (Paramètres Windows)"), L("Icônes de la zone de notification, comportements"),
            "ms-settings:taskbar", L("zone de notification, icones systeme, system tray, masquer automatiquement"));
        yield return Settings("custom.settings.start", L("Démarrer (Paramètres Windows)"), L("Dossiers à côté du bouton Marche/Arrêt, disposition"),
            "ms-settings:personalization-start", L("dossiers demarrer, start folders, menu demarrer"));
        yield return Settings("custom.settings.fonts", L("Polices (Paramètres Windows)"), L("Polices installées, ajout de polices"),
            "ms-settings:fonts", L("police, fonts, typographie"));
        yield return Settings("custom.settings.mouse", L("Souris (Paramètres Windows)"), L("Vitesse du pointeur, bouton principal, défilement"),
            "ms-settings:mousetouchpad", L("vitesse souris, defilement, bouton principal, mouse speed"));
        yield return Settings("custom.settings.keyboard", L("Clavier et accessibilité (Paramètres Windows)"), L("Touches rémanentes, filtres, bascules"),
            "ms-settings:easeofaccess-keyboard", L("touches remanentes, sticky keys, clavier visuel"));

        yield return new SearchEntry
        {
            Id = "custom.tool.desktopicons", Title = L("Paramètres des icônes du bureau"),
            Subtitle = L("Fenêtre classique : icônes Ce PC, Corbeille, Réseau… et leur apparence"),
            Glyph = "", Kind = SearchEntryKind.Tool,
            Keywords = [L("icones du bureau, desktop icons, desk.cpl, corbeille, ce pc")],
            Execute = () => ProcessRunner.Launch(SystemTool.Rundll32, "shell32.dll,Control_RunDLL", "desk.cpl,,0"),
        };
        yield return new SearchEntry
        {
            Id = "custom.tool.folderoptions", Title = L("Options des dossiers de l'Explorateur"),
            Subtitle = L("Fenêtre classique : affichage, fichiers cachés, navigation"),
            Glyph = "", Kind = SearchEntryKind.Tool,
            Keywords = [L("options des dossiers, folder options, options de l'explorateur, affichage dossiers")],
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
