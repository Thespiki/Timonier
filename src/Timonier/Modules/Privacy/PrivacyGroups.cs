namespace Timonier.Modules.Privacy;

/// <summary>Section de la page Confidentialité (= groupe des réglages).</summary>
/// <param name="Key">Identifiant stable (ancre de navigation « section:&lt;clé&gt; »).</param>
/// <param name="Title">Titre du groupe (TweakDefinition.Group), affiché comme titre de section.</param>
/// <param name="ShortTitle">Libellé court (pastilles du score).</param>
/// <param name="Glyph">Icône Segoe Fluent Icons.</param>
public sealed record PrivacySection(string Key, string Title, string ShortTitle, string Glyph);

/// <summary>Groupes de réglages de confidentialité, dans l'ordre d'affichage.</summary>
public static class PrivacyGroups
{
    public const string Telemetry = "Télémétrie et diagnostics";
    public const string Ads = "Publicité et suggestions";
    public const string Search = "Recherche et IA";
    public const string Activity = "Activité et historique";
    public const string Input = "Saisie, voix et langue";
    public const string Location = "Localisation";
    public const string Permissions = "Autorisations des applications";

    public static readonly IReadOnlyList<PrivacySection> All =
    [
        new("telemetry", Telemetry, "Télémétrie", ""),
        new("ads", Ads, "Publicité", ""),
        new("search", Search, "Recherche et IA", ""),
        new("activity", Activity, "Activité", ""),
        new("input", Input, "Saisie et voix", ""),
        new("location", Location, "Localisation", ""),
        new("permissions", Permissions, "Autorisations", ""),
    ];

    public static PrivacySection? ByKey(string key) => All.FirstOrDefault(s => s.Key == key);

    public static PrivacySection? ByGroup(string? group) => group is null ? null : All.FirstOrDefault(s => s.Title == group);
}
