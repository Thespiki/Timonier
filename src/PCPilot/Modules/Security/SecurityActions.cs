using System.Runtime.InteropServices;
using Microsoft.Win32;
using PcPilot.Core.Catalog;
using PcPilot.Core.Model;
using PcPilot.Core.Platform;
using PcPilot.Core.Security;

namespace PcPilot.Modules.Security;

/// <summary>Règle de pare-feu créée par PC Pilot (lue dans le magasin local du pare-feu).</summary>
internal sealed record PcPilotFirewallRule(string Name, string? Application, string Direction, bool Enabled);

/// <summary>
/// Règles de pare-feu « PC Pilot ». Lecture : magasin local du pare-feu dans le registre (lisible sans droits, rapide).
/// Écriture : API COM HNetCfg (INetFwPolicy2 / INetFwRule), uniquement dans le broker. PC Pilot ne touche jamais
/// une règle dont le groupe n'est pas exactement « PC Pilot ».
/// </summary>
internal static class FirewallRules
{
    public const string Grouping = "PC Pilot";
    public const string NamePrefix = "PC Pilot — ";
    private const string RulesKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";
    private const int DirectionIn = 1, DirectionOut = 2, ActionBlock = 0, AllProfiles = 0x7FFFFFFF;

    private sealed record RawRule(string Name, string? Group, string? App, string Direction, string Action, bool Active);

    private static List<RawRule> ReadStore()
    {
        var rules = new List<RawRule>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(RulesKey, false);
        if (key is null) return rules;
        foreach (var valueName in key.GetValueNames())
        {
            if (key.GetValue(valueName) is not string data) continue;
            string? name = null, group = null, app = null, dir = "", action = "";
            var active = true;
            foreach (var field in data.Split('|'))
            {
                var eq = field.IndexOf('=');
                if (eq <= 0) continue;
                var v = field[(eq + 1)..];
                switch (field[..eq])
                {
                    case "Name": name = v; break;
                    case "EmbedCtxt": group = v; break;
                    case "App": app = v; break;
                    case "Dir": dir = v; break;
                    case "Action": action = v; break;
                    case "Active": active = !v.Equals("FALSE", StringComparison.OrdinalIgnoreCase); break;
                }
            }
            if (name is not null) rules.Add(new RawRule(name, group, app, dir, action, active));
        }
        return rules;
    }

    /// <summary>Règles créées par PC Pilot (lecture seule, sans élévation).</summary>
    public static List<PcPilotFirewallRule> List() =>
        [.. ReadStore()
            .Where(r => r.Group == Grouping && r.Name.StartsWith(NamePrefix, StringComparison.Ordinal))
            .Select(r => new PcPilotFirewallRule(r.Name, r.App is null ? null : Environment.ExpandEnvironmentVariables(r.App),
                r.Direction.Equals("In", StringComparison.OrdinalIgnoreCase) ? "In" : "Out", r.Active))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Crée les règles de blocage (sortant, et entrant si demandé). Renvoie le nom des règles.</summary>
    public static string Block(string exePath, bool inbound)
    {
        var existing = List();
        if (existing.Any(r => SamePath(r.Application, exePath)))
            throw new InvalidOperationException("Cette application est déjà bloquée par PC Pilot.");

        var exe = Path.GetFileName(exePath);
        var name = NamePrefix + exe;
        var store = ReadStore();
        for (var i = 2; store.Any(r => r.Name == name) && i < 100; i++) name = $"{NamePrefix}{exe} ({i})";

        var policy = CreateCom("HNetCfg.FwPolicy2");
        try
        {
            dynamic rules = ((dynamic)policy).Rules;
            foreach (var direction in inbound ? new[] { DirectionOut, DirectionIn } : new[] { DirectionOut })
            {
                var ruleObj = CreateCom("HNetCfg.FWRule");
                try
                {
                    dynamic rule = ruleObj;
                    rule.Name = name;
                    rule.Description = "Créée par PC Pilot : bloque l'accès réseau de " + exePath;
                    rule.ApplicationName = exePath;
                    rule.Direction = direction;
                    rule.Action = ActionBlock;
                    rule.Profiles = AllProfiles;
                    rule.Grouping = Grouping;
                    rule.Enabled = true;
                    rules.Add(rule);
                }
                finally { Marshal.FinalReleaseComObject(ruleObj); }
            }
        }
        finally { Marshal.FinalReleaseComObject(policy); }
        return name;
    }

    /// <summary>Supprime les règles PC Pilot portant ce nom ; refuse si une autre règle porte le même nom.</summary>
    public static int Unblock(string name)
    {
        var matching = ReadStore().Where(r => r.Name == name).ToList();
        if (matching.Count == 0) throw new InvalidOperationException("Cette règle n'existe plus.");
        if (matching.Any(r => r.Group != Grouping))
            throw new InvalidOperationException("Une règle qui n'a pas été créée par PC Pilot porte ce nom : suppression refusée par sécurité.");

        var policy = CreateCom("HNetCfg.FwPolicy2");
        try
        {
            dynamic rules = ((dynamic)policy).Rules;
            // INetFwRules.Remove supprime une règle par appel : on répète tant qu'il en reste (borné).
            for (var i = 0; i < matching.Count + 2; i++)
            {
                if (!ReadStore().Any(r => r.Name == name && r.Group == Grouping)) break;
                rules.Remove(name);
            }
        }
        finally { Marshal.FinalReleaseComObject(policy); }

        var left = ReadStore().Count(r => r.Name == name);
        if (left > 0) throw new InvalidOperationException($"{left} règle(s) n'ont pas pu être supprimées.");
        return matching.Count;
    }

    private static object CreateCom(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: true)!;
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("Objet COM indisponible : " + progId);
    }

