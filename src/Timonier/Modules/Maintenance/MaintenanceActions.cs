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
    public string Title => L("Nettoyer les fichiers système");
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
                      ?? throw new ValidationException(L("Catégorie de nettoyage non autorisée : {0}", part));
            if (!keys.Contains(key)) keys.Add(key);
        }
        if (keys.Count == 0) throw new ValidationException(L("Aucune catégorie de nettoyage choisie."));
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
            ctx.Progress?.Report(analyze ? L("Analyse : {0}…", category.Title) : L("Nettoyage : {0}…", category.Title));
            CleanupStats stats;
            try
            {
                if (analyze)
                    stats = CleanupEngine.Analyze(category, TimeSpan.FromSeconds(20), ctx.Cancellation);
                else if (category.Kind == CleanupKind.DeliveryOptimization)
                    stats = await CleanDeliveryOptimizationAsync(ctx.Cancellation).ConfigureAwait(false);
                else if (key == "wucache" && CleanupCatalog.RebootPending())
                    stats = new CleanupStats { Note = L("Ignoré : un redémarrage de Windows Update est en attente.") };
                else
                    stats = CleanupEngine.Clean(category, TimeSpan.FromMinutes(3), ctx.Cancellation);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warn("Maintenance", $"nettoyage {key} : {ex.Message}");
                stats = new CleanupStats { Note = L("Erreur : {0}", ex.Message) };
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
            ? L("Analyse terminée : {0} récupérables.", Format.Bytes(total))
            : skipped > 0
                ? LP(skipped, "Nettoyage système terminé : {1} libérés. {0} fichier en cours d'utilisation ignoré.",
                    "Nettoyage système terminé : {1} libérés. {0} fichiers en cours d'utilisation ignorés.", Format.Bytes(total))
                : L("Nettoyage système terminé : {0} libérés.", Format.Bytes(total));
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
            return new CleanupStats { Note = L("Le cache n'a pas pu être vidé (service d'optimisation de la distribution indisponible ?).") };
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
    public string Title => L("Vérifier les fichiers système (SFC)");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        ctx.Progress?.Report(L("Démarrage de la vérification des fichiers système…"));
        var r = await RepairRunner.RunAsync(SystemTool.Sfc, ["/scannow"], TimeSpan.FromMinutes(90), Encoding.Unicode, L("Vérification des fichiers système :"), ctx).ConfigureAwait(false);
        if (r.TimedOut) return ActionResult.Fail(L("La vérification a dépassé 90 minutes et a été interrompue."));
        return Interpret(RepairRunner.Normalize(r.CombinedOutput), r.ExitCode);
    }

    internal static ActionResult Interpret(string o, int exitCode)
    {
        // Textes comparés à la sortie de sfc (français ou anglais selon Windows) : jamais traduits.
        if (RepairRunner.Has(o, "pas pu en réparer", "n'ont pas pu être réparés", "unable to fix"))
            return ActionResult.Fail(L("SFC a trouvé des fichiers système endommagés mais n'a pas pu tous les réparer. Lancez « Réparer l'image de Windows (DISM) », redémarrez, puis relancez SFC."));
        if (RepairRunner.Has(o, "réparation du système est en attente", "system repair pending"))
            return ActionResult.Fail(L("Une réparation du système est déjà en attente : redémarrez le PC, puis relancez la vérification."));
        if (RepairRunner.Has(o, "pas pu effectuer l'opération", "could not perform the requested operation"))
            return ActionResult.Fail(L("SFC n'a pas pu effectuer l'analyse. Redémarrez puis réessayez ; si le problème persiste, lancez d'abord DISM."));
        if (RepairRunner.Has(o, "aucune violation", "did not find any integrity violations"))
            return ActionResult.Ok(L("Aucune violation d'intégrité : les fichiers système de Windows sont intacts."));
        if (RepairRunner.Has(o, "réparés", "successfully repaired"))
            return ActionResult.Ok(L("Des fichiers système endommagés ont été trouvés et réparés. Redémarrez le PC pour terminer la réparation.")) with { Effect = ApplyEffect.Reboot };
        return exitCode == 0
            ? ActionResult.Ok(L("Vérification des fichiers système terminée."))
            : ActionResult.Fail(L("La vérification s'est terminée avec une erreur (code {0}). {1}", exitCode, RepairRunner.LastLine(o)));
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
        ctx.Progress?.Report(L("Démarrage de DISM…"));
        var r = await RepairRunner.RunAsync(SystemTool.Dism, args, TimeSpan.FromMinutes(120), null, label, ctx).ConfigureAwait(false);
        if (r.TimedOut) return ActionResult.Fail(L("DISM a dépassé 2 heures et a été interrompu."));
        return interpret(RepairRunner.Normalize(r.CombinedOutput), r.ExitCode);
    }

    public static RepairDismAction RestoreHealth() => new(RestoreHealthId, L("Réparer l'image de Windows (DISM)"),
        ["/Online", "/Cleanup-Image", "/RestoreHealth", "/English"], L("Réparation de l'image :"), (o, code) =>
        {
            if (code == 0 || code == 3010)
            {
                var msg = RepairRunner.Has(o, "No component store corruption detected")
                    ? L("Aucune corruption détectée : l'image de Windows est saine.")
                    : RepairRunner.Has(o, "corruption was repaired")
                        ? L("Des corruptions ont été détectées et réparées. Relancez maintenant la vérification des fichiers système (SFC).")
                        : L("Réparation de l'image de Windows terminée avec succès.");
                return code == 3010 ? ActionResult.Ok(L("{0} Un redémarrage est nécessaire.", msg)) with { Effect = ApplyEffect.Reboot } : ActionResult.Ok(msg);
            }
            return ActionResult.Fail(DismError(code, o));
        });

    public static RepairDismAction ComponentCleanup() => new(ComponentCleanupId, L("Nettoyer le magasin de composants (DISM)"),
        ["/Online", "/Cleanup-Image", "/StartComponentCleanup", "/English"], L("Nettoyage des composants :"), (o, code) =>
            code is 0 or 3010
                ? ActionResult.Ok(L("Magasin de composants nettoyé : les anciennes versions des composants mis à jour ont été supprimées."))
                : ActionResult.Fail(DismError(code, o)));

    private static string DismError(int code, string output) => unchecked((uint)code) switch
    {
        0x800F081F => L("DISM n'a pas trouvé les fichiers nécessaires à la réparation (0x800F081F). Vérifiez la connexion Internet et que Windows Update fonctionne, puis réessayez."),
        0x800F0906 or 0x800F0907 => L("DISM n'a pas pu télécharger les fichiers de réparation (connexion Internet ou stratégie de l'organisation)."),
        0x800F0954 => L("DISM n'a pas pu joindre Windows Update (serveur de mises à jour de l'organisation ?)."),
        740 => L("DISM doit être exécuté en administrateur."),
        87 => L("Commande DISM non reconnue par cette version de Windows."),
        1726 or 1734 => L("Le service de maintenance de Windows ne répond pas. Redémarrez le PC puis réessayez."),
        _ => L("DISM a échoué (code 0x{0:X8}). {1}", unchecked((uint)code), RepairRunner.LastLine(output)),
    };
}

