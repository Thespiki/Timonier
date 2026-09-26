using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Apps;

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
        if (ids.Count is < 1 or > Max) throw new ValidationException(L("Specify between 1 and {0} winget IDs.", Max));
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
    public string Title => L("Install apps (winget)");
    public bool RequiresAdmin => true;

    /// <summary>Le broker affiche sa confirmation dès qu'un identifiant sort du catalogue vérifié.</summary>
    public bool RequiresElevatedConfirmationFor(IReadOnlyDictionary<string, string> p) => WingetIds.Parse(p).Any(i => !AppsCatalog.Contains(i));

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> parameters)
    {
        var ids = WingetIds.Parse(parameters);
        var outside = ids.Where(i => !AppsCatalog.Contains(i)).ToList();
        var list = string.Join("\n", outside.Select(i => "• " + i));
        return ids.Count > outside.Count
            ? LP(ids.Count - outside.Count,
                "Timonier is about to use winget to install software that isn't in its verified catalog:\n\n{1}\n\n(and {0} app from the catalog)\n\nOnly accept if you know this software. Installing means you accept each publisher's license.",
                "Timonier is about to use winget to install software that isn't in its verified catalog:\n\n{1}\n\n(and {0} apps from the catalog)\n\nOnly accept if you know this software. Installing means you accept each publisher's license.",
                list)
            : L("Timonier is about to use winget to install software that isn't in its verified catalog:\n\n{0}\n\nOnly accept if you know this software. Installing means you accept each publisher's license.", list);
    }

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => WingetIds.Parse(p);

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var ids = WingetIds.Parse(p);
        if (!Winget.IsAvailable) return ActionResult.Fail(L("winget (App Installer) wasn't found on this PC."));

        return await WingetBatch.RunAsync(ctx, ids, upgrade: false, id =>
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
    public string Title => L("Update apps (winget)");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => WingetIds.Parse(p);

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var ids = WingetIds.Parse(p);
        if (!Winget.IsAvailable) return ActionResult.Fail(L("winget wasn't found on this PC."));
        return await WingetBatch.RunAsync(ctx, ids, upgrade: true, id =>
            ["upgrade", "--id", id, "--exact", "--source", "winget", "--silent",
             "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"]).ConfigureAwait(false);
    }
}

/// <summary>Exécution séquentielle d'une commande winget par identifiant, avec progression et bilan par application.</summary>
internal static class WingetBatch
{
    /// <param name="upgrade">Vrai pour une mise à jour (textes adaptés ; « déjà à jour » compte comme un succès).</param>
    public static async Task<ActionResult> RunAsync(ActionContext ctx, List<string> ids, bool upgrade, Func<string, string[]> args)
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
                ctx.Progress?.Report(upgrade
                    ? L("[{0}/{1}] Updating {2}…", i + 1, ids.Count, WingetIds.Label(id))
                    : L("[{0}/{1}] Installing {2}…", i + 1, ids.Count, WingetIds.Label(id)));
                var r = await Winget.RunAsync(args(id), TimeSpan.FromMinutes(30), ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
                var meaning = Winget.Describe(r.ExitCode, r.TimedOut);
                var ok = !r.TimedOut && (Winget.IsSuccess(r.ExitCode) || (upgrade && unchecked((uint)r.ExitCode) == 0x8A15002B));
                // Préfixes « ok : » / « erreur : » non traduits : lus par la carte d'activité et les profils.
                data[id] = (ok ? "ok : " : "erreur : ") + meaning;
                Log.Info("Apps", $"winget {args(id)[0]} {id} → {r.ExitCode} ({meaning})");
                if (ok) done++;
                else failures.Add($"{AppsCatalog.Find(id)?.Name ?? id} ({meaning})");
                ctx.Progress?.Report((ok ? "✓ " : "✗ ") + L("{0}: {1}", AppsCatalog.Find(id)?.Name ?? id, meaning));
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var id in ids.Where(i => !data.ContainsKey(i))) data[id] = "erreur : " + L("canceled");
            return new ActionResult(false, upgrade
                ? LP(done, "Update stopped: {0} of {1} completed.", "Update stopped: {0} of {1} completed.", ids.Count)
                : LP(done, "Installation stopped: {0} of {1} completed.", "Installation stopped: {0} of {1} completed.", ids.Count)) { Data = data };
        }

        if (failures.Count == 0)
            return ActionResult.Ok(ids.Count == 1 ? L("{0}: {1}.", AppsCatalog.Find(ids[0])?.Name ?? ids[0], data[ids[0]]["ok : ".Length..])
                : upgrade ? LP(ids.Count, "{0} app updated.", "{0} apps updated.")
                : LP(ids.Count, "{0} app installed.", "{0} apps installed."), data);
        var message = done == 0
            ? L("Failed: {0}.", string.Join(", ", failures))
            : L("{0}, {1}: {2}.",
                upgrade ? LP(done, "{0} updated", "{0} updated") : LP(done, "{0} installed", "{0} installed"),
                LP(failures.Count, "{0} failed", "{0} failed"), string.Join(", ", failures));
        return new ActionResult(false, message) { Data = data };
    }
}

