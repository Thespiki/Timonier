using PcPilot.Core.Catalog;
using PcPilot.Core.Platform;
using PcPilot.Core.Security;

namespace PcPilot.Modules.Apps;

/// <summary>Validation commune des listes d'identifiants winget (« ids » séparés par des virgules).</summary>
internal static class WingetIds
{
    public const int Max = 40;

    public static List<string> Parse(IReadOnlyDictionary<string, string> p)
    {
        var raw = Validate.Required(p, "ids", Max * 130);
        var ids = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Validate.WingetId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count is < 1 or > Max) throw new ValidationException($"Indiquez entre 1 et {Max} identifiants winget.");
        return ids;
    }

    public static string Label(string id) => AppsCatalog.Find(id)?.Name is { } n ? $"{n} ({id})" : id;
}

/// <summary>
/// Installe des applications avec winget, une par une, depuis la source communautaire « winget ».
/// <para>CONTRAT (utilisé par d'autres modules) : paramètre « ids » = identifiants winget séparés par des virgules (1 à 40).
/// Les identifiants du <see cref="AppsCatalog"/> sont acceptés directement ; tout autre identifiant exige la
/// confirmation affichée par le processus administrateur. <c>Data</c> : identifiant → « ok : … » ou « erreur : … ».</para>
/// </summary>
public sealed class WingetInstallAction : IActionHandler
{
    public const string ActionId = "apps.winget.install";

    public string Id => ActionId;
    public string Title => "Installer des applications (winget)";
    public bool RequiresAdmin => true;

    /// <summary>Le broker affiche sa confirmation dès qu'un identifiant sort du catalogue vérifié.</summary>
    public bool RequiresElevatedConfirmationFor(IReadOnlyDictionary<string, string> p) => WingetIds.Parse(p).Any(i => !AppsCatalog.Contains(i));

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> parameters)
    {
        var ids = WingetIds.Parse(parameters);
        var outside = ids.Where(i => !AppsCatalog.Contains(i)).ToList();
        return "PC Pilot va installer avec winget des logiciels qui ne font pas partie de son catalogue vérifié :\n\n"
               + string.Join("\n", outside.Select(i => "• " + i))
               + (ids.Count > outside.Count ? $"\n\n(et {ids.Count - outside.Count} application(s) du catalogue)" : "")
               + "\n\nN'acceptez que si vous connaissez ces logiciels. Installer vaut acceptation de la licence de chaque éditeur.";
    }

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => WingetIds.Parse(p);

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var ids = WingetIds.Parse(p);
        if (!Winget.IsAvailable) return ActionResult.Fail("winget (Programme d'installation d'application) est introuvable sur ce PC.");

        return await WingetBatch.RunAsync(ctx, ids, "Installation", "installée", "installées", id =>
            ["install", "--id", id, "--exact", "--source", "winget", "--silent",
             "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"]).ConfigureAwait(false);
    }
}

/// <summary>
/// Met à jour des applications déjà installées (« ids »). winget refuse de mettre à jour un logiciel absent :
/// cette action ne peut donc pas installer de nouveau logiciel.
/// </summary>
public sealed class WingetUpgradeAction : IActionHandler
{
    public const string ActionId = "apps.winget.upgrade";
    public string Id => ActionId;
    public string Title => "Mettre à jour des applications (winget)";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => WingetIds.Parse(p);

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var ids = WingetIds.Parse(p);
        if (!Winget.IsAvailable) return ActionResult.Fail("winget est introuvable sur ce PC.");
        return await WingetBatch.RunAsync(ctx, ids, "Mise à jour", "mise à jour", "mises à jour", id =>
            ["upgrade", "--id", id, "--exact", "--source", "winget", "--silent",
             "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"]).ConfigureAwait(false);
    }
}

/// <summary>Exécution séquentielle d'une commande winget par identifiant, avec progression et bilan par application.</summary>
internal static class WingetBatch
{
    public static async Task<ActionResult> RunAsync(ActionContext ctx, List<string> ids, string verb, string doneOne, string doneMany,
        Func<string, string[]> args)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        var done = 0;
        try
        {
            for (var i = 0; i < ids.Count; i++)
            {
                ctx.Cancellation.ThrowIfCancellationRequested();
                var id = ids[i];
                ctx.Progress?.Report($"[{i + 1}/{ids.Count}] {verb} de {WingetIds.Label(id)}…");
                var r = await Winget.RunAsync(args(id), TimeSpan.FromMinutes(30), ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
                var meaning = Winget.Describe(r.ExitCode, r.TimedOut);
                var ok = !r.TimedOut && (Winget.IsSuccess(r.ExitCode) || (verb == "Mise à jour" && unchecked((uint)r.ExitCode) == 0x8A15002B));
                data[id] = (ok ? "ok : " : "erreur : ") + meaning;
                Log.Info("Apps", $"winget {args(id)[0]} {id} → {r.ExitCode} ({meaning})");
                if (ok) done++;
                else failures.Add($"{AppsCatalog.Find(id)?.Name ?? id} ({meaning})");
                ctx.Progress?.Report(ok ? $"✓ {AppsCatalog.Find(id)?.Name ?? id} : {meaning}" : $"✗ {AppsCatalog.Find(id)?.Name ?? id} : {meaning}");
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var id in ids.Where(i => !data.ContainsKey(i))) data[id] = "erreur : annulé";
            return new ActionResult(false, $"{verb} interrompue : {done} sur {ids.Count} terminée(s).") { Data = data };
        }

        if (failures.Count == 0)
            return ActionResult.Ok(ids.Count == 1 ? $"{AppsCatalog.Find(ids[0])?.Name ?? ids[0]} : {data[ids[0]][5..]}."
                : $"{ids.Count} applications {doneMany}.", data);
        var message = done == 0
            ? $"Échec : {string.Join(", ", failures)}."
            : $"{done} {(done > 1 ? doneMany : doneOne)}, {failures.Count} échec(s) : {string.Join(", ", failures)}.";
        return new ActionResult(false, message) { Data = data };
    }
}

