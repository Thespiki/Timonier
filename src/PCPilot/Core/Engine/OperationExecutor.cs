using Microsoft.Win32;
using PcPilot.Core.Model;
using PcPilot.Core.Platform;

namespace PcPilot.Core.Engine;

/// <summary>Contexte d'exécution : processus courant (non élevé) ou broker (élevé, HKCU -> HKU\SID client).</summary>
public sealed class ExecContext
{
    public required bool Elevated { get; init; }
    /// <summary>SID de l'utilisateur ciblé pour HKCU (renseigné dans le broker).</summary>
    public string? UserSid { get; init; }
    public IProgress<string>? Progress { get; init; }
    public CancellationToken Cancellation { get; init; }

    public static ExecContext Local() => new() { Elevated = false };
}

/// <summary>
/// Exécute les opérations déclaratives en capturant l'état précédent (journal d'annulation).
/// Utilisé à l'identique par l'interface (réglages utilisateur) et par le broker (réglages admin).
/// </summary>
public static class OperationExecutor
{
    public static UndoRecord Execute(Operation op, ExecContext ctx)
    {
        ctx.Cancellation.ThrowIfCancellationRequested();
        if (op.RequiresAdmin && !ctx.Elevated)
            throw new UnauthorizedAccessException("Cette opération nécessite les droits administrateur : " + op.Describe());

        switch (op)
        {
            case RegSet set:
            {
                using var root = RegistryAccess.OpenRoot(set.Hive, ctx.UserSid);
                using var key = root.CreateSubKey(set.Key, true) ?? throw new IOException("Clé inaccessible : " + set.Key);
                var previous = key.GetValue(set.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var undo = new RegValueUndo(set.Hive, set.Key, set.Name, previous is not null,
                    previous is null ? null : RegValueSnapshot.From(previous, key.GetValueKind(set.Name)));
                key.SetValue(set.Name, set.Value, set.Kind);
                return undo;
            }
            case RegDeleteValue del:
            {
                using var root = RegistryAccess.OpenRoot(del.Hive, ctx.UserSid);
                using var key = root.OpenSubKey(del.Key, true);
                if (key is null) return new RegValueUndo(del.Hive, del.Key, del.Name, false, null);
                var previous = key.GetValue(del.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (previous is null) return new RegValueUndo(del.Hive, del.Key, del.Name, false, null);
                var undo = new RegValueUndo(del.Hive, del.Key, del.Name, true, RegValueSnapshot.From(previous, key.GetValueKind(del.Name)));
                key.DeleteValue(del.Name, false);
                return undo;
            }
            case RegDeleteKey delKey:
            {
                using var root = RegistryAccess.OpenRoot(delKey.Hive, ctx.UserSid);
                using var key = root.OpenSubKey(delKey.Key, false);
                if (key is null) return new RegKeyUndo(delKey.Hive, delKey.Key, false, null);
                var tree = SnapshotTree(key, depth: 0);
                root.DeleteSubKeyTree(delKey.Key, false);
                return new RegKeyUndo(delKey.Hive, delKey.Key, true, tree);
            }
            case ServiceStartOp svc:
            {
                var previous = ServiceConfig.ReadStart(svc.ServiceName);
                if (previous is null) return new NoUndo($"Service {svc.ServiceName} absent de ce PC (ignoré).");
                ServiceConfig.SetStart(svc.ServiceName, svc.Start);
                if (svc.Start == ServiceStartKind.Disabled && svc.StopIfDisabled)
                    ServiceConfig.TryStop(svc.ServiceName, TimeSpan.FromSeconds(15));
                return new ServiceUndo(svc.ServiceName, previous.Value);
            }
            case ScheduledTaskOp task:
            {
                var previous = TaskSchedulerHelper.IsEnabled(task.TaskPath);
                if (previous is null) return new NoUndo($"Tâche {task.TaskPath} absente de ce PC (ignorée).");
                TaskSchedulerHelper.SetEnabled(task.TaskPath, task.Enabled);
                return new TaskUndo(task.TaskPath, previous.Value);
            }
            case RunToolOp tool:
            {
                var result = ProcessRunner.RunAsync(tool.Tool, tool.Args, new RunOptions { LineProgress = ctx.Progress, Timeout = TimeSpan.FromMinutes(10) }, ctx.Cancellation)
                                          .GetAwaiter().GetResult();
                if (!result.Success)
                    throw new InvalidOperationException($"{SystemTools.FileName(tool.Tool)} a échoué (code {result.ExitCode}) : {Trim(result.CombinedOutput)}");
                return new NoUndo("Commande système (non annulable automatiquement).");
            }
            case BroadcastSettingChangeOp b:
                Native.BroadcastSettingChange(b.Area);
                return new NoUndo("Notification");
            default:
                throw new NotSupportedException(op.GetType().Name);
        }
    }

    /// <summary>Annule dans l'ordre inverse. Renvoie les erreurs éventuelles (l'annulation continue malgré elles).</summary>
    public static List<string> Undo(IEnumerable<UndoRecord> records, ExecContext ctx)
    {
        var errors = new List<string>();
        foreach (var record in records.Reverse())
        {
            try { UndoOne(record, ctx); }
            catch (Exception ex) { errors.Add(ex.Message); Log.Error("Undo", record.ToString() ?? "", ex); }
        }
        return errors;
    }

    private static void UndoOne(UndoRecord record, ExecContext ctx)
    {
        switch (record)
        {
            case RegValueUndo r:
            {
                using var root = RegistryAccess.OpenRoot(r.Hive, ctx.UserSid);
                if (r.Existed && r.Previous is not null)
                {
                    using var key = root.CreateSubKey(r.Key, true);
                    key.SetValue(r.Name, r.Previous.ToValue(), r.Previous.Kind);
                }
                else
                {
                    using var key = root.OpenSubKey(r.Key, true);
                    key?.DeleteValue(r.Name, false);
                }
                break;
            }
            case RegKeyUndo k:
            {
                using var root = RegistryAccess.OpenRoot(k.Hive, ctx.UserSid);
                if (k.Existed && k.Tree is not null)
                {
                    using var key = root.CreateSubKey(k.Key, true);
                    RestoreTree(key, k.Tree);
                }
                else
                {
                    root.DeleteSubKeyTree(k.Key, false);
                }
                break;
            }
            case ServiceUndo s:
                ServiceConfig.SetStart(s.Name, s.PreviousStart);
                if (s.PreviousStart is ServiceStartKind.Automatic or ServiceStartKind.AutomaticDelayed)
                    ServiceConfig.TryStart(s.Name, TimeSpan.FromSeconds(15));
                break;
            case TaskUndo t:
                TaskSchedulerHelper.SetEnabled(t.Path, t.PreviousEnabled);
                break;
            case NoUndo:
                break;
        }
    }

    private static RegTreeSnapshot SnapshotTree(RegistryKey key, int depth)
    {
        if (depth > 8) throw new InvalidOperationException("Arborescence de registre trop profonde pour être sauvegardée.");
        var values = new Dictionary<string, RegValueSnapshot>();
        foreach (var name in key.GetValueNames().Take(2000))
        {
            var v = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (RegValueSnapshot.From(v, key.GetValueKind(name)) is { } snap) values[name] = snap;
        }
        var subs = new Dictionary<string, RegTreeSnapshot>();
        foreach (var sub in key.GetSubKeyNames().Take(500))
        {
            using var sk = key.OpenSubKey(sub, false);
            if (sk is not null) subs[sub] = SnapshotTree(sk, depth + 1);
        }
        return new RegTreeSnapshot(values, subs);
    }

    private static void RestoreTree(RegistryKey key, RegTreeSnapshot tree)
    {
        foreach (var (name, snap) in tree.Values) key.SetValue(name, snap.ToValue(), snap.Kind);
        foreach (var (name, sub) in tree.SubKeys)
        {
            using var sk = key.CreateSubKey(name, true);
            RestoreTree(sk, sub);
        }
    }

    private static string Trim(string s) => s.Length > 400 ? s[..400] + "…" : s.Trim();
}

/// <summary>État détecté d'un réglage.</summary>
public sealed record TweakState(string? OptionKey, bool Partial, bool Unknown)
{
    public static readonly TweakState UnknownState = new(null, false, true);
}

/// <summary>Détection de l'état actuel d'un réglage à partir de ses opérations (lecture seule, sans élévation).</summary>
public static class StateDetector
{
    public static TweakState Detect(TweakDefinition t)
    {
        if (t.CustomDetect is not null)
        {
            try { return t.CustomDetect() is { } key ? new TweakState(key, false, false) : TweakState.UnknownState; }
            catch (Exception ex) { Log.Warn("Detect", $"{t.Id}: {ex.Message}"); return TweakState.UnknownState; }
        }
        if (t.Kind == TweakKind.Action) return TweakState.UnknownState;

        string? best = null;
        var bestOps = -1;
        string? partialBest = null;
        var partialScore = 0.0;
        foreach (var option in t.Options)
        {
            var checkable = option.Operations.Where(IsCheckable).ToList();
            if (checkable.Count == 0) continue;
            var matched = checkable.Count(Matches);
            if (matched == checkable.Count && checkable.Count > bestOps)
            {
                best = option.Key;
                bestOps = checkable.Count;
            }
            else if (matched > 0 && (double)matched / checkable.Count > partialScore)
            {
                partialBest = option.Key;
                partialScore = (double)matched / checkable.Count;
            }
        }
        if (best is not null) return new TweakState(best, false, false);
        if (t.WindowsDefault is not null) return new TweakState(t.WindowsDefault, false, false);
        if (partialBest is not null) return new TweakState(partialBest, true, false);
        return TweakState.UnknownState;
    }

    private static bool IsCheckable(Operation op) => op is RegSet or RegDeleteValue or RegDeleteKey or ServiceStartOp or ScheduledTaskOp;

    public static bool Matches(Operation op)
    {
        try
        {
            return op switch
            {
                RegSet s => RegistryAccess.ValueEquals(RegistryAccess.Read(s.Hive, s.Key, s.Name), s.Kind, s.Value),
                RegDeleteValue d => RegistryAccess.Read(d.Hive, d.Key, d.Name) is null,
                RegDeleteKey k => !RegistryAccess.KeyExists(k.Hive, k.Key),
                // Un service ou une tâche absent(e) est considéré(e) conforme : il n'y a rien à faire.
                ServiceStartOp svc => ServiceConfig.ReadStart(svc.ServiceName) is not { } start || start == svc.Start,
                ScheduledTaskOp task => TaskSchedulerHelper.IsEnabled(task.TaskPath) is not { } enabled || enabled == task.Enabled,
                _ => false,
            };
        }
        catch { return false; }
    }
}
