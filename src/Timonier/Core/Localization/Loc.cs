using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Timonier.Core.Platform;

namespace Timonier.Core.Localization;

/// <summary>
/// Traduction de l'interface, façon gettext : le texte écrit dans le code (langue source) sert de clé, et chaque langue
/// a un fichier Localization/&lt;code&gt;.json incorporé à l'exécutable. Seule la langue active est chargée, une fois,
/// au démarrage (changer de langue demande de relancer l'application). Un texte sans traduction s'affiche dans la
/// langue source. Utilisation (via <c>global using static</c>) :
/// <code>
/// L("Settings")                         // texte simple
/// L("{0} of {1} applied", done, total)  // avec paramètres (string.Format dans la culture de l'interface)
/// LC("verb", "Open")                    // même texte source, sens différent selon le contexte
/// LP(count, "{0} file", "{0} files")    // pluriel selon les règles CLDR de la langue
/// </code>
/// Les arguments de L/LC/LP doivent être des littéraux : l'outil tools/I18n les extrait du code.
/// </summary>
public static class Loc
{
    /// <summary>Langue dans laquelle les textes sont écrits dans le code (clés des fichiers de traduction).</summary>
    public const string SourceLanguage = "en";

    private const char ContextSeparator = '\u0004';

    private static FrozenDictionary<string, string> _strings = FrozenDictionary<string, string>.Empty;
    private static FrozenDictionary<string, FrozenDictionary<string, string>> _plurals =
        FrozenDictionary<string, FrozenDictionary<string, string>>.Empty;
    private static readonly HashSet<string> _missing = [];

    /// <summary>Code de la langue active (voir <see cref="Languages.All"/>).</summary>
    public static string Language { get; private set; } = SourceLanguage;

    /// <summary>Culture de l'interface : dates, nombres et tri dans la langue active (variante régionale de Windows si elle correspond).</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public static bool IsRightToLeft { get; private set; }

    /// <summary>Mots déclenchant une intention dans la recherche (« on », « off », « open ») pour la langue active.</summary>
    public static IReadOnlyDictionary<string, string[]> SearchWords { get; private set; } = new Dictionary<string, string[]>();

    /// <summary>
    /// Choisit la langue : un code de <see cref="Languages.All"/>, ou « auto » / null pour suivre la langue d'affichage
    /// de Windows (anglais si elle n'est pas prise en charge). À appeler avant toute création de texte (modules, fenêtres).
    /// </summary>
    public static void Initialize(string? preference)
    {
        var windows = CultureInfo.CurrentUICulture;
        var info = Languages.Find(preference) is { } chosen && IsAvailable(chosen.Code) ? chosen : AutomaticLanguage;
        Language = info.Code;
        IsRightToLeft = info.RightToLeft;
        Culture = Languages.Match(windows)?.Code == info.Code && !windows.IsNeutralCulture
            ? windows
            : SafeCulture(info.Code);

        _strings = FrozenDictionary<string, string>.Empty;
        _plurals = FrozenDictionary<string, FrozenDictionary<string, string>>.Empty;
        SearchWords = new Dictionary<string, string[]>();
        if (!string.Equals(Language, SourceLanguage, StringComparison.OrdinalIgnoreCase)) Load(Language);
    }

    public static string L(string text) =>
        _strings.TryGetValue(text, out var translated) ? translated : Missing(text);

    public static string L(string text, params object?[] args) => SafeFormat(L(text), text, args);

    public static string LC(string context, string text) =>
        _strings.TryGetValue(context + ContextSeparator + text, out var translated) ? translated : Missing(context + ContextSeparator + text, text);

    public static string LC(string context, string text, params object?[] args) => SafeFormat(LC(context, text), text, args);