    private static bool SamePath(string? a, string b) =>
        a is not null && string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(a)), b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Validation partagée (interface et broker) du programme à bloquer.</summary>
    public static string ValidateExecutable(string raw)
    {
        var path = Validate.ExistingLocalFile(raw, ".exe");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Les programmes de Windows ne peuvent pas être bloqués ici : cela risquerait de casser Windows Update, " +
                                          "le réseau ou Sécurité Windows.");
        if (string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("PC Pilot ne peut pas se bloquer lui-même.");
        return path;
    }

    public static string ValidateRuleName(string raw)
    {
        if (!raw.StartsWith(NamePrefix, StringComparison.Ordinal) || raw.Length > NamePrefix.Length + 280 || raw.Length == NamePrefix.Length)
            throw new ValidationException("Seules les règles créées par PC Pilot peuvent être supprimées.");
        if (raw.Any(c => char.IsControl(c) || c is '|' or '"'))
            throw new ValidationException("Nom de règle invalide.");
        return raw;
    }
}

/// <summary>« security.firewall.block » : bloque l'accès réseau d'un programme (.exe local validé).</summary>
public sealed class FirewallBlockAction : IActionHandler
{
    public const string ActionId = "security.firewall.block";
    public string Id => ActionId;
    public string Title => "Bloquer l'accès Internet d'une application";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        FirewallRules.ValidateExecutable(Validate.Required(p, "path", 1024));
        Validate.Bool(p, "inbound", true);
        foreach (var key in p.Keys)
            if (key is not "path" and not "inbound") throw new ValidationException($"Paramètre inattendu : {key}");
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var path = FirewallRules.ValidateExecutable(Validate.Required(p, "path", 1024));
        var inbound = Validate.Bool(p, "inbound", true);
        ctx.Progress?.Report("Création des règles de pare-feu…");
        var name = FirewallRules.Block(path, inbound);
        return Task.FromResult(ActionResult.Ok($"« {Path.GetFileName(path)} » ne peut plus accéder au réseau.",
            new Dictionary<string, string> { ["name"] = name }));
    }
}

/// <summary>« security.firewall.unblock » : supprime uniquement des règles du groupe « PC Pilot ».</summary>
public sealed class FirewallUnblockAction : IActionHandler
{
    public const string ActionId = "security.firewall.unblock";
    public string Id => ActionId;
    public string Title => "Débloquer une application dans le pare-feu";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        FirewallRules.ValidateRuleName(Validate.Required(p, "name", 400));
        if (p.Count != 1) throw new ValidationException("Paramètres inattendus.");
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var name = FirewallRules.ValidateRuleName(Validate.Required(p, "name", 400));
        var count = FirewallRules.Unblock(name);
        return Task.FromResult(ActionResult.Ok($"« {name[FirewallRules.NamePrefix.Length..]} » peut de nouveau accéder au réseau ({count} règle(s) supprimée(s))."));
    }
}

/// <summary>« security.firewall.enable » : réactive le pare-feu Windows sur tous les profils (jamais l'inverse).</summary>
public sealed class FirewallEnableAction : IActionHandler
{
    public const string ActionId = "security.firewall.enable";
    public string Id => ActionId;
    public string Title => "Réactiver le pare-feu Windows";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        if (p.Count != 0) throw new ValidationException("Cette action n'accepte aucun paramètre.");
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var r = await ProcessRunner.RunAsync(SystemTool.Netsh, ["advfirewall", "set", "allprofiles", "state", "on"],
            new RunOptions { Timeout = TimeSpan.FromSeconds(30) }, ctx.Cancellation);
        return r.Success
            ? ActionResult.Ok("Pare-feu Windows réactivé pour les réseaux privés, publics et de domaine.")
            : ActionResult.Fail("netsh n'a pas pu réactiver le pare-feu : " + r.CombinedOutput.Trim());
    }
}

/// <summary>« security.smb1.uninstall » : désinstalle la fonctionnalité facultative SMB1Protocol (script PowerShell constant).</summary>
public sealed class Smb1UninstallAction : IActionHandler
{
    public const string ActionId = "security.smb1.uninstall";

    private const string Script =
        "$f = Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol;" +
        "if ($null -eq $f -or $f.State -ne 'Enabled') { 'ABSENT'; exit 0 };" +
        "$r = Disable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -NoRestart;" +
        "if ($r.RestartNeeded) { 'REBOOT' } else { 'OK' }";

    public string Id => ActionId;
    public string Title => "Désinstaller le protocole SMBv1";
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> parameters) =>
        "PC Pilot va désinstaller la fonctionnalité Windows « Support de partage de fichiers SMB 1.0/CIFS ». " +
        "Les appareils réseau très anciens qui n'utilisent que SMBv1 ne seront plus accessibles. Un redémarrage sera nécessaire.";

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        if (p.Count != 0) throw new ValidationException("Cette action n'accepte aucun paramètre.");
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        ctx.Progress?.Report("Désinstallation de SMBv1…");
        var r = await PowerShellRunner.RunAsync(Script, timeout: TimeSpan.FromMinutes(10), ct: ctx.Cancellation);
        if (!r.Success) return ActionResult.Fail("La désinstallation de SMBv1 a échoué : " + r.CombinedOutput.Trim());
        var output = r.Output;
        if (output.Contains("ABSENT", StringComparison.Ordinal)) return ActionResult.Ok("SMBv1 n'est pas installé : rien à faire.");
        return new ActionResult(true, "SMBv1 désinstallé.")
        {
            Effect = output.Contains("REBOOT", StringComparison.Ordinal) ? ApplyEffect.Reboot : ApplyEffect.None,
        };
    }
}
