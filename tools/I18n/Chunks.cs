// Commandes « chunks » et « import » : traduction par lots numérotés.
//
//   chunks <taille> [dossier]   découpe _catalog.json en lots (artifacts/i18n/chunks/NN.json par défaut). Les textes sont
//                               regroupés par fichier source (contexte homogène) et numérotés "NN-iiii".
//   import <langue> <dossier> [--source-switch]
//                               lit les traductions <dossier>/<langue>-NN.json ({ "NN-iiii": "texte" | { "one": …, … } })
//                               et écrit Localization/<langue>.json ; avec --source-switch, écrit à la place une carte
//                               pour « rekey » (artifacts/i18n/map-<langue>.json, anciens textes gardés en « fr »).
// Les traducteurs ne recopient jamais les textes source : aucune faute de frappe ne peut casser une clé.
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Timonier.Core.Localization;

static partial class Chunker
{
    public static int Split(string catalogPath, string outDir, int size, int maxRefs = 3)
    {
        var catalog = JsonNode.Parse(File.ReadAllText(catalogPath))!;
        var entries = catalog["entries"]!.AsArray().Select(n => n!.AsObject())
            .OrderBy(e => FirstRef(e), StringComparer.Ordinal)
            .ThenBy(e => e["key"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToList();
        Directory.CreateDirectory(outDir);
        foreach (var old in Directory.GetFiles(outDir, "*.json")) File.Delete(old);
        var chunkCount = (entries.Count + size - 1) / size;
        for (var c = 0; c < chunkCount; c++)
        {
            var items = new JsonArray();
            foreach (var (e, i) in entries.Skip(c * size).Take(size).Select((e, i) => (e, i)))
            {
                var o = new JsonObject { ["id"] = $"{c + 1:00}-{i + 1:0000}" };
                if (e["context"] is { } ctx) o["context"] = ctx.GetValue<string>();
                if (e["one"] is { } one) o["one"] = one.GetValue<string>();
                o["text"] = e["key"]!.GetValue<string>();
                if (maxRefs > 0) o["refs"] = new JsonArray([.. e["refs"]!.AsArray().Take(maxRefs).Select(r => (JsonNode)r!.GetValue<string>())]);
                items.Add(o);
            }
            Write(Path.Combine(outDir, $"{c + 1:00}.json"), new JsonObject
            {
                ["sourceLanguage"] = catalog["sourceLanguage"]?.GetValue<string>(),
                ["chunk"] = c + 1,
                ["count"] = items.Count,
                ["entries"] = items,
            });
        }
        Console.WriteLine($"{entries.Count} textes → {chunkCount} lot(s) de {size} au plus dans {outDir}");
        return 0;
    }

    /// <param name="only">Numéro de lot (ex. "03") : validation de ce seul lot, sans rien écrire.</param>
    public static int Import(string lang, string chunkDir, string transDir, string locDir, string artifacts, bool sourceSwitch, string? only = null)
    {
        var sources = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var f in Directory.GetFiles(chunkDir, "*.json").Order())
            foreach (var e in JsonNode.Parse(File.ReadAllText(f))!["entries"]!.AsArray())
                if (only is null || e!["id"]!.GetValue<string>().StartsWith(only + "-", StringComparison.Ordinal))
                    sources[e!["id"]!.GetValue<string>()] = e.AsObject();

        var translations = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var f in Directory.GetFiles(transDir, $"{lang}-*.json").Order())
        {
            JsonNode? doc;
            try { doc = JsonNode.Parse(File.ReadAllText(f), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (JsonException ex) { problems.Add($"{Path.GetFileName(f)} : JSON invalide ({ex.Message})"); continue; }
            var obj = doc?["translations"] as JsonObject ?? doc as JsonObject;
            if (obj is null) continue;
            foreach (var (id, v) in obj)
            {
                if (id is "language" or "search") continue;
                if (only is not null && !id.StartsWith(only + "-", StringComparison.Ordinal)) continue;
                if (!sources.ContainsKey(id)) { problems.Add($"{Path.GetFileName(f)} : identifiant inconnu {id}"); continue; }
                translations[id] = v?.DeepClone();
            }
        }

        var needed = new HashSet<string>();
        var targetLang = sourceSwitch ? lang : lang;
        for (var n = 0; n <= 1000; n++) needed.Add(PluralRules.Category(targetLang, n));

        var strings = new JsonObject();
        var contexts = new JsonObject();
        var plurals = new JsonObject();
        var missing = 0;
        foreach (var (id, src) in sources)
        {
            var text = src["text"]!.GetValue<string>();
            var ctx = src["context"]?.GetValue<string>();
            var one = src["one"]?.GetValue<string>();
            if (!translations.TryGetValue(id, out var t) || t is null) { missing++; if (missing <= 30) problems.Add($"{id} sans traduction : « {Short(text)} »"); continue; }

            if (one is not null)
            {
                if (t is not JsonObject forms) { problems.Add($"{id} : pluriel attendu (objet one/other…)"); continue; }
                var result = new JsonObject();
                foreach (var (cat, f) in forms)
                {
                    var s = f?.GetValue<string>() ?? "";
                    if (!Same(text, s, allowSubset: cat is "one" or "zero" or "two")) problems.Add($"{id} ({cat}) : variables différentes « {Short(text)} » → « {Short(s)} »");
                    result[cat] = s;
                }
                var required = sourceSwitch ? new HashSet<string> { "one", "other" } : needed;
                foreach (var cat in required.Where(c => !forms.ContainsKey(c))) problems.Add($"{id} : forme de pluriel « {cat} » manquante");
                plurals[text] = result;
                continue;
            }

            if (t is not JsonValue v || v.GetValueKind() != JsonValueKind.String) { problems.Add($"{id} : texte attendu"); continue; }
            var tr = v.GetValue<string>();
            if (tr.Length == 0) { problems.Add($"{id} : traduction vide"); continue; }
            if (!Same(text, tr, allowSubset: false)) problems.Add($"{id} : variables différentes « {Short(text)} » → « {Short(tr)} »");
            if (ctx is null) strings[text] = tr;
            else
            {
                if (contexts[ctx] is not JsonObject g) contexts[ctx] = g = new JsonObject();
                g[text] = tr;
            }
        }
        if (missing > 30) problems.Add($"… {missing - 30} autre(s) texte(s) sans traduction");

        // Mots d'intention de la recherche (facultatifs) : premier fichier qui en fournit.
        JsonNode? search = null;
        foreach (var f in Directory.GetFiles(transDir, $"{lang}-*.json").Order())
            if (JsonNode.Parse(File.ReadAllText(f))?["search"] is JsonObject s) { search = s.DeepClone(); break; }

        Console.WriteLine($"{lang} : {sources.Count - missing}/{sources.Count} textes traduits, {problems.Count} problème(s)");
        foreach (var p in problems.Take(60)) Console.WriteLine("  - " + p);
        if (problems.Count > 0) return 1;
        if (only is not null) { Console.WriteLine($"Lot {only} valide."); return 0; }

        if (sourceSwitch)
        {
            // Carte pour « rekey » : anciens textes (clés) → nouveaux textes source ; les anciens deviennent « fr ».
            var map = new JsonObject { ["keepOldAs"] = "fr", ["strings"] = strings, ["contexts"] = contexts };
            var pl = new JsonObject();
            foreach (var (k, v) in plurals) pl[k] = new JsonObject { ["one"] = v!["one"]!.GetValue<string>(), ["other"] = v["other"]!.GetValue<string>() };
            map["plurals"] = pl;
            var path = Path.Combine(artifacts, "i18n", $"map-{lang}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Write(path, map);
            Console.WriteLine("Carte écrite : " + path);
        }
        else
        {
            var path = Path.Combine(locDir, lang + ".json");
            Write(path, new JsonObject { ["language"] = lang, ["strings"] = strings, ["contexts"] = contexts, ["plurals"] = plurals, ["search"] = search ?? new JsonObject() });
            Console.WriteLine("Fichier de langue écrit : " + path);
        }
        return 0;
    }

    private static string FirstRef(JsonObject e) =>
        e["refs"]!.AsArray().Select(r => r!.GetValue<string>()).Order(StringComparer.Ordinal).FirstOrDefault() ?? "";

    private static bool Same(string a, string b, bool allowSubset)
    {
        var pa = Placeholders(a);
        var pb = Placeholders(b);
        return allowSubset ? pb.IsSubsetOf(pa) : pa.SetEquals(pb);
    }

    private static HashSet<int> Placeholders(string s) =>
        [.. PlaceholderRx().Matches(s.Replace("{{", "").Replace("}}", "")).Select(m => int.Parse(m.Groups[1].Value))];

    [GeneratedRegex(@"\{(\d+)(?:,[^}:]*)?(?::[^}]*)?\}")]
    private static partial Regex PlaceholderRx();

    private static string Short(string s) => s.Length <= 70 ? s : s[..67] + "…";

    private static void Write(string path, JsonNode node)
    {
        var json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(path, json.Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
    }
}
