using System.ComponentModel;
using Timonier.Broker;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Core.Engine;

public static class JournalWriter
{
    public static UserJournalStore UserStore { get; } = new();

    public static void Write(JournalEntry entry, bool elevated)
    {
        if (elevated) MachineJournalStore.Write(entry);
        else UserStore.Add(entry);
    }
}

/// <summary>Résultat d'une application/annulation, présenté à l'utilisateur.</summary>
public sealed record ApplyOutcome(bool Success, string Message, ApplyEffect Effect = ApplyEffect.None, Guid? JournalId = null)
{
    public Dictionary<string, string>? Data { get; init; }
    public bool Cancelled { get; init; }

    public static ApplyOutcome Cancel(string message) => new(false, message) { Cancelled = true };
}

public sealed class TweakChangedEventArgs(string sourceId) : EventArgs
{
    public string SourceId { get; } = sourceId;
}

/// <summary>
/// Point d'entrée unique pour appliquer un réglage, lancer une action ou annuler. Décide seul de l'exécution
/// locale (sans élévation) ou via le broker admin (une seule invite UAC par session admin).
/// </summary>
public sealed class TweakEngine(ModuleRegistry registry, BrokerClient broker, SystemProfile profile)
{
    public event EventHandler<TweakChangedEventArgs>? Changed;

    public TweakState Detect(TweakDefinition tweak) => StateDetector.Detect(tweak);

    public Task<TweakState> DetectAsync(TweakDefinition tweak) => Task.Run(() => StateDetector.Detect(tweak));

    public string? Unavailability(TweakDefinition tweak) => tweak.Requirement.Check(profile);

    public async Task<ApplyOutcome> ApplyAsync(TweakDefinition tweak, string optionKey, CancellationToken ct = default)
    {
        if (tweak.GetOption(optionKey) is not { } option)
            return new ApplyOutcome(false, $"Option inconnue « {optionKey} ».");
        if (Unavailability(tweak) is { } reason)
            return new ApplyOutcome(false, reason);

        try
        {
            ApplyOutcome outcome;
            if (tweak.RequiresAdmin)
            {
                outcome = await broker.ApplyTweakAsync(tweak.Id, optionKey, ct).ConfigureAwait(false);
            }
            else
            {
                var from = StateDetector.Detect(tweak).OptionKey;
                var entry = await Task.Run(() => ApplyCore(tweak, option, from, new ExecContext { Elevated = false, Cancellation = ct }), ct).ConfigureAwait(false);
                outcome = new ApplyOutcome(true, $"{tweak.Title} : {option.Label}", tweak.Effect, entry.Id);
            }
            if (outcome.Success) Changed?.Invoke(this, new TweakChangedEventArgs(tweak.Id));
            return outcome;
        }
        catch (OperationCanceledException ex) { return ApplyOutcome.Cancel(ex.Message); }
        catch (Exception ex)
        {
            Log.Error("Engine", $"application de {tweak.Id}={optionKey}", ex);
            return new ApplyOutcome(false, Friendly(ex));
        }
    }

    /// <summary>
    /// Applique plusieurs réglages (profils) : une seule session admin, progression détaillée. Ne lève pas d'exception
    /// à l'annulation : renvoie les résultats déjà obtenus (le dernier est marqué <see cref="ApplyOutcome.Cancelled"/>
    /// si l'annulation l'a interrompu ; les réglages suivants ne sont pas tentés et n'apparaissent pas).
    /// </summary>
    public async Task<List<(TweakDefinition Tweak, ApplyOutcome Outcome)>> ApplyManyAsync(
        IEnumerable<(TweakDefinition Tweak, string Option)> items, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var results = new List<(TweakDefinition, ApplyOutcome)>();
        foreach (var (tweak, option) in items)
        {
            if (ct.IsCancellationRequested) break;
            progress?.Report($"{tweak.Title}…");
            var outcome = await ApplyAsync(tweak, option, ct).ConfigureAwait(false);
            results.Add((tweak, outcome));
            if (outcome.Cancelled) break;
        }
        return results;
    }

