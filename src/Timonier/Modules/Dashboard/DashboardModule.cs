using Timonier.Core.Catalog;
using Timonier.Core.Search;

namespace Timonier.Modules.Dashboard;

/// <summary>
/// Accueil (tableau de bord) : identité du PC, mesures en direct, santé, recommandations, actions rapides, outils du
/// fabricant et transparence. Déclarations uniquement : appelé aussi dans le broker (aucun accès à AppHost ici).
/// </summary>
public sealed class DashboardModule : IModule
{
    public const string PageId = "dashboard";
    internal const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddPage(new PageInfo(PageId, L("Home"), Glyph, NavSection.Overview, 0, () => new DashboardPage())
        {
            Description = L("Overview: health status, live metrics, recommendations and quick actions."),
            Keywords = [L("home, dashboard, overview, summary, my pc, system information, pc status, health, status")],
        });

        // Contrôles de santé (lecture seule, exécutés par la page hors du thread UI).
        r.AddHealthCheck(HealthCheck.Sync("dashboard.disk-space", L("Disk space"), "", "maintenance", DashboardHealth.DiskSpace));
        r.AddHealthCheck(HealthCheck.Sync("dashboard.uptime", L("Time since last restart"), "", null, DashboardHealth.Uptime));
        r.AddHealthCheck(HealthCheck.Sync("dashboard.pending-reboot", L("Restart pending"), "", null, DashboardHealth.PendingReboot));
        r.AddHealthCheck(HealthCheck.Sync("dashboard.disk-health", L("Disk health"), "", null, DashboardHealth.DiskHealth));

        // Actions rapides (sans élévation).
        r.AddQuickAction(new QuickAction("dashboard.restart-explorer", LC("quick action", "Restart Explorer"), "",
            L("Restarts the taskbar and desktop when they stop responding or after an appearance setting."),
            DashboardQuickActions.RestartExplorerAsync)
        {
            Keywords = [L("explorer, explorer.exe, restart explorer, taskbar frozen, taskbar not responding, desktop frozen, file explorer")],
            Order = 20,
        });
        r.AddQuickAction(new QuickAction("dashboard.task-manager", L("Task Manager"), "",
            L("See open apps and processes and their resource usage, and close the ones that stop responding."),
            DashboardQuickActions.OpenTaskManagerAsync)
        {
            Keywords = [L("task manager, taskmgr, processes, frozen app, not responding, end task, ctrl alt del")],
            Order = 10,
        });
        r.AddQuickAction(new QuickAction("dashboard.windows-settings", L("Windows Settings"), "",
            L("Opens the Windows Settings app."),
            DashboardQuickActions.OpenSettingsAsync)
        {
            Keywords = [L("settings, ms-settings, windows settings, control panel, preferences")],
            Order = 90,
        });
        r.AddQuickAction(new QuickAction("dashboard.lock", L("Lock the PC"), "",
            L("Locks the session immediately (like Windows + L). Your apps stay open."),
            DashboardQuickActions.LockAsync)
        {
            Keywords = [L("lock, lock pc, lock screen, windows l, win+l, lock computer, step away")],
            Order = 15,
        });

        r.AddSearchEntries(
        [
            new SearchEntry
            {
                Id = "dashboard.section.health", Title = L("PC health"),
                Subtitle = L("Disk space, pending restart, security, disks… every check at a glance"),
                Glyph = "", PageId = PageId, PageParameter = "section:health", Boost = 0.1,
                Keywords = [L("health, pc status, diagnostics, check, problem, checkup, health check")],
            },
            new SearchEntry
            {
                Id = "dashboard.section.live", Title = L("Processor and memory usage"),
                Subtitle = L("Processor, memory, disk, battery and network, live"),
                Glyph = "", PageId = PageId, PageParameter = "section:live",
                Keywords = [L("cpu, processor, ram, memory, usage, load, battery, uptime")],
            },
            new SearchEntry
            {
                Id = "dashboard.section.reco", Title = L("Recommendations for this PC"),
                Subtitle = L("Settings that differ from Timonier's recommendation, by category"),
                Glyph = "", PageId = PageId, PageParameter = "section:reco",
                Keywords = [L("recommendations, tips, optimize, improve, suggestions, advice")],
            },
            new SearchEntry
            {
                Id = "dashboard.section.pc", Title = L("PC information"),
                Subtitle = L("Model, Windows, processor, memory, graphics card, disk, Secure Boot"),
                Glyph = "", PageId = PageId, PageParameter = "section:pc",
                Keywords = [L("system information, configuration, specs, specifications, model, windows version, my pc, about, hardware")],
            },
            new SearchEntry
            {
                Id = "dashboard.section.vendor", Title = L("Manufacturer tools"),
                Subtitle = L("Lenovo Vantage, HP Support Assistant, MyASUS, NVIDIA, AMD or Intel drivers…"),
                Glyph = "", PageId = PageId, PageParameter = "section:vendor",
                Keywords = [L("manufacturer, oem, vantage, myasus, support assistant, supportassist, graphics drivers, bios")],
            },
        ]);

        Synonyms.AddGroup("accueil", "tableau de bord", "dashboard", "vue d'ensemble", "page d'accueil");
        Synonyms.AddGroup("verrouiller", "verrouillage", "lock", "bloquer la session");
        Synonyms.AddGroup("gestionnaire des taches", "task manager", "taskmgr");
        Synonyms.AddGroup("temps de fonctionnement", "uptime", "allume depuis", "dernier redemarrage");
    }
}
