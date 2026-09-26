using System.Globalization;
using System.Text.RegularExpressions;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Performance;

/// <summary>
/// Active un plan d'alimentation existant (PowerSetActiveScheme). Un utilisateur standard peut normalement le faire :
/// la variante « .admin » n'est utilisée que si Windows refuse l'accès (plan protégé par une stratégie).
/// Le GUID doit figurer dans l'énumération actuelle des plans (aucun identifiant arbitraire).
/// </summary>
public sealed class ActivateSchemeAction(bool admin) : IActionHandler
{
    public const string UserId = "perf.power.activate";
    public const string AdminId = "perf.power.activate.admin";

    public string Id => admin ? AdminId : UserId;
    public string Title => L("Activer un plan d'alimentation");
    public bool RequiresAdmin => admin;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Validate.Guid(p, "scheme");

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var id = Validate.Guid(p, "scheme");
        var scheme = PowerApi.EnumerateSchemes().FirstOrDefault(s => s.Id == id)
                     ?? throw new ValidationException(L("Ce plan d'alimentation n'existe pas (ou plus) sur ce PC."));
        var rc = PowerApi.SetActiveScheme(id);
        if (rc == 0) return Task.FromResult(ActionResult.Ok(L("Plan « {0} » activé.", scheme.Name)));
        return Task.FromResult(new ActionResult(false, rc == PowerApi.ErrorAccessDenied
            ? L("Windows a refusé le changement de plan (accès refusé).")
            : L("Impossible d'activer le plan « {0} » (code {1}).", scheme.Name, rc))
        {
            Data = new Dictionary<string, string> { ["code"] = rc.ToString(CultureInfo.InvariantCulture) },
        });
    }
}

/// <summary>
/// Ajoute un plan à partir d'un modèle intégré à Windows (powercfg -duplicatescheme, GUID constant de la liste blanche).
/// Sert surtout à « Performances optimales », masqué par défaut.
/// </summary>
public sealed partial class AddSchemeAction : IActionHandler
{
    public const string ActionId = "perf.power.add";

    /// <summary>Modèles autorisés : clé → (GUID du modèle, personnalité attendue).</summary>
    public static readonly IReadOnlyDictionary<string, (Guid Template, string Label)> Templates = new Dictionary<string, (Guid, string)>
    {
        ["ultimate"] = (PowerApi.UltimateTemplate, L("Performances optimales")),
        ["high"] = (PowerApi.HighPerformance, L("Performances élevées")),
        ["saver"] = (PowerApi.PowerSaver, L("Économie d'énergie")),
    };

    public string Id => ActionId;
    public string Title => L("Ajouter un plan d'alimentation");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => Validate.OneOf(p, "template", [.. Templates.Keys]);

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRx();

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var key = Validate.OneOf(p, "template", [.. Templates.Keys]);
        var (template, label) = Templates[key];

        // Évite les doublons : un plan portant déjà ce nom (ou ce GUID) est simplement signalé.
        var templateName = PowerApi.ReadFriendlyName(template) ?? label;
        var existing = PowerApi.EnumerateSchemes().FirstOrDefault(s =>
            s.Id == template || string.Equals(s.Name, templateName, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null)
            return ActionResult.Ok(L("Le plan « {0} » existe déjà.", existing.Name), new() { ["scheme"] = existing.Id.ToString() });

        ctx.Progress?.Report(L("Création du plan…"));
        var result = await ProcessRunner.RunAsync(SystemTool.PowerCfg, ["-duplicatescheme", template.ToString()],
            new RunOptions { Timeout = TimeSpan.FromSeconds(30), OutputEncoding = ProcessRunner.OemEncoding }, ctx.Cancellation);
        if (!result.Success)
            return ActionResult.Fail(L("powercfg a échoué (code {0}) : {1}", result.ExitCode, result.CombinedOutput.Trim()));

        var data = new Dictionary<string, string>();
        foreach (Match m in GuidRx().Matches(result.Output))
        {
            if (Guid.TryParse(m.Value, out var g) && g != template) { data["scheme"] = g.ToString(); break; }
        }
        return ActionResult.Ok(L("Plan « {0} » ajouté.", templateName), data);
    }
}

/// <summary>
/// Minuteries du plan actif : extinction de l'écran et mise en veille, sur secteur ou sur batterie
/// (powercfg /change, arguments en liste, minutes bornées 0–600 ; 0 = jamais).
/// </summary>
public sealed class SetTimeoutAction : IActionHandler
{
    public const string ActionId = "perf.power.timeout";

    public string Id => ActionId;
    public string Title => L("Modifier les délais de mise en veille");
    public bool RequiresAdmin => false;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.OneOf(p, "kind", "monitor", "standby");
        Validate.OneOf(p, "source", "ac", "dc");
        Validate.Int(p, "minutes", 0, 600);
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var kind = Validate.OneOf(p, "kind", "monitor", "standby");
        var source = Validate.OneOf(p, "source", "ac", "dc");
        var minutes = Validate.Int(p, "minutes", 0, 600);
        var setting = $"{kind}-timeout-{source}"; // valeurs issues exclusivement des listes blanches ci-dessus

        var result = await ProcessRunner.RunAsync(SystemTool.PowerCfg,
            ["/change", setting, minutes.ToString(CultureInfo.InvariantCulture)],
            new RunOptions { Timeout = TimeSpan.FromSeconds(30), OutputEncoding = ProcessRunner.OemEncoding }, ctx.Cancellation);
        if (!result.Success)
            return ActionResult.Fail(L("powercfg a échoué (code {0}) : {1}", result.ExitCode, result.CombinedOutput.Trim()));

