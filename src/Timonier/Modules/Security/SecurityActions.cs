using System.Runtime.InteropServices;
using Microsoft.Win32;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Security;

/// <summary>Règle de pare-feu créée par Timonier (lue dans le magasin local du pare-feu).</summary>
/// <param name="Legacy">Règle créée par une version antérieure au changement de nom (groupe « PC Pilot »).</param>
internal sealed record TimonierFirewallRule(string Name, string? Application, string Direction, bool Enabled, bool Legacy)
{
    /// <summary>Nom affiché : le nom de la règle sans son préfixe (« Timonier — » ou « PC Pilot — »).</summary>
    public string DisplayName => FirewallRules.DisplayName(Name);
}

/// <summary>
/// Règles de pare-feu « Timonier ». Lecture : magasin local du pare-feu dans le registre (lisible sans droits, rapide).
/// Écriture : API COM HNetCfg (INetFwPolicy2 / INetFwRule), uniquement dans le broker. Timonier ne touche jamais
/// une règle dont le groupe n'est pas exactement « Timonier » (ou « PC Pilot » pour les règles créées par les versions
/// antérieures au changement de nom, qui restent listées et supprimables mais ne sont plus jamais créées).
/// </summary>
internal static class FirewallRules
{
    public const string Grouping = "Timonier";
    public const string NamePrefix = "Timonier — ";
    /// <summary>Groupe et préfixe utilisés par les versions publiées sous le nom « PC Pilot ».</summary>
    public const string LegacyGrouping = "PC Pilot";
    public const string LegacyNamePrefix = "PC Pilot — ";
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

    /// <summary>Groupe attendu pour une règle d'après le préfixe de son nom (null si le nom n'est pas celui d'une règle Timonier).</summary>
    private static string? OwnerGroup(string name) =>
        name.StartsWith(NamePrefix, StringComparison.Ordinal) ? Grouping
        : name.StartsWith(LegacyNamePrefix, StringComparison.Ordinal) ? LegacyGrouping
        : null;

    public static string DisplayName(string name) =>
        name.StartsWith(NamePrefix, StringComparison.Ordinal) ? name[NamePrefix.Length..]
        : name.StartsWith(LegacyNamePrefix, StringComparison.Ordinal) ? name[LegacyNamePrefix.Length..]
        : name;

