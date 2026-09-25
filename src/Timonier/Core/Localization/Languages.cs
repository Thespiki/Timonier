using System.Globalization;
using System.Text.RegularExpressions;

namespace Timonier.Core.Localization;

/// <summary>Langue proposée par l'application.</summary>
/// <param name="Code">Code BCP 47 utilisé pour le fichier de traduction (Localization/&lt;code&gt;.json).</param>
/// <param name="NativeName">Nom de la langue dans cette langue (affiché dans le sélecteur).</param>
/// <param name="EnglishName">Nom anglais (documentation, journaux).</param>
/// <param name="RightToLeft">Écriture de droite à gauche (interface en miroir).</param>
public sealed record LanguageInfo(string Code, string NativeName, string EnglishName, bool RightToLeft = false);

/// <summary>Langues prises en charge et correspondance avec la langue d'affichage de Windows.</summary>
public static partial class Languages
{
    public static readonly IReadOnlyList<LanguageInfo> All =
    [
        new("en", "English", "English"),
        new("fr", "Français", "French"),
        new("de", "Deutsch", "German"),
        new("es", "Español", "Spanish"),
        new("it", "Italiano", "Italian"),
        new("pt-BR", "Português (Brasil)", "Portuguese (Brazil)"),
        new("pt-PT", "Português (Portugal)", "Portuguese (Portugal)"),
        new("nl", "Nederlands", "Dutch"),
        new("pl", "Polski", "Polish"),
        new("cs", "Čeština", "Czech"),
        new("sk", "Slovenčina", "Slovak"),
        new("sl", "Slovenščina", "Slovenian"),
        new("hr", "Hrvatski", "Croatian"),
        new("sr-Latn", "Srpski (latinica)", "Serbian (Latin)"),
        new("hu", "Magyar", "Hungarian"),
        new("ro", "Română", "Romanian"),
        new("bg", "Български", "Bulgarian"),
        new("el", "Ελληνικά", "Greek"),
        new("ru", "Русский", "Russian"),
        new("uk", "Українська", "Ukrainian"),
        new("tr", "Türkçe", "Turkish"),
        new("sv", "Svenska", "Swedish"),
        new("da", "Dansk", "Danish"),
        new("nb", "Norsk bokmål", "Norwegian Bokmål"),
        new("fi", "Suomi", "Finnish"),
        new("et", "Eesti", "Estonian"),
        new("lv", "Latviešu", "Latvian"),
        new("lt", "Lietuvių", "Lithuanian"),
        new("ca", "Català", "Catalan"),
        new("ar", "العربية", "Arabic", RightToLeft: true),
        new("he", "עברית", "Hebrew", RightToLeft: true),
        new("ja", "日本語", "Japanese"),
        new("ko", "한국어", "Korean"),
        new("zh-Hans", "中文（简体）", "Chinese (Simplified)"),
        new("zh-Hant", "中文（繁體）", "Chinese (Traditional)"),
        new("th", "ไทย", "Thai"),
        new("vi", "Tiếng Việt", "Vietnamese"),
        new("id", "Bahasa Indonesia", "Indonesian"),
        new("hi", "हिन्दी", "Hindi"),
    ];

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8}){0,2}$")]
    private static partial Regex CodeShape();

    /// <summary>Langue connue pour ce code exact (insensible à la casse), sinon null. Sert aussi à valider un argument.</summary>
    public static LanguageInfo? Find(string? code) =>
        code is { Length: <= 20 } && CodeShape().IsMatch(code)
            ? All.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>Langue de l'application la plus proche d'une culture Windows (ex. fr-CA → fr, zh-TW → zh-Hant), sinon null.</summary>
    public static LanguageInfo? Match(CultureInfo culture)
    {
        var name = culture.Name;
        if (Find(name) is { } exact) return exact;
        var lang = culture.TwoLetterISOLanguageName;
        switch (lang)
        {
            case "zh":
                var traditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);
                return Find(traditional ? "zh-Hant" : "zh-Hans");
            case "pt": return Find(name.EndsWith("-BR", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "pt-PT");
            case "sr": return Find("sr-Latn");
            case "nb" or "nn" or "no": return Find("nb");
            default: return Find(lang);
        }
    }
}
