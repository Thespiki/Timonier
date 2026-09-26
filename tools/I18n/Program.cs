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
    "rekey" when args.Length >= 2 => Rekey(args[1..]),
    // chunks <taille> [dossier] [--refs N]
    "chunks" when args.Length >= 2 && int.TryParse(args[1], out var chunkSize) =>
        Chunker.Split(catalogPath,
            args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : Path.Combine(root, "artifacts", "i18n", "chunks"),
            chunkSize,
            int.TryParse(args.SkipWhile(a => a != "--refs").Skip(1).FirstOrDefault(), out var maxRefs) ? maxRefs : 3),
    // import <langue> <dossier des traductions> [--source-switch] [--chunks <dossier des lots>]
    "import" when args.Length >= 3 => Chunker.Import(args[1],
        args.SkipWhile(a => a != "--chunks").Skip(1).FirstOrDefault() ?? Path.Combine(root, "artifacts", "i18n", "chunks"),
        args[2], locDir, Path.Combine(root, "artifacts"), args.Contains("--source-switch"),
        args.SkipWhile(a => a != "--only").Skip(1).FirstOrDefault()),
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

// rekey [--check] map1.json [map2.json…]
// Cartes : { "strings":  { "ancien": "nouveau" | { "text": "nouveau", "context": "ctx" } },
//            "contexts": { "ctx": { "ancien": "nouveau" } },
//            "plurals":  { "ancien pluriel": { "one": "nouveau singulier", "other": "nouveau pluriel" } },
//            "keepOldAs": "fr" }  ← facultatif : les anciens textes deviennent la traduction de cette langue.
// Un texte simple peut recevoir un contexte (L → LC) : c'est ainsi qu'on sépare deux anciens textes qui auraient la
// même traduction (ex. « Désactivé » et « Désactivée » → « Off ») sans perdre la nuance dans l'ancienne langue.
// --check : n'écrit rien ; signale les textes du catalogue sans correspondance et les collisions.
int Rekey(string[] mapArgs)
{
    var checkOnly = mapArgs.Contains("--check");
    var partial = mapArgs.Contains("--partial");   // accepte des textes sans correspondance (essais, migration par étapes)
    var collisionsOut = mapArgs.SkipWhile(a => a != "--collisions").Skip(1).FirstOrDefault();
    var paths = mapArgs.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && a != collisionsOut).ToList();
    var simple = new Dictionary<string, (string? Ctx, string Text)>(StringComparer.Ordinal);
    var ctxMap = new Dictionary<string, string>(StringComparer.Ordinal);
    var plural = new Dictionary<string, (string One, string Other)>(StringComparer.Ordinal);
    string? keepCode = null;
    var problems = new List<string>();

    foreach (var path in paths)
    {
        var map = JsonNode.Parse(File.ReadAllText(path))!;
        keepCode ??= map["keepOldAs"]?.GetValue<string>();
        // "override": true — carte de corrections, placée après les autres : ses entrées remplacent les précédentes.
        var isOverride = map["override"]?.GetValue<bool>() == true;
        if (map["strings"] is JsonObject s)
            foreach (var (k, v) in s)
            {
                (string? Ctx, string Text) target = v is JsonObject o
                    ? (o["context"]?.GetValue<string>(), o["text"]!.GetValue<string>())
                    : (null, v!.GetValue<string>());
                if (!isOverride && simple.TryGetValue(k, out var prev) && prev != target) problems.Add($"conflit entre cartes pour « {Short(k)} »");
                simple[k] = target;
            }
        if (map["contexts"] is JsonObject c)
            foreach (var (ctx, g) in c)
                foreach (var (k, v) in g!.AsObject())
                {
                    var id = ctx + '\u0004' + k;
                    if (ctxMap.TryGetValue(id, out var prev) && prev != v!.GetValue<string>()) problems.Add($"conflit entre cartes pour « {ctx}/{Short(k)} »");
                    ctxMap[id] = v!.GetValue<string>();
                }
        if (map["plurals"] is JsonObject p)
            foreach (var (k, v) in p) plural[k] = (v!["one"]!.GetValue<string>(), v!["other"]!.GetValue<string>());
    }

    var (entries, errors) = Scan();
    if (errors.Count > 0) { foreach (var e in errors) Console.Error.WriteLine("ERREUR " + e); return 1; }

    // Couverture : chaque texte du code doit avoir sa correspondance (sinon le code mélangerait deux langues source).
    var unmapped = entries.Values.Where(e => e.One is not null ? !plural.ContainsKey(e.Key)
        : e.Context is not null ? !ctxMap.ContainsKey(e.Id) : !simple.ContainsKey(e.Key)).ToList();
    if (!partial)
    {
        foreach (var e in unmapped.Take(50)) problems.Add($"sans correspondance : {(e.Context is null ? "" : e.Context + " / ")}« {Short(e.Key)} » ({e.Refs.FirstOrDefault()})");
        if (unmapped.Count > 50) problems.Add($"… {unmapped.Count - 50} autre(s) texte(s) sans correspondance");
    }

    // Variables : le nouveau texte garde exactement les emplacements {n} de l'ancien.
    foreach (var (old, nw) in simple)
        if (!SamePlaceholders(old, nw.Text, allowSubset: false)) problems.Add($"variables différentes : « {Short(old)} » → « {Short(nw.Text)} »");
    foreach (var (id, nw) in ctxMap)
        if (!SamePlaceholders(id[(id.IndexOf('\u0004') + 1)..], nw, allowSubset: false)) problems.Add($"variables différentes : « {Short(id)} » → « {Short(nw)} »");
    foreach (var (old, nw) in plural)
        if (!SamePlaceholders(old, nw.Other, allowSubset: false)) problems.Add($"variables différentes (pluriel) : « {Short(old)} » → « {Short(nw.Other)} »");

    // Collisions : deux anciens textes différents ne doivent pas aboutir à la même nouvelle clé.
    var targets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    void Target(string key, string source) { if (!targets.TryGetValue(key, out var l)) targets[key] = l = []; if (!l.Contains(source)) l.Add(source); }
    foreach (var e in entries.Values)
    {
        if (e.One is not null && plural.TryGetValue(e.Key, out var pl)) Target("\u0001" + pl.Other, e.Key);
        else if (e.Context is not null && ctxMap.TryGetValue(e.Id, out var ct)) Target(e.Context + '\u0004' + ct, e.Id);
        else if (e.Context is null && simple.TryGetValue(e.Key, out var st)) Target(st.Ctx is null ? st.Text : st.Ctx + '\u0004' + st.Text, e.Key);
    }
    var collisions = targets.Where(t => t.Value.Count > 1).ToList();
    // --collisions <fichier> : liste complète (textes entiers, références) pour les résoudre avec des contextes.
    if (mapArgs.SkipWhile(a => a != "--collisions").Skip(1).FirstOrDefault() is { } collisionsPath)
    {
        var arr = new JsonArray();
        foreach (var (key, srcs) in collisions)
            arr.Add(new JsonObject
            {
                ["target"] = key.Replace('\u0004', '/').TrimStart('\u0001'),
                ["sources"] = new JsonArray([.. srcs.Select(id => (JsonNode)new JsonObject
                {
                    ["text"] = id.TrimStart('\u0001').Contains('\u0004') ? id[(id.IndexOf('\u0004') + 1)..] : id.TrimStart('\u0001'),
                    ["context"] = id.Contains('\u0004') ? id[..id.IndexOf('\u0004')] : null,
                    ["refs"] = new JsonArray([.. (entries.TryGetValue(id, out var en) ? en.Refs.Distinct().Take(4) : []).Select(r => (JsonNode)r!)]),
                })]),
            });
        WriteJson(collisionsPath, arr);
        Console.WriteLine($"Collisions écrites : {collisionsPath}");
    }
    foreach (var (key, sources) in collisions)
        problems.Add($"collision : « {Short(key.Replace('\u0004', '/').TrimStart('\u0001'))} » ← {string.Join(" | ", sources.Select(x => "« " + Short(x.Replace('\u0004', '/').TrimStart('\u0001')) + " »"))}");

    Console.WriteLine($"{entries.Count} textes, {unmapped.Count} sans correspondance, {collisions.Count} collision(s)");
    foreach (var pr in problems) Console.WriteLine("  - " + pr);
    if (checkOnly || problems.Count > 0) return problems.Count > 0 ? 1 : 0;

    // 1) Code C# : réécriture des littéraux (espaces et commentaires conservés) ; L → LC quand un contexte est ajouté.
    var options = new CSharpParseOptions(LanguageVersion.Preview);
    var changedFiles = 0;
    static LiteralExpressionSyntax Lit(string value, LiteralExpressionSyntax like) =>
        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(like.Token.LeadingTrivia, SymbolDisplay.FormatLiteral(value, true), value, like.Token.TrailingTrivia));
    foreach (var file in SourceFiles("*.cs"))
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, file);
        // Chaque remplacement est calculé à partir du nœud déjà réécrit (appels L imbriqués dans les arguments).
        var replacements = new Dictionary<SyntaxNode, Func<SyntaxNode, SyntaxNode>>();
        foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = LocMethod(call);
            if (name is null) continue;
            var a = call.ArgumentList.Arguments;
            LiteralExpressionSyntax Arg(int i) => (LiteralExpressionSyntax)a[i].Expression;
            switch (name)
            {
                case "L" when simple.TryGetValue(Arg(0).Token.ValueText, out var nw):
                    if (nw.Ctx is null) { var lit0 = Arg(0); replacements[lit0] = _ => Lit(nw.Text, lit0); break; }
                    replacements[call] = rewritten =>
                    {
                        var r = (InvocationExpressionSyntax)rewritten;
                        var ra = r.ArgumentList.Arguments;
                        var args = new List<ArgumentSyntax> { SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(nw.Ctx))) };
                        args.Add(ra[0].WithExpression(Lit(nw.Text, (LiteralExpressionSyntax)ra[0].Expression)));
                        args.AddRange(ra.Skip(1));
                        var commas = Enumerable.Range(0, args.Count - 1).Select(_ => SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space));
                        var newExpr = r.Expression is MemberAccessExpressionSyntax ma
                            ? (ExpressionSyntax)ma.WithName(SyntaxFactory.IdentifierName("LC"))
                            : SyntaxFactory.IdentifierName("LC").WithTriviaFrom(r.Expression);
                        return r.WithExpression(newExpr).WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args, commas)).WithTriviaFrom(r.ArgumentList));
                    };
                    break;
                case "LC" when ctxMap.TryGetValue(Arg(0).Token.ValueText + '\u0004' + Arg(1).Token.ValueText, out var nwc):
                    { var lit1 = Arg(1); replacements[lit1] = _ => Lit(nwc, lit1); }
                    break;
                case "LP" when plural.TryGetValue(Arg(2).Token.ValueText, out var nwp):
                    { var l1 = Arg(1); var l2 = Arg(2); replacements[l1] = _ => Lit(nwp.One, l1); replacements[l2] = _ => Lit(nwp.Other, l2); }
                    break;
            }
        }
        if (replacements.Count == 0) continue;
        var root2 = tree.GetRoot().ReplaceNodes(replacements.Keys, (o, rewritten) => replacements[o](rewritten));
        File.WriteAllText(file, root2.ToFullString(), new UTF8Encoding(HasBom(file)));
        changedFiles++;
    }

    // 2) XAML.
    foreach (var file in SourceFiles("*.xaml"))
    {
        var text = File.ReadAllText(file);
        var rewritten = XamlL().Replace(text, m =>
        {
            var old = Regex.Unescape(m.Groups["text"].Value.Replace("\\'", "'"));
            if (m.Groups["ctx"].Success)
                return ctxMap.TryGetValue(m.Groups["ctx"].Value + '\u0004' + old, out var nc)
                    ? m.Value.Replace("'" + m.Groups["text"].Value + "'", "'" + nc.Replace("'", "\\'") + "'") : m.Value;
            if (!simple.TryGetValue(old, out var nw)) return m.Value;
            var esc = nw.Text.Replace("'", "\\'");
            return nw.Ctx is null ? m.Value.Replace("'" + m.Groups["text"].Value + "'", "'" + esc + "'") : $"{{loc:L Text='{esc}', Context={nw.Ctx}}}";
        });
        if (rewritten != text) { File.WriteAllText(file, rewritten, new UTF8Encoding(HasBom(file))); changedFiles++; }
    }

    // 3) Fichiers de langue existants : mêmes traductions sous les nouvelles clés (un texte peut passer dans « contexts »).
    foreach (var file in Directory.GetFiles(locDir, "*.json").Where(f => !Path.GetFileName(f).StartsWith('_')))
    {
        var doc = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        var strings = new JsonObject();
        var contexts = new JsonObject();
        if (doc["contexts"] is JsonObject oldCtx)
            foreach (var (ctx, g) in oldCtx)
                foreach (var (k, v) in g!.AsObject())
                    Put(contexts, ctx, ctxMap.GetValueOrDefault(ctx + '\u0004' + k) ?? k, v?.DeepClone());
        if (doc["strings"] is JsonObject oldStr)
            foreach (var (k, v) in oldStr)
            {
                if (!simple.TryGetValue(k, out var nw)) { strings[k] = v?.DeepClone(); continue; }
                if (nw.Ctx is null) strings[nw.Text] = v?.DeepClone(); else Put(contexts, nw.Ctx, nw.Text, v?.DeepClone());
            }
        doc["strings"] = strings;
        doc["contexts"] = contexts;
        doc["plurals"] = RekeyObject(doc["plurals"] as JsonObject, plural.ToDictionary(kv => kv.Key, kv => kv.Value.Other));
        WriteJson(file, doc);
    }

    // 4) Anciens textes conservés comme traduction (ex. le français d'origine quand l'anglais devient la source).
    if (keepCode is not null)
    {
        var path = Path.Combine(locDir, keepCode + ".json");
        var doc = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject { ["language"] = keepCode };
        var strings = doc["strings"] as JsonObject ?? new JsonObject();
        var contexts = doc["contexts"] as JsonObject ?? new JsonObject();
        foreach (var (old, nw) in simple)
            if (nw.Ctx is null) strings[nw.Text] = old; else Put(contexts, nw.Ctx, nw.Text, old);
        foreach (var (key, nw) in ctxMap)
        {
            var ctx = key[..key.IndexOf('\u0004')];
            Put(contexts, ctx, nw, key[(ctx.Length + 1)..]);
        }
        doc["strings"] = strings;
        doc["contexts"] = contexts;
        var plurals = doc["plurals"] as JsonObject ?? new JsonObject();
        foreach (var e in entries.Values.Where(e => e.One is not null && plural.ContainsKey(e.Key)))
            plurals[plural[e.Key].Other] = new JsonObject { ["one"] = e.One, ["other"] = e.Key };
        doc["plurals"] = plurals;
        WriteJson(path, doc);
    }

    Console.WriteLine($"{changedFiles} fichier(s) source réécrit(s). Relancez « extract » puis « check ».");
    return 0;

    static void Put(JsonObject contexts, string ctx, string key, JsonNode? value)
    {
        if (contexts[ctx] is not JsonObject g) contexts[ctx] = g = new JsonObject();
        g[key] = value;
    }
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
