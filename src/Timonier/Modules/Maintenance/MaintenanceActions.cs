using System.Globalization;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Timonier.Core.Catalog;
using Timonier.Core.Localization;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Maintenance;

// Toutes les actions de ce fichier peuvent s'exécuter dans le broker élevé : aucune référence à AppHost ni à l'interface.

/// <summary>Nettoyage des catégories système. Paramètre « categories » : clés séparées par des virgules, prises dans une liste blanche.</summary>
internal sealed class CleanupRunAction : IActionHandler
{
    public const string ActionId = "maintenance.cleanup.run";
    public string Id => ActionId;
    public string Title => L("Clean up system files");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        ParseCategories(p);
        if (Validate.Optional(p, "mode", 16) is not null) Validate.OneOf(p, "mode", "clean", "analyze");
    }

    public static List<string> ParseCategories(IReadOnlyDictionary<string, string> p)
    {
        var raw = Validate.Required(p, "categories", 200);
        var keys = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var key = CleanupCatalog.AdminKeys.FirstOrDefault(k => k == part)
                      ?? throw new ValidationException(L("Cleanup category not allowed: {0}", part));
            if (!keys.Contains(key)) keys.Add(key);
        }
        if (keys.Count == 0) throw new ValidationException(L("No cleanup category selected."));
        return keys;
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var keys = ParseCategories(p);
        var analyze = string.Equals(Validate.Optional(p, "mode", 16), "analyze", StringComparison.OrdinalIgnoreCase);
        var data = new Dictionary<string, string>();
        long total = 0, files = 0, skipped = 0;

        foreach (var key in keys)
        {
            ctx.Cancellation.ThrowIfCancellationRequested();
            var category = CleanupCatalog.Get(key)!;
            ctx.Progress?.Report(analyze ? L("Scanning: {0}…", category.Title) : L("Cleaning up: {0}…", category.Title));
            CleanupStats stats;
            try
            {
                if (analyze)
                    stats = CleanupEngine.Analyze(category, TimeSpan.FromSeconds(20), ctx.Cancellation);
                else if (category.Kind == CleanupKind.DeliveryOptimization)
                    stats = await CleanDeliveryOptimizationAsync(ctx.Cancellation).ConfigureAwait(false);
                else if (key == "wucache" && CleanupCatalog.RebootPending())
                    stats = new CleanupStats { Note = L("Skipped: a Windows Update restart is pending.") };
                else
                    stats = CleanupEngine.Clean(category, TimeSpan.FromMinutes(3), ctx.Cancellation);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warn("Maintenance", $"nettoyage {key} : {ex.Message}");
                stats = new CleanupStats { Note = L("Error: {0}", ex.Message) };
            }
            data[key + ".bytes"] = stats.Bytes.ToString(CultureInfo.InvariantCulture);
            data[key + ".files"] = stats.Files.ToString(CultureInfo.InvariantCulture);
            data[key + ".skipped"] = stats.Skipped.ToString(CultureInfo.InvariantCulture);
            if (stats.Partial) data[key + ".partial"] = "1";
            if (stats.Note is { } note) data[key + ".note"] = note;
            total += stats.Bytes; files += stats.Files; skipped += stats.Skipped;
        }
        data["total.bytes"] = total.ToString(CultureInfo.InvariantCulture);
        data["total.files"] = files.ToString(CultureInfo.InvariantCulture);
        data["total.skipped"] = skipped.ToString(CultureInfo.InvariantCulture);

        var message = analyze
            ? L("Scan complete: {0} can be freed.", Format.Bytes(total))
            : skipped > 0
                ? LP(skipped, "System cleanup complete: {1} freed. {0} file in use skipped.",
                    "System cleanup complete: {1} freed. {0} files in use skipped.", Format.Bytes(total))
                : L("System cleanup complete: {0} freed.", Format.Bytes(total));
        return ActionResult.Ok(message, data);
    }

    private const string DeleteDoCacheScript = "Delete-DeliveryOptimizationCache -Force";

    private static async Task<CleanupStats> CleanDeliveryOptimizationAsync(CancellationToken ct)
    {
        var before = CleanupEngine.MeasureFolder(CleanupCatalog.DeliveryOptimizationCacheDir, TimeSpan.FromSeconds(15), ct);
        var r = await PowerShellRunner.RunAsync(DeleteDoCacheScript, null, TimeSpan.FromMinutes(5), null, ct).ConfigureAwait(false);
        if (!r.Success)
        {
            Log.Warn("Maintenance", "Delete-DeliveryOptimizationCache : " + r.Error.Trim());
            return new CleanupStats { Note = L("The cache couldn't be cleared (Delivery Optimization service unavailable?).") };
        }
        var after = CleanupEngine.MeasureFolder(CleanupCatalog.DeliveryOptimizationCacheDir, TimeSpan.FromSeconds(15), ct);
        return new CleanupStats { Bytes = Math.Max(0, before.Bytes - after.Bytes), Files = Math.Max(0, before.Files - after.Files) };
    }
}