/// <summary>Analyse en ligne du volume système (chkdsk C: /scan), sans verrouiller le disque ni redémarrer.</summary>
internal sealed class ChkdskScanAction : IActionHandler
{
    public const string ActionId = "maintenance.repair.chkdsk";
    public string Id => ActionId;
    public string Title => L("Analyser le disque système (chkdsk /scan)");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var drive = (Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\").TrimEnd('\\'); // ex. « C: » (valeur système, jamais un paramètre)
        ctx.Progress?.Report(L("Analyse du volume {0}…", drive));
        var r = await RepairRunner.RunAsync(SystemTool.Chkdsk, [drive, "/scan"], TimeSpan.FromMinutes(90), null, L("Analyse du disque :"), ctx).ConfigureAwait(false);
        if (r.TimedOut) return ActionResult.Fail(L("L'analyse du disque a dépassé 90 minutes et a été interrompue."));
        return r.ExitCode switch
        {
            0 => ActionResult.Ok(L("Aucun problème détecté sur le système de fichiers du volume {0}.", drive)),
            1 => ActionResult.Ok(L("Des erreurs ont été trouvées sur {0} et corrigées en ligne.", drive)),
            2 => ActionResult.Ok(L("Analyse de {0} terminée (maintenance mineure effectuée, aucune erreur bloquante).", drive)),
            3 => ActionResult.Fail(L("Des problèmes n'ont pas pu être corrigés en ligne sur {0}. Une réparation au redémarrage est nécessaire : dans un terminal administrateur, lancez « chkdsk {1} /spotfix » puis redémarrez.", drive, drive)),
            _ => ActionResult.Fail(L("L'analyse du disque a échoué (code {0}). {1}", r.ExitCode, RepairRunner.LastLine(r.CombinedOutput))),
        };
    }
}

