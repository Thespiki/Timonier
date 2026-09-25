// Outil de traduction de Timonier (développement uniquement).
//
//   dotnet run --project tools/I18n -- extract   écrit src/<app>/Localization/_catalog.json (tous les textes L/LC/LP du code et du XAML)
//   dotnet run --project tools/I18n -- check     vérifie chaque fichier de langue (textes manquants ou obsolètes, variables {0}…)
//   dotnet run --project tools/I18n -- rekey <map.json>
//                                               remplace des textes source dans le code et dans les fichiers de langue
//                                               (ex. passage de la langue source du français à l'anglais)
//
// Les appels L("…"), LC("contexte", "…") et LP(n, "…", "…") doivent recevoir des littéraux : un argument calculé
// est une erreur, car il ne pourrait pas être extrait ni traduit.
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Timonier.Core.Localization;

var root = FindRepoRoot();
var appDir = Directory.GetDirectories(Path.Combine(root, "src")).First(d => Directory.GetFiles(d, "*.csproj").Length > 0);
var locDir = Path.Combine(appDir, "Localization");
var catalogPath = Path.Combine(locDir, "_catalog.json");

var command = args.FirstOrDefault() ?? "help";
return command switch
{
    "extract" => Extract(),
    "check" => Check(),
    "rekey" when args.Length == 2 => Rekey(args[1]),
    // wrap [préfixe…] : limite aux fichiers dont le chemin relatif à src/<app> commence par l'un des préfixes.
    "wrap" => Wrapper.Run(SourceFiles("*.cs").Where(f => !Path.GetRelativePath(appDir, f).Replace('\\', '/').StartsWith("Core/Search/", StringComparison.Ordinal)
            && (args.Length == 1 || args.Skip(1).Any(p => Path.GetRelativePath(appDir, f).Replace('\\', '/').StartsWith(p, StringComparison.OrdinalIgnoreCase)))),
        root, Path.Combine(root, "artifacts", "i18n-wrap-report.txt")),
    _ => Help(),
};

int Help()
{
    Console.WriteLine("Usage : I18n extract | check | rekey <map.json> | wrap [préfixe…]");
    return 2;
}

// ---------------------------------------------------------------- extraction

int Extract()
{
    var (entries, errors) = Scan();
    foreach (var e in errors) Console.Error.WriteLine("ERREUR " + e);
    var catalog = new JsonObject
    {
        ["sourceLanguage"] = SourceLanguage(),
        ["count"] = entries.Count,
        ["entries"] = new JsonArray([.. entries.Values.OrderBy(e => e.Context ?? "").ThenBy(e => e.Key, StringComparer.Ordinal).Select(e => e.ToJson())]),
    };
    WriteJson(catalogPath, catalog);
    Console.WriteLine($"{entries.Count} textes extraits ({entries.Values.Count(e => e.One is not null)} pluriels, "
        + $"{entries.Values.Count(e => e.Context is not null)} avec contexte) → {Rel(catalogPath)}");
    return errors.Count == 0 ? 0 : 1;
}

(Dictionary<string, Entry> Entries, List<string> Errors) Scan()
{
    var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    var errors = new List<string>();
    var options = new CSharpParseOptions(LanguageVersion.Preview);

    foreach (var file in SourceFiles("*.cs"))
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, file);
        foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = LocMethod(call);
            if (name is null) continue;
            var a = call.ArgumentList.Arguments;
            var where = $"{Rel(file)}:{call.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
            string? Lit(int i)
            {
                if (i >= a.Count) { errors.Add($"{where} : {name}() sans argument {i + 1}"); return null; }
                if (a[i].Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } lit) return lit.Token.ValueText;
                errors.Add($"{where} : l'argument {i + 1} de {name}() doit être un littéral (« {Short(a[i].ToString())} »)");
                return null;
            }
            switch (name)
            {
                case "L" when Lit(0) is { } text: Add(entries, new Entry(text, null, null), where); break;
                case "LC" when (Lit(0), Lit(1)) is ({ } ctx, { } text): Add(entries, new Entry(text, ctx, null), where); break;
                case "LP" when (Lit(1), Lit(2)) is ({ } one, { } other): Add(entries, new Entry(other, null, one), where); break;
            }
        }
    }

    foreach (var file in SourceFiles("*.xaml"))
    {
        var text = File.ReadAllText(file);
        foreach (Match m in XamlL().Matches(text))
        {
            var line = text[..m.Index].Count(c => c == '\n') + 1;
            var value = Regex.Unescape(m.Groups["text"].Value.Replace("\\'", "'"));
            Add(entries, new Entry(value, m.Groups["ctx"].Success ? m.Groups["ctx"].Value : null, null), $"{Rel(file)}:{line}");
        }
    }
    return (entries, errors);
}

