using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Apps;

/// <summary>
/// Applications : catalogue d'installation winget, programmes installés, applications préinstallées et mises à jour.
/// Déclarations uniquement (appelé aussi dans le broker) : aucune E/S ici, le catalogue est une table statique.
/// </summary>
public sealed class AppsModule : IModule
{
    public const string Category = "apps";
    public const string PageId = "apps";
    public const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        // Aucun réglage déclaratif : la catégorie n'est pas enregistrée (la page est dédiée).
        r.AddAction(new WingetInstallAction());
        r.AddAction(new WingetUpgradeAction());
        r.AddAction(new WingetUpgradeAllAction());
        r.AddAction(new WingetUninstallAction());
        r.AddAction(new WingetUninstallUserAction());
        r.AddAction(new AppxRemoveAction());
        r.AddAction(new AppxDeprovisionAction());

        r.AddPage(new PageInfo(PageId, L("Apps"), Glyph, NavSection.Tools, 10, () => new AppsPage())
        {
            CategoryId = Category,
            Description = L("Install verified software in one click, uninstall programs, remove preinstalled apps, and update everything."),
            Keywords = [L("apps, applications, software, programs, install, uninstall, winget, bloatware, updates, store, preinstalled")],
        });

        r.AddQuickAction(new QuickAction("apps.update-all", L("Update all apps"), "",
            L("Installs all updates known to winget (browsers, players, utilities…)."),
            () => { AppHost.Navigator.Navigate(PageId, "action:update-all"); return Task.CompletedTask; })
        {
            Keywords = [L("update, update software, winget upgrade, update apps, upgrade all, refresh programs")],
            Order = 30,
            RequiresAdmin = true,
        });
        r.AddQuickAction(new QuickAction("apps.remove-bloat", L("Remove preinstalled apps"), "",
            L("Candy Crush, TikTok, News…: remove the unnecessary apps installed out of the box."),
            () => { AppHost.Navigator.Navigate(PageId, "view:bloat"); return Task.CompletedTask; })
        {
            Keywords = [L("bloatware, debloat, unwanted apps, candy crush, clean up windows, remove apps, uninstall apps, junk apps")],
            Order = 35,
        });

        RegisterSearchEntries(r);

        Synonyms.AddGroup("logiciel", "application", "programme", "app", "software");
        Synonyms.AddGroup("desinstaller", "supprimer un programme", "uninstall", "enlever un logiciel", "retirer un programme");
        Synonyms.AddGroup("bloatware", "applications preinstallees", "bloat", "crapware", "applications inutiles", "debloat");
        Synonyms.AddGroup("mettre a jour", "mise a jour", "update", "upgrade", "actualiser");
        Synonyms.AddGroup("winget", "gestionnaire de paquets", "package manager", "programme d'installation d'application");
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        Feature(r, "apps.view.install", L("Install apps"), L("Catalog of verified software, batch install"), "", "view:install",
            [L("install software, download program, catalog, ninite, install multiple apps, batch install")]);
        Feature(r, "apps.view.installed", L("Installed programs"), L("List, size, install date and uninstall"), "", "view:installed",
            [L("uninstall, programs and features, add or remove programs, software list, program size, installed apps")]);
        Feature(r, "apps.view.bloat", L("Preinstalled apps"), L("Remove Candy Crush, TikTok, News, Clipchamp…"), "", "view:bloat",
            [L("bloatware, candy crush, unwanted apps, cortana, xbox, debloat, remove windows apps, uninstall apps")]);
        Feature(r, "apps.view.updates", L("App updates"), L("See and install new versions of your software"), "", "view:updates",
            [L("update software, winget upgrade, versions, outdated, update programs, upgrade apps")]);

        r.AddSearchEntry(new SearchEntry
        {
            Id = "apps.win.appsfeatures",
            Title = L("Installed apps (Windows Settings)"),
            Subtitle = L("Settings page to modify or uninstall an app"),
            Glyph = "",
            Keywords = [L("installed apps, appwiz, programs and features, apps features, uninstall")],
            Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:appsfeatures"),
        });

        // Chaque application du catalogue est trouvable par son nom : la recherche ouvre le catalogue filtré.
        foreach (var app in AppsCatalog.All)
        {
            r.AddSearchEntry(new SearchEntry
            {
                Id = "apps.install." + app.WingetId,
                Title = L("Install {0}", app.Name),
                Subtitle = app.Category + " · " + app.Description,
                Glyph = AppsCatalog.GlyphFor(app.Category),
                Keywords = [app.Name, app.WingetId, L("install, download")],
                Kind = SearchEntryKind.Feature,
                PageId = PageId,
                PageParameter = "search:" + app.Name,
                Boost = -0.05,
            });
        }
    }

    private static void Feature(ModuleRegistry r, string id, string title, string subtitle, string glyph, string parameter, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id, Title = title, Subtitle = subtitle, Glyph = glyph, Keywords = keywords,
            Kind = SearchEntryKind.Feature, PageId = PageId, PageParameter = parameter,
        });
}
