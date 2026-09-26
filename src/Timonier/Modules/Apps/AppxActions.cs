using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Security;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Timonier.Modules.Apps;

/// <summary>Application du Store (paquet principal) installée pour l'utilisateur courant.</summary>
public sealed record AppxInfo(string Name, string FamilyName, string FullName, string DisplayName, string Publisher, string Version,
    DateTimeOffset? Installed, bool IsSystemSigned)
{
    public BloatEntry? Bloat => BloatCatalog.Find(Name);
    public bool IsProtected => BloatCatalog.IsProtected(Name);

    /// <summary>Nom lisible : titre de la table des applications préinstallées, sinon nom affiché du paquet.</summary>
    public string Title => DisplayName != Name ? DisplayName : Bloat?.Title ?? Name;
}

/// <summary>Énumération des paquets Appx/MSIX de l'utilisateur courant (API WinRT PackageManager, sans élévation).</summary>
public static class AppxService
{
    public static List<AppxInfo> ListCurrentUser()
    {
        var pm = new PackageManager();
        var list = new List<AppxInfo>();
        foreach (var p in pm.FindPackagesForUserWithPackageTypes("", PackageTypes.Main))
        {
            try
            {
                if (p.IsFramework || p.IsResourcePackage) continue;
                var id = p.Id;
                string display;
                try { display = p.DisplayName; } catch { display = ""; }
                if (string.IsNullOrWhiteSpace(display) || display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) display = id.Name;
                string publisher;
                try { publisher = p.PublisherDisplayName ?? ""; } catch { publisher = ""; }
                DateTimeOffset? installed = null;
                try { installed = p.InstalledDate; } catch { /* non disponible */ }
                var v = id.Version;
                list.Add(new AppxInfo(id.Name, id.FamilyName, id.FullName, display.Trim(), publisher, $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                    installed, p.SignatureKind == PackageSignatureKind.System));
            }
            catch (Exception ex) { Log.Warn("Apps", "paquet illisible : " + ex.Message); }
        }
        return list;
    }
}

/// <summary>
/// Supprime des applications du Store pour l'utilisateur courant (sans élévation). Paramètre « fullName » : un ou
/// plusieurs noms complets de paquets séparés par des virgules (1 à 60). Chaque paquet doit être installé au moment de
/// l'exécution, ne pas être un composant protégé ni un paquet système. Réinstallables depuis le Microsoft Store.
/// <para><c>Data</c> : titre → « ok : … » / « erreur : … », et « removed » = « famille|titre » séparés par « ; ».</para>
/// </summary>
public sealed class AppxRemoveAction : IActionHandler
{
    public const string ActionId = "apps.appx.remove";
    public const int Max = 60;
    public string Id => ActionId;
    public string Title => L("Supprimer des applications préinstallées (utilisateur courant)");
    public bool RequiresAdmin => false;

