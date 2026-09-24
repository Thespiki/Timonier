using Microsoft.Win32;
using PcPilot.Core.Catalog;
using PcPilot.Core.Model;
using PcPilot.Core.Platform;
using PcPilot.Core.Security;

namespace PcPilot.Modules.Devices;

/// <summary>
/// Contrôles effectués dans le broker AVANT toute action sur un périphérique : identifiant valide, périphérique réellement
/// présent (énumération WMI, correspondance exacte), classe non protégée, disque non système. Tout refus lève une
/// <see cref="ValidationException"/> au message clair.
/// </summary>
internal static class DeviceGuard
{
    /// <summary><c>pnputil /disable-device</c> et <c>/enable-device</c> existent depuis Windows 10 2004 (build 19041).</summary>
    public const int MinBuild = 19041;

    public static string ReadId(IReadOnlyDictionary<string, string> p) => Validate.DeviceInstanceId(Validate.Required(p, "id", 400));

    public static DeviceEntry CheckDisable(string id, bool sensitiveConfirmed)
    {
        var entry = DeviceInventory.Find(id)
                    ?? throw new ValidationException("Ce périphérique n'est pas présent sur le PC (il a peut-être été débranché).");
        var (level, reason) = entry.Protection;
        if (level == DeviceProtection.Protected)
            throw new ValidationException($"PC Pilot refuse de désactiver « {entry.Name} » : {reason}");
        if (string.Equals(entry.PnpClass, "DiskDrive", StringComparison.OrdinalIgnoreCase)) CheckExternalDataDisk(id, entry.Name);
        if (level == DeviceProtection.Sensitive && !sensitiveConfirmed)
            throw new ValidationException($"« {entry.Name} » est un périphérique sensible : sa désactivation doit être confirmée par le processus administrateur.");
        return entry;
    }

    /// <summary>Un disque ne peut être désactivé que s'il est relié en USB et ne contient pas Windows (vérification fail-safe).</summary>
    private static void CheckExternalDataDisk(string id, string name)
    {
        try
        {
            var disk = WmiQuery.Query("SELECT Index, InterfaceType, PNPDeviceID FROM Win32_DiskDrive WHERE PNPDeviceID = '" + WmiQuery.Escape(id) + "'")
                .FirstOrDefault(r => string.Equals(r.GetValueOrDefault("PNPDeviceID") as string, id, StringComparison.OrdinalIgnoreCase));
            if (disk is null) return; // lecteur sans support (lecteur de cartes vide) : aucun volume en dépend
            if (!string.Equals(disk.GetValueOrDefault("InterfaceType") as string, "USB", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"PC Pilot refuse de désactiver « {name} » : ce n'est pas un disque USB externe.");
            var index = Convert.ToInt64(disk.GetValueOrDefault("Index") ?? -1L);
            var root = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\');
            if (root.Length != 2 || root[1] != ':') throw new ValidationException("Lecteur système non identifiable.");
            var partitions = WmiQuery.Query("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + root + "'} WHERE AssocClass = Win32_LogicalDiskToPartition");
            if (partitions.Any(pt => Convert.ToInt64(pt.GetValueOrDefault("DiskIndex") ?? -2L) == index))
                throw new ValidationException($"PC Pilot refuse de désactiver « {name} » : Windows est installé sur ce disque.");
        }
        catch (ValidationException) { throw; }
        catch (Exception ex)
        {
            Log.Warn("Devices", "contrôle du disque système impossible : " + ex.Message);
            throw new ValidationException("Impossible de vérifier que ce disque ne contient pas Windows : action refusée par précaution.");
        }
    }

    /// <summary>Pour réactiver, le périphérique peut être absent (débranché) : il doit alors être connu de Windows (clé Enum).</summary>
    public static string CheckEnable(string id)
    {
        if (DeviceInventory.Find(id) is { } entry) return entry.Name;
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + id, false);
        if (key is null) throw new ValidationException("Ce périphérique est inconnu de Windows (il a peut-être été désinstallé).");
        if (key.GetValue("FriendlyName") is string friendly && friendly.Length > 0) return friendly;
        // DeviceDesc est souvent de la forme « @fichier.inf,%cle%;Nom lisible ».
        if (key.GetValue("DeviceDesc") is string desc && desc.Length > 0) return desc.Contains(';') ? desc[(desc.LastIndexOf(';') + 1)..] : desc;
        return "Périphérique";
    }

    public static void CheckBuild()
    {
        if (Environment.OSVersion.Version.Build < MinBuild)
            throw new ValidationException("Nécessite Windows 10 version 2004 ou plus récent.");
    }

    /// <summary>Exécute pnputil (liste blanche, ArgumentList : aucune interprétation par un shell).</summary>
    public static async Task<ActionResult> RunPnpUtilAsync(ActionContext ctx, string verb, string id, string name, string okMessage)
    {
        var r = await ProcessRunner.RunAsync(SystemTool.PnpUtil, [verb, id],
            new RunOptions { Timeout = TimeSpan.FromSeconds(90) }, ctx.Cancellation).ConfigureAwait(false);
        var data = new Dictionary<string, string> { ["name"] = name };
        if (r.ExitCode == 0) return ActionResult.Ok(okMessage, data);
        if (r.ExitCode == 3010) // ERROR_SUCCESS_REBOOT_REQUIRED
            return new ActionResult(true, okMessage + " Un redémarrage est nécessaire pour terminer.") { Data = data, Effect = ApplyEffect.Reboot };
        var detail = r.CombinedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        Log.Warn("Devices", $"pnputil {verb} : code {r.ExitCode}");
        return ActionResult.Fail(r.TimedOut ? "L'opération a expiré." : $"Windows a refusé l'opération (code {r.ExitCode}){(detail is null ? "." : " : " + detail)}");
    }
}

/// <summary>Désactive un périphérique non sensible (confirmation simple dans l'interface).</summary>
public sealed class DisableDeviceAction : IActionHandler
{
    public const string ActionId = "devices.device.disable";
    public string Id => ActionId;
    public string Title => "Désactiver un périphérique";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => DeviceGuard.ReadId(p);

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var id = DeviceGuard.ReadId(p);
        DeviceGuard.CheckBuild();
        ctx.Progress?.Report("Vérification du périphérique…");
        var entry = DeviceGuard.CheckDisable(id, sensitiveConfirmed: false);
        if (entry.IsDisabled) return ActionResult.Ok($"« {entry.Name} » est déjà désactivé.", new() { ["name"] = entry.Name });
        ctx.Progress?.Report("Désactivation…");
        return await DeviceGuard.RunPnpUtilAsync(ctx, "/disable-device", id, entry.Name, $"« {entry.Name} » est désactivé.").ConfigureAwait(false);
    }
}

