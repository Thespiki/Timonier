using PcPilot.Core.Catalog;
using PcPilot.Core.Platform;
using PcPilot.Core.Search;

namespace PcPilot.Modules.Apps;

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
        r.AddAction(new AppxRemoveAction());
        r.AddAction(new AppxDeprovisionAction());

        r.AddPage(new PageInfo(PageId, "Applications", Glyph, NavSection.Tools, 10, () => new AppsPage())
        {
            CategoryId = Category,
            Description = "Installer des logiciels vérifiés en un clic, désinstaller des programmes, supprimer les applications préinstallées et tout mettre à jour.",
            Keywords = ["applications", "logiciels", "programmes", "installer", "désinstaller", "winget", "bloatware", "mises à jour",
                        "apps", "software", "store", "préinstallées"],
        });

        r.AddQuickAction(new QuickAction("apps.update-all", "Mettre à jour toutes les applications", "",
            "Installe toutes les mises à jour connues de winget (navigateurs, lecteurs, utilitaires…).",
            () => { AppHost.Navigator.Navigate(PageId, "action:update-all"); return Task.CompletedTask; })
        {
            Keywords = ["mettre a jour", "mise a jour logiciels", "winget upgrade", "update apps", "upgrade all", "actualiser les programmes"],
            Order = 30,
            RequiresAdmin = true,
        });
        r.AddQuickAction(new QuickAction("apps.remove-bloat", "Supprimer les applications préinstallées", "",
            "Candy Crush, TikTok, Actualités… : retirez les applications superflues installées d'office.",
            () => { AppHost.Navigator.Navigate(PageId, "view:bloat"); return Task.CompletedTask; })
        {
            Keywords = ["bloatware", "debloat", "applications inutiles", "candy crush", "nettoyer windows", "supprimer applications"],
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
        Feature(r, "apps.view.install", "Installer des applications", "Catalogue de logiciels vérifiés, installation groupée", "", "view:install",
            ["installer logiciel", "telecharger programme", "catalogue", "ninite", "installer plusieurs applications"]);
        Feature(r, "apps.view.installed", "Programmes installés", "Liste, taille, date d'installation et désinstallation", "", "view:installed",
            ["desinstaller", "programmes et fonctionnalites", "ajout suppression de programmes", "liste des logiciels", "taille des programmes"]);
        Feature(r, "apps.view.bloat", "Applications préinstallées", "Supprimer Candy Crush, TikTok, Actualités, Clipchamp…", "", "view:bloat",
            ["bloatware", "candy crush", "applications inutiles", "cortana", "xbox", "debloat", "supprimer applications windows"]);
        Feature(r, "apps.view.updates", "Mises à jour des applications", "Voir et installer les nouvelles versions de vos logiciels", "", "view:updates",
            ["mise a jour logiciels", "winget upgrade", "versions", "obsolete", "mettre a jour programmes"]);

        r.AddSearchEntry(new SearchEntry
        {
            Id = "apps.win.appsfeatures",
            Title = "Applications installées (Paramètres Windows)",
            Subtitle = "Page des Paramètres pour modifier ou désinstaller une application",
            Glyph = "",
            Keywords = ["applications installees", "appwiz", "programmes et fonctionnalites", "apps features"],
            Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:appsfeatures"),
        });

        // Chaque application du catalogue est trouvable par son nom : la recherche ouvre le catalogue filtré.
        foreach (var app in AppsCatalog.All)
        {
            r.AddSearchEntry(new SearchEntry
            {
                Id = "apps.install." + app.WingetId,
                Title = "Installer " + app.Name,
                Subtitle = app.Category + " · " + app.Description,
                Glyph = AppsCatalog.GlyphFor(app.Category),
                Keywords = [app.Name, app.WingetId, "installer", "telecharger"],
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
