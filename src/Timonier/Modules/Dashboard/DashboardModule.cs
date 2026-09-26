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
        r.AddPage(new PageInfo(PageId, L("Accueil"), Glyph, NavSection.Overview, 0, () => new DashboardPage())
        {
            Description = L("Vue d'ensemble : état de santé, mesures en direct, recommandations et actions rapides."),
            Keywords = [L("accueil, tableau de bord, dashboard, vue d'ensemble, résumé, mon pc, informations système, état du pc, santé")],
        });

        // Contrôles de santé (lecture seule, exécutés par la page hors du thread UI).
        r.AddHealthCheck(HealthCheck.Sync("dashboard.disk-space", L("Espace disque"), "", "maintenance", DashboardHealth.DiskSpace));
        r.AddHealthCheck(HealthCheck.Sync("dashboard.uptime", L("Temps depuis le dernier redémarrage"), "", null, DashboardHealth.Uptime));
        r.AddHealthCheck(HealthCheck.Sync("dashboard.pending-reboot", L("Redémarrage en attente"), "", null, DashboardHealth.PendingReboot));
        r.AddHealthCheck(HealthCheck.Sync("dashboard.disk-health", L("Santé des disques"), "", null, DashboardHealth.DiskHealth));

        // Actions rapides (sans élévation).
        r.AddQuickAction(new QuickAction("dashboard.restart-explorer", L("Redémarrer l'Explorateur"), "",
            L("Relance la barre des tâches et le bureau quand ils ne répondent plus ou après un réglage d'apparence."),
            DashboardQuickActions.RestartExplorerAsync)
        {
            Keywords = [L("explorateur, explorer.exe, redemarrer explorateur, barre des taches bloquee, bureau fige, restart explorer")],
            Order = 20,
        });
        r.AddQuickAction(new QuickAction("dashboard.task-manager", L("Gestionnaire des tâches"), "",
            L("Voir les applications et processus ouverts, leur consommation, et fermer celles qui ne répondent plus."),
            DashboardQuickActions.OpenTaskManagerAsync)
        {
            Keywords = [L("gestionnaire des taches, task manager, taskmgr, processus, application bloquee, ctrl alt suppr")],
            Order = 10,
        });
        r.AddQuickAction(new QuickAction("dashboard.windows-settings", L("Paramètres Windows"), "",
            L("Ouvre l'application Paramètres de Windows."),
            DashboardQuickActions.OpenSettingsAsync)
        {
            Keywords = [L("parametres, settings, ms-settings, reglages windows, panneau de configuration")],
            Order = 90,
        });
        r.AddQuickAction(new QuickAction("dashboard.lock", L("Verrouiller le PC"), "",
            L("Verrouille immédiatement la session (comme Windows + L). Vos applications restent ouvertes."),
            DashboardQuickActions.LockAsync)
        {
            Keywords = [L("verrouiller, lock, verrouillage, windows l, ecran de verrouillage, quitter le poste")],
            Order = 15,
        });

        r.AddSearchEntries(
        [
            new SearchEntry
            {
                Id = "dashboard.section.health", Title = L("Santé du PC"),
                Subtitle = L("Espace disque, redémarrage en attente, sécurité, disques… tous les contrôles en un coup d'œil"),
                Glyph = "", PageId = PageId, PageParameter = "section:health", Boost = 0.1,
                Keywords = [L("sante, etat du pc, diagnostic, verification, probleme, check-up, bilan")],
            },
            new SearchEntry
            {
                Id = "dashboard.section.live", Title = L("Utilisation du processeur et de la mémoire"),
                Subtitle = L("Processeur, mémoire, disque, batterie et réseau en direct"),
                Glyph = "", PageId = PageId, PageParameter = "section:live",
                Keywords = [L("cpu, processeur, ram, memoire, utilisation, charge, batterie, temps de fonctionnement, uptime")],
            },
            new SearchEntry
            {
                Id = "dashboard.section.reco", Title = L("Recommandations pour ce PC"),
                Subtitle = L("Réglages qui diffèrent de la recommandation de Timonier, par catégorie"),
                Glyph = "", PageId = PageId, PageParameter = "section:reco",
                Keywords = [L("recommandations, conseils, optimiser, ameliorer, suggestions")],
            },
            new SearchEntry
            {
                Id = "dashboard.section.pc", Title = L("Informations sur ce PC"),
                Subtitle = L("Modèle, Windows, processeur, mémoire, carte graphique, disque, Secure Boot"),
                Glyph = "", PageId = PageId, PageParameter = "section:pc",
                Keywords = [L("informations systeme, configuration, specifications, modele, version de windows, mon pc, fiche technique")],
            },
            new SearchEntry
            {
                Id = "dashboard.section.vendor", Title = L("Outils du fabricant"),
                Subtitle = L("Lenovo Vantage, HP Support Assistant, MyASUS, pilotes NVIDIA, AMD ou Intel…"),
                Glyph = "", PageId = PageId, PageParameter = "section:vendor",
                Keywords = [L("fabricant, constructeur, vantage, myasus, support assistant, supportassist, pilotes graphiques, bios")],
            },
        ]);

        Synonyms.AddGroup("accueil", "tableau de bord", "dashboard", "vue d'ensemble", "page d'accueil");
        Synonyms.AddGroup("verrouiller", "verrouillage", "lock", "bloquer la session");
        Synonyms.AddGroup("gestionnaire des taches", "task manager", "taskmgr");
        Synonyms.AddGroup("temps de fonctionnement", "uptime", "allume depuis", "dernier redemarrage");
    }
}
