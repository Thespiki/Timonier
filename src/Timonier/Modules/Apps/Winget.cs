using System.Text;
using System.Text.RegularExpressions;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Apps;

/// <summary>Mise à jour disponible signalée par <c>winget upgrade</c>.</summary>
public sealed record UpgradeItem(string Name, string Id, string Version, string Available, string Source);

/// <summary>
/// Exécution de winget (liste blanche <see cref="SystemTool.Winget"/>, arguments séparés, sortie UTF-8) et interprétation
/// de ses codes de retour et de ses tableaux. Aucun shell, aucune concaténation de commande.
/// </summary>
public static partial class Winget
{
    public const string StoreProductUri = "ms-windows-store://pdp/?productid=9NBLGGH4NNS1";

    public static bool IsAvailable => SystemTools.IsAvailable(SystemTool.Winget);

    /// <summary>Lance winget ; les lignes utiles (sans barres de progression ni animations) sont transmises à <paramref name="progress"/>.</summary>
    public static Task<ProcessResult> RunAsync(IReadOnlyList<string> args, TimeSpan timeout, IProgress<string>? progress, CancellationToken ct)
    {
        IProgress<string>? filtered = progress is null ? null : new LineFilter(progress);
        return ProcessRunner.RunAsync(SystemTool.Winget, args, new RunOptions
        {
            Timeout = timeout,
            OutputEncoding = Encoding.UTF8,
            LineProgress = filtered,
        }, ct);
    }

    private sealed class LineFilter(IProgress<string> inner) : IProgress<string>
    {
        private string? _last;
        public void Report(string value)
        {
            var line = CleanLine(value);
            if (line is null || line == _last) return;
            _last = line;
            inner.Report(line);
        }
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiRx();

    [GeneratedRegex(@"^[\s\-\\|/]*$")]
    private static partial Regex SpinnerRx();

    [GeneratedRegex(@"[█▓▒░]+\s*(?<tail>.*)$")]
    private static partial Regex BarRx();

    /// <summary>Nettoie une ligne de sortie : retours chariot, séquences ANSI, animations. Null si rien d'utile.</summary>
    public static string? CleanLine(string raw)
    {
        var s = raw;
        var cr = s.LastIndexOf('\r');
        if (cr >= 0) s = s[(cr + 1)..];
        s = AnsiRx().Replace(s, "").Trim();
        if (s.Length == 0 || SpinnerRx().IsMatch(s)) return null;
        var bar = BarRx().Match(s);
        if (bar.Success)
        {
            var tail = bar.Groups["tail"].Value.Trim();
            return tail.Length == 0 ? null : L("Progression : {0}", tail);
        }
        return s.Length > 300 ? s[..300] + "…" : s;
    }

    // ------------------------------------------------------------------ Codes de retour

    /// <summary>Le code signifie que le logiciel est en place (installé, déjà présent, ou redémarrage pour terminer).</summary>
    public static bool IsSuccess(int exitCode) => unchecked((uint)exitCode) switch
    {
        0 or 0x8A150061 or 0x8A15010D or 0x8A150109 => true,
        _ => false,
    };

