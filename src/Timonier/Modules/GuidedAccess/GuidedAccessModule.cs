using Timonier.Core.Catalog;
using Timonier.Core.Search;
using Timonier.UI.Services;

namespace Timonier.Modules.GuidedAccess;

/// <summary>
/// Accès guidé : verrouille le PC sur une seule application jusqu'à la saisie d'un code (comme sur iPhone).
/// Entièrement au niveau utilisateur, sans élévation et sans modifier durablement Windows.
/// Déclarations uniquement : Register est aussi appelé dans le broker.
/// </summary>
public sealed class GuidedAccessModule : IModule
{
    public const string PageId = "guided";
    internal const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddPage(new PageInfo(PageId, L("Guided access"), Glyph, NavSection.Control, 50, () => new GuidedPage())
        {
            Description = L("Lock the PC to a single app until a code is entered."),
            Keywords = [L("guided access, lock an app, kids mode, child mode, single app, block windows key, alt tab, lend your pc, parental controls, pin an app")],
        });

        r.AddQuickAction(new QuickAction("guided.start", L("Start guided access"), Glyph,
            L("Lock the PC to one app until a code is entered."),
            () => { AppHost.Navigator.Navigate(PageId); return Task.CompletedTask; })
        {
            Keywords = [L("guided access, kids mode, child mode, lock an app, lend your pc")],
            Order = 60,
        });

        // Session interrompue brutalement (Timonier arrêté de force) : la barre des tâches est réaffichée dès le démarrage
        // de l'interface, quelle que soit la page ouverte (ou en arrière-plan).
        r.AddUiStartupTask("guided.taskbar-recovery", () =>
        {
            if (TaskbarGuard.RecoverIfNeeded())
                AppHost.Toasts?.Show(L("The taskbar, which stayed hidden after an interrupted guided access session, has been shown again."), ToastKind.Info);
        });

        // Lecture seule (préférences).
        r.AddHealthCheck(HealthCheck.Sync("guided.state", L("Guided access"), Glyph, PageId, () =>
        {
            return GuidedPin.IsSet
                ? new HealthResult(HealthStatus.Good, L("Exit code set"), L("Guided access is ready to use."))
                : new HealthResult(HealthStatus.Info, L("No exit code"), L("Set a code to be able to start guided access."));
        }));

        r.AddSearchEntry(new SearchEntry
        {
            Id = "guided.search.main", Title = L("Guided access"), Subtitle = L("Lock the PC to a single app with a code"),
            Glyph = Glyph, PageId = PageId, Keywords = [L("guided access, simple kiosk, single app, one app, lock app")], Boost = 0.1,
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "guided.search.lockapp", Title = L("Lock an app"), Subtitle = L("Guided access · prevent leaving the app"),
            Glyph = "", PageId = PageId, Keywords = [L("lock an app, lock to one app, block alt tab, prevent alt tab, block windows key")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "guided.search.child", Title = L("Kids mode"),Subtitle = L("Guided access · lend the PC to a child on a single app"),
            Glyph = "", PageId = PageId, Keywords = [L("kids mode, child, kids, lend the pc, parental controls, time limit")],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "guided.search.pin", Title = L("Guided access code"), Subtitle = L("Guided access · set or change the exit code"),
            Glyph = "", PageId = PageId, PageParameter = "section:pin", Keywords = [L("guided access code, pin, exit code, unlock code")],
        });

        Synonyms.AddGroup("acces guide", "guided access", "mode enfant", "verrouiller une application", "app unique");
    }
}