    private static List<string> Parse(IReadOnlyDictionary<string, string> p)
    {
        var raw = Validate.Required(p, "fullName", Max * 200);
        var list = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();
        if (list.Count is < 1 or > Max) throw new ValidationException(L("Indiquez entre 1 et {0} paquets.", Max));
        foreach (var full in list)
        {
            if (!BloatCatalog.IsValidFullName(full)) throw new ValidationException(L("Nom de paquet invalide : {0}", full));
            if (BloatCatalog.IsProtected(BloatCatalog.NameOf(full)))
                throw new ValidationException(L("{0} est un composant protégé de Windows : Timonier ne le supprime pas.", BloatCatalog.NameOf(full)));
        }
        return list;
    }

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Parse(p);

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var wanted = Parse(p);
        var installed = AppxService.ListCurrentUser().ToDictionary(a => a.FullName, StringComparer.Ordinal);
        var pm = new PackageManager();
        var data = new Dictionary<string, string>();
        var removed = new List<string>();
        var failures = new List<string>();
        try
        {
            for (var i = 0; i < wanted.Count; i++)
            {
                ctx.Cancellation.ThrowIfCancellationRequested();
                if (!installed.TryGetValue(wanted[i], out var app))
                {
                    data[BloatCatalog.NameOf(wanted[i])] = "erreur : " + L("n'est plus installée pour votre compte");
                    failures.Add(BloatCatalog.NameOf(wanted[i]));
                    continue;
                }
                var title = app.Title;
                if (app.IsProtected || app.IsSystemSigned)
                {
                    data[title] = "erreur : " + L("composant protégé");
                    failures.Add(title);
                    continue;
                }
                ctx.Progress?.Report(L("[{0}/{1}] Suppression de {2}…", i + 1, wanted.Count, title));
                var result = await pm.RemovePackageAsync(app.FullName).AsTask(ctx.Cancellation).ConfigureAwait(false);
                if (result.ExtendedErrorCode is { } err && err.HResult != 0)
                {
                    Log.Warn("Apps", $"suppression de {app.Name} : 0x{err.HResult:X8} {result.ErrorText}");
                    data[title] = "erreur : " + Clean(result.ErrorText, err.HResult);
                    failures.Add(title);
                }
                else
                {
                    Log.Info("Apps", "application supprimée pour l'utilisateur : " + app.Name);
                    data[title] = "ok : " + L("supprimée");
                    removed.Add(app.FamilyName + "|" + title.Replace(';', ',').Replace('|', '/'));
                }
            }
        }
        catch (OperationCanceledException)
        {
            data["removed"] = string.Join(";", removed);
            return new ActionResult(false, LP(removed.Count, "Suppression interrompue : {0} application supprimée.", "Suppression interrompue : {0} applications supprimées.")) { Data = data };
        }

        data["removed"] = string.Join(";", removed);
        if (failures.Count == 0)
            return ActionResult.Ok(removed.Count == 1 ? L("« {0} » a été supprimée pour votre compte.", data.Keys.First())
                : LP(removed.Count, "{0} application supprimée pour votre compte.", "{0} applications supprimées pour votre compte."), data);
        return new ActionResult(false, removed.Count == 0
            ? L("Aucune application supprimée ({0}).", string.Join(", ", failures))
            : L("{0}, {1} : {2}.", LP(removed.Count, "{0} supprimée", "{0} supprimées"), LP(failures.Count, "{0} échec", "{0} échecs"), string.Join(", ", failures))) { Data = data };
    }

    private static string Clean(string? text, int hr)
    {
        var t = (text ?? "").Trim();
        return t.Length is > 0 and < 300 ? t : L("erreur 0x{0:X8}", hr);
    }
}

/// <summary>
/// Supprime des applications préinstallées pour TOUS les comptes et les retire de l'image de Windows (elles ne seront
/// plus installées pour les nouveaux comptes). Paramètre « family » : noms de famille séparés par des virgules (1 à 60),
/// limités à la table des applications supprimables. Script PowerShell constant, données par variable d'environnement.
/// </summary>
public sealed class AppxDeprovisionAction : IActionHandler
{
    public const string ActionId = "apps.appx.deprovision";
    public string Id => ActionId;
    public string Title => L("Supprimer des applications préinstallées pour tous les comptes");
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    private static List<string> Parse(IReadOnlyDictionary<string, string> p)
    {
        var raw = Validate.Required(p, "family", AppxRemoveAction.Max * 120);
        var list = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();
        if (list.Count is < 1 or > AppxRemoveAction.Max) throw new ValidationException(L("Indiquez entre 1 et {0} applications.", AppxRemoveAction.Max));
        foreach (var family in list)
        {
            if (!BloatCatalog.IsValidFamilyName(family)) throw new ValidationException(L("Nom de famille de paquet invalide : {0}", family));
            var name = BloatCatalog.NameOf(family);
            if (BloatCatalog.Find(name) is null || BloatCatalog.IsProtected(name))
                throw new ValidationException(L("{0} ne fait pas partie des applications préinstallées supprimables.", name));
        }
        return list;
    }