static void Add(Dictionary<string, Entry> entries, Entry e, string where)
{
    if (!entries.TryGetValue(e.Id, out var existing)) entries[e.Id] = existing = e;
    existing.Refs.Add(where);
}

static string? LocMethod(InvocationExpressionSyntax call) => call.Expression switch
{
    IdentifierNameSyntax id when id.Identifier.Text is "L" or "LC" or "LP" => id.Identifier.Text,
    MemberAccessExpressionSyntax { Name.Identifier.Text: "L" or "LC" or "LP" } ma when ma.Expression.ToString().EndsWith("Loc", StringComparison.Ordinal)
        => ma.Name.Identifier.Text,
    _ => null,
};

// ---------------------------------------------------------------- vérification

int Check()
{
    if (!File.Exists(catalogPath)) { Console.Error.WriteLine("Catalogue absent : lancez d'abord « extract »."); return 2; }
    var catalog = LoadCatalog();
    var source = SourceLanguage();
    var failed = false;
    Console.WriteLine($"{catalog.Count} textes source ({source})");
    foreach (var file in Directory.GetFiles(locDir, "*.json").Where(f => !Path.GetFileName(f).StartsWith('_')).Order())
    {
        var code = Path.GetFileNameWithoutExtension(file);
        var problems = new List<string>();
        if (Languages.Find(code) is null) problems.Add($"code de langue inconnu « {code} »");
        JsonNode? doc;
        try { doc = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { Console.WriteLine($"  {code,-8} JSON invalide : {ex.Message}"); failed = true; continue; }

        int translated = 0, obsolete = 0;
        var needed = PluralCategories(code);
        foreach (var (id, value) in Pairs(doc))
        {
            if (!catalog.TryGetValue(id, out var entry)) { obsolete++; continue; }
            if (value is JsonValue v && v.GetValueKind() == JsonValueKind.String)
            {
                if (entry.One is not null) { problems.Add($"« {Short(entry.Key)} » est un pluriel : objet {{ one, other… }} attendu"); continue; }
                var t = v.GetValue<string>();
                if (t.Length == 0) continue;
                translated++;
                if (!SamePlaceholders(entry.Key, t, allowSubset: false)) problems.Add($"variables différentes : « {Short(entry.Key)} » → « {Short(t)} »");
            }
            else if (value is JsonObject forms)
            {
                if (entry.One is null) { problems.Add($"« {Short(entry.Key)} » n'est pas un pluriel"); continue; }
                translated++;
                foreach (var cat in needed.Where(c => !forms.ContainsKey(c))) problems.Add($"pluriel « {Short(entry.Key)} » : forme « {cat} » manquante");
                foreach (var (cat, f) in forms)
                {
                    var t = f?.GetValue<string>() ?? "";
                    // « one » peut s'écrire sans {0} (« un fichier ») ; les autres formes gardent toutes les variables.
                    if (!SamePlaceholders(entry.Key, t, allowSubset: cat is "one" or "zero" or "two")) problems.Add($"variables différentes ({cat}) : « {Short(entry.Key)} » → « {Short(t)} »");
                }
            }
        }
        var pct = catalog.Count == 0 ? 100 : 100.0 * translated / catalog.Count;
        Console.WriteLine($"  {code,-8} {pct,5:0.0} % traduit ({translated}/{catalog.Count}), {obsolete} obsolète(s), {problems.Count} erreur(s)");
        foreach (var p in problems.Take(30)) Console.WriteLine("           - " + p);
        if (problems.Count > 30) Console.WriteLine($"           … et {problems.Count - 30} autre(s)");
        failed |= problems.Count > 0;
    }
    return failed ? 1 : 0;
}

static IEnumerable<(string Id, JsonNode? Value)> Pairs(JsonNode? doc)
{
    if (doc?["strings"] is JsonObject s) foreach (var (k, v) in s) yield return (k, v);
    if (doc?["contexts"] is JsonObject c)
        foreach (var (ctx, group) in c)
            if (group is JsonObject g) foreach (var (k, v) in g) yield return (ctx + '\u0004' + k, v);
    if (doc?["plurals"] is JsonObject p) foreach (var (k, v) in p) yield return ("\u0001" + k, v);
}

static HashSet<string> PluralCategories(string code)
{
    var set = new HashSet<string>();
    for (var n = 0; n <= 1000; n++) set.Add(PluralRules.Category(code, n));
    return set;
}

static bool SamePlaceholders(string source, string translated, bool allowSubset)
{
    var a = Placeholders(source);
    var b = Placeholders(translated);
    return allowSubset ? b.IsSubsetOf(a) : a.SetEquals(b);
}

static HashSet<int> Placeholders(string s) =>
    [.. PlaceholderRx().Matches(s.Replace("{{", "").Replace("}}", "")).Select(m => int.Parse(m.Groups[1].Value))];

// ---------------------------------------------------------------- changement de textes source

int Rekey(string mapPath)
{
    // map.json : { "strings": { "ancien": "nouveau" }, "contexts": { "ctx": { "ancien": "nouveau" } },
    //              "plurals": { "ancien pluriel": { "one": "nouveau singulier", "other": "nouveau pluriel" } },
    //              "keepOldAs": "fr" }   ← facultatif : écrit les anciens textes comme traduction dans <code>.json
    var map = JsonNode.Parse(File.ReadAllText(mapPath))!;
    var simple = new Dictionary<string, string>(StringComparer.Ordinal);
    if (map["strings"] is JsonObject s) foreach (var (k, v) in s) simple[k] = v!.GetValue<string>();
    var ctxMap = new Dictionary<string, string>(StringComparer.Ordinal);
    if (map["contexts"] is JsonObject c) foreach (var (ctx, g) in c) foreach (var (k, v) in g!.AsObject()) ctxMap[ctx + '\u0004' + k] = v!.GetValue<string>();
    var plural = new Dictionary<string, (string One, string Other)>(StringComparer.Ordinal);
    if (map["plurals"] is JsonObject p) foreach (var (k, v) in p) plural[k] = (v!["one"]!.GetValue<string>(), v!["other"]!.GetValue<string>());

    var (entries, errors) = Scan();
    if (errors.Count > 0) { foreach (var e in errors) Console.Error.WriteLine("ERREUR " + e); return 1; }

    // 1) Code C# : réécriture des littéraux (espaces et commentaires conservés).
    var options = new CSharpParseOptions(LanguageVersion.Preview);
    var changedFiles = 0;
    foreach (var file in SourceFiles("*.cs"))
    {
        var original = File.ReadAllText(file);
        var tree = CSharpSyntaxTree.ParseText(original, options, file);
        var replacements = new Dictionary<SyntaxToken, SyntaxToken>();
        foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = LocMethod(call);
            if (name is null) continue;
            var a = call.ArgumentList.Arguments;
            SyntaxToken Tok(int i) => ((LiteralExpressionSyntax)a[i].Expression).Token;
            void Swap(SyntaxToken t, string value) => replacements[t] = SyntaxFactory.Literal(t.LeadingTrivia, SymbolDisplay.FormatLiteral(value, true), value, t.TrailingTrivia);
            switch (name)
            {
                case "L" when simple.TryGetValue(Tok(0).ValueText, out var nw): Swap(Tok(0), nw); break;
                case "LC" when ctxMap.TryGetValue(Tok(0).ValueText + '\u0004' + Tok(1).ValueText, out var nw): Swap(Tok(1), nw); break;
                case "LP" when plural.TryGetValue(Tok(2).ValueText, out var nw): Swap(Tok(1), nw.One); Swap(Tok(2), nw.Other); break;
            }
        }
        if (replacements.Count == 0) continue;
        var rewritten = tree.GetRoot().ReplaceTokens(replacements.Keys, (o, _) => replacements[o]).ToFullString();
        File.WriteAllText(file, rewritten, new UTF8Encoding(HasBom(file)));
        changedFiles++;
    }

    // 2) XAML.
    foreach (var file in SourceFiles("*.xaml"))
    {
        var text = File.ReadAllText(file);
        var rewritten = XamlL().Replace(text, m =>
        {
            var old = Regex.Unescape(m.Groups["text"].Value.Replace("\\'", "'"));
            var key = m.Groups["ctx"].Success ? m.Groups["ctx"].Value + '\u0004' + old : old;
            var nw = m.Groups["ctx"].Success ? ctxMap.GetValueOrDefault(key) : simple.GetValueOrDefault(key);
            return nw is null ? m.Value : m.Value.Replace("'" + m.Groups["text"].Value + "'", "'" + nw.Replace("'", "\\'") + "'");
        });
        if (rewritten != text) { File.WriteAllText(file, rewritten, new UTF8Encoding(HasBom(file))); changedFiles++; }
    }

    // 3) Fichiers de langue : mêmes traductions sous les nouvelles clés.
    foreach (var file in Directory.GetFiles(locDir, "*.json").Where(f => !Path.GetFileName(f).StartsWith('_')))
    {
        var doc = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        doc["strings"] = RekeyObject(doc["strings"] as JsonObject, simple);
        if (doc["contexts"] is JsonObject ctxs)
        {
            var result = new JsonObject();
            foreach (var (ctx, g) in ctxs)
                result[ctx] = RekeyObject(g as JsonObject, ctxMap.Where(kv => kv.Key.StartsWith(ctx + '\u0004', StringComparison.Ordinal))
                    .ToDictionary(kv => kv.Key[(ctx.Length + 1)..], kv => kv.Value));
            doc["contexts"] = result;
        }
        doc["plurals"] = RekeyObject(doc["plurals"] as JsonObject, plural.ToDictionary(kv => kv.Key, kv => kv.Value.Other));
        WriteJson(file, doc);
    }

    // 4) Anciens textes conservés comme traduction (ex. le français d'origine quand l'anglais devient la source).
    if (map["keepOldAs"]?.GetValue<string>() is { } keepCode)
    {
        var path = Path.Combine(locDir, keepCode + ".json");
        var doc = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject { ["language"] = keepCode };
        var strings = doc["strings"] as JsonObject ?? new JsonObject();
        foreach (var (old, nw) in simple) strings[nw] = old;
        doc["strings"] = strings;
        var contexts = doc["contexts"] as JsonObject ?? new JsonObject();
        foreach (var (key, nw) in ctxMap)
        {
            var ctx = key[..key.IndexOf('\u0004')];
            if (contexts[ctx] is not JsonObject g) contexts[ctx] = g = new JsonObject();
            g[nw] = key[(ctx.Length + 1)..];
        }
        doc["contexts"] = contexts;
        var plurals = doc["plurals"] as JsonObject ?? new JsonObject();
        foreach (var e in entries.Values.Where(e => e.One is not null && plural.ContainsKey(e.Key)))
            plurals[plural[e.Key].Other] = new JsonObject { ["one"] = e.One, ["other"] = e.Key };
        doc["plurals"] = plurals;
        WriteJson(path, doc);
    }

    Console.WriteLine($"{changedFiles} fichier(s) source réécrit(s). Relancez « extract » puis « check ».");
    return 0;
}