    /// <summary>Signification en français d'un code de retour winget (HRESULT APPINSTALLER_CLI_ERROR_*).</summary>
    public static string Describe(int exitCode, bool timedOut = false)
    {
        if (timedOut) return L("délai dépassé");
        return unchecked((uint)exitCode) switch
        {
            0 => L("réussi"),
            0x8A150005 => L("interrompu"),
            0x8A150008 => L("échec du téléchargement"),
            0x8A150010 => L("aucun installateur compatible avec ce PC"),
            0x8A150011 => L("empreinte de l'installateur incorrecte : installation refusée par sécurité"),
            0x8A150014 => L("introuvable dans la source winget"),
            0x8A150016 => L("plusieurs correspondances : nom ambigu"),
            0x8A15002B => L("déjà à jour (aucune mise à jour applicable)"),
            0x8A15002C => L("au moins une mise à jour a échoué"),
            0x8A150061 => L("déjà installé"),
            0x8A150101 => L("application en cours d'utilisation : fermez-la puis réessayez"),
            0x8A150102 => L("une autre installation est en cours"),
            0x8A150103 => L("un fichier est en cours d'utilisation"),
            0x8A150104 => L("dépendance manquante"),
            0x8A150105 => L("disque plein"),
            0x8A150106 => L("mémoire insuffisante"),
            0x8A150107 => L("pas de connexion réseau"),
            0x8A150108 => L("erreur de l'installateur (voir l'éditeur)"),
            0x8A150109 => L("redémarrage nécessaire pour terminer"),
            0x8A15010A => L("redémarrez le PC puis réessayez"),
            0x8A15010B => L("redémarrage lancé par l'installateur"),
            0x8A15010C => L("annulé"),
            0x8A15010D => L("déjà installé"),
            0x8A15010E => L("une version plus récente est déjà installée"),
            0x8A15010F => L("bloqué par une stratégie de l'organisation"),
            0x8A150110 => L("échec de l'installation d'une dépendance"),
            0x8A150111 => L("utilisé par une autre application"),
            0x8A150112 => L("paramètre refusé par l'installateur"),
            0x8A150113 => L("non compatible avec ce système"),
            _ => L("erreur 0x{0:X8}", unchecked((uint)exitCode)),
        };
    }

    // ------------------------------------------------------------------ Tableau « winget upgrade »

    [GeneratedRegex(@"^\s*-{10,}\s*$")]
    private static partial Regex DashesRx();

    /// <summary>
    /// Analyse les tableaux de <c>winget upgrade</c> : les positions des colonnes viennent de la ligne d'en-tête placée
    /// juste au-dessus de la ligne de tirets (en-têtes traduits selon la langue : seule la position compte).
    /// Tolérant : une ligne illisible est ignorée.
    /// </summary>
    public static List<UpgradeItem> ParseUpgradeTable(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(l => { var i = l.LastIndexOf('\r'); return AnsiRx().Replace(i >= 0 ? l[(i + 1)..] : l, "").TrimEnd(); })
            .ToList();
        var items = new List<UpgradeItem>();
        for (var i = 1; i < lines.Count; i++)
        {
            if (!DashesRx().IsMatch(lines[i])) continue;
            var starts = ColumnStarts(lines[i - 1]);
            if (starts.Count < 4) continue;
            for (var j = i + 1; j < lines.Count; j++)
            {
                var row = lines[j];
                if (row.Trim().Length == 0 || DashesRx().IsMatch(row)) break;
                if (ParseRow(row, starts) is { } item) items.Add(item);
            }
        }
        return [.. items.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())];
    }

    private static List<int> ColumnStarts(string header)
    {
        var starts = new List<int>();
        for (var c = 0; c < header.Length; c++)
            if (header[c] != ' ' && (c == 0 || header[c - 1] == ' ')) starts.Add(c);
        return starts;
    }

    private static UpgradeItem? ParseRow(string row, List<int> starts)
    {
        string Col(int k)
        {
            if (k >= starts.Count || starts[k] >= row.Length) return "";
            var end = k + 1 < starts.Count ? Math.Min(starts[k + 1], row.Length) : row.Length;
            return row[starts[k]..end].Trim();
        }

        var id = Col(1);
        if (IsId(id)) return new UpgradeItem(Col(0), id, Col(2), Col(3), starts.Count > 4 ? Col(4) : "");

        // Colonnes décalées (caractères larges dans le nom) : on relit la ligne depuis la droite.
        var tokens = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var n = starts.Count > 4 ? 4 : 3;
        if (tokens.Length <= n) return null;
        var tail = tokens[^n..];
        if (!IsId(tail[0])) return null;
        var name = string.Join(' ', tokens[..^n]);
        return new UpgradeItem(name, tail[0], tail[1], tail[2], n == 4 ? tail[3] : "");
    }

    private static bool IsId(string value)
    {
        if (value.Length < 3) return false;
        try { Validate.WingetId(value); return true; }
        catch (ValidationException) { return false; }
    }
}