        var delay = PerfText.Minutes(minutes);
        return ActionResult.Ok((kind, source) switch
        {
            ("monitor", "ac") => L("Extinction de l'écran sur secteur : {0}.", delay),
            ("monitor", _) => L("Extinction de l'écran sur batterie : {0}.", delay),
            (_, "ac") => L("Mise en veille sur secteur : {0}.", delay),
            _ => L("Mise en veille sur batterie : {0}.", delay),
        });
    }
}

/// <summary>Mode d'alimentation choisi pour le secteur ou la batterie (API documentée de Windows 11).</summary>
public sealed class SetPowerModeAction : IActionHandler
{
    public const string ActionId = "perf.power.mode";

    public string Id => ActionId;
    public string Title => L("Changer le mode d'alimentation");
    public bool RequiresAdmin => false;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.OneOf(p, "source", "ac", "dc");
        Validate.OneOf(p, "mode", [.. PowerApi.Modes.Select(m => m.Key)]);
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var ac = Validate.OneOf(p, "source", "ac", "dc") == "ac";
        var mode = PowerApi.ModeFromKey(Validate.OneOf(p, "mode", [.. PowerApi.Modes.Select(m => m.Key)]))!;
        if (!PowerApi.UserModeApiAvailable)
            return Task.FromResult(ActionResult.Fail(L("Cette version de Windows ne permet pas de changer le mode ici : utilisez les Paramètres Windows.")));
        var rc = PowerApi.SetUserMode(ac, mode.Id);
        return Task.FromResult(rc == 0
            ? ActionResult.Ok(ac ? L("Mode « {0} » sur secteur.", mode.Label) : L("Mode « {0} » sur batterie.", mode.Label))
            : ActionResult.Fail(L("Windows a refusé le changement de mode (code {0}).", rc)));
    }
}

/// <summary>
/// Options graphiques par défaut de DirectX (Paramètres > Écran > Graphiques) : elles partagent une seule valeur
/// « clé=valeur; » que l'on modifie en préservant les autres entrées (HDR automatique, etc.). Journalisé, annulable.
/// </summary>
public sealed class DirectXSettingAction : IActionHandler
{
    public const string ActionId = "perf.dx.set";
    public const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string ValueName = "DirectXUserGlobalSettings";

    /// <summary>Entrées autorisées → libellé.</summary>
    public static readonly IReadOnlyDictionary<string, string> Settings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SwapEffectUpgradeEnable"] = L("Optimisations pour les jeux fenêtrés"),
        ["VRROptimizeEnable"] = L("Optimisations pour la fréquence d'actualisation variable"),
    };

    public string Id => ActionId;
    public string Title => L("Options graphiques de DirectX");
    public bool RequiresAdmin => false;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.OneOf(p, "setting", [.. Settings.Keys]);
        Validate.OneOf(p, "value", "default", "1", "0");
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var setting = Validate.OneOf(p, "setting", [.. Settings.Keys]);
        var value = Validate.OneOf(p, "value", "default", "1", "0");

        var entries = Parse(RegistryAccess.Read(RegHive.CurrentUser, Key, ValueName, ctx.UserSid) as string);
        if (value == "default") entries.Remove(setting);
        else entries[setting] = value;

        Operation op = entries.Count == 0
            ? Reg.CuDel(Key, ValueName)
            : Reg.CuString(Key, ValueName, string.Concat(entries.Select(e => $"{e.Key}={e.Value};")));
        var label = value switch { "1" => L("Activé"), "0" => L("Désactivé"), _ => L("Par défaut de Windows") };
        var entry = ctx.ApplyJournaled(ActionId, Settings[setting], label, [op]);
        var message = value switch
        {
            "1" => L("{0} : activé. Pris en compte au prochain lancement des jeux.", Settings[setting]),
            "0" => L("{0} : désactivé. Pris en compte au prochain lancement des jeux.", Settings[setting]),
            _ => L("{0} : par défaut de Windows. Pris en compte au prochain lancement des jeux.", Settings[setting]),
        };
        return Task.FromResult(new ActionResult(true, message)
        {
            JournalId = entry.Id,
        });
    }

    /// <summary>Découpe « A=1;B=0; » en dictionnaire ordonné (entrées invalides ignorées).</summary>
    public static Dictionary<string, string> Parse(string? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var k = part[..eq].Trim();
            var v = part[(eq + 1)..].Trim();
            if (k.Length is > 0 and <= 64 && v.Length <= 64 && k.All(char.IsLetterOrDigit)) result[k] = v;
        }
        return result;
    }

    /// <summary>État actuel d'une entrée : "1", "0" ou null (valeur par défaut de Windows).</summary>
    public static string? Current(string setting) =>
        Parse(RegistryAccess.ReadString(RegHive.CurrentUser, Key, ValueName)).TryGetValue(setting, out var v) ? v : null;
}

/// <summary>Formats de texte partagés du module.</summary>
public static class PerfText
{
    public static string Minutes(int minutes) => minutes switch
    {
        0 => L("jamais"),
        < 60 => L("{0} min", minutes),
        _ when minutes % 60 == 0 => LP(minutes / 60, "{0} heure", "{0} heures"),
        _ => L("{0} h {1:00}", minutes / 60, minutes % 60),
    };
}
