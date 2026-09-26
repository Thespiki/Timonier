using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Startup;

/// <summary>
/// Démarrage et services : applications lancées à l'ouverture de session (format du Gestionnaire des tâches),
/// services Win32 et tâches planifiées. Déclarations uniquement : appelé aussi dans le broker.
/// </summary>
public sealed class StartupModule : IModule
{
    public const string Category = "startup";
    public const string PageId = "startup";
    internal const string Glyph = "";
    internal const string ServicesGlyph = "";
    internal const string TasksGlyph = "";
    internal const string AppsGlyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, L("Démarrage et services"), Glyph,
            L("Applications lancées à l'ouverture de session, services Windows et tâches planifiées.")));

        r.AddTweaks(StartupTweaks.All());

        r.AddAction(new SetStartupItemEnabledUserAction());
        r.AddAction(new SetStartupItemEnabledAction());
        r.AddAction(new DeleteStartupItemUserAction());
        r.AddAction(new DeleteStartupItemAction());
        r.AddAction(new SetServiceStartAction());
        r.AddAction(new ServiceControlAction());
        r.AddAction(new SetTaskEnabledAction());

        r.AddPage(new PageInfo(PageId, L("Démarrage et services"), Glyph, NavSection.Tools, 20, () => new StartupPage())
        {
            CategoryId = Category,
            Description = L("Applications au démarrage, services Windows et tâches planifiées : voir, activer, désactiver."),
            Keywords = [L("démarrage, startup, ouverture de session, lancement automatique, services, tâches planifiées, démarrage lent, boot, autorun, msconfig, gestionnaire des tâches")],
        });

        // Contrôle de santé : lecture seule du registre et des dossiers Démarrage (exécuté dans l'interface, hors thread UI).
        r.AddHealthCheck(HealthCheck.Sync("startup.items", L("Applications au démarrage"), Glyph, PageId, CheckStartup));

        r.AddQuickAction(new QuickAction("startup.open", L("Gérer les applications au démarrage"), Glyph,
            L("Voir et désactiver les applications qui se lancent à l'ouverture de session."),
            () => { AppHost.Navigator.Navigate(PageId, "section:apps"); return Task.CompletedTask; })
        {
            Keywords = [L("démarrage, startup, lancement automatique, accélérer le démarrage, démarrage lent")],
            Order = 30,
        });

        RegisterSearchEntries(r);

        Synonyms.AddGroup("demarrage", "startup", "lancement automatique", "autorun", "ouverture de session", "autostart");
        Synonyms.AddGroup("service", "services windows", "services.msc", "daemon");
        Synonyms.AddGroup("tache planifiee", "scheduled task", "planificateur", "task scheduler", "taskschd");
        Synonyms.AddGroup("demarrage lent", "pc lent au demarrage", "slow boot", "accelerer demarrage");
    }

    private static HealthResult CheckStartup()
    {
        var (enabled, total) = StartupInventory.Count();
        var low = AppHost.Profile?.Tier == PerformanceTier.Low;
        var threshold = low ? 8 : 12;
        var summary = LP(enabled, "{0} application lancée au démarrage", "{0} applications lancées au démarrage");
        if (enabled > threshold)
            return new HealthResult(HealthStatus.Warning, summary,
                low
                    ? L("Au-delà de {0} applications sur un PC d'entrée de gamme, l'ouverture de session ralentit nettement. Désactivez celles dont vous n'avez pas besoin dès le démarrage.", threshold)
                    : L("Au-delà de {0} applications, l'ouverture de session ralentit nettement. Désactivez celles dont vous n'avez pas besoin dès le démarrage.", threshold));
        return new HealthResult(HealthStatus.Info, summary, LP(total - enabled, "{0} désactivée sur {1}.", "{0} désactivées sur {1}.", total));
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.section.apps", Title = L("Applications au démarrage"),
            Subtitle = L("Activer ou désactiver les programmes lancés à l'ouverture de session"),
            Glyph = Glyph, PageId = PageId, PageParameter = "section:apps", Boost = 0.1,
            Keywords = [L("applications demarrage, programmes au demarrage, startup apps, run, desactiver demarrage, autorun")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.section.services", Title = L("Services Windows"),
            Subtitle = L("Type de démarrage, démarrer ou arrêter un service"),
            Glyph = ServicesGlyph, PageId = PageId, PageParameter = "section:services",
            Keywords = [L("services, service windows, services.msc, type de demarrage, arreter service, desactiver service")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.section.tasks", Title = L("Tâches planifiées"),
            Subtitle = L("Tâches des applications lancées au démarrage ou à heure fixe"),
            Glyph = TasksGlyph, PageId = PageId, PageParameter = "section:tasks",
            Keywords = [L("taches planifiees, planificateur, scheduled tasks, task scheduler, mises a jour automatiques applications")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.win.startupapps", Title = L("Applications de démarrage (Paramètres Windows)"),
            Subtitle = L("Ouvre Paramètres › Applications › Démarrage"), Glyph = Glyph, Kind = SearchEntryKind.WindowsSetting,
            Keywords = [L("parametres demarrage, startup apps settings")],
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:startupapps"),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.tool.services", Title = L("Console Services (services.msc)"),
            Subtitle = L("Outil d'administration des services de Windows"), Glyph = ServicesGlyph, Kind = SearchEntryKind.Tool,
            Keywords = [L("services.msc, console services, gestionnaire de services")],
            Execute = () => StartupUi.OpenConsole("services.msc"),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.tool.taskschd", Title = L("Planificateur de tâches (taskschd.msc)"),
            Subtitle = L("Outil d'administration des tâches planifiées"), Glyph = TasksGlyph, Kind = SearchEntryKind.Tool,
            Keywords = [L("taskschd.msc, planificateur de taches, task scheduler")],
            Execute = () => StartupUi.OpenConsole("taskschd.msc"),
        });
    }
}
