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
    public static readonly string Telemetry = L("Telemetry and diagnostics");
    public static readonly string Ads = L("Ads and suggestions");
    public static readonly string Search = L("Search and AI");
    public static readonly string Activity = L("Activity and history");
    public static readonly string Input = L("Typing, voice and language");
    public static readonly string Location = L("Location");
    public static readonly string Permissions = L("App permissions");

    public static readonly IReadOnlyList<PrivacySection> All =
    [
        new("telemetry", Telemetry, L("Telemetry"), ""),
        new("ads", Ads, L("Ads"), ""),
        new("search", Search, L("Search and AI"), ""),
        new("activity", Activity, L("Activity"), ""),
        new("input", Input, L("Typing and voice"), ""),
        new("location", Location, L("Location"), ""),
        new("permissions", Permissions, L("Permissions"), ""),
    ];

    public static PrivacySection? ByKey(string key) => All.FirstOrDefault(s => s.Key == key);

    public static PrivacySection? ByGroup(string? group) => group is null ? null : All.FirstOrDefault(s => s.Title == group);
}
