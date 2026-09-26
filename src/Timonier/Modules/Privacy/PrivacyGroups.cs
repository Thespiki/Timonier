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
    // Titres traduits : même valeur pour TweakDefinition.Group et PrivacySection.Title (ByGroup compare les deux).
    public static readonly string Telemetry = L("Télémétrie et diagnostics");
    public static readonly string Ads = L("Publicité et suggestions");
    public static readonly string Search = L("Recherche et IA");
    public static readonly string Activity = L("Activité et historique");
    public static readonly string Input = L("Saisie, voix et langue");
    public static readonly string Location = L("Localisation");
    public static readonly string Permissions = L("Autorisations des applications");

    public static readonly IReadOnlyList<PrivacySection> All =
    [
        new("telemetry", Telemetry, L("Télémétrie"), ""),
        new("ads", Ads, L("Publicité"), ""),
        new("search", Search, L("Recherche et IA"), ""),
        new("activity", Activity, L("Activité"), ""),
        new("input", Input, L("Saisie et voix"), ""),
        new("location", Location, L("Localisation"), ""),
        new("permissions", Permissions, L("Autorisations"), ""),
    ];

    public static PrivacySection? ByKey(string key) => All.FirstOrDefault(s => s.Key == key);

    public static PrivacySection? ByGroup(string? group) => group is null ? null : All.FirstOrDefault(s => s.Title == group);
}