/// <summary>Met à jour toutes les applications connues de winget (<c>winget upgrade --all</c>).</summary>
public sealed class WingetUpgradeAllAction : IActionHandler
{
    public const string ActionId = "apps.winget.upgradeall";
    public string Id => ActionId;
    public string Title => L("Update all apps (winget)");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        if (p.Count > 0) throw new ValidationException(L("This action takes no parameters."));
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        if (!Winget.IsAvailable) return ActionResult.Fail(L("winget wasn't found on this PC."));
        ctx.Progress?.Report(L("Checking for and installing updates…"));
        var r = await Winget.RunAsync(
            ["upgrade", "--all", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
            TimeSpan.FromHours(3), ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
        var meaning = Winget.Describe(r.ExitCode, r.TimedOut);
        var data = new Dictionary<string, string> { ["exitCode"] = r.ExitCode.ToString(), ["result"] = meaning };
        Log.Info("Apps", $"winget upgrade --all → {r.ExitCode} ({meaning})");
        return r.Success || unchecked((uint)r.ExitCode) == 0x8A15002B
            ? ActionResult.Ok(L("Updates finished."), data)
            : new ActionResult(false, L("Updates finished with errors: {0}.", meaning)) { Data = data };
    }
}

/// <summary>
/// Désinstalle un programme de bureau inscrit POUR TOUS LES UTILISATEURS (HKLM) avec winget, désigné par son nom exact
/// (« name ») ou son code produit MSI (« productCode »). Le programme doit figurer dans la liste actuelle des programmes
/// installés ; la chaîne de désinstallation brute du registre n'est jamais exécutée par Timonier.
/// <para>Sécurité : les entrées de HKCU sont modifiables sans droits. Exécuter leur désinstalleur dans le broker élevé
/// permettrait à n'importe quel programme de l'utilisateur d'obtenir les droits administrateur : l'action élevée est donc
/// limitée aux entrées machine (winget « --scope machine ») et refuse un nom ou code produit également présent dans HKCU.
/// Les programmes installés pour l'utilisateur passent par <see cref="WingetUninstallUserAction"/>, sans élévation.</para>
/// </summary>
public sealed class WingetUninstallAction : IActionHandler
{
    public const string ActionId = "apps.winget.uninstall";
    public string Id => ActionId;
    public string Title => L("Uninstall a program (winget)");
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p)
    {
        var (name, code) = WingetUninstall.Read(p);
        return code is null
            ? L("Uninstall “{0}” with winget?\n\nThe program and its files will be removed; the publisher's wizard may appear.", name)
            : name is null ? L("Uninstall MSI program {0} with winget?", code)
            : L("Uninstall MSI program {0} (“{1}”) with winget?", code, name);
    }

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => WingetUninstall.Read(p);

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        return WingetUninstall.RunAsync(ctx, p, perUser: false);
    }
}

