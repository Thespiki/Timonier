using Timonier.Core.Catalog;
using Timonier.Core.Search;

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
        r.AddPage(new PageInfo(PageId, "Accès guidé", Glyph, NavSection.Control, 50, () => new GuidedPage())
        {
            Description = "Verrouiller le PC sur une seule application jusqu'à la saisie d'un code.",
            Keywords = ["accès guidé", "guided access", "verrouiller une application", "mode enfant", "une seule application",
                        "bloquer touche windows", "alt tab", "prêter son pc", "contrôle parental", "épingler une application"],
        });

        r.AddQuickAction(new QuickAction("guided.start", "Démarrer l'accès guidé", Glyph,
            "Verrouiller le PC sur une application jusqu'à la saisie d'un code.",
            () => { AppHost.Navigator.Navigate(PageId); return Task.CompletedTask; })
        {
            Keywords = ["accès guidé", "mode enfant", "verrouiller une application", "prêter le pc"],
            Order = 60,
        });

        // Lecture seule (préférences) ; si une session a été interrompue brutalement, réaffiche la barre des tâches.
        r.AddHealthCheck(HealthCheck.Sync("guided.state", "Accès guidé", Glyph, PageId, () =>
        {
            if (TaskbarGuard.RecoverIfNeeded())
                return new HealthResult(HealthStatus.Warning, "Barre des tâches réaffichée",
                    "Une session d'accès guidé avait été interrompue brutalement (Timonier arrêté de force) : la barre des tâches masquée a été restaurée.");
            return GuidedPin.IsSet
                ? new HealthResult(HealthStatus.Good, "Code de sortie défini", "L'accès guidé est prêt à l'emploi.")
                : new HealthResult(HealthStatus.Info, "Aucun code de sortie", "Définissez un code pour pouvoir démarrer l'accès guidé.");
        }));

        r.AddSearchEntry(new SearchEntry
        {
            Id = "guided.search.main", Title = "Accès guidé", Subtitle = "Verrouiller le PC sur une seule application avec un code",
            Glyph = Glyph, PageId = PageId, Keywords = ["accès guidé", "guided access", "kiosque simple", "une seule app"], Boost = 0.1,
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "guided.search.lockapp", Title = "Verrouiller une application", Subtitle = "Accès guidé · empêcher de quitter l'application",
            Glyph = "", PageId = PageId, Keywords = ["verrouiller une application", "bloquer sur une application", "empêcher alt tab", "bloquer touche windows"],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "guided.search.child", Title = "Mode enfant", Subtitle = "Accès guidé · prêter le PC à un enfant sur une seule application",
            Glyph = "", PageId = PageId, Keywords = ["mode enfant", "enfant", "prêter le pc", "contrôle parental", "limite de temps"],
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "guided.search.pin", Title = "Code de l'accès guidé", Subtitle = "Accès guidé · définir ou modifier le code de sortie",
            Glyph = "", PageId = PageId, PageParameter = "section:pin", Keywords = ["code accès guidé", "pin", "code de sortie"],
        });

        Synonyms.AddGroup("acces guide", "guided access", "mode enfant", "verrouiller une application", "app unique");
    }
}