/// <summary>Exécution d'un outil de réparation avec progression relayée (pourcentages) et délai long.</summary>
internal static partial class RepairRunner
{
    [GeneratedRegex(@"(\d{1,3}(?:[.,]\d)?)\s?%")]
    private static partial Regex PercentRx();

    public static Task<ProcessResult> RunAsync(SystemTool tool, string[] args, TimeSpan timeout, Encoding? encoding, string label, ActionContext ctx)
    {
        var lastPercent = "";
        var lastReport = DateTime.MinValue;
        var progress = new LineHandler(line =>
        {
            var text = line.Replace("\0", "").Trim();
            if (text.Length == 0) return;
            var m = PercentRx().Match(text);
            if (m.Success)
            {
                var pct = m.Groups[1].Value.Replace(',', '.');
                var dot = pct.IndexOf('.');
                if (dot > 0) pct = pct[..dot];
                if (pct == lastPercent) return;
                lastPercent = pct;
                ctx.Progress?.Report($"{label} {(int.TryParse(pct, out var n) ? Format.Percent(n / 100.0) : pct + " %")}");
                return;
            }
            // Autres lignes : relayées sans inonder le canal.
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds < 400) return;
            lastReport = DateTime.UtcNow;
            ctx.Progress?.Report(text.Length > 160 ? text[..160] + "…" : text);
        });
        return ProcessRunner.RunAsync(tool, args, new RunOptions { Timeout = timeout, OutputEncoding = encoding, LineProgress = progress }, ctx.Cancellation);
    }

    /// <summary>Traitement direct et ordonné des lignes (Progress&lt;T&gt; posterait sur le pool, dans le désordre).</summary>
    private sealed class LineHandler(Action<string> handle) : IProgress<string>
    {
        private readonly Lock _gate = new();
        public void Report(string value)
        {
            lock (_gate) handle(value);
        }
    }

    public static string Normalize(string output) => output.Replace("\0", "").Replace('’', '\'');

    public static string LastLine(string output) =>
        Normalize(output).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";

    public static bool Has(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Vérificateur des fichiers système (sfc /scannow). Sortie en UTF-16 lorsqu'elle est redirigée.</summary>
internal sealed class RepairSfcAction : IActionHandler
{
    public const string ActionId = "maintenance.repair.sfc";
    public string Id => ActionId;
    public string Title => L("Check system files (SFC)");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        ctx.Progress?.Report(L("Starting the system file check…"));
        var r = await RepairRunner.RunAsync(SystemTool.Sfc, ["/scannow"], TimeSpan.FromMinutes(90), Encoding.Unicode, L("System file check:"), ctx).ConfigureAwait(false);
        if (r.TimedOut) return ActionResult.Fail(L("The check took more than 90 minutes and was stopped."));
        return Interpret(RepairRunner.Normalize(r.CombinedOutput), r.ExitCode);
    }

    internal static ActionResult Interpret(string o, int exitCode)
    {
        // Textes comparés à la sortie de sfc (français ou anglais selon Windows) : jamais traduits.
        if (RepairRunner.Has(o, "pas pu en réparer", "n'ont pas pu être réparés", "unable to fix"))
            return ActionResult.Fail(L("SFC found corrupted system files but couldn't repair all of them. Run “Repair Windows image (DISM)”, restart, then run SFC again."));
        if (RepairRunner.Has(o, "réparation du système est en attente", "system repair pending"))
            return ActionResult.Fail(L("A system repair is already pending: restart the PC, then run the check again."));
        if (RepairRunner.Has(o, "pas pu effectuer l'opération", "could not perform the requested operation"))
            return ActionResult.Fail(L("SFC couldn't perform the scan. Restart and try again; if the problem persists, run DISM first."));
        if (RepairRunner.Has(o, "aucune violation", "did not find any integrity violations"))
            return ActionResult.Ok(L("No integrity violations: Windows system files are intact."));
        if (RepairRunner.Has(o, "réparés", "successfully repaired"))
            return ActionResult.Ok(L("Corrupted system files were found and repaired. Restart the PC to finish the repair.")) with { Effect = ApplyEffect.Reboot };
        return exitCode == 0
            ? ActionResult.Ok(L("System file check complete."))
            : ActionResult.Fail(L("The check ended with an error (code {0}). {1}", exitCode, RepairRunner.LastLine(o)));
    }
}

