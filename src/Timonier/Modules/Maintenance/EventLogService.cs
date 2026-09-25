using System.Diagnostics.Eventing.Reader;
using Timonier.Core.Platform;

namespace Timonier.Modules.Maintenance;

internal enum HintTone { Harmless, Info, Attention }

internal sealed record EventHint(string Text, HintTone Tone);

/// <summary>Groupe d'événements (même journal, même source, même identifiant).</summary>
internal sealed class EventGroup
{
    public required string Log { get; init; }
    public required string Provider { get; init; }
    public required int EventId { get; init; }
    public required bool Critical { get; set; }
    public int Count { get; set; }
    public DateTime Last { get; set; }
    public string FirstLine { get; set; } = "";
    public EventHint? Hint { get; init; }

    public string ShortProvider => Provider.StartsWith("Microsoft-Windows-", StringComparison.OrdinalIgnoreCase) ? Provider[18..] : Provider;
}

internal sealed record EventScan(List<EventGroup> Groups, int Total, bool Capped, string? Error);

/// <summary>
/// Erreurs et événements critiques des 7 derniers jours (journaux Système et Application), lus avec une requête
/// XPath CONSTANTE, hors du thread UI, limités à 500 événements. Le texte n'est formaté qu'une fois par groupe.
/// </summary>
internal static class EventLogService
{
    public const int Cap = 500;
    private const int MaxReadPerLog = 3000;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);
    // Niveau 1 = critique, 2 = erreur ; 604 800 000 ms = 7 jours.
    private const string Query = "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 604800000]]]";

    public static EventScan Load(CancellationToken ct)
    {
        var groups = new Dictionary<string, EventGroup>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var meaningful = 0;
        var capped = false;
        string? error = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var log in new[] { "System", "Application" })
        {
            try
            {
                using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, Query) { ReverseDirection = true });
                var perLog = 0;
                var readInLog = 0;
                // Plafonds : 500 événements significatifs (dont 300 au plus pour le journal Système, pour laisser de la place
                // au journal Application). Les événements connus sans gravité (ex. DCOM 10016, très fréquents) sont comptés
                // à part pour ne pas masquer les vraies erreurs, avec une limite de lecture et de durée.
                var limit = log == "System" ? 300 : Cap;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    if (meaningful >= Cap || perLog >= limit || readInLog >= MaxReadPerLog || clock.Elapsed > Budget)
                    {
                        capped = true;
                        break;
                    }
                    using var record = reader.ReadEvent();
                    if (record is null) break;
                    total++;
                    readInLog++;
                    var provider = record.ProviderName ?? "?";
                    var key = log + "|" + provider + "|" + record.Id;
                    if (!groups.TryGetValue(key, out var g))
                    {
                        g = new EventGroup
                        {
                            Log = log, Provider = provider, EventId = record.Id, Critical = record.Level == 1,
                            Last = record.TimeCreated ?? DateTime.MinValue,
                            FirstLine = FirstLine(record),
                            Hint = HintFor(provider, record.Id),
                        };
                        groups[key] = g;
                    }
                    g.Count++;
                    if (record.Level == 1) g.Critical = true;
                    if (g.Hint?.Tone != HintTone.Harmless) { perLog++; meaningful++; }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
            {
                Log.Warn("Maintenance", $"journal {log} : {ex.Message}");
                error = $"Le journal « {(log == "System" ? "Système" : "Application")} » n'a pas pu être lu.";
            }
        }
        var list = groups.Values
            .OrderByDescending(g => g.Critical)
            .ThenByDescending(g => g.Hint?.Tone == HintTone.Attention)
            .ThenByDescending(g => g.Last)
            .ToList();
        return new EventScan(list, total, capped, error);
    }

    private static string FirstLine(EventRecord record)
    {
        string? text = null;
        try { text = record.FormatDescription(); }
        catch (EventLogException) { }
        if (string.IsNullOrWhiteSpace(text)) return "(description indisponible : le fournisseur de cet événement n'est pas installé)";
        var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return line.Length > 220 ? line[..220] + "…" : line;
    }

    /// <summary>Explications en français simple des événements les plus courants.</summary>
    public static EventHint? HintFor(string provider, int id)
    {
        var p = provider.ToLowerInvariant();
        return (p, id) switch
        {
            ("microsoft-windows-kernel-power", 41) => new("Le PC a redémarré sans s'éteindre proprement (coupure de courant, bouton d'alimentation maintenu, blocage ou écran bleu). Isolé, c'est sans gravité ; fréquent, cela évoque l'alimentation, la surchauffe ou un pilote.", HintTone.Attention),
            ("eventlog", 6008) => new("L'arrêt précédent du système était inattendu (voir aussi Kernel-Power 41).", HintTone.Info),
            ("microsoft-windows-wer-systemerrorreporting", 1001) or ("bugcheck", 1001) => new("Le PC a redémarré après un écran bleu. Si cela se répète, notez le code d'arrêt et mettez à jour les pilotes ; les vidages mémoire permettent le diagnostic.", HintTone.Attention),
            ("disk", 7) => new("Un disque signale un secteur défectueux : sauvegardez vos données et vérifiez l'état du disque.", HintTone.Attention),
            ("disk", 51) => new("Erreur d'accès au disque pendant une opération de pagination : câble, disque fatigué, ou clé USB/disque externe retiré.", HintTone.Attention),
            ("disk", 153) => new("Une opération de lecture/écriture a dû être relancée. Isolée, c'est bénin ; répétée, le disque ou son câble peut faiblir.", HintTone.Info),
            ("ntfs", 55) or ("ntfs", 98) or ("microsoft-windows-ntfs", 55) or ("microsoft-windows-ntfs", 98) => new("Le système de fichiers a détecté une incohérence : lancez « Analyser le disque système » dans la section Réparation.", HintTone.Attention),
            ("microsoft-windows-whea-logger", _) => new("Erreur matérielle signalée par le processeur, la mémoire ou le bus PCI Express. Les erreurs « corrigées » répétées méritent une mise à jour du BIOS et des pilotes.", HintTone.Attention),
            ("application error", 1000) => new("Une application s'est fermée brutalement (plantage). Son nom figure au début du message ; mettez-la à jour ou réinstallez-la si cela se répète.", HintTone.Info),
            ("application hang", 1002) => new("Une application a cessé de répondre et a été fermée.", HintTone.Info),
            (".net runtime", 1026) => new("Une application .NET s'est arrêtée à cause d'une erreur interne non gérée. Le nom de l'application figure dans le message.", HintTone.Info),
            ("microsoft-windows-distributedcom", 10016) => new("Avertissement d'autorisations DCOM connu et sans gravité : Microsoft recommande de l'ignorer.", HintTone.Harmless),
            ("microsoft-windows-distributedcom", 10010) => new("Un composant ne s'est pas enregistré à temps au démarrage ; généralement sans conséquence visible.", HintTone.Harmless),
            ("service control manager", 7000) or ("service control manager", 7009) or ("service control manager", 7011) =>
                new("Un service n'a pas démarré ou a répondu trop lentement. Sans gravité si tout fonctionne ; sinon, le nom du service est dans le message.", HintTone.Info),
            ("service control manager", 7023) or ("service control manager", 7024) or ("service control manager", 7031) or ("service control manager", 7034) =>
                new("Un service s'est arrêté de façon inattendue ; Windows le redémarre souvent automatiquement.", HintTone.Info),
            ("microsoft-windows-windowsupdateclient", 20) => new("L'installation d'une mise à jour a échoué. Windows réessaiera ; si l'échec se répète, lancez la réparation de l'image (DISM).", HintTone.Attention),
            ("volsnap", 25) or ("volsnap", 36) => new("Des points de restauration ont été supprimés faute d'espace réservé suffisant.", HintTone.Info),
            ("display", 4101) => new("Le pilote graphique a cessé de répondre puis a récupéré. Si cela se répète, mettez à jour le pilote graphique.", HintTone.Attention),
            ("schannel", _) => new("Erreur de connexion sécurisée (TLS) lors d'un échange réseau ; généralement sans gravité.", HintTone.Harmless),
            ("sidebyside", 33) => new("Une application n'a pas trouvé une bibliothèque Visual C++ : réinstallez-la ou installez le « Microsoft Visual C++ Redistributable ».", HintTone.Info),
            ("microsoft-windows-perflib", _) or ("perflib", _) => new("Un compteur de performances d'un logiciel est défectueux ; sans gravité.", HintTone.Harmless),
            ("microsoft-windows-security-spp", 8198) => new("Échec ponctuel de vérification de la licence Windows ; sans gravité si Windows est activé.", HintTone.Harmless),
            ("microsoft-windows-time-service", _) => new("Le service de temps n'a pas pu joindre son serveur ; vous pouvez resynchroniser l'heure dans la section Réparation.", HintTone.Info),
            ("microsoft-windows-dns-client", _) => new("Délai dépassé lors d'une résolution de nom : sans gravité si Internet fonctionne.", HintTone.Harmless),
            ("microsoft-windows-kernel-pnp", 219) => new("Un pilote de périphérique n'a pas pu être chargé au démarrage ; souvent sans conséquence.", HintTone.Harmless),
            ("microsoft-windows-kernel-boot", _) => new("Information de démarrage de Windows ; généralement sans gravité.", HintTone.Harmless),
            _ => null,
        };
    }
}
