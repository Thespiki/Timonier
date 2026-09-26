using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Privacy;

/// <summary>
/// Confidentialité : télémétrie, publicité et suggestions, recherche et IA, historique d'activité, saisie et voix,
/// localisation et autorisations des applications (compte courant). Déclarations uniquement : appelé aussi dans le broker.
/// </summary>
public sealed class PrivacyModule : IModule
{
    public const string Category = "privacy";
    public const string PageId = "privacy";
    private const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, L("Confidentialité"), Glyph,
            L("Télémétrie, publicité, recherche, IA, historique d'activité et autorisations des applications.")));

        r.AddTweaks(PrivacyTweaks.All());

        r.AddPage(new PageInfo(PageId, L("Confidentialité"), Glyph, NavSection.Settings, 10, () => new PrivacyPage())
        {
            CategoryId = Category,
            Description = L("Score de confidentialité, télémétrie, publicité, Copilot et Recall, autorisations des applications."),
            Keywords = [L("vie privée, télémétrie, confidentialité, privacy, données personnelles, espionnage, pistage, autorisations, publicité, suggestions, copilot, recall")],
        });

        // Contrôle de santé du tableau de bord : exécuté dans l'interface uniquement (hors thread UI), lecture seule.
        r.AddHealthCheck(HealthCheck.Sync("privacy.score", L("Confidentialité"), Glyph, PageId,
            () => PrivacyScore.ToHealth(PrivacyScore.Compute(AppHost.Registry.TweaksIn(Category), AppHost.Profile))));

        r.AddQuickAction(new QuickAction("privacy.apply-recommended", L("Appliquer le niveau de confidentialité recommandé"), Glyph,
            L("Aligne les réglages de confidentialité sur les recommandations de Timonier pour ce PC, après confirmation. Tout reste annulable."),
            async () => await PrivacyRecommendations.RunAsync())
        {
            Keywords = [L("confidentialité, vie privée, télémétrie, recommandé, privacy, protéger mes données")],
            Order = 20,
            RequiresAdmin = true,
        });

        RegisterSearchEntries(r);

        Synonyms.AddGroup("recall", "retrouver", "instantane", "snapshot");
        Synonyms.AddGroup("click to do", "actions par clic");
        Synonyms.AddGroup("autorisation", "permission", "acces application", "app permission", "consentement");
        Synonyms.AddGroup("spotlight", "windows a la une", "a la une");
        Synonyms.AddGroup("commentaires", "feedback", "siuf", "demande avis");
        Synonyms.AddGroup("rapport erreur", "error reporting", "wer", "plantage", "crash");
        Synonyms.AddGroup("historique activite", "activity history", "chronologie", "timeline");
        Synonyms.AddGroup("identifiant publicite", "advertising id", "id publicitaire", "ciblage publicitaire");
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        r.AddSearchEntry(new SearchEntry
        {
            Id = "privacy.score",
            Title = L("Score de confidentialité"),
            Subtitle = L("Part des réglages de confidentialité au niveau recommandé pour ce PC"),
            Glyph = Glyph,
            Keywords = [L("score, niveau confidentialite, audit, bilan vie privee, privacy score")],
            PageId = PageId,
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "privacy.section.permissions",
            Title = L("Autorisations des applications"),
            Subtitle = L("Caméra, micro, position, contacts, calendrier… pour votre compte"),
            Glyph = Glyph,
            Keywords = [L("autorisations, permissions, acces applications, camera, micro, contacts, calendrier, position")],
            PageId = PageId,
            PageParameter = "section:permissions",
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "privacy.section.telemetry",
            Title = L("Télémétrie et données de diagnostic"),
            Subtitle = L("Niveau de diagnostic, service DiagTrack, rapports d'erreurs, tâches de collecte"),
            Glyph = "",
            Keywords = [L("telemetrie, telemetry, diagnostic, diagtrack, collecte donnees")],
            PageId = PageId,
            PageParameter = "section:telemetry",
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "privacy.section.ai",
            Title = L("Copilot, Recall et recherche web"),
            Subtitle = L("Bing dans la recherche, Copilot, Recall (Retrouver), Click to Do"),
            Glyph = "",
            Keywords = [L("copilot, recall, ia, intelligence artificielle, bing, recherche web")],
            PageId = PageId,
            PageParameter = "section:search",
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "privacy.section.ads",
            Title = L("Publicités et suggestions de Windows"),
            Subtitle = L("Identifiant de publicité, suggestions, applications installées automatiquement"),
            Glyph = "",
            Keywords = [L("publicite, pub, suggestions, bloatware, applications sponsorisees")],
            PageId = PageId,
            PageParameter = "section:ads",
        });

        AddWindowsSetting(r, "privacy.win.privacy", L("Confidentialité et sécurité (Paramètres Windows)"),
            L("Ouvre la page Confidentialité et sécurité des Paramètres"), "ms-settings:privacy",
            [L("parametres confidentialite, privacy settings, confidentialite securite")]);
        AddWindowsSetting(r, "privacy.win.diagnostics", L("Diagnostics et commentaires (Paramètres Windows)"),
            L("Voir ou supprimer les données de diagnostic envoyées à Microsoft"), "ms-settings:privacy-feedback",
            [L("supprimer donnees diagnostic, visionneuse donnees diagnostic, diagnostic data viewer, delete diagnostic data")]);
        AddWindowsSetting(r, "privacy.win.activity", L("Historique des activités (Paramètres Windows)"),
            L("Effacer l'historique des activités de ce compte"), "ms-settings:privacy-activityhistory",
            [L("effacer historique activite, clear activity history")]);
    }

    private static void AddWindowsSetting(ModuleRegistry r, string id, string title, string subtitle, string uri, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id,
            Title = title,
            Subtitle = subtitle,
            Glyph = "",
            Keywords = keywords,
            Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri(uri),
        });
}