/// <summary>DISM : réparation de l'image, nettoyage du magasin de composants. Sortie forcée en anglais (/English) pour l'interpréter.</summary>
internal sealed class RepairDismAction(string id, string title, string[] args, string label, Func<string, int, ActionResult> interpret) : IActionHandler
{
    public const string RestoreHealthId = "maintenance.repair.dism";
    public const string ComponentCleanupId = "maintenance.repair.componentcleanup";

    public string Id => id;
    public string Title => title;
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        ctx.Progress?.Report(L("Starting DISM…"));
        var r = await RepairRunner.RunAsync(SystemTool.Dism, args, TimeSpan.FromMinutes(120), null, label, ctx).ConfigureAwait(false);
        if (r.TimedOut) return ActionResult.Fail(L("DISM took more than 2 hours and was stopped."));
        return interpret(RepairRunner.Normalize(r.CombinedOutput), r.ExitCode);
    }

    public static RepairDismAction RestoreHealth() => new(RestoreHealthId, L("Repair Windows image (DISM)"),
        ["/Online", "/Cleanup-Image", "/RestoreHealth", "/English"], L("Image repair:"), (o, code) =>
        {
            if (code == 0 || code == 3010)
            {
                var msg = RepairRunner.Has(o, "No component store corruption detected")
                    ? L("No corruption detected: the Windows image is healthy.")
                    : RepairRunner.Has(o, "corruption was repaired")
                        ? L("Corruption was detected and repaired. Now run the system file check (SFC) again.")
                        : L("Windows image repair completed successfully.");
                return code == 3010 ? ActionResult.Ok(L("{0} A restart is required.", msg)) with { Effect = ApplyEffect.Reboot } : ActionResult.Ok(msg);
            }
            return ActionResult.Fail(DismError(code, o));
        });

    public static RepairDismAction ComponentCleanup() => new(ComponentCleanupId, L("Clean up the component store (DISM)"),
        ["/Online", "/Cleanup-Image", "/StartComponentCleanup", "/English"], L("Component cleanup:"), (o, code) =>
            code is 0 or 3010
                ? ActionResult.Ok(L("Component store cleaned up: old versions of updated components were removed."))
                : ActionResult.Fail(DismError(code, o)));

    private static string DismError(int code, string output) => unchecked((uint)code) switch
    {
        0x800F081F => L("DISM couldn't find the files needed for the repair (0x800F081F). Check your internet connection and that Windows Update is working, then try again."),
        0x800F0906 or 0x800F0907 => L("DISM couldn't download the repair files (internet connection or organization policy)."),
        0x800F0954 => L("DISM couldn't reach Windows Update (organization's update server?)."),
        740 => L("DISM must be run as administrator."),
        87 => L("DISM command not recognized by this version of Windows."),
        1726 or 1734 => L("The Windows servicing service isn't responding. Restart the PC, then try again."),
        _ => L("DISM failed (code 0x{0:X8}). {1}", unchecked((uint)code), RepairRunner.LastLine(output)),
    };
}

