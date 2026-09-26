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
        r.AddCategory(new CategoryInfo(Category, L("Startup & services"), Glyph,
            L("Apps that run at sign-in, Windows services and scheduled tasks.")));

        r.AddTweaks(StartupTweaks.All());

        r.AddAction(new SetStartupItemEnabledUserAction());
        r.AddAction(new SetStartupItemEnabledAction());
        r.AddAction(new DeleteStartupItemUserAction());
        r.AddAction(new DeleteStartupItemAction());
        r.AddAction(new SetServiceStartAction());
        r.AddAction(new ServiceControlAction());
        r.AddAction(new SetTaskEnabledAction());

        r.AddPage(new PageInfo(PageId, L("Startup & services"), Glyph, NavSection.Tools, 20, () => new StartupPage())
        {
            CategoryId = Category,
            Description = L("Startup apps, Windows services and scheduled tasks: view, enable, disable."),
            Keywords = [L("startup, sign-in, login, autostart, services, scheduled tasks, slow startup, slow boot, boot, autorun, msconfig, task manager")],
        });

        // Contrôle de santé : lecture seule du registre et des dossiers Démarrage (exécuté dans l'interface, hors thread UI).
        r.AddHealthCheck(HealthCheck.Sync("startup.items", L("Startup apps"), Glyph, PageId, CheckStartup));

        r.AddQuickAction(new QuickAction("startup.open", L("Manage startup apps"), Glyph,
            L("See and disable the apps that run at sign-in."),
            () => { AppHost.Navigator.Navigate(PageId, "section:apps"); return Task.CompletedTask; })
        {
            Keywords = [L("startup, autostart, speed up startup, faster boot, slow startup, slow boot")],
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
        var summary = LP(enabled, "{0} app runs at startup", "{0} apps run at startup");
        if (enabled > threshold)
            return new HealthResult(HealthStatus.Warning, summary,
                low
                    ? L("Beyond {0} apps on an entry-level PC, sign-in slows down noticeably. Disable the ones you don't need right at startup.", threshold)
                    : L("Beyond {0} apps, sign-in slows down noticeably. Disable the ones you don't need right at startup.", threshold));
        return new HealthResult(HealthStatus.Info, summary, LP(total - enabled, "{0} of {1} disabled.", "{0} of {1} disabled.", total));
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.section.apps", Title = L("Startup apps"),
            Subtitle = L("Enable or disable the programs that run at sign-in"),
            Glyph = Glyph, PageId = PageId, PageParameter = "section:apps", Boost = 0.1,
            Keywords = [L("startup apps, startup programs, run, disable startup, autorun, autostart")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.section.services", Title = L("Windows services"),
            Subtitle = L("Startup type, start or stop a service"),
            Glyph = ServicesGlyph, PageId = PageId, PageParameter = "section:services",
            Keywords = [L("services, windows service, services.msc, startup type, stop service, disable service")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.section.tasks", Title = L("Scheduled tasks"),
            Subtitle = L("App tasks that run at startup or at set times"),
            Glyph = TasksGlyph, PageId = PageId, PageParameter = "section:tasks",
            Keywords = [L("scheduled tasks, scheduler, task scheduler, automatic app updates")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.win.startupapps", Title = L("Startup apps (Windows Settings)"),
            Subtitle = L("Opens Settings › Apps › Startup"), Glyph = Glyph, Kind = SearchEntryKind.WindowsSetting,
            Keywords = [L("startup settings, startup apps settings, startup apps")],
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:startupapps"),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.tool.services", Title = L("Services console (services.msc)"),
            Subtitle = L("Windows services administration tool"), Glyph = ServicesGlyph, Kind = SearchEntryKind.Tool,
            Keywords = [L("services.msc, services console, service manager")],
            Execute = () => StartupUi.OpenConsole("services.msc"),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "startup.tool.taskschd", Title = L("Task Scheduler (taskschd.msc)"),
            Subtitle = L("Scheduled tasks administration tool"), Glyph = TasksGlyph, Kind = SearchEntryKind.Tool,
            Keywords = [L("taskschd.msc, task scheduler, scheduled tasks")],
            Execute = () => StartupUi.OpenConsole("taskschd.msc"),
        });
    }
}
