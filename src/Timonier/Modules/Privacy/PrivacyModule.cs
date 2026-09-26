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
        r.AddCategory(new CategoryInfo(Category, L("Privacy"), Glyph,
            L("Telemetry, ads, search, AI, activity history and app permissions.")));

        r.AddTweaks(PrivacyTweaks.All());

        r.AddPage(new PageInfo(PageId, L("Privacy"), Glyph, NavSection.Settings, 10, () => new PrivacyPage())
        {
            CategoryId = Category,
            Description = L("Privacy score, telemetry, ads, Copilot and Recall, app permissions."),
            Keywords = [L("privacy, telemetry, personal data, spying, tracking, permissions, ads, advertising, suggestions, copilot, recall")],
        });

        // Contrôle de santé du tableau de bord : exécuté dans l'interface uniquement (hors thread UI), lecture seule.
        r.AddHealthCheck(HealthCheck.Sync("privacy.score", L("Privacy"), Glyph, PageId,
            () => PrivacyScore.ToHealth(PrivacyScore.Compute(AppHost.Registry.TweaksIn(Category), AppHost.Profile))));

        r.AddQuickAction(new QuickAction("privacy.apply-recommended", L("Apply the recommended privacy level"), Glyph,
            L("Aligns privacy settings with Timonier's recommendations for this PC, after confirmation. Everything can still be undone."),
            async () => await PrivacyRecommendations.RunAsync())
        {
            Keywords = [L("privacy, telemetry, recommended, protect my data, privacy settings")],
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
            Title = L("Privacy score"),
            Subtitle = L("Share of privacy settings at the recommended level for this PC"),
            Glyph = Glyph,
            Keywords = [L("score, privacy level, audit, privacy check, privacy score, privacy report")],
            PageId = PageId,
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "privacy.section.permissions",
            Title = L("App permissions"),
            Subtitle = L("Camera, microphone, location, contacts, calendar… for your account"),
            Glyph = Glyph,
            Keywords = [L("permissions, app access, camera, microphone, mic, contacts, calendar, location")],
            PageId = PageId,
            PageParameter = "section:permissions",
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "privacy.section.telemetry",
            Title = L("Telemetry and diagnostic data"),
            Subtitle = L("Diagnostic level, DiagTrack service, error reporting, collection tasks"),
            Glyph = "",
            Keywords = [L("telemetry, diagnostics, diagtrack, data collection, diagnostic data")],
            PageId = PageId,
            PageParameter = "section:telemetry",
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "privacy.section.ai",
            Title = L("Copilot, Recall and web search"),
            Subtitle = L("Bing in search, Copilot, Recall, Click to Do"),
            Glyph = "",
            Keywords = [L("copilot, recall, ai, artificial intelligence, bing, web search")],
            PageId = PageId,
            PageParameter = "section:search",
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "privacy.section.ads",
            Title = L("Windows ads and suggestions"),
            Subtitle = L("Advertising ID, suggestions, automatically installed apps"),
            Glyph = "",
            Keywords = [L("advertising, ads, suggestions, bloatware, sponsored apps, advertising id")],
            PageId = PageId,
            PageParameter = "section:ads",
        });

        AddWindowsSetting(r, "privacy.win.privacy", L("Privacy & security (Windows Settings)"),
            L("Opens the Privacy & security page in Settings"), "ms-settings:privacy",
            [L("privacy settings, privacy & security, privacy and security")]);
        AddWindowsSetting(r, "privacy.win.diagnostics", L("Diagnostics & feedback (Windows Settings)"),
            L("View or delete diagnostic data sent to Microsoft"), "ms-settings:privacy-feedback",
            [L("delete diagnostic data, diagnostic data viewer, diagnostic data, view diagnostic data")]);
        AddWindowsSetting(r, "privacy.win.activity", L("Activity history (Windows Settings)"),
            L("Clear this account's activity history"), "ms-settings:privacy-activityhistory",
            [L("clear activity history, activity history, delete activity history")]);
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
