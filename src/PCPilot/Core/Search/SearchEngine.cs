using System.Globalization;
using System.Text;
using PcPilot.Core.Catalog;

namespace PcPilot.Core.Search;

public enum SearchIntent { None, TurnOn, TurnOff, Open }

/// <summary>Document indexé.</summary>
public sealed class SearchDocument
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string Glyph { get; init; } = "";
    public SearchEntryKind Kind { get; init; }
    public double Boost { get; init; }
    /// <summary>TweakDefinition, PageInfo ou SearchEntry.</summary>
    public required object Payload { get; init; }
    /// <summary>Vrai pour un réglage de type interrupteur (l'intention activer/désactiver s'applique).</summary>
    public bool IsToggle { get; init; }

    internal string[] TitleTokens = [];
    internal string[] KeywordTokens = [];
    internal string[] BodyTokens = [];
    internal string[] CategoryTokens = [];
    internal Dictionary<int, double> Concepts = [];
    internal string NormalizedTitle = "";

    public static SearchDocument Create(string id, string title, string? subtitle, string glyph, SearchEntryKind kind, object payload,
        IEnumerable<string>? keywords = null, string? body = null, string? category = null, double boost = 0, bool isToggle = false)
    {
        var doc = new SearchDocument { Id = id, Title = title, Subtitle = subtitle, Glyph = glyph, Kind = kind, Payload = payload, Boost = boost, IsToggle = isToggle };
        var kw = string.Join(' ', keywords ?? []);
        doc.NormalizedTitle = TextNormalizer.Normalize(title);
        doc.TitleTokens = TextNormalizer.Tokens(title);
        doc.KeywordTokens = TextNormalizer.Tokens(kw);
        doc.BodyTokens = TextNormalizer.Tokens((subtitle ?? "") + " " + (body ?? ""));
        doc.CategoryTokens = TextNormalizer.Tokens(category ?? "");
        AddConcepts(doc.Concepts, title, SearchEngine.WTitle);
        AddConcepts(doc.Concepts, kw, SearchEngine.WKeyword);
        AddConcepts(doc.Concepts, category ?? "", SearchEngine.WCategory);
        AddConcepts(doc.Concepts, (subtitle ?? "") + " " + (body ?? ""), SearchEngine.WBody);
        return doc;
    }

    private static void AddConcepts(Dictionary<int, double> concepts, string text, double weight)
    {
        foreach (var c in Synonyms.ConceptsIn(TextNormalizer.Normalize(text)))
            if (!concepts.TryGetValue(c, out var w) || w < weight) concepts[c] = weight;
    }
}

public sealed record SearchHit(SearchDocument Document, double Score, SearchIntent Intent);

/// <summary>
/// Recherche locale floue : insensible aux accents et à la casse, tolère les fautes de frappe (distance d'édition),
/// complète les mots en cours de frappe, comprend les synonymes FR/EN (« wifi » = « sans fil »…) et détecte l'intention
/// (« désactiver la caméra » → propose directement l'interrupteur). Aucune donnée ne quitte le PC.
/// </summary>
public sealed class SearchEngine(IReadOnlyList<SearchDocument> documents)
{
    internal const double WTitle = 3.0, WKeyword = 2.0, WCategory = 1.2, WBody = 1.0;

    public int Count => documents.Count;

    public IReadOnlyList<SearchHit> Search(string query, int max = 30)
    {
        var normalized = TextNormalizer.Normalize(query);
        if (normalized.Length == 0) return [];

        var rawTokens = TextNormalizer.Tokens(query, keepStopWords: false);
        var intent = DetectIntent(rawTokens);
        var tokens = rawTokens.Where(t => !Synonyms.IsIntentWord(t)).ToArray();
        if (tokens.Length == 0) tokens = rawTokens; // « activer » seul : on cherche quand même
        if (tokens.Length == 0) return [];

        // Unités de requête : concepts multi-mots d'abord (ex. « barre des tâches »), puis mots isolés.
        var units = BuildUnits(string.Join(' ', tokens), tokens);
        var lastToken = tokens[^1];

        var hits = new List<SearchHit>();
        foreach (var doc in documents)
        {
            double total = 0;
            var matched = 0;
            foreach (var unit in units)
            {
                var s = ScoreUnit(doc, unit, isLast: unit.Tokens.Contains(lastToken));
                if (s > 0) { matched++; total += s; }
            }
            if (matched == 0) continue;
            var coverage = (double)matched / units.Count;
            var score = total / units.Count * coverage * coverage;
            if (doc.NormalizedTitle == normalized) score += 1.5;
            else if (doc.NormalizedTitle.StartsWith(string.Join(' ', tokens), StringComparison.Ordinal)) score += 0.6;
            score += doc.Boost;
            if (intent is SearchIntent.TurnOn or SearchIntent.TurnOff && doc.IsToggle) score += 0.35;
            if (intent == SearchIntent.Open && doc.Kind is SearchEntryKind.Page or SearchEntryKind.Tool or SearchEntryKind.WindowsSetting) score += 0.25;
            if (score < 0.55) continue;
            hits.Add(new SearchHit(doc, score, doc.IsToggle ? intent : intent == SearchIntent.Open ? SearchIntent.Open : SearchIntent.None));
        }
        return [.. hits.OrderByDescending(h => h.Score).ThenBy(h => h.Document.Title.Length).Take(max)];
    }