/// <summary>Analyse en ligne du volume système (chkdsk C: /scan), sans verrouiller le disque ni redémarrer.</summary>
internal sealed class ChkdskScanAction : IActionHandler
{
    public const string ActionId = "maintenance.repair.chkdsk";
    public string Id => ActionId;
    public string Title => L("Scan system disk (chkdsk /scan)");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var drive = (Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\").TrimEnd('\\'); // ex. « C: » (valeur système, jamais un paramètre)
        ctx.Progress?.Report(L("Scanning volume {0}…", drive));
        var r = await RepairRunner.RunAsync(SystemTool.Chkdsk, [drive, "/scan"], TimeSpan.FromMinutes(90), null, L("Disk scan:"), ctx).ConfigureAwait(false);
        if (r.TimedOut) return ActionResult.Fail(L("The disk scan took more than 90 minutes and was stopped."));
        return r.ExitCode switch
        {
            0 => ActionResult.Ok(L("No problems detected on the file system of volume {0}.", drive)),
            1 => ActionResult.Ok(L("Errors were found on {0} and fixed online.", drive)),
            2 => ActionResult.Ok(L("Scan of {0} complete (minor maintenance done, no blocking errors).", drive)),
            3 => ActionResult.Fail(L("Some problems couldn't be fixed online on {0}. A repair at restart is required: in an administrator terminal, run “chkdsk {1} /spotfix”, then restart.", drive, drive)),
            _ => ActionResult.Fail(L("The disk scan failed (code {0}). {1}", r.ExitCode, RepairRunner.LastLine(r.CombinedOutput))),
        };
    }
}

/// <summary>Resynchronisation de l'horloge avec le serveur de temps configuré (w32tm /resync).</summary>
internal sealed class TimeResyncAction : IActionHandler
{
    public const string ActionId = "maintenance.time.resync";
    public string Id => ActionId;
    public string Title => L("Resync time");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        ctx.Progress?.Report(L("Starting the Windows Time service…"));
        // Démarrage ponctuel (le type de démarrage du service n'est pas modifié).
        ServiceConfig.TryStart("W32Time", TimeSpan.FromSeconds(15));
        ctx.Progress?.Report(L("Syncing with the time server…"));
        var r = await ProcessRunner.RunAsync(SystemTool.W32tm, ["/resync"], new RunOptions { Timeout = TimeSpan.FromSeconds(60) }, ctx.Cancellation).ConfigureAwait(false);
        if (r.Success)
            return ActionResult.Ok(L("Time resynced: it's {0}.", DateTime.Now.ToString("T", Loc.Culture)));
        return ActionResult.Fail(unchecked((uint)r.ExitCode) switch
        {
            0x80070426 or 1058 => L("The Windows Time service is disabled: syncing isn't possible."),
            _ => L("Sync failed: check your internet connection (the time service must reach its server). {0}", RepairRunner.LastLine(r.CombinedOutput)),
        });
    }
}

/// <summary>État de la protection du système (lecture registre, sans droits).</summary>
internal static class RestoreStatus
{
    private const string SrKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string SrPolicy = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";

    public static bool DisabledByPolicy =>
        RegistryAccess.ReadDword(RegHive.LocalMachine, SrPolicy, "DisableSR") == 1;

    /// <summary>true = activée, false = désactivée, null = inconnu. RPSessionInterval vaut 1 dès qu'un lecteur est protégé.</summary>
    public static bool? ProtectionEnabled =>
        DisabledByPolicy ? false
        : RegistryAccess.ReadDword(RegHive.LocalMachine, SrKey, "RPSessionInterval") switch { 1 => true, 0 => false, _ => null };
}