    /// <summary>Règles créées par Timonier, y compris sous son ancien nom (lecture seule, sans élévation).</summary>
    public static List<TimonierFirewallRule> List() =>
        [.. ReadStore()
            .Where(r => r.Group is not null && r.Group == OwnerGroup(r.Name))
            .Select(r => new TimonierFirewallRule(r.Name, r.App is null ? null : Environment.ExpandEnvironmentVariables(r.App),
                r.Direction.Equals("In", StringComparison.OrdinalIgnoreCase) ? "In" : "Out", r.Active, r.Group == LegacyGrouping))
            .OrderBy(r => DisplayName(r.Name), StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Crée les règles de blocage (sortant, et entrant si demandé). Renvoie le nom des règles.</summary>
    public static string Block(string exePath, bool inbound)
    {
        var existing = List();
        if (existing.Any(r => SamePath(r.Application, exePath)))
            throw new InvalidOperationException(L("This app is already blocked by Timonier."));

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
                    rule.Description = L("Created by Timonier: blocks network access for {0}", exePath);
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

    /// <summary>Supprime les règles Timonier portant ce nom ; refuse si une autre règle porte le même nom.</summary>
    public static int Unblock(string name)
    {
        var group = OwnerGroup(name) ?? throw new ValidationException(L("Only rules created by Timonier can be deleted."));
        var matching = ReadStore().Where(r => r.Name == name).ToList();
        if (matching.Count == 0) throw new InvalidOperationException(L("This rule no longer exists."));
        if (matching.Any(r => r.Group != group))
            throw new InvalidOperationException(L("A rule that wasn't created by Timonier has this name: deletion refused for safety."));

        var policy = CreateCom("HNetCfg.FwPolicy2");
        try
        {
            dynamic rules = ((dynamic)policy).Rules;
            // INetFwRules.Remove supprime une règle par appel : on répète tant qu'il en reste (borné).
            for (var i = 0; i < matching.Count + 2; i++)
            {
                if (!ReadStore().Any(r => r.Name == name && r.Group == group)) break;
                rules.Remove(name);
            }
        }
        finally { Marshal.FinalReleaseComObject(policy); }

        var left = ReadStore().Count(r => r.Name == name);
        if (left > 0) throw new InvalidOperationException(LP(left, "{0} rule couldn't be deleted.", "{0} rules couldn't be deleted."));
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
            throw new ValidationException(L("Windows programs can't be blocked here: it could break Windows Update, networking or Windows Security."));
        if (DefenderFolders().Any(d => path.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException(L("Microsoft Defender components can't be blocked: the antivirus could no longer update its definitions or use cloud-delivered protection."));
        if (string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException(L("Timonier can't block itself."));
        return path;
    }

    /// <summary>Dossiers de Microsoft Defender situés hors du dossier Windows (moteur, plateforme, outils).</summary>
    private static IEnumerable<string> DefenderFolders()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var baseDir = root.TrimEnd('\\') + "\\";
            yield return baseDir + "Windows Defender\\";
            yield return baseDir + "Windows Defender Advanced Threat Protection\\";
            yield return baseDir + "Microsoft\\Windows Defender\\";
            yield return baseDir + "Microsoft\\Windows Defender Advanced Threat Protection\\";
        }
    }

    public static string ValidateRuleName(string raw)
    {
        if (OwnerGroup(raw) is null || raw.Length > NamePrefix.Length + 280 || DisplayName(raw).Length == 0)
            throw new ValidationException(L("Only rules created by Timonier can be deleted."));
        if (raw.Any(c => char.IsControl(c) || c is '|' or '"'))
            throw new ValidationException(L("Invalid rule name."));
        return raw;
    }
}

/// <summary>« security.firewall.block » : bloque l'accès réseau d'un programme (.exe local validé).</summary>
public sealed class FirewallBlockAction : IActionHandler
{
    public const string ActionId = "security.firewall.block";
    public string Id => ActionId;
    public string Title => L("Block an app's internet access");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        FirewallRules.ValidateExecutable(Validate.Required(p, "path", 1024));
        Validate.Bool(p, "inbound", true);
        foreach (var key in p.Keys)
            if (key is not "path" and not "inbound") throw new ValidationException(L("Unexpected parameter: {0}", key));
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var path = FirewallRules.ValidateExecutable(Validate.Required(p, "path", 1024));
        var inbound = Validate.Bool(p, "inbound", true);
        ctx.Progress?.Report(L("Creating firewall rules…"));
        var name = FirewallRules.Block(path, inbound);
        return Task.FromResult(ActionResult.Ok(L("“{0}” can no longer access the network.", Path.GetFileName(path)),
            new Dictionary<string, string> { ["name"] = name }));
    }
}

/// <summary>« security.firewall.unblock » : supprime uniquement des règles du groupe « Timonier ».</summary>
public sealed class FirewallUnblockAction : IActionHandler
{
    public const string ActionId = "security.firewall.unblock";
    public string Id => ActionId;
    public string Title => L("Unblock an app in the firewall");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        FirewallRules.ValidateRuleName(Validate.Required(p, "name", 400));
        if (p.Count != 1) throw new ValidationException(L("Unexpected parameters."));
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var name = FirewallRules.ValidateRuleName(Validate.Required(p, "name", 400));
        var count = FirewallRules.Unblock(name);
        return Task.FromResult(ActionResult.Ok(LP(count, "“{1}” can access the network again ({0} rule deleted).", "“{1}” can access the network again ({0} rules deleted).", FirewallRules.DisplayName(name))));
    }
}

/// <summary>« security.firewall.enable » : réactive le pare-feu Windows sur tous les profils (jamais l'inverse).</summary>
public sealed class FirewallEnableAction : IActionHandler
{
    public const string ActionId = "security.firewall.enable";
    public string Id => ActionId;
    public string Title => L("Turn Windows Firewall back on");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        if (p.Count != 0) throw new ValidationException(L("This action doesn't accept any parameters."));
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var r = await ProcessRunner.RunAsync(SystemTool.Netsh, ["advfirewall", "set", "allprofiles", "state", "on"],
            new RunOptions { Timeout = TimeSpan.FromSeconds(30) }, ctx.Cancellation);
        return r.Success
            ? ActionResult.Ok(L("Windows Firewall turned back on for private, public and domain networks."))
            : ActionResult.Fail(L("netsh couldn't turn the firewall back on: {0}", r.CombinedOutput.Trim()));
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
    public string Title => L("Uninstall the SMBv1 protocol");
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> parameters) =>
        L("Timonier will uninstall the “SMB 1.0/CIFS File Sharing Support” Windows feature. Very old network devices that only use SMBv1 will no longer be accessible. A restart will be required.");

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        if (p.Count != 0) throw new ValidationException(L("This action doesn't accept any parameters."));
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        ctx.Progress?.Report(L("Uninstalling SMBv1…"));
        var r = await PowerShellRunner.RunAsync(Script, timeout: TimeSpan.FromMinutes(10), ct: ctx.Cancellation);
        if (!r.Success) return ActionResult.Fail(L("Uninstalling SMBv1 failed: {0}", r.CombinedOutput.Trim()));
        var output = r.Output;
        if (output.Contains("ABSENT", StringComparison.Ordinal)) return ActionResult.Ok(L("SMBv1 isn't installed: nothing to do."));
        return new ActionResult(true, L("SMBv1 uninstalled."))
        {
            Effect = output.Contains("REBOOT", StringComparison.Ordinal) ? ApplyEffect.Reboot : ApplyEffect.None,
        };
    }
}