/// <summary>Resynchronisation de l'horloge avec le serveur de temps configuré (w32tm /resync).</summary>
internal sealed class TimeResyncAction : IActionHandler
{
    public const string ActionId = "maintenance.time.resync";
    public string Id => ActionId;
    public string Title => L("Resynchroniser l'heure");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        ctx.Progress?.Report(L("Démarrage du service de temps Windows…"));
        // Démarrage ponctuel (le type de démarrage du service n'est pas modifié).
        ServiceConfig.TryStart("W32Time", TimeSpan.FromSeconds(15));
        ctx.Progress?.Report(L("Synchronisation avec le serveur de temps…"));
        var r = await ProcessRunner.RunAsync(SystemTool.W32tm, ["/resync"], new RunOptions { Timeout = TimeSpan.FromSeconds(60) }, ctx.Cancellation).ConfigureAwait(false);
        if (r.Success)
            return ActionResult.Ok(L("Heure resynchronisée : il est {0}.", DateTime.Now.ToString("T", Loc.Culture)));
        return ActionResult.Fail(unchecked((uint)r.ExitCode) switch
        {
            0x80070426 or 1058 => L("Le service de temps Windows est désactivé : la synchronisation est impossible."),
            _ => L("Échec de la synchronisation : vérifiez la connexion Internet (le service de temps doit joindre son serveur). {0}", RepairRunner.LastLine(r.CombinedOutput)),
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
        0 => L("Installation d'application"),
        1 => L("Désinstallation d'application"),
        7 => L("Point de contrôle système"),
        10 => L("Installation de pilote"),
        12 => L("Modification de paramètres"),
        13 => LC("restore", "Annulation"),
        _ => L("Point de restauration"),
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
    public string Title => L("Créer un point de restauration");
    public bool RequiresAdmin => true;

    [GeneratedRegex(@"^[\p{L}\p{N} .,;:!?'()_\-]{1,64}$")]
    private static partial Regex DescriptionRx();

    public static string? CheckDescription(string value)
    {
        var v = value.Trim();
        if (v.Length is 0 or > 64) return L("La description doit contenir de 1 à 64 caractères.");
        return DescriptionRx().IsMatch(v) ? null : L("Utilisez uniquement des lettres, des chiffres, des espaces et la ponctuation simple (. , ; : ! ? ' ( ) - _).");
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
            return ActionResult.Fail(L("La restauration du système est désactivée par une stratégie de l'organisation sur ce PC."));

        ctx.Progress?.Report(L("Création du point de restauration (cela peut prendre une minute)…"));
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
                    1058 or 0x80070422 => L("La protection du système est désactivée (ou son service est arrêté) : activez-la sur le lecteur système puis réessayez."),
                    _ => L("Windows n'a pas pu créer le point de restauration (code 0x{0:X8}). Vérifiez que la protection du système est activée sur le lecteur système.", rv),
                });
            }
            var after = SafeList();
            var newest = after.FirstOrDefault();
            var created = newest is not null && (before.Count == 0 || newest.Sequence > before[0].Sequence);
            if (created)
            {
                return ActionResult.Ok(L("Point de restauration « {0} » créé.", description), new() { ["created"] = "true", ["skipped"] = "false" });
            }
            var last = before.FirstOrDefault();
            var message = last is null
                ? L("Aucun nouveau point créé : Windows n'en crée qu'un toutes les 24 heures au maximum, et un point récent existe déjà.")
                : L("Aucun nouveau point créé : Windows n'en crée qu'un toutes les 24 heures au maximum, et un point récent existe déjà. Le dernier point ({0}) reste disponible.", Format.Date(last.Created));
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
    public string Title => L("Lister les points de restauration");
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
            return ActionResult.Ok(points.Count == 0 ? L("Aucun point de restauration sur ce PC.") : LP(points.Count, "{0} point de restauration.", "{0} points de restauration."), data);
        }, ctx.Cancellation);
    }
}