internal sealed record RestorePointInfo(uint Sequence, string Description, DateTime Created, int Type);

internal static class RestorePoints
{
    public const int MaxListed = 64;

    /// <summary>Points de restauration existants (classe WMI root\default:SystemRestore, lecture ADMIN uniquement).</summary>
    public static List<RestorePointInfo> List()
    {
        var list = new List<RestorePointInfo>();
        foreach (var row in WmiQuery.Query("SELECT SequenceNumber, Description, CreationTime, RestorePointType FROM SystemRestore", @"root\default", 30))
        {
            try
            {
                var created = row["CreationTime"] is string s ? ManagementDateTimeConverter.ToDateTime(s) : DateTime.MinValue;
                list.Add(new RestorePointInfo(Convert.ToUInt32(row["SequenceNumber"], CultureInfo.InvariantCulture),
                    row["Description"] as string ?? "", created, Convert.ToInt32(row["RestorePointType"] ?? 0, CultureInfo.InvariantCulture)));
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentOutOfRangeException) { }
        }
        return [.. list.OrderByDescending(r => r.Sequence)];
    }

    public static string TypeLabel(int type) => type switch
    {
        0 => L("App installation"),
        1 => L("App uninstallation"),
        7 => L("System checkpoint"),
        10 => L("Driver installation"),
        12 => L("Settings change"),
        13 => LC("restore", "Canceled operation"),
        _ => L("Restore point"),
    };
}

/// <summary>
/// CONTRAT (utilisé par le module Profils) : paramètre « description » (1 à 64 caractères : lettres, chiffres, espace,
/// ponctuation simple) → SystemRestore.CreateRestorePoint(description, 12 MODIFY_SETTINGS, 100 BEGIN_SYSTEM_CHANGE).
/// Data : « created » = "true"/"false", « skipped » = "true" si Windows a appliqué sa limite de fréquence (24 h).
/// </summary>
internal sealed partial class RestorePointCreateAction : IActionHandler
{
    public const string ActionId = "maintenance.restorepoint.create";
    public string Id => ActionId;
    public string Title => L("Create a restore point");
    public bool RequiresAdmin => true;

    [GeneratedRegex(@"^[\p{L}\p{N} .,;:!?'()_\-]{1,64}$")]
    private static partial Regex DescriptionRx();

    public static string? CheckDescription(string value)
    {
        var v = value.Trim();
        if (v.Length is 0 or > 64) return L("The description must be 1 to 64 characters long.");
        return DescriptionRx().IsMatch(v) ? null : L("Use only letters, digits, spaces and simple punctuation (. , ; : ! ? ' ( ) - _).");
    }

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        var d = Validate.Required(p, "description", 64);
        if (CheckDescription(d) is { } error) throw new ValidationException(error);
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var description = Validate.Required(p, "description", 64);
        if (RestoreStatus.DisabledByPolicy)
            return ActionResult.Fail(L("System Restore is turned off by an organization policy on this PC."));