    private static string TitleOf(string family) => BloatCatalog.Find(BloatCatalog.NameOf(family))?.Title ?? family;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p)
    {
        var list = Parse(p);
        return L("Supprimer ces applications pour TOUS les comptes de ce PC et les retirer de l'image de Windows ?\n\n{0}\n\nElles ne seront plus installées pour les nouveaux comptes. Chacun pourra les réinstaller depuis le Microsoft Store.", string.Join("\n", list.Select(f => $"• {TitleOf(f)} ({BloatCatalog.NameOf(f)})")));
    }

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Parse(p);

    // Script constant : les noms de famille arrivent uniquement par $env:TMN_FAMILIES (déjà validés par liste blanche).
    private const string Script = """
        foreach ($fam in ($env:TMN_FAMILIES -split ',')) {
            $name = $fam.Substring(0, $fam.LastIndexOf('_'))
            $count = 0; $failed = 0
            foreach ($p in @(Get-AppxPackage -AllUsers | Where-Object { $_.PackageFamilyName -eq $fam -and -not $_.IsFramework -and -not $_.NonRemovable })) {
                try { Remove-AppxPackage -Package $p.PackageFullName -AllUsers; $count++ }
                catch { $failed++; "Échec : " + $p.PackageFullName + " : " + $_.Exception.Message }
            }
            foreach ($p in @(Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq $name })) {
                try { Remove-AppxProvisionedPackage -Online -PackageName $p.PackageName -AllUsers | Out-Null; $count++ }
                catch { $failed++; "Échec (image) : " + $p.PackageName + " : " + $_.Exception.Message }
            }
            "TMN_RESULT|" + $fam + "|" + $count + "|" + $failed
        }
        """;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var families = Parse(p);
        ctx.Progress?.Report(families.Count == 1 ? L("Suppression de {0} pour tous les comptes…", TitleOf(families[0]))
            : L("Suppression de {0} applications pour tous les comptes…", families.Count));
        var r = await PowerShellRunner.RunAsync(Script, new Dictionary<string, string> { ["FAMILIES"] = string.Join(",", families) },
            TimeSpan.FromMinutes(20), ctx.Progress, ctx.Cancellation).ConfigureAwait(false);

        var data = new Dictionary<string, string>();
        var removed = new List<string>();
        var failures = 0;
        foreach (var line in r.Output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("TMN_RESULT|", StringComparison.Ordinal)))
        {
            var parts = line.Split('|');
            if (parts.Length != 4 || !families.Contains(parts[1])) continue;
            var title = TitleOf(parts[1]);
            _ = int.TryParse(parts[2], out var count);
            _ = int.TryParse(parts[3], out var failed);
            if (failed > 0) { data[title] = "erreur : " + L("suppression partielle"); failures++; }
            else
            {
                data[title] = "ok : " + (count == 0 ? L("absente de ce PC") : L("supprimée pour tous les comptes"));
                if (count > 0) removed.Add(parts[1] + "|" + title);
            }
        }
        data["removed"] = string.Join(";", removed);
        if (!r.Success)
        {
            Log.Warn("Apps", "déprovisionnement : " + r.CombinedOutput);
            return new ActionResult(false, r.TimedOut ? L("La suppression pour tous les comptes a échoué : délai dépassé.") : L("La suppression pour tous les comptes a échoué : voir le journal.")) { Data = data };
        }
        return failures == 0
            ? ActionResult.Ok(removed.Count == 0 ? L("Ces applications n'étaient installées pour aucun compte.")
                : LP(removed.Count, "{0} application supprimée pour tous les comptes et retirée de l'image.", "{0} applications supprimées pour tous les comptes et retirées de l'image."), data)
            : new ActionResult(false, LP(failures, "Suppression partielle : {0} application n'a pas pu être entièrement supprimée.", "Suppression partielle : {0} applications n'ont pas pu être entièrement supprimées.")) { Data = data };
    }
}