/// <summary>Active la protection du système sur le lecteur système (Enable-ComputerRestore, script constant).</summary>
internal sealed class RestoreEnableAction : IActionHandler
{
    public const string ActionId = "maintenance.restorepoint.enable";
    public string Id => ActionId;
    public string Title => L("Activer la protection du système");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    private const string Script = "Enable-ComputerRestore -Drive \"$env:SystemDrive\\\"";

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        if (RestoreStatus.DisabledByPolicy)
            return ActionResult.Fail(L("La restauration du système est désactivée par une stratégie de l'organisation : impossible de l'activer ici."));
        ctx.Progress?.Report(L("Activation de la protection du système…"));
        var r = await PowerShellRunner.RunAsync(Script, null, TimeSpan.FromMinutes(2), null, ctx.Cancellation).ConfigureAwait(false);
        return r.Success
            ? ActionResult.Ok(L("Protection du système activée sur le lecteur système. Windows y réserve un peu d'espace pour les points de restauration."))
            : ActionResult.Fail(L("L'activation a échoué : {0}", RepairRunner.LastLine(r.Error.Length > 0 ? r.Error : r.Output)));
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
    public string Title => L("Suspendre les mises à jour");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Validate.Int(p, "weeks", 1, 5);

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var weeks = Validate.Int(p, "weeks", 1, 5);
        if (WuKeys.PauseBlockedByPolicy)
            return Task.FromResult(ActionResult.Fail(L("La mise en pause des mises à jour est interdite par une stratégie de l'organisation.")));
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
        var label = LP(weeks, "{0} semaine", "{0} semaines");
        var entry = ctx.ApplyJournaled(ActionId, L("Mises à jour suspendues"), label, ops);
        var until = Format.Day(now.AddDays(7 * weeks).ToLocalTime());
        return Task.FromResult(ActionResult.Ok(L("Mises à jour suspendues jusqu'au {0}. Windows les reprendra automatiquement ensuite.", until)) with { JournalId = entry.Id });
    }
}

internal sealed class UpdatesResumeAction : IActionHandler
{
    public const string ActionId = "maintenance.updates.resume";
    public string Id => ActionId;
    public string Title => L("Reprendre les mises à jour");
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p) { }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var entry = ctx.ApplyJournaled(ActionId, L("Mises à jour reprises"), L("Reprise"),
            WuKeys.PauseValues.Select(v => (Operation)Reg.LmDel(WuKeys.UxSettings, v)));
        return Task.FromResult(ActionResult.Ok(L("Mises à jour reprises : Windows les recherchera lors de sa prochaine vérification.")) with { JournalId = entry.Id });
    }
}

/// <summary>Heures d'activité manuelles (0–23 h, plage de 18 h au plus), comme dans les Paramètres.</summary>
internal sealed class ActiveHoursAction : IActionHandler
{
    public const string ActionId = "maintenance.updates.activehours";
    public string Id => ActionId;
    public string Title => L("Définir les heures d'activité");
    public bool RequiresAdmin => true;

    public static int Span(int start, int end) => ((end - start) % 24 + 24) % 24;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        var start = Validate.Int(p, "start", 0, 23);
        var end = Validate.Int(p, "end", 0, 23);
        var span = Span(start, end);
        if (span == 0) throw new ValidationException(L("Le début et la fin des heures d'activité doivent être différents."));
        if (span > 18) throw new ValidationException(L("Les heures d'activité ne peuvent pas dépasser 18 heures."));
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var start = Validate.Int(p, "start", 0, 23);
        var end = Validate.Int(p, "end", 0, 23);
        var entry = ctx.ApplyJournaled(ActionId, L("Heures d'activité"), L("{0} h – {1} h", start, end),
        [
            Reg.LmDword(WuKeys.UxSettings, "SmartActiveHoursState", 0),
            Reg.LmDword(WuKeys.UxSettings, "ActiveHoursStart", start),
            Reg.LmDword(WuKeys.UxSettings, "ActiveHoursEnd", end),
        ]);
        return Task.FromResult(ActionResult.Ok(L("Heures d'activité : de {0} h à {1} h. Windows ne redémarrera pas automatiquement pendant cette plage.", start, end)) with { JournalId = entry.Id });
    }
}