        ctx.Progress?.Report(L("Creating the restore point (this may take a minute)…"));
        return await Task.Run(() =>
        {
            var before = SafeList();
            uint rv;
            using (var cls = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null))
            using (var input = cls.GetMethodParameters("CreateRestorePoint"))
            {
                input["Description"] = description;
                input["RestorePointType"] = 12;   // MODIFY_SETTINGS
                input["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE
                using var output = cls.InvokeMethod("CreateRestorePoint", input, new InvokeMethodOptions { Timeout = TimeSpan.FromMinutes(10) });
                rv = Convert.ToUInt32(output?["ReturnValue"] ?? 1u, CultureInfo.InvariantCulture);
            }
            if (rv != 0)
            {
                return ActionResult.Fail(rv switch
                {
                    1058 or 0x80070422 => L("System protection is turned off (or its service is stopped): turn it on for the system drive, then try again."),
                    _ => L("Windows couldn't create the restore point (code 0x{0:X8}). Make sure system protection is turned on for the system drive.", rv),
                });
            }
            var after = SafeList();
            var newest = after.FirstOrDefault();
            var created = newest is not null && (before.Count == 0 || newest.Sequence > before[0].Sequence);
            if (created)
            {
                return ActionResult.Ok(L("Restore point “{0}” created.", description), new() { ["created"] = "true", ["skipped"] = "false" });
            }
            var last = before.FirstOrDefault();
            var message = last is null
                ? L("No new restore point created: Windows creates at most one every 24 hours, and a recent one already exists.")
                : L("No new restore point created: Windows creates at most one every 24 hours, and a recent one already exists. The latest restore point ({0}) is still available.", Format.Date(last.Created));
            return ActionResult.Ok(message,
                new() { ["created"] = "false", ["skipped"] = "true" });
        }, ctx.Cancellation).ConfigureAwait(false);
    }

    private static List<RestorePointInfo> SafeList()
    {
        try { return RestorePoints.List(); }
        catch (Exception ex) { Log.Warn("Maintenance", "liste des points : " + ex.Message); return []; }
    }
}

/// <summary>Liste des points de restauration (lecture WMI réservée aux administrateurs). Data : count, i.seq/desc/date/type.</summary>
internal sealed class RestorePointListAction : IActionHandler
{
    public const string ActionId = "maintenance.restorepoint.list";
    public string Id => ActionId;
    public string Title => L("List restore points");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        return Task.Run(() =>
        {
            var points = RestorePoints.List();
            var data = new Dictionary<string, string> { ["count"] = Math.Min(points.Count, RestorePoints.MaxListed).ToString(CultureInfo.InvariantCulture) };
            for (var i = 0; i < points.Count && i < RestorePoints.MaxListed; i++)
            {
                var rp = points[i];
                data[$"{i}.seq"] = rp.Sequence.ToString(CultureInfo.InvariantCulture);
                data[$"{i}.desc"] = rp.Description.Length > 120 ? rp.Description[..120] : rp.Description;
                data[$"{i}.date"] = rp.Created.ToString("o", CultureInfo.InvariantCulture);
                data[$"{i}.type"] = rp.Type.ToString(CultureInfo.InvariantCulture);
            }
            return ActionResult.Ok(points.Count == 0 ? L("No restore points on this PC.") : LP(points.Count, "{0} restore point.", "{0} restore points."), data);
        }, ctx.Cancellation);
    }
}

/// <summary>Active la protection du système sur le lecteur système (Enable-ComputerRestore, script constant).</summary>
internal sealed class RestoreEnableAction : IActionHandler
{
    public const string ActionId = "maintenance.restorepoint.enable";
    public string Id => ActionId;
    public string Title => L("Turn on system protection");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    private const string Script = "Enable-ComputerRestore -Drive \"$env:SystemDrive\\\"";

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        if (RestoreStatus.DisabledByPolicy)
            return ActionResult.Fail(L("System Restore is turned off by an organization policy: it can't be turned on here."));
        ctx.Progress?.Report(L("Turning on system protection…"));
        var r = await PowerShellRunner.RunAsync(Script, null, TimeSpan.FromMinutes(2), null, ctx.Cancellation).ConfigureAwait(false);
        return r.Success
            ? ActionResult.Ok(L("System protection turned on for the system drive. Windows reserves a little space on it for restore points."))
            : ActionResult.Fail(L("Couldn't turn it on: {0}", RepairRunner.LastLine(r.Error.Length > 0 ? r.Error : r.Output)));
    }
}

/// <summary>Valeurs de Windows Update écrites par l'application Paramètres (pause, heures d'activité).</summary>
internal static class WuKeys
{
    public const string UxSettings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    public const string Policy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    public static readonly string[] PauseValues =
    [
        "PauseUpdatesStartTime", "PauseUpdatesExpiryTime",
        "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
        "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime",
    ];

    public static string Iso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static bool PauseBlockedByPolicy =>
        RegistryAccess.ReadDword(RegHive.LocalMachine, Policy, "SetDisablePauseUXAccess") == 1;
}

