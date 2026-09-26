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
        r.AddCategory(new CategoryInfo(Category, L("Personalization"), Glyph,
            L("Windows appearance, wallpaper, taskbar, Start menu, File Explorer and sign-in.")));

        r.AddTweaks(CustomizationTweaks.All());

        r.AddAction(new SetWallpaperAction());
        r.AddAction(new SetSolidColorAction());
        r.AddAction(new SetLockScreenImageAction());

        r.AddPage(new PageInfo(PageId, L("Personalization"), Glyph, NavSection.Settings, 20, () => new CustomizationPage())
        {
            CategoryId = Category,
            Description = L("Light or dark theme, colors, wallpaper, taskbar, Start, File Explorer."),
            Keywords =
            [L("personalization, appearance, theme, dark mode, wallpaper, taskbar, start menu, file explorer, desktop, lock screen, customize, look")],
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
            Id = "custom.section.wallpaper", Title = L("Change the wallpaper"),
            Subtitle = L("Picture or solid color, position (fill, fit, tile…), go back to the previous wallpaper"),
            Glyph = "", PageId = PageId, PageParameter = "section:wallpaper", Boost = 0.1,
            Keywords = [L("wallpaper, background, desktop background, background image, solid color, desktop picture")],
        };
        yield return new SearchEntry
        {
            Id = "custom.section.lockscreen", Title = L("Lock screen picture"),
            Subtitle = L("Choose the picture shown when your PC is locked"),
            Glyph = "", PageId = PageId, PageParameter = "section:lockscreen",
            Keywords = [L("lock screen, lock screen picture, windows spotlight, spotlight")],
        };
        yield return new SearchEntry
        {
            Id = "custom.section.appearance", Title = L("Light, dark or mixed mode"),
            Subtitle = L("Change the Windows and app theme in one click"),
            Glyph = "", PageId = PageId, PageParameter = "section:appearance", Boost = 0.1,
            Keywords = [L("dark mode, light mode, theme, accent color, accent, night mode")],
        };

        // Pages des Paramètres de Windows (ouvertes directement).
        yield return Settings("custom.settings.colors", L("Colors (Windows Settings)"), L("Precise accent color, mode, transparency"),
            "ms-settings:colors", L("accent color, accent, colors"));
        yield return Settings("custom.settings.themes", L("Themes (Windows Settings)"), L("Full themes, sounds, mouse pointers, desktop icons"),
            "ms-settings:themes", L("themes, windows theme, theme pack"));
        yield return Settings("custom.settings.background", L("Background (Windows Settings)"), L("Picture, slideshow, Windows spotlight"),
            "ms-settings:personalization-background", L("slideshow, windows spotlight, spotlight"));
        yield return Settings("custom.settings.lockscreen", L("Lock screen (Windows Settings)"), L("Picture, detailed status, widgets"),
            "ms-settings:lockscreen", L("lock screen"));
        yield return Settings("custom.settings.taskbar", L("Taskbar (Windows Settings)"), L("Notification area icons, behaviors"),
            "ms-settings:taskbar", L("notification area, system icons, system tray, tray icons, automatically hide, auto-hide"));
        yield return Settings("custom.settings.start", L("Start (Windows Settings)"), L("Folders next to the Power button, layout"),
            "ms-settings:personalization-start", L("start folders, start menu"));
        yield return Settings("custom.settings.fonts", L("Fonts (Windows Settings)"), L("Installed fonts, add fonts"),
            "ms-settings:fonts", L("font, fonts, typography, typeface"));
        yield return Settings("custom.settings.mouse", L("Mouse (Windows Settings)"), L("Pointer speed, primary button, scrolling"),
            "ms-settings:mousetouchpad", L("mouse speed, pointer speed, scrolling, primary button"));
        yield return Settings("custom.settings.keyboard", L("Keyboard and accessibility (Windows Settings)"), L("Sticky keys, filter keys, toggle keys"),
            "ms-settings:easeofaccess-keyboard", L("sticky keys, on-screen keyboard"));

        yield return new SearchEntry
        {
            Id = "custom.tool.desktopicons", Title = L("Desktop icon settings"),
            Subtitle = L("Classic window: This PC, Recycle Bin, Network… icons and their appearance"),
            Glyph = "", Kind = SearchEntryKind.Tool,
            Keywords = [L("desktop icons, desk.cpl, recycle bin, this pc")],
            Execute = () => ProcessRunner.Launch(SystemTool.Rundll32, "shell32.dll,Control_RunDLL", "desk.cpl,,0"),
        };
        yield return new SearchEntry
        {
            Id = "custom.tool.folderoptions", Title = L("File Explorer folder options"),
            Subtitle = L("Classic window: view, hidden files, navigation"),
            Glyph = "", Kind = SearchEntryKind.Tool,
            Keywords = [L("folder options, explorer options, folder view, file explorer options, view settings")],
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
