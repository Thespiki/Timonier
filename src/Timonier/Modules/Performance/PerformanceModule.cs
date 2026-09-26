using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Performance;

/// <summary>
/// Performances : profil matériel, plans et modes d'alimentation, veille, effets visuels, jeux, énergie, arrière-plan,
/// services et stockage. Déclarations uniquement (appelé aussi dans le broker).
/// </summary>
public sealed class PerformanceModule : IModule
{
    public const string Category = "performance";
    public const string PageId = "performance";
    public const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, L("Performances"), Glyph,
            L("Alimentation, veille, effets visuels, jeux, applications en arrière-plan, services et stockage.")));

        r.AddTweaks(PerformanceTweaks.All());

        r.AddAction(new ActivateSchemeAction(admin: false));
        r.AddAction(new ActivateSchemeAction(admin: true));
        r.AddAction(new AddSchemeAction());
        r.AddAction(new SetTimeoutAction());
        r.AddAction(new SetPowerModeAction());
        r.AddAction(new DirectXSettingAction());

        r.AddPage(new PageInfo(PageId, L("Performances"), Glyph, NavSection.Settings, 30, () => new PerformancePage())
        {
            CategoryId = Category,
            Description = L("Profil matériel, plans d'alimentation, mode d'alimentation, veille, effets visuels, jeux et services."),
            Keywords = [L("performances, vitesse, lent, accélérer, alimentation, énergie, batterie, veille, jeux, gaming, effets visuels, services, optimiser")],
        });

        // Lecture seule, exécuté hors du thread UI par le tableau de bord.
        r.AddHealthCheck(HealthCheck.Sync("perf.power", L("Alimentation"), "", PageId, PowerHealth.Check));

        r.AddQuickAction(new QuickAction("perf.mode-game", L("Mode jeu"), "",
            L("Active le Mode Jeu, coupe l'enregistrement en arrière-plan et applique la recommandation de planification GPU, après confirmation."),
            PerfQuickActions.GameModeAsync)
        {
            Keywords = [L("mode jeu, gaming, jouer, fps, game mode, performances jeux")],
            Order = 40,
        });
        r.AddQuickAction(new QuickAction("perf.mode-eco", L("Économie d'énergie"), "",
            L("Passe le PC en mode « Meilleure efficacité énergétique » (ou sur le plan Économie d'énergie) pour préserver la batterie."),
            PerfQuickActions.EcoModeAsync)
        {
            Keywords = [L("économie d'énergie, batterie, autonomie, efficacité énergétique, power saver, eco")],
            Order = 45,
        });

        RegisterSearchEntries(r);

        Synonyms.AddGroup("plan d alimentation", "mode de gestion de l alimentation", "power plan", "power scheme", "powercfg");
        Synonyms.AddGroup("performances optimales", "ultimate performance", "performances maximales");
        Synonyms.AddGroup("veille prolongee", "hibernation", "hiberner", "hibernate");
        Synonyms.AddGroup("mise en veille", "veille", "sleep", "standby");
        Synonyms.AddGroup("lent", "lenteur", "rame", "ralenti", "accelerer", "optimiser", "booster");
        Synonyms.AddGroup("fps", "images par seconde", "framerate", "fluidite jeux");
        Synonyms.AddGroup("planification gpu", "hags", "hardware accelerated gpu scheduling");
        Synonyms.AddGroup("sysmain", "superfetch", "prefetch");
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        AddSection(r, "perf.section.hardware", L("Profil matériel de ce PC"), L("Niveau de performance, processeur, mémoire, disque, carte graphique"),
            "", "hardware", [L("profil materiel, configuration, processeur, memoire, ram, disque, carte graphique, niveau performance")]);
        AddSection(r, "perf.section.plans", L("Plans d'alimentation"), L("Activer un plan, ajouter « Performances optimales »"),
            "", "power", [L("plan alimentation, power plan, performances optimales, performances elevees, ultimate")]);
        AddSection(r, "perf.section.mode", L("Mode d'alimentation"), L("Économie, Équilibré ou Performances, sur secteur et sur batterie"),
            "", "power", [L("mode alimentation, power mode, curseur alimentation, efficacite energetique, meilleures performances")]);
        AddSection(r, "perf.section.sleep", L("Veille et extinction de l'écran"), L("Délais sur secteur et sur batterie"),
            "", "sleep", [L("veille, ecran, delai, eteindre ecran, mise en veille, timeout, sleep")]);
        AddSection(r, "perf.section.graphics", L("Options graphiques des jeux"), L("Optimisations des jeux fenêtrés, fréquence d'actualisation variable"),
            "", "graphics", [L("jeux fenetres, windowed games, vrr, frequence actualisation variable, directx, flip model")]);

        AddWindows(r, "perf.win.power", L("Alimentation et batterie (Paramètres Windows)"), L("Mode d'alimentation, veille, économiseur de batterie"),
            "ms-settings:powersleep", [L("parametres alimentation, power settings, alimentation batterie")]);
        AddWindows(r, "perf.win.batterysaver", L("Économiseur de batterie (Paramètres Windows)"), L("Seuil d'activation et utilisation de la batterie"),
            "ms-settings:batterysaver", [L("economiseur batterie, battery saver, economie energie, utilisation batterie")]);
        AddWindows(r, "perf.win.gamemode", L("Mode Jeu (Paramètres Windows)"), L("Paramètres de jeu de Windows"),
            "ms-settings:gaming-gamemode", [L("mode jeu, game mode, parametres jeux")]);
        AddWindows(r, "perf.win.graphics", L("Graphiques (Paramètres Windows)"), L("Préférence de carte graphique par application"),
            "ms-settings:display-advancedgraphics", [L("preference gpu, carte graphique application, gpu preference, graphiques")]);

        AddTool(r, "perf.tool.fxdialog", L("Options de performances (Windows)"), L("Boîte classique des effets visuels et de la mémoire virtuelle"),
            SystemTool.SystemPropertiesPerformance, [], [L("options de performances, memoire virtuelle, fichier d echange, pagefile, effets visuels")]);
        AddTool(r, "perf.tool.powercpl", L("Options d'alimentation (Panneau de configuration)"), L("Paramètres avancés des plans d'alimentation"),
            SystemTool.Control, ["powercfg.cpl"], [L("options alimentation, parametres avances alimentation, powercfg.cpl")]);
    }

    private static void AddSection(ModuleRegistry r, string id, string title, string subtitle, string glyph, string section, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id, Title = title, Subtitle = subtitle, Glyph = glyph, Keywords = keywords,
            PageId = PageId, PageParameter = "section:" + section,
        });

    private static void AddWindows(ModuleRegistry r, string id, string title, string subtitle, string uri, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id, Title = title, Subtitle = subtitle, Glyph = "", Keywords = keywords,
            Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri(uri),
        });

    private static void AddTool(ModuleRegistry r, string id, string title, string subtitle, SystemTool tool, string[] args, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id, Title = title, Subtitle = subtitle, Glyph = "", Keywords = keywords,
            Kind = SearchEntryKind.Tool,
            Execute = () => ProcessRunner.Launch(tool, args),
        });
}