    /// <summary>
    /// Pluriel : <paramref name="one"/> et <paramref name="other"/> sont les deux formes de la langue source ; {0} reçoit
    /// <paramref name="count"/>, {1}… les arguments suivants. La clé de traduction est la forme <paramref name="other"/>.
    /// </summary>
    public static string LP(long count, string one, string other, params object?[] args)
    {
        string format;
        if (_plurals.TryGetValue(other, out var forms))
            format = forms.TryGetValue(PluralRules.Category(Language, count), out var f) || forms.TryGetValue(PluralRules.Other, out f) ? f : other;
        else
        {
            if (!string.Equals(Language, SourceLanguage, StringComparison.OrdinalIgnoreCase)) Missing(other);
            format = PluralRules.Category(SourceLanguage, count) == PluralRules.One ? one : other;
        }
        var all = new object?[args.Length + 1];
        all[0] = count;
        args.CopyTo(all, 1);
        return SafeFormat(format, count == 1 ? one : other, all);
    }

    /// <summary>La langue peut-elle être choisie : langue source, ou traduction incorporée à l'exécutable ?</summary>
    public static bool IsAvailable(string code) =>
        string.Equals(code, SourceLanguage, StringComparison.OrdinalIgnoreCase)
        || Array.Exists(Assembly.GetExecutingAssembly().GetManifestResourceNames(),
            n => n.Equals($"Localization/{code}.json", StringComparison.OrdinalIgnoreCase));

    /// <summary>Langue qu'« auto » choisirait sur ce PC (langue d'affichage de Windows, sinon anglais).</summary>
    public static LanguageInfo AutomaticLanguage =>
        Languages.Match(CultureInfo.CurrentUICulture) is { } m && IsAvailable(m.Code) ? m : Languages.Find("en")!;

    /// <summary>Textes demandés sans traduction dans la langue active depuis le démarrage (contrôle qualité).</summary>
    public static IReadOnlyCollection<string> MissingTexts { get { lock (_missing) return [.. _missing]; } }

    private static string Missing(string key, string? display = null)
    {
        if (_strings.Count > 0) lock (_missing) _missing.Add(key);
        return display ?? key;
    }

    private static string SafeFormat(string format, string source, object?[] args)
    {
        try { return string.Format(Culture, format, args); }
        catch (FormatException)
        {
            Log.Warn("Loc", $"traduction mal formée ({Language}) : {format}");
            try { return string.Format(Culture, source, args); } catch (FormatException) { return source; }
        }
    }

    private static CultureInfo SafeCulture(string code)
    {
        try { return CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    private static void Load(string code)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Localization/{code}.json");
            if (stream is null) { Log.Warn("Loc", $"aucune traduction incorporée pour {code}"); return; }
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("strings", out var s))
                foreach (var p in s.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } v) strings[p.Name] = v;
            if (root.TryGetProperty("contexts", out var c))
                foreach (var ctx in c.EnumerateObject())
                    foreach (var p in ctx.Value.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } v)
                            strings[ctx.Name + ContextSeparator + p.Name] = v;

            var plurals = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal);
            if (root.TryGetProperty("plurals", out var pl))
                foreach (var p in pl.EnumerateObject())
                {
                    var forms = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var f in p.Value.EnumerateObject())
                        if (f.Value.GetString() is { Length: > 0 } v) forms[f.Name] = v;
                    if (forms.Count > 0) plurals[p.Name] = forms.ToFrozenDictionary(StringComparer.Ordinal);
                }

            var words = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (root.TryGetProperty("search", out var sw))
                foreach (var p in sw.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Array)
                        words[p.Name] = [.. p.Value.EnumerateArray().Select(e => e.GetString()).OfType<string>().Where(w => w.Length > 0)];

            _strings = strings.ToFrozenDictionary(StringComparer.Ordinal);
            _plurals = plurals.ToFrozenDictionary(StringComparer.Ordinal);
            SearchWords = words;
            Log.Info("Loc", $"langue {code} : {strings.Count} textes, {plurals.Count} pluriels");
        }
        catch (Exception ex)
        {
            Log.Error("Loc", $"traduction {code} illisible", ex);
        }
    }
}