/// <summary>Suspend les mises à jour de 1 à 5 semaines, exactement comme l'application Paramètres (valeurs Pause* en UTC ISO 8601).</summary>
internal sealed class UpdatesPauseAction : IActionHandler
{
    public const string ActionId = "maintenance.updates.pause";
    public string Id => ActionId;
    public string Title => L("Pause updates");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Validate.Int(p, "weeks", 1, 5);

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var weeks = Validate.Int(p, "weeks", 1, 5);
        if (WuKeys.PauseBlockedByPolicy)
            return Task.FromResult(ActionResult.Fail(L("Pausing updates is prohibited by an organization policy.")));
        var now = DateTime.UtcNow;
        var start = WuKeys.Iso(now);
        var end = WuKeys.Iso(now.AddDays(7 * weeks));
        Operation[] ops =
        [
            Reg.LmString(WuKeys.UxSettings, "PauseUpdatesStartTime", start),
            Reg.LmString(WuKeys.UxSettings, "PauseUpdatesExpiryTime", end),
            Reg.LmString(WuKeys.UxSettings, "PauseFeatureUpdatesStartTime", start),
            Reg.LmString(WuKeys.UxSettings, "PauseFeatureUpdatesEndTime", end),
            Reg.LmString(WuKeys.UxSettings, "PauseQualityUpdatesStartTime", start),
            Reg.LmString(WuKeys.UxSettings, "PauseQualityUpdatesEndTime", end),
        ];
        var label = LP(weeks, "{0} week", "{0} weeks");
        var entry = ctx.ApplyJournaled(ActionId, L("Updates paused"), label, ops);
        var until = Format.Day(now.AddDays(7 * weeks).ToLocalTime());
        return Task.FromResult(ActionResult.Ok(L("Updates paused until {0}. Windows will resume them automatically afterward.", until)) with { JournalId = entry.Id });
    }
}

internal sealed class UpdatesResumeAction : IActionHandler
{
    public const string ActionId = "maintenance.updates.resume";
    public string Id => ActionId;
    public string Title => L("Resume updates");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var entry = ctx.ApplyJournaled(ActionId, L("Updates resumed"), L("Resumed"),
            WuKeys.PauseValues.Select(v => (Operation)Reg.LmDel(WuKeys.UxSettings, v)));
        return Task.FromResult(ActionResult.Ok(L("Updates resumed: Windows will look for them at its next check.")) with { JournalId = entry.Id });
    }
}

/// <summary>Heures d'activité manuelles (0–23 h, plage de 18 h au plus), comme dans les Paramètres.</summary>
internal sealed class ActiveHoursAction : IActionHandler
{
    public const string ActionId = "maintenance.updates.activehours";
    public string Id => ActionId;
    public string Title => L("Set active hours");
    public bool RequiresAdmin => true;

    public static int Span(int start, int end) => ((end - start) % 24 + 24) % 24;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        var start = Validate.Int(p, "start", 0, 23);
        var end = Validate.Int(p, "end", 0, 23);
        var span = Span(start, end);
        if (span == 0) throw new ValidationException(L("The start and end of active hours must be different."));
        if (span > 18) throw new ValidationException(L("Active hours can't exceed 18 hours."));
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var start = Validate.Int(p, "start", 0, 23);
        var end = Validate.Int(p, "end", 0, 23);
        var entry = ctx.ApplyJournaled(ActionId, L("Active hours"), L("{0}:00 – {1}:00", start, end),
        [
            Reg.LmDword(WuKeys.UxSettings, "SmartActiveHoursState", 0),
            Reg.LmDword(WuKeys.UxSettings, "ActiveHoursStart", start),
            Reg.LmDword(WuKeys.UxSettings, "ActiveHoursEnd", end),
        ]);
        return Task.FromResult(ActionResult.Ok(L("Active hours: from {0}:00 to {1}:00. Windows won't restart automatically during this period.", start, end)) with { JournalId = entry.Id });
    }
}
