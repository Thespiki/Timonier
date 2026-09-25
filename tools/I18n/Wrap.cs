// Commande « wrap » : entoure automatiquement de L(...) les textes d'interface écrits en français dans le code C#.
//
// Prudente par construction : elle ne touche qu'aux littéraux reconnus comme du français (accents, mots courants de
// l'interface, phrases avec des mots outils) et jamais aux identifiants, chemins, URI, scripts, formats de date,
// journaux (Log.*), ni aux contextes où un appel est impossible (const, case, attributs, valeurs par défaut).
// Ce qu'elle ne peut pas traiter sans risque est listé dans un rapport pour relecture humaine.
//
// Transformations :
//   "Texte"                          → L("Texte")
//   $"Il reste {n} min"              → L("Il reste {0} min", n)
//   "Point du " + date + " créé"     → L("Point du {0} créé", date)        (chaîne commençant par un texte)
//   .Keywords("a", "b") / Keywords = ["a", "b"]  → une seule chaîne traduisible L("a, b")
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

static partial class Wrapper
{
    public static int Run(IEnumerable<string> files, string root, string reportPath)
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var report = new StringBuilder();
        int changedFiles = 0, total = 0;
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(text, options, file);
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            // Une méthode locale nommée L, LC ou LP masquerait Loc.L : on laisse le fichier intact et on le signale.
            var clash = tree.GetRoot().DescendantNodes().FirstOrDefault(n =>
                n is MethodDeclarationSyntax { Identifier.Text: "L" or "LC" or "LP" } or LocalFunctionStatementSyntax { Identifier.Text: "L" or "LC" or "LP" });
            if (clash is not null)
            {
                report.AppendLine($"{rel}:{tree.GetLineSpan(clash.Span).StartLinePosition.Line + 1} [conflit de nom] une méthode nommée L/LC/LP masque Loc.L : renommez-la puis relancez wrap sur ce fichier");
                continue;
            }
            var rewriter = new Rewriter(tree, rel);
            var newRoot = rewriter.Visit(tree.GetRoot());
            foreach (var r in rewriter.Report) report.AppendLine(r);
            if (rewriter.Count == 0) continue;
            File.WriteAllText(file, newRoot.ToFullString(), new UTF8Encoding(HasBom(file)));
            changedFiles++;
            total += rewriter.Count;
        }
        File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"{total} texte(s) entouré(s) dans {changedFiles} fichier(s) ; points à relire : {report.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} → {reportPath}");
        return 0;
    }

    static bool HasBom(string file)
    {
        using var fs = File.OpenRead(file);
        Span<byte> b = stackalloc byte[3];
        return fs.Read(b) == 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
    }

    // ------------------------------------------------------------------ reconnaissance du français

    [GeneratedRegex("[àâäçéèêëîïôöùûüÿœæÀÂÄÇÉÈÊËÎÏÔÖÙÛÜŸŒÆ«»’…]")]
    private static partial Regex FrenchChars();

    [GeneratedRegex(@"(?i)(^|[\s'’(])(de|la|le|les|des|du|un|une|et|ou|pour|avec|sur|dans|par|est|sont|pas|ne|vous|votre|vos|ce|cette|ces|au|aux|en|qui|que|plus|sans|son|sa|ses|il|elle|nous|mise|jour|aucun|aucune|tous|toutes|tout|peut|peuvent|faire|fait)(?=$|[\s,.;:!?'’)])")]
    private static partial Regex FrenchWord();

    private static readonly HashSet<string> UiWords = new(StringComparer.Ordinal)
    {
        "Annuler", "Fermer", "Ouvrir", "Appliquer", "Activer", "Actualiser", "Rechercher", "Supprimer", "Modifier",
        "Installer", "Enregistrer", "Retour", "Suivant", "Terminer", "Continuer", "Oui", "Non", "Aucun", "Aucune",
        "Tout", "Tous", "Toutes", "Voir", "Afficher", "Masquer", "Copier", "Exporter", "Importer", "Relancer",
        "Bloquer", "Autoriser", "Choisir", "Parcourir", "Valider", "Confirmer", "Ignorer", "Quitter", "Chargement",
        "Inconnu", "Inconnue", "Actif", "Inactif", "Automatique", "Manuel", "Administrateur", "Utilisateur",
        "Batterie", "Secteur", "Processeur", "Stockage", "Disque", "Nom", "Taille", "Statut", "Filtrer", "Trier",
        "Moins", "Accueil", "Outils", "Profils", "Pilotes", "Mises", "Heures", "Jours", "Semaines", "Minutes",
        "Suspendre", "Reprendre", "Renommer", "Oublier", "Restaurer", "Nettoyer", "Analyser", "Mesurer", "Lancer",
        "Planifier", "Retirer", "Ajouter", "Changer", "Chercher", "Configurer", "Rejeter", "Bloqué", "Aucun",
        "Mot", "Compte", "Comptes", "Connexion", "Connecté", "Déconnecté", "Pilote", "Carte", "Adresse", "Masque",
        "Passerelle", "Fabricant", "Modèle", "Autres", "Autre", "Divers", "Avancé", "Erreur", "Erreurs",
        "Avertissement", "Information", "Critique", "Recommandé", "Facultatif", "Obligatoire",
    };

    public static bool IsFrench(string s)
    {
        if (s.Length < 2 || !s.Any(char.IsLetter)) return false;
        if (s.Contains('\\') || s.Contains("://") || s.StartsWith("ms-", StringComparison.Ordinal) || s.StartsWith("shell:", StringComparison.Ordinal)) return false;
        if (FrenchChars().IsMatch(s)) return true;
        var trimmed = s.Trim(' ', ':', '.', '…', '!', '?');
        if (UiWords.Contains(trimmed)) return true;
        return s.Contains(' ') && FrenchWord().IsMatch(s);
    }

    // ------------------------------------------------------------------ réécriture

    sealed partial class Rewriter(SyntaxTree tree, string file) : CSharpSyntaxRewriter(visitIntoStructuredTrivia: false)
    {
        public int Count;
        public List<string> Report { get; } = [];

        // Méthodes dont les arguments texte ne sont pas des textes d'interface.
        private static readonly HashSet<string> SkipMethods = new(StringComparer.Ordinal)
        {
            "nameof", "AddGroup", "Tags", "GetValue", "SetValue", "OpenSubKey", "CreateSubKey", "DeleteValue",
            "DeleteSubKey", "DeleteSubKeyTree", "GetManifestResourceStream", "GetEnvironmentVariable",
            "SetEnvironmentVariable", "Equals", "StartsWith", "EndsWith", "Contains", "IndexOf", "LastIndexOf",
            "Replace", "Split", "Trim", "TrimStart", "TrimEnd", "IsMatch", "Match", "Matches", "Escape", "Query",
            "FindResource", "TryFindResource", "SetResourceReference", "Parse", "ParseExact", "TryParse",
            "TryParseExact", "ToString", "OpenSettingsUri", "OpenUrl", "Launch", "GetProperty", "TryGetProperty",
            "GetString", "GetInt32", "Combine", "GetFullPath", "Exists", "Delete", "Move", "Copy", "Load",
            "InvokeMember", "GetType", "GetTypeFromProgID", "CreateInstance", "Element", "Elements", "Descendants",
            "Attribute", "RegisterClassName", "GetProcessesByName",
            // Écritures dans des fichiers : marqueurs et contenus lus par la machine ne doivent pas être traduits.
            "WriteAllText", "WriteAllLines", "AppendAllText", "AppendAllLines", "WriteLine", "Write",
        };

        // Récepteurs dont aucun argument n'est un texte d'interface.
        private static readonly HashSet<string> SkipReceivers = new(StringComparer.Ordinal)
        {
            "Log", "Debug", "Trace", "Console", "Reg", "Sys", "Registry", "RegistryAccess", "ProcessRunner",
            "PowerShellRunner", "WmiQuery", "Path", "File", "Directory", "Regex", "Environment", "Validate",
            "Process", "Loc", "Synonyms",
        };

        private string Where(SyntaxNode n) => $"{file}:{tree.GetLineSpan(n.Span).StartLinePosition.Line + 1}";

        private void Note(SyntaxNode n, string why, string text) =>
            Report.Add($"{Where(n)} [{why}] {Short(text)}");

        private static string Short(string s) => (s.Length <= 90 ? s : s[..87] + "…").Replace("\n", "\\n").Replace("\r", "");

        // Contexte d'un nœud d'origine : null = on peut entourer ; sinon raison du refus (et faut-il le signaler ?).
        private (string Why, bool Report)? Blocked(SyntaxNode node)
        {
            for (var p = node.Parent; p is not null; p = p.Parent)
            {
                switch (p)
                {
                    case AttributeArgumentSyntax: return ("attribut", true);
                    case CaseSwitchLabelSyntax or ConstantPatternSyntax or RelationalPatternSyntax: return ("motif constant", true);
                    case ParameterSyntax: return ("valeur par défaut", true);
                    case FieldDeclarationSyntax f when f.Modifiers.Any(SyntaxKind.ConstKeyword): return ("const", true);
                    case LocalDeclarationStatementSyntax l when l.IsConst: return ("const", true);
                    case InvocationExpressionSyntax inv when IsLocCall(inv): return ("déjà traduit", false);
                    case InvocationExpressionSyntax inv:
                        var (receiver, name) = Callee(inv);
                        if (receiver is not null && SkipReceivers.Contains(receiver))
                            return receiver is "Log" or "Debug" or "Trace" or "Console" or "Synonyms" ? ("journal", false) : ($"{receiver}.{name}", true);
                        // Synonymes de recherche français/anglais : volontairement propres à ces langues.
                        if (name == "AddGroup") return ("synonymes", false);
                        if (name is not null && SkipMethods.Contains(name)) return ($"{name}()", true);
                        // Argument direct d'un autre appel : on s'arrête au premier appel englobant.
                        if (node.Ancestors().TakeWhile(a => a != p).Any(a => a is ArgumentSyntax)) return null;
                        break;
                    case ElementAccessExpressionSyntax: return ("indexeur", false);
                    // Comparaison avec un texte : souvent la sortie d'un outil Windows ou une valeur stockée.
                    case BinaryExpressionSyntax be when be.IsKind(SyntaxKind.EqualsExpression) || be.IsKind(SyntaxKind.NotEqualsExpression):
                        return ("comparaison ==", true);
                    case ObjectCreationExpressionSyntax oc when oc.Type.ToString() is "Uri" or "Regex" or "ProcessStartInfo" or "ManagementObjectSearcher" or "ManagementClass" or "ObjectQuery":
                        return ($"new {oc.Type}", true);
                    case StatementSyntax or MemberDeclarationSyntax: return null;
                }
            }
            return null;
        }

        private static bool IsLocCall(InvocationExpressionSyntax inv) => Callee(inv).Name is "L" or "LC" or "LP" && Callee(inv).Receiver is null or "Loc";

        private static (string? Receiver, string? Name) Callee(InvocationExpressionSyntax inv) => inv.Expression switch
        {
            IdentifierNameSyntax id => (null, id.Identifier.Text),
            GenericNameSyntax g => (null, g.Identifier.Text),
            MemberAccessExpressionSyntax ma => (LastName(ma.Expression), ma.Name.Identifier.Text),
            _ => (null, null),
        };

        private static string? LastName(ExpressionSyntax e) => e switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            PredefinedTypeSyntax pt => pt.Keyword.Text,
            _ => null,
        };

        private static InvocationExpressionSyntax MakeL(string format, IEnumerable<ExpressionSyntax> args)
        {
            var list = new List<ArgumentSyntax> { Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(format))) };
            list.AddRange(args.Select(a => Argument(a.WithoutTrivia())));
            var commas = Enumerable.Range(0, list.Count - 1).Select(_ => Token(SyntaxKind.CommaToken).WithTrailingTrivia(Space));
            return InvocationExpression(IdentifierName("L"), ArgumentList(SeparatedList(list, commas)));
        }

        private static bool IsRegularString(LiteralExpressionSyntax lit) =>
            lit.IsKind(SyntaxKind.StringLiteralExpression) && lit.Token.Text.StartsWith('"') && !lit.Token.Text.StartsWith("\"\"\"");

        // --- littéraux
        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.StringLiteralExpression)) return node;
            var value = node.Token.ValueText;
            if (!IsFrench(value)) return node;
            if (!IsRegularString(node)) { Note(node, "verbatim/brut", value); return node; }
            if (Blocked(node) is { } b) { if (b.Report) Note(node, b.Why, value); return node; }
            Count++;
            return MakeL(value, []).WithTriviaFrom(node);
        }

        // --- chaînes interpolées
        public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            var visited = (InterpolatedStringExpressionSyntax)base.VisitInterpolatedStringExpression(node)!;
            var texts = string.Concat(node.Contents.OfType<InterpolatedStringTextSyntax>().Select(t => t.TextToken.ValueText));
            if (!IsFrench(texts)) return visited;
            if (!node.StringStartToken.IsKind(SyntaxKind.InterpolatedStringStartToken)) { Note(node, "interpolée verbatim/brute", texts); return visited; }
            if (Blocked(node) is { } b) { if (b.Report) Note(node, b.Why, texts); return visited; }
            var (format, args) = ToFormat(visited, 0);
            Count++;
            return MakeL(format, args).WithTriviaFrom(node);
        }

        private static (string Format, List<ExpressionSyntax> Args) ToFormat(InterpolatedStringExpressionSyntax s, int offset)
        {
            var sb = new StringBuilder();
            var args = new List<ExpressionSyntax>();
            foreach (var c in s.Contents)
            {
                if (c is InterpolatedStringTextSyntax t) sb.Append(EscapeBraces(t.TextToken.ValueText));
                else if (c is InterpolationSyntax i)
                {
                    sb.Append('{').Append(offset + args.Count);
                    if (i.AlignmentClause is { } al) sb.Append(',').Append(al.Value.ToString());
                    if (i.FormatClause is { } fc) sb.Append(':').Append(fc.FormatStringToken.ValueText);
                    sb.Append('}');
                    args.Add(i.Expression);
                }
            }
            return (sb.ToString(), args);
        }

        private static string EscapeBraces(string s) => s.Replace("{", "{{").Replace("}", "}}");

        // --- concaténations « texte " + x + " texte »
        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var visited = base.VisitBinaryExpression(node);
            if (!node.IsKind(SyntaxKind.AddExpression) || node.Parent is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression }) return visited;
            if (visited is not BinaryExpressionSyntax v) return visited;

            var original = Flatten(node);
            var operands = Flatten(v);
            if (operands.Count != original.Count || operands.Count < 2) return visited;
            // Il faut au moins un morceau français, et que la chaîne commence par un texte (sinon « a + b + "x" » additionne).
            if (!operands.Any(o => TextOf(o) is { French: true })) return visited;
            if (TextOf(operands[0]) is null)
            {
                Note(node, "concaténation à revoir", node.ToString());
                return visited;
            }
            if (Blocked(node) is { } b) { if (b.Report) Note(node, b.Why, node.ToString()); return visited; }

            var sb = new StringBuilder();
            var args = new List<ExpressionSyntax>();
            foreach (var o in operands)
            {
                var t = TextOf(o);
                if (t is null) { sb.Append('{').Append(args.Count).Append('}'); args.Add(o); continue; }
                sb.Append(Renumber(t.Value.Format, args.Count));
                args.AddRange(t.Value.Args);
            }
            // Les morceaux déjà entourés comptaient chacun pour un texte : on les remplace par la phrase entière.
            Count -= operands.Count(o => TextOf(o) is { French: true }) - 1;
            return MakeL(sb.ToString(), args).WithTriviaFrom(node);
        }

        private static List<ExpressionSyntax> Flatten(ExpressionSyntax e) =>
            e is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression) ? [.. Flatten(b.Left), .. Flatten(b.Right)]
            : e is ParenthesizedExpressionSyntax { Expression: BinaryExpressionSyntax inner } && inner.IsKind(SyntaxKind.AddExpression) ? [e]
            : [e];

        // Morceau de texte : littéral simple, ou appel L(format, args…) produit par cette réécriture.
        private static (string Format, List<ExpressionSyntax> Args, bool French)? TextOf(ExpressionSyntax e)
        {
            if (e is LiteralExpressionSyntax lit && IsRegularString(lit))
                return (EscapeBraces(lit.Token.ValueText), [], false);
            if (e is InvocationExpressionSyntax inv && inv.Expression is IdentifierNameSyntax { Identifier.Text: "L" }
                && inv.ArgumentList.Arguments.Count >= 1
                && inv.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax l && IsRegularString(l))
            {
                var args = inv.ArgumentList.Arguments.Skip(1).Select(a => a.Expression).ToList();
                // L("texte") sans argument : le texte n'est pas un format, ses accolades doivent être échappées.
                var fmt = args.Count == 0 ? EscapeBraces(l.Token.ValueText) : l.Token.ValueText;
                return (fmt, args, true);
            }
            return null;
        }

        [GeneratedRegex(@"\{(\d+)((?:,[^}:]*)?(?::[^}]*)?)\}")]
        private static partial Regex Placeholder();

        private static string Renumber(string format, int offset)
        {
            if (offset == 0) return format;
            // On ignore les accolades doublées ({{ }}), qui ne sont pas des emplacements.
            var parts = format.Split("{{");
            for (var i = 0; i < parts.Length; i++)
                parts[i] = Placeholder().Replace(parts[i], m => "{" + (int.Parse(m.Groups[1].Value) + offset) + m.Groups[2].Value + "}");
            return string.Join("{{", parts);
        }

        // --- mots-clés de recherche : une seule chaîne traduisible
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (Callee(node).Name == "Keywords" && node.ArgumentList.Arguments.Count > 0
                && node.ArgumentList.Arguments.All(a => a.Expression is LiteralExpressionSyntax l && IsRegularString(l))
                && node.ArgumentList.Arguments.Any(a => IsFrench(((LiteralExpressionSyntax)a.Expression).Token.ValueText) || true))
            {
                var joined = string.Join(", ", node.ArgumentList.Arguments.Select(a => ((LiteralExpressionSyntax)a.Expression).Token.ValueText));
                Count++;
                var expr = (ExpressionSyntax)Visit(node.Expression)!;
                return node.WithExpression(expr).WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(MakeL(joined, [])))));
            }
            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (node.Left is IdentifierNameSyntax { Identifier.Text: "Keywords" } && node.Right is CollectionExpressionSyntax coll
                && coll.Elements.Count > 0
                && coll.Elements.All(e => e is ExpressionElementSyntax { Expression: LiteralExpressionSyntax l } && IsRegularString(l)))
            {
                var joined = string.Join(", ", coll.Elements.Cast<ExpressionElementSyntax>().Select(e => ((LiteralExpressionSyntax)e.Expression).Token.ValueText));
                Count++;
                var newColl = CollectionExpression(SingletonSeparatedList<CollectionElementSyntax>(ExpressionElement(MakeL(joined, []))))
                    .WithTriviaFrom(coll);
                return node.WithRight(newColl);
            }
            return base.VisitAssignmentExpression(node);
        }
    }
}
