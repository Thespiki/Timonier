namespace Timonier.Core.Localization;

/// <summary>
/// Catégories de pluriel CLDR (nombres entiers uniquement) pour les langues de <see cref="Languages"/>.
/// Référence : https://www.unicode.org/cldr/charts/latest/supplemental/language_plural_rules.html
/// </summary>
public static class PluralRules
{
    public const string Zero = "zero", One = "one", Two = "two", Few = "few", Many = "many", Other = "other";

    public static string Category(string language, long count)
    {
        var n = Math.Abs(count);
        long n10 = n % 10, n100 = n % 100;
        var lang = language.Split('-')[0];
        return lang switch
        {
            // Pas de pluriel grammatical.
            "ja" or "ko" or "zh" or "th" or "vi" or "id" => Other,
            // 0 et 1 au singulier.
            "fr" or "hi" => n <= 1 ? One : Other,
            "pt" => language.Equals("pt-PT", StringComparison.OrdinalIgnoreCase) ? (n == 1 ? One : Other) : (n <= 1 ? One : Other),
            "ru" or "uk" => n10 == 1 && n100 != 11 ? One
                : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? Few
                : Many,
            "pl" => n == 1 ? One
                : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? Few
                : Many,
            "cs" or "sk" => n == 1 ? One : n is >= 2 and <= 4 ? Few : Other,
            "hr" or "sr" => n10 == 1 && n100 != 11 ? One
                : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? Few
                : Other,
            "sl" => n100 == 1 ? One : n100 == 2 ? Two : n100 is 3 or 4 ? Few : Other,
            "lt" => n10 == 1 && n100 is < 11 or > 19 ? One
                : n10 >= 2 && n100 is < 11 or > 19 ? Few
                : Other,
            "lv" => n10 == 0 || n100 is >= 11 and <= 19 ? Zero : n10 == 1 && n100 != 11 ? One : Other,
            "ro" => n == 1 ? One : n == 0 || n100 is >= 1 and <= 19 ? Few : Other,
            "ar" => n == 0 ? Zero : n == 1 ? One : n == 2 ? Two
                : n100 is >= 3 and <= 10 ? Few
                : n100 is >= 11 and <= 99 ? Many
                : Other,
            "he" => n == 1 ? One : n == 2 ? Two : Other,
            // en, de, es, it, nl, sv, da, nb, fi, et, hu, el, bg, tr, ca…
            _ => n == 1 ? One : Other,
        };
    }
}