static JsonObject RekeyObject(JsonObject? obj, IReadOnlyDictionary<string, string> map)
{
    var result = new JsonObject();
    if (obj is null) return result;
    foreach (var (k, v) in obj) result[map.TryGetValue(k, out var nk) ? nk : k] = v?.DeepClone();
    return result;
}

// ---------------------------------------------------------------- utilitaires

Dictionary<string, Entry> LoadCatalog()
{
    var doc = JsonNode.Parse(File.ReadAllText(catalogPath))!;
    var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
    foreach (var n in doc["entries"]!.AsArray())
    {
        var e = new Entry(n!["key"]!.GetValue<string>(), n["context"]?.GetValue<string>(), n["one"]?.GetValue<string>());
        result[e.Id] = e;
    }
    return result;
}

string SourceLanguage()
{
    var loc = File.ReadAllText(Path.Combine(appDir, "Core", "Localization", "Loc.cs"));
    return Regex.Match(loc, "SourceLanguage\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
}

// Le moteur de traduction lui-même (Core/Localization) manipule des textes variables : il n'est pas analysé.
IEnumerable<string> SourceFiles(string pattern)
{
    var sep = Path.DirectorySeparatorChar;
    var engine = Path.Combine(appDir, "Core", "Localization") + sep;
    return Directory.EnumerateFiles(appDir, pattern, SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}") && !f.StartsWith(engine, StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal);
}

string Rel(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

static string Short(string s) => s.Length <= 70 ? s : s[..67] + "…";

static bool HasBom(string file)
{
    using var fs = File.OpenRead(file);
    Span<byte> b = stackalloc byte[3];
    return fs.Read(b) == 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
}

static void WriteJson(string path, JsonNode node)
{
    var json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    File.WriteAllText(path, json.Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
}

// Racine du dépôt : celle du dossier courant (permet de travailler sur un worktree), sinon celle de l'outil.
static string FindRepoRoot() => FindRoot(Directory.GetCurrentDirectory()) ?? FindRoot(AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory();

static string? FindRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")) && !File.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
    return dir?.FullName;
}

sealed record Entry(string Key, string? Context, string? One)
{
    public string Id => One is not null ? "\u0001" + Key : Context is null ? Key : Context + '\u0004' + Key;
    public List<string> Refs { get; } = [];

    public JsonObject ToJson()
    {
        var o = new JsonObject();
        if (Context is not null) o["context"] = Context;
        if (One is not null) o["one"] = One;
        o["key"] = Key;
        o["refs"] = new JsonArray([.. Refs.Distinct().Order(StringComparer.Ordinal).Select(r => (JsonNode)r!)]);
        return o;
    }
}

partial class Program
{
    [GeneratedRegex(@"\{(\d+)(?:,[^}:]*)?(?::[^}]*)?\}")]
    private static partial Regex PlaceholderRx();

    [GeneratedRegex(@"\{loc:L\s+(?:Text=)?'(?<text>(?:[^'\\]|\\.)*)'(?:\s*,\s*Context=(?<ctx>[^,}\s]+))?\s*\}")]
    private static partial Regex XamlL();
}
