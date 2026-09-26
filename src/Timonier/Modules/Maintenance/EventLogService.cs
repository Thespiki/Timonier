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
                error = L("The “{0}” log couldn't be read.", (log == "System" ? LC("journal", "System") : LC("journal", "Application")));
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
        if (string.IsNullOrWhiteSpace(text)) return L("(description unavailable: the provider of this event isn't installed)");
        var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return line.Length > 220 ? line[..220] + "…" : line;
    }

    /// <summary>Explications en français simple des événements les plus courants.</summary>
    public static EventHint? HintFor(string provider, int id)
    {
        var p = provider.ToLowerInvariant();
        return (p, id) switch
        {
            ("microsoft-windows-kernel-power", 41) => new(L("The PC restarted without shutting down cleanly (power outage, power button held down, freeze or blue screen). Once in a while, it's harmless; if it happens often, it points to the power supply, overheating or a driver."), HintTone.Attention),
            ("eventlog", 6008) => new(L("The previous system shutdown was unexpected (see also Kernel-Power 41)."), HintTone.Info),
            ("microsoft-windows-wer-systemerrorreporting", 1001) or ("bugcheck", 1001) => new(L("The PC restarted after a blue screen. If it happens again, note the stop code and update your drivers; memory dumps help with diagnosis."), HintTone.Attention),
            ("disk", 7) => new(L("A disk reports a bad sector: back up your data and check the disk's health."), HintTone.Attention),
            ("disk", 51) => new(L("Disk access error during a paging operation: cable, worn-out disk, or USB drive/external disk removed."), HintTone.Attention),
            ("disk", 153) => new(L("A read/write operation had to be retried. Once in a while, it's harmless; if it keeps happening, the disk or its cable may be failing."), HintTone.Info),
            ("ntfs", 55) or ("ntfs", 98) or ("microsoft-windows-ntfs", 55) or ("microsoft-windows-ntfs", 98) => new(L("The file system detected an inconsistency: run “Scan system disk” in the Repair section."), HintTone.Attention),
            ("microsoft-windows-whea-logger", _) => new(L("Hardware error reported by the processor, memory or PCI Express bus. Repeated “corrected” errors call for a BIOS and driver update."), HintTone.Attention),
            ("application error", 1000) => new(L("An app closed abruptly (crash). Its name appears at the start of the message; update or reinstall it if this happens again."), HintTone.Info),
            ("application hang", 1002) => new(L("An app stopped responding and was closed."), HintTone.Info),
            (".net runtime", 1026) => new(L("A .NET app stopped because of an unhandled internal error. The app's name is in the message."), HintTone.Info),
            ("microsoft-windows-distributedcom", 10016) => new(L("Known, harmless DCOM permissions warning: Microsoft recommends ignoring it."), HintTone.Harmless),
            ("microsoft-windows-distributedcom", 10010) => new(L("A component didn't register in time at startup; usually no visible effect."), HintTone.Harmless),
            ("service control manager", 7000) or ("service control manager", 7009) or ("service control manager", 7011) =>
                new(L("A service didn't start or responded too slowly. Harmless if everything works; otherwise, the service name is in the message."), HintTone.Info),
            ("service control manager", 7023) or ("service control manager", 7024) or ("service control manager", 7031) or ("service control manager", 7034) =>
                new(L("A service stopped unexpectedly; Windows often restarts it automatically."), HintTone.Info),
            ("microsoft-windows-windowsupdateclient", 20) => new(L("An update failed to install. Windows will try again; if it keeps failing, run the image repair (DISM)."), HintTone.Attention),
            ("volsnap", 25) or ("volsnap", 36) => new(L("Restore points were deleted because there wasn't enough reserved space."), HintTone.Info),
            ("display", 4101) => new(L("The graphics driver stopped responding and then recovered. If this happens again, update the graphics driver."), HintTone.Attention),
            ("schannel", _) => new(L("Secure connection (TLS) error during a network exchange; usually harmless."), HintTone.Harmless),
            ("sidebyside", 33) => new(L("An app couldn't find a Visual C++ library: reinstall it or install the “Microsoft Visual C++ Redistributable”."), HintTone.Info),
            ("microsoft-windows-perflib", _) or ("perflib", _) => new(L("A program's performance counter is faulty; harmless."), HintTone.Harmless),
            ("microsoft-windows-security-spp", 8198) => new(L("One-off Windows license check failure; harmless if Windows is activated."), HintTone.Harmless),
            ("microsoft-windows-time-service", _) => new(L("The time service couldn't reach its server; you can resync the time in the Repair section."), HintTone.Info),
            ("microsoft-windows-dns-client", _) => new(L("Timeout during name resolution: harmless if the internet works."), HintTone.Harmless),
            ("microsoft-windows-kernel-pnp", 219) => new(L("A device driver couldn't be loaded at startup; often without consequence."), HintTone.Harmless),
            ("microsoft-windows-kernel-boot", _) => new(L("Windows startup information; usually harmless."), HintTone.Harmless),
            _ => null,
        };
    }
}