    /// <summary>Exécute une action paramétrée (localement ou via le broker selon <see cref="IActionHandler.RequiresAdmin"/>).</summary>
    public async Task<ApplyOutcome> RunActionAsync(string actionId, IReadOnlyDictionary<string, string>? parameters = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var handler = registry.GetAction(actionId);
        if (handler is null) return new ApplyOutcome(false, $"Action inconnue : {actionId}");
        var p = parameters ?? new Dictionary<string, string>();
        try
        {
            Validate.ParameterBag(p);
            handler.ValidateParameters(p); // validation côté interface (message immédiat), refaite par le broker
            ApplyOutcome outcome;
            if (handler.RequiresAdmin)
            {
                outcome = await broker.RunActionAsync(actionId, p, progress, ct).ConfigureAwait(false);
            }
            else
            {
                var ctx = new ActionContext { Exec = new ExecContext { Elevated = false, Progress = progress, Cancellation = ct } };
                var r = await Task.Run(() => handler.ExecuteAsync(ctx, p), ct).ConfigureAwait(false);
                outcome = new ApplyOutcome(r.Success, r.Message, r.Effect, r.JournalId) { Data = r.Data };
            }
            if (outcome.Success) Changed?.Invoke(this, new TweakChangedEventArgs(actionId));
            return outcome;
        }
        catch (ValidationException ex) { return new ApplyOutcome(false, ex.Message); }
        catch (OperationCanceledException ex) { return ApplyOutcome.Cancel(ex.Message); }
        catch (Exception ex)
        {
            Log.Error("Engine", "action " + actionId, ex);
            return new ApplyOutcome(false, Friendly(ex));
        }
    }

    public async Task<ApplyOutcome> UndoAsync(JournalEntry entry, CancellationToken ct = default)
    {
        if (!entry.CanUndo) return new ApplyOutcome(false, "Cette modification ne peut pas être annulée automatiquement.");
        try
        {
            ApplyOutcome outcome;
            if (entry.Machine)
            {
                outcome = await broker.UndoAsync(entry.Id, ct).ConfigureAwait(false);
            }
            else
            {
                var errors = await Task.Run(() => OperationExecutor.Undo(entry.Undo, ExecContext.Local()), ct).ConfigureAwait(false);
                entry.Undone = true;
                entry.UndoneAt = DateTimeOffset.Now;
                if (errors.Count > 0) entry.Note = string.Join(" ; ", errors);
                JournalWriter.UserStore.Update(entry);
                outcome = errors.Count == 0
                    ? new ApplyOutcome(true, $"Annulé : {entry.Title}", EffectOf(entry.SourceId))
                    : new ApplyOutcome(false, "Annulation partielle : " + string.Join(" ; ", errors));
            }
            if (outcome.Success) Changed?.Invoke(this, new TweakChangedEventArgs(entry.SourceId));
            return outcome;
        }
        catch (OperationCanceledException ex) { return ApplyOutcome.Cancel(ex.Message); }
        catch (Exception ex)
        {
            Log.Error("Engine", "annulation " + entry.Id, ex);
            return new ApplyOutcome(false, Friendly(ex));
        }
    }

    /// <summary>Journal complet (utilisateur + machine), du plus récent au plus ancien.</summary>
    public List<JournalEntry> JournalAll() =>
        [.. JournalWriter.UserStore.All().Concat(MachineJournalStore.ReadAll()).OrderByDescending(e => e.At)];

    private ApplyEffect EffectOf(string sourceId) => registry.GetTweak(sourceId)?.Effect ?? ApplyEffect.None;

    /// <summary>Cœur d'application, partagé avec le broker. Rollback automatique si une opération échoue.</summary>
    public static JournalEntry ApplyCore(TweakDefinition tweak, TweakOption option, string? fromOption, ExecContext ctx)
    {
        var entry = new JournalEntry
        {
            SourceId = tweak.Id,
            Title = tweak.Title,
            FromOption = fromOption,
            ToOption = option.Key,
            ToLabel = option.Label,
            Machine = ctx.Elevated,
            UserSid = ctx.UserSid,
        };
        try
        {
            foreach (var op in option.Operations)
                entry.Undo.Add(OperationExecutor.Execute(op, ctx));
            var notes = entry.Undo.OfType<NoUndo>().Select(n => n.Reason).Where(r => r != "Notification").Distinct().ToList();
            if (notes.Count > 0) entry.Note = string.Join(" ", notes);
            // Dans le bloc protégé : une modification qu'on ne peut pas journaliser (donc pas annuler) est défaite.
            JournalWriter.Write(entry, ctx.Elevated);
        }
        catch
        {
            var errors = OperationExecutor.Undo(entry.Undo, ctx);
            if (errors.Count > 0) Log.Warn("Engine", $"rollback incomplet pour {tweak.Id}: {string.Join(" ; ", errors)}");
            throw;
        }
        return entry;
    }

    public static string Friendly(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Accès refusé. " + ex.Message,
        Win32Exception { NativeErrorCode: 5 } => "Accès refusé par Windows (élément protégé).",
        System.Security.SecurityException => "Accès refusé par Windows (élément protégé).",
        _ => ex.Message,
    };
}