/// <summary>
/// Désactive un périphérique sensible (clavier, souris, réseau, affichage, contrôleur USB…) : le broker affiche sa propre
/// confirmation, qu'un programme non élevé ne peut pas valider à la place de l'utilisateur.
/// </summary>
public sealed class DisableSensitiveDeviceAction : IActionHandler
{
    public const string ActionId = "devices.device.disable-sensitive";
    public string Id => ActionId;
    public string Title => "Désactiver un périphérique sensible";
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => DeviceGuard.ReadId(p);

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p)
    {
        try
        {
            var id = DeviceGuard.ReadId(p);
            var entry = DeviceInventory.Find(id);
            if (entry is null) return "Désactiver un périphérique sensible ?";
            var (_, reason) = entry.Protection;
            return $"Désactiver « {entry.Name} » ({entry.Class.Title}) ?\n\n{reason}\n\n" +
                   "Pour le réactiver : PC Pilot, page Périphériques, section « Désactivés par PC Pilot » (ou le Gestionnaire de périphériques).";
        }
        catch (Exception ex) when (ex is ValidationException or System.Management.ManagementException)
        {
            return "Désactiver un périphérique sensible ?";
        }
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var id = DeviceGuard.ReadId(p);
        DeviceGuard.CheckBuild();
        ctx.Progress?.Report("Vérification du périphérique…");
        var entry = DeviceGuard.CheckDisable(id, sensitiveConfirmed: true);
        if (entry.IsDisabled) return ActionResult.Ok($"« {entry.Name} » est déjà désactivé.", new() { ["name"] = entry.Name });
        ctx.Progress?.Report("Désactivation…");
        return await DeviceGuard.RunPnpUtilAsync(ctx, "/disable-device", id, entry.Name, $"« {entry.Name} » est désactivé.").ConfigureAwait(false);
    }
}

/// <summary>Réactive un périphérique (présent, ou connu de Windows s'il est débranché).</summary>
public sealed class EnableDeviceAction : IActionHandler
{
    public const string ActionId = "devices.device.enable";
    public string Id => ActionId;
    public string Title => "Réactiver un périphérique";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => DeviceGuard.ReadId(p);

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var id = DeviceGuard.ReadId(p);
        DeviceGuard.CheckBuild();
        ctx.Progress?.Report("Vérification du périphérique…");
        var name = DeviceGuard.CheckEnable(id);
        ctx.Progress?.Report("Réactivation…");
        return await DeviceGuard.RunPnpUtilAsync(ctx, "/enable-device", id, name, $"« {name} » est réactivé.").ConfigureAwait(false);
    }
}