    private sealed record Unit(string[] Tokens, int[] Concepts);

    private static List<Unit> BuildUnits(string normalizedQuery, string[] tokens)
    {
        var units = new List<Unit>();
        var consumed = new HashSet<string>();
        foreach (var (phrase, concept) in Synonyms.PhrasesIn(normalizedQuery))
        {
            var words = phrase.Split(' ');
            units.Add(new Unit(words, [concept]));
            foreach (var w in words) consumed.Add(w);
        }
        foreach (var t in tokens)
        {
            if (consumed.Contains(t)) continue;
            units.Add(new Unit([t], Synonyms.ConceptsOfWord(t)));
        }
        return units;
    }

    private static double ScoreUnit(SearchDocument doc, Unit unit, bool isLast)
    {
        double best = 0;
        foreach (var c in unit.Concepts)
            if (doc.Concepts.TryGetValue(c, out var w)) best = Math.Max(best, w * 0.9);
        if (unit.Tokens.Length == 1)
        {
            var t = unit.Tokens[0];
            best = Math.Max(best, WTitle * BestTokenMatch(t, doc.TitleTokens, isLast));
            best = Math.Max(best, WKeyword * BestTokenMatch(t, doc.KeywordTokens, isLast));
            best = Math.Max(best, WCategory * BestTokenMatch(t, doc.CategoryTokens, isLast));
            best = Math.Max(best, WBody * BestTokenMatch(t, doc.BodyTokens, isLast));
        }
        return best;
    }

    private static double BestTokenMatch(string q, string[] candidates, bool isLast)
    {
        double best = 0;
        foreach (var c in candidates)
        {
            double s;
            if (c == q) s = 1.0;
            else if (q.Length >= 2 && c.StartsWith(q, StringComparison.Ordinal)) s = isLast ? 0.9 : 0.75;
            else if (c.Length >= 4 && q.StartsWith(c, StringComparison.Ordinal)) s = 0.6;
            else if (q.Length >= 4 && Math.Abs(c.Length - q.Length) <= 2 && Distance(q, c, q.Length >= 7 ? 2 : 1) is var d && d >= 0) s = d == 1 ? 0.7 : 0.55;
            else if (q.Length >= 4 && c.Length > q.Length && c.Contains(q, StringComparison.Ordinal)) s = 0.45;
            else continue;
            if (s > best) { best = s; if (best >= 1.0) break; }
        }
        return best;
    }

    /// <summary>Distance de Damerau-Levenshtein bornée ; -1 si supérieure à <paramref name="max"/>.</summary>
    internal static int Distance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return -1;
        var prev2 = new int[b.Length + 1];
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            var rowMin = cur[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, prev2[j - 2] + 1);
                cur[j] = v;
                rowMin = Math.Min(rowMin, v);
            }
            if (rowMin > max) return -1;
            (prev2, prev, cur) = (prev, cur, prev2);
        }
        var result = prev[b.Length];
        return result <= max ? result : -1;
    }

    private static SearchIntent DetectIntent(string[] tokens)
    {
        foreach (var t in tokens)
        {
            if (Synonyms.OffWords.Contains(t)) return SearchIntent.TurnOff;
            if (Synonyms.OnWords.Contains(t)) return SearchIntent.TurnOn;
            if (Synonyms.OpenWords.Contains(t)) return SearchIntent.Open;
        }
        return SearchIntent.None;
    }
}

public static class TextNormalizer
{
    private static readonly HashSet<string> StopWords =
    [
        "le", "la", "les", "l", "de", "du", "des", "d", "un", "une", "et", "ou", "a", "au", "aux", "en", "pour", "par", "sur",
        "dans", "avec", "mon", "ma", "mes", "ton", "ta", "tes", "son", "sa", "ses", "ce", "cet", "cette", "ces", "qui", "que",
        "quoi", "est", "sont", "je", "j", "tu", "il", "elle", "nous", "vous", "on", "me", "m", "te", "t", "se", "s", "ne", "n",
        "comment", "faire", "veux", "voudrais", "peux", "puis", "svp", "stp", "moi", "y", "c", "qu",
        "the", "an", "of", "to", "for", "in", "my", "is", "how", "do", "i", "want", "can", "with", "please",
    ];

    /// <summary>Minuscules, sans accents, ponctuation remplacée par des espaces.</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastSpace = true;
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch switch { 'œ' => "oe", 'æ' => "ae", _ => ch.ToString() });
                lastSpace = false;
            }
            else if (ch == '-' && sb.Length > 0 && !lastSpace)
            {
                continue; // « wi-fi » -> « wifi », « pare-feu » -> « parefeu »
            }
            else if (!lastSpace)
            {
                sb.Append(' ');
                lastSpace = true;
            }
        }
        return sb.ToString().Trim();
    }

    public static string[] Tokens(string text, bool keepStopWords = false)
    {
        var n = Normalize(text);
        if (n.Length == 0) return [];
        var parts = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokens = keepStopWords ? parts : parts.Where(p => !StopWords.Contains(p)).ToArray();
        // Pluriels simples : « parametres » ≈ « parametre ».
        return [.. tokens.Select(t => t.Length > 4 && t.EndsWith('s') && !t.EndsWith("ss") ? t[..^1] : t).Distinct()];
    }
}