/// <summary>Met à jour toutes les applications connues de winget (<c>winget upgrade --all</c>).</summary>
public sealed class WingetUpgradeAllAction : IActionHandler
{
    public const string ActionId = "apps.winget.upgradeall";
    public string Id => ActionId;
    public string Title => "Mettre à jour toutes les applications (winget)";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        if (p.Count > 0) throw new ValidationException("Cette action ne prend aucun paramètre.");
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        if (!Winget.IsAvailable) return ActionResult.Fail("winget est introuvable sur ce PC.");
        ctx.Progress?.Report("Recherche et installation des mises à jour…");
        var r = await Winget.RunAsync(
            ["upgrade", "--all", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
            TimeSpan.FromHours(3), ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
        var meaning = Winget.Describe(r.ExitCode, r.TimedOut);
        var data = new Dictionary<string, string> { ["exitCode"] = r.ExitCode.ToString(), ["result"] = meaning };
        Log.Info("Apps", $"winget upgrade --all → {r.ExitCode} ({meaning})");
        return r.Success || unchecked((uint)r.ExitCode) == 0x8A15002B
            ? ActionResult.Ok("Mises à jour terminées.", data)
            : new ActionResult(false, "Mises à jour terminées avec des erreurs : " + meaning + ".") { Data = data };
    }
}

/// <summary>
/// Désinstalle un programme de bureau avec winget, désigné par son nom exact (« name ») ou son code produit MSI
/// (« productCode »). Le programme doit figurer dans la liste actuelle des programmes installés ; la chaîne de
/// désinstallation brute du registre n'est jamais exécutée par PC Pilot.
/// </summary>
public sealed class WingetUninstallAction : IActionHandler
{
    public const string ActionId = "apps.winget.uninstall";
    public string Id => ActionId;
    public string Title => "Désinstaller un programme (winget)";
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p)
    {
        var (name, code) = Read(p);
        return code is null
            ? $"Désinstaller « {name} » avec winget ?\n\nLe programme et ses fichiers seront supprimés ; l'assistant de l'éditeur peut s'afficher."
            : $"Désinstaller le programme MSI {code}{(name is null ? "" : $" (« {name} »)")} avec winget ?";
    }

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Read(p);

    private static (string? Name, string? ProductCode) Read(IReadOnlyDictionary<string, string> p)
    {
        var code = Validate.Optional(p, "productCode", 38);
        var name = Validate.Optional(p, "name", 260);
        if (code is null && name is null) throw new ValidationException("Indiquez le nom exact du programme ou son code produit.");
        if (code is not null && !InstalledPrograms.IsProductCode(code)) throw new ValidationException("Code produit MSI invalide.");
        if (name is not null && (name.StartsWith('-') || name.Any(char.IsControl)))
            throw new ValidationException("Nom de programme invalide.");
        return (name, code?.ToUpperInvariant());
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var (name, code) = Read(p);

        // Le programme doit exister MAINTENANT dans la liste des programmes installés.
        var installed = await Task.Run(() => InstalledPrograms.Read(ctx.UserSid)).ConfigureAwait(false);
        var match = code is not null
            ? installed.FirstOrDefault(x => string.Equals(x.ProductCode, code, StringComparison.OrdinalIgnoreCase))
            : installed.FirstOrDefault(x => string.Equals(x.DisplayName, name, StringComparison.Ordinal));
        if (match is null) return ActionResult.Fail("Ce programme ne figure plus dans la liste des programmes installés.");
        if (match.NoRemove) return ActionResult.Fail($"« {match.DisplayName} » ne peut pas être désinstallé (protégé par son éditeur).");
        if (!Winget.IsAvailable) return ActionResult.Fail("winget est introuvable sur ce PC.");

        ctx.Progress?.Report($"Désinstallation de {match.DisplayName}…");
        string[] args = code is not null
            ? ["uninstall", "--product-code", code, "--silent", "--accept-source-agreements", "--disable-interactivity"]
            : ["uninstall", "--name", match.DisplayName, "--exact", "--silent", "--accept-source-agreements", "--disable-interactivity"];
        var r = await Winget.RunAsync(args, TimeSpan.FromMinutes(20), ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
        var meaning = Winget.Describe(r.ExitCode, r.TimedOut);
        Log.Info("Apps", $"winget uninstall → {r.ExitCode} ({meaning})");
        var data = new Dictionary<string, string> { ["name"] = match.DisplayName, ["result"] = meaning };
        if (r.Success) return ActionResult.Ok($"« {match.DisplayName} » a été désinstallé.", data);
        if (unchecked((uint)r.ExitCode) == 0x8A150109)
            return ActionResult.Ok($"« {match.DisplayName} » est désinstallé ; redémarrez le PC pour terminer.", data) with { Effect = Core.Model.ApplyEffect.Reboot };
        return new ActionResult(false, $"Désinstallation de « {match.DisplayName} » : {meaning}.") { Data = data };
    }
}