/// <summary>
/// Désinstalle un programme installé pour l'utilisateur courant seulement (HKCU), SANS élévation : son désinstalleur
/// s'exécute avec les droits de l'utilisateur, comme depuis les Paramètres de Windows. Mêmes paramètres que
/// <see cref="WingetUninstallAction"/>.
/// </summary>
public sealed class WingetUninstallUserAction : IActionHandler
{
    public const string ActionId = "apps.winget.uninstall.user";
    public string Id => ActionId;
    public string Title => L("Uninstall a program from your account (winget)");
    public bool RequiresAdmin => false;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => WingetUninstall.Read(p);

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        return WingetUninstall.RunAsync(ctx, p, perUser: true);
    }
}

/// <summary>Validation et exécution communes aux deux actions de désinstallation.</summary>
internal static class WingetUninstall
{
    public static (string? Name, string? ProductCode) Read(IReadOnlyDictionary<string, string> p)
    {
        var code = Validate.Optional(p, "productCode", 38);
        var name = Validate.Optional(p, "name", 260);
        if (code is null && name is null) throw new ValidationException(L("Enter the program's exact name or its product code."));
        if (code is not null && !InstalledPrograms.IsProductCode(code)) throw new ValidationException(L("Invalid MSI product code."));
        if (name is not null && (name.StartsWith('-') || name.Any(char.IsControl)))
            throw new ValidationException(L("Invalid program name."));
        return (name, code?.ToUpperInvariant());
    }

    public static async Task<ActionResult> RunAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p, bool perUser)
    {
        var (name, code) = Read(p);

        // Le programme doit exister MAINTENANT dans la liste des programmes installés, dans la bonne portée.
        var installed = await Task.Run(() => InstalledPrograms.Read(ctx.UserSid)).ConfigureAwait(false);
        var match = code is not null
            ? installed.FirstOrDefault(x => x.PerUser == perUser && string.Equals(x.ProductCode, code, StringComparison.OrdinalIgnoreCase))
            : installed.FirstOrDefault(x => x.PerUser == perUser && string.Equals(x.DisplayName, name, StringComparison.Ordinal));
        if (match is null) return ActionResult.Fail(L("This program is no longer in the list of installed programs."));
        if (match.NoRemove) return ActionResult.Fail(L("“{0}” can't be uninstalled (protected by its publisher).", match.DisplayName));
        if (!perUser && await Task.Run(() => InstalledPrograms.UserHiveHasEntry(ctx.UserSid, match.DisplayName, code)).ConfigureAwait(false))
            return ActionResult.Fail(L("A program named “{0}” is also registered for your account only: for safety, Timonier doesn't uninstall it with administrator rights. Use Windows Settings (Installed apps).", match.DisplayName));
        if (!Winget.IsAvailable) return ActionResult.Fail(L("winget wasn't found on this PC."));

        ctx.Progress?.Report(L("Uninstalling {0}…", match.DisplayName));
        var scope = perUser ? "user" : "machine";
        string[] args = code is not null
            ? ["uninstall", "--product-code", code, "--scope", scope, "--silent", "--accept-source-agreements", "--disable-interactivity"]
            : ["uninstall", "--name", match.DisplayName, "--exact", "--scope", scope, "--silent", "--accept-source-agreements", "--disable-interactivity"];
        var r = await Winget.RunAsync(args, TimeSpan.FromMinutes(20), ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
        var meaning = Winget.Describe(r.ExitCode, r.TimedOut);
        Log.Info("Apps", $"winget uninstall → {r.ExitCode} ({meaning})");
        var data = new Dictionary<string, string> { ["name"] = match.DisplayName, ["result"] = meaning };
        if (r.Success) return ActionResult.Ok(L("“{0}” was uninstalled.", match.DisplayName), data);
        if (unchecked((uint)r.ExitCode) == 0x8A150109)
            return ActionResult.Ok(L("“{0}” is uninstalled; restart the PC to finish.", match.DisplayName), data) with { Effect = Core.Model.ApplyEffect.Reboot };
        return new ActionResult(false, L("Uninstalling “{0}”: {1}.", match.DisplayName, meaning)) { Data = data };
    }
}
