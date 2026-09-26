using System.Runtime.InteropServices;
using Microsoft.Win32;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Devices;

/// <summary>
/// Contrôles effectués dans le broker AVANT toute action sur un périphérique : identifiant valide, périphérique réellement
/// présent (énumération WMI, correspondance exacte), classe non protégée, disque non système. Tout refus lève une
/// <see cref="ValidationException"/> au message clair.
/// </summary>
internal static partial class DeviceGuard
{
    /// <summary><c>pnputil /disable-device</c> et <c>/enable-device</c> existent depuis Windows 10 2004 (build 19041).</summary>
    public const int MinBuild = 19041;

    public static string ReadId(IReadOnlyDictionary<string, string> p) => Validate.DeviceInstanceId(Validate.Required(p, "id", 400));

    public static DeviceEntry CheckDisable(string id, bool sensitiveConfirmed)
    {
        var entry = DeviceInventory.Find(id)
                    ?? throw new ValidationException(L("This device isn't present on the PC (it may have been unplugged)."));
        var (level, reason) = entry.Protection;
        if (level == DeviceProtection.Protected)
            throw new ValidationException(L("Timonier won't disable “{0}”: {1}", entry.Name, reason));
        CheckNotOnSystemDiskPath(entry.InstanceId, entry.Name);
        if (string.Equals(entry.PnpClass, "DiskDrive", StringComparison.OrdinalIgnoreCase)) CheckExternalDataDisk(id, entry.Name);
        if (level == DeviceProtection.Sensitive && !sensitiveConfirmed)
            throw new ValidationException(L("“{0}” is a sensitive device: disabling it must be confirmed by the administrator process.", entry.Name));
        return entry;
    }

    /// <summary>
    /// Refuse tout périphérique situé sur le chemin matériel du disque de Windows (le disque lui-même, son contrôleur
    /// SD/eMMC, NVMe, USB ou SATA, les ponts PCI…), quelle que soit sa classe : les PC à stockage eMMC rangent par
    /// exemple le contrôleur du disque système dans la classe « SDHost ». Vérification fail-safe : en cas de doute, refus.
    /// </summary>
    private static void CheckNotOnSystemDiskPath(string id, string name)
    {
        HashSet<string> chain;
        try { chain = SystemDiskDeviceChain(); }
        catch (Exception ex)
        {
            Log.Warn("Devices", "chemin matériel du disque système illisible : " + ex.Message);
            throw new ValidationException(L("Couldn't verify that the Windows drive doesn't need this device: action refused as a precaution."));
        }
        if (chain.Contains(id))
            throw new ValidationException(L("Timonier won't disable “{0}”: the drive Windows is installed on depends on it.", name));
    }

    /// <summary>Identifiants d'instance du disque système et de tous ses parents dans l'arborescence Plug-and-Play.</summary>
    private static HashSet<string> SystemDiskDeviceChain()
    {
        var root = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\');
        if (root.Length != 2 || root[1] != ':') throw new InvalidOperationException("lecteur système non identifiable");
        var indexes = WmiQuery.Query("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + root + "'} WHERE AssocClass = Win32_LogicalDiskToPartition")
            .Select(pt => Convert.ToInt64(pt.GetValueOrDefault("DiskIndex") ?? -1L))
            .Where(i => i >= 0)
            .ToHashSet();
        var disks = WmiQuery.Query("SELECT Index, PNPDeviceID FROM Win32_DiskDrive")
            .Where(d => indexes.Contains(Convert.ToInt64(d.GetValueOrDefault("Index") ?? -1L)))
            .Select(d => d.GetValueOrDefault("PNPDeviceID") as string)
            .OfType<string>()
            .ToList();
        if (disks.Count == 0) throw new InvalidOperationException("disque système introuvable");

        var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new char[MaxDeviceIdLength + 1];
        foreach (var disk in disks)
        {
            if (CM_Locate_DevNode(out var node, disk, 0) != 0) throw new InvalidOperationException("nœud du disque système introuvable");
            chain.Add(disk);
            for (var depth = 0; depth < 64 && CM_Get_Parent(out var parent, node, 0) == 0; depth++)
            {
                if (CM_Get_Device_ID(parent, buffer, (uint)buffer.Length, 0) != 0) throw new InvalidOperationException("identifiant de parent illisible");
                var end = Array.IndexOf(buffer, '\0');
                chain.Add(new string(buffer, 0, end < 0 ? buffer.Length : end));
                node = parent;
            }
        }
        return chain;
    }

    private const int MaxDeviceIdLength = 400;

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Parent")]
    private static partial uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_ID(uint devInst, [Out] char[] buffer, uint bufferLength, uint flags);

    /// <summary>Un disque ne peut être désactivé que s'il est relié en USB et ne contient pas Windows (vérification fail-safe).</summary>
    private static void CheckExternalDataDisk(string id, string name)
    {
        try
        {
            var disk = WmiQuery.Query("SELECT Index, InterfaceType, PNPDeviceID FROM Win32_DiskDrive WHERE PNPDeviceID = '" + WmiQuery.Escape(id) + "'")
                .FirstOrDefault(r => string.Equals(r.GetValueOrDefault("PNPDeviceID") as string, id, StringComparison.OrdinalIgnoreCase));
            if (disk is null) return; // lecteur sans support (lecteur de cartes vide) : aucun volume en dépend
            if (!string.Equals(disk.GetValueOrDefault("InterfaceType") as string, "USB", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException(L("Timonier won't disable “{0}”: it isn't an external USB drive.", name));
            var index = Convert.ToInt64(disk.GetValueOrDefault("Index") ?? -1L);
            var root = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\');
            if (root.Length != 2 || root[1] != ':') throw new ValidationException(L("Couldn't identify the system drive."));
            var partitions = WmiQuery.Query("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + root + "'} WHERE AssocClass = Win32_LogicalDiskToPartition");
            if (partitions.Any(pt => Convert.ToInt64(pt.GetValueOrDefault("DiskIndex") ?? -2L) == index))
                throw new ValidationException(L("Timonier won't disable “{0}”: Windows is installed on this drive.", name));
        }
        catch (ValidationException) { throw; }
        catch (Exception ex)
        {
            Log.Warn("Devices", "contrôle du disque système impossible : " + ex.Message);
            throw new ValidationException(L("Couldn't verify that this drive doesn't contain Windows: action refused as a precaution."));
        }
    }

    /// <summary>Pour réactiver, le périphérique peut être absent (débranché) : il doit alors être connu de Windows (clé Enum).</summary>
    public static string CheckEnable(string id)
    {
        if (DeviceInventory.Find(id) is { } entry) return entry.Name;
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + id, false);
        if (key is null) throw new ValidationException(L("Windows doesn't recognize this device (it may have been uninstalled)."));
        if (key.GetValue("FriendlyName") is string friendly && friendly.Length > 0) return friendly;
        // DeviceDesc est souvent de la forme « @fichier.inf,%cle%;Nom lisible ».
        if (key.GetValue("DeviceDesc") is string desc && desc.Length > 0) return desc.Contains(';') ? desc[(desc.LastIndexOf(';') + 1)..] : desc;
        return L("Device");
    }

    public static void CheckBuild()
    {
        if (Environment.OSVersion.Version.Build < MinBuild)
            throw new ValidationException(L("Requires Windows 10 version 2004 or later."));
    }

    /// <summary>Exécute pnputil (liste blanche, ArgumentList : aucune interprétation par un shell).</summary>
    public static async Task<ActionResult> RunPnpUtilAsync(ActionContext ctx, string verb, string id, string name, string okMessage)
    {
        var r = await ProcessRunner.RunAsync(SystemTool.PnpUtil, [verb, id],
            new RunOptions { Timeout = TimeSpan.FromSeconds(90) }, ctx.Cancellation).ConfigureAwait(false);
        var data = new Dictionary<string, string> { ["name"] = name };
        if (r.ExitCode == 0) return ActionResult.Ok(okMessage, data);
        if (r.ExitCode == 3010) // ERROR_SUCCESS_REBOOT_REQUIRED
            return new ActionResult(true, okMessage + " " + L("A restart is required to finish.")) { Data = data, Effect = ApplyEffect.Reboot };
        var detail = r.CombinedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        Log.Warn("Devices", $"pnputil {verb} : code {r.ExitCode}");
        return ActionResult.Fail(r.TimedOut ? L("The operation timed out.") : detail is null ? L("Windows refused the operation (code {0}).", r.ExitCode) : L("Windows refused the operation (code {0}): {1}", r.ExitCode, detail));
    }
}

/// <summary>Désactive un périphérique non sensible (confirmation simple dans l'interface).</summary>
public sealed class DisableDeviceAction : IActionHandler
{
    public const string ActionId = "devices.device.disable";
    public string Id => ActionId;
    public string Title => L("Disable a device");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => DeviceGuard.ReadId(p);

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var id = DeviceGuard.ReadId(p);
        DeviceGuard.CheckBuild();
        ctx.Progress?.Report(L("Checking the device…"));
        var entry = DeviceGuard.CheckDisable(id, sensitiveConfirmed: false);
        if (entry.IsDisabled) return ActionResult.Ok(L("“{0}” is already disabled.", entry.Name), new() { ["name"] = entry.Name });
        ctx.Progress?.Report(L("Disabling…"));
        return await DeviceGuard.RunPnpUtilAsync(ctx, "/disable-device", id, entry.Name, L("“{0}” is disabled.", entry.Name)).ConfigureAwait(false);
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
    public string Title => L("Disable a sensitive device");
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => DeviceGuard.ReadId(p);

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> p)
    {
        try
        {
            var id = DeviceGuard.ReadId(p);
            var entry = DeviceInventory.Find(id);
            if (entry is null) return L("Disable a sensitive device?");
            var (_, reason) = entry.Protection;
            return L("Disable “{0}” ({1})?\n\n{2}\n\nTo re-enable it: Timonier, Devices page, “Disabled by Timonier” section (or Device Manager).", entry.Name, entry.Class.Title, reason);
        }
        catch (Exception ex) when (ex is ValidationException or System.Management.ManagementException)
        {
            return L("Disable a sensitive device?");
        }
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var id = DeviceGuard.ReadId(p);
        DeviceGuard.CheckBuild();
        ctx.Progress?.Report(L("Checking the device…"));
        var entry = DeviceGuard.CheckDisable(id, sensitiveConfirmed: true);
        if (entry.IsDisabled) return ActionResult.Ok(L("“{0}” is already disabled.", entry.Name), new() { ["name"] = entry.Name });
        ctx.Progress?.Report(L("Disabling…"));
        return await DeviceGuard.RunPnpUtilAsync(ctx, "/disable-device", id, entry.Name, L("“{0}” is disabled.", entry.Name)).ConfigureAwait(false);
    }
}

/// <summary>Réactive un périphérique (présent, ou connu de Windows s'il est débranché).</summary>
public sealed class EnableDeviceAction : IActionHandler
{
    public const string ActionId = "devices.device.enable";
    public string Id => ActionId;
    public string Title => L("Re-enable a device");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p) => DeviceGuard.ReadId(p);

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var id = DeviceGuard.ReadId(p);
        DeviceGuard.CheckBuild();
        ctx.Progress?.Report(L("Checking the device…"));
        var name = DeviceGuard.CheckEnable(id);
        ctx.Progress?.Report(L("Re-enabling…"));
        return await DeviceGuard.RunPnpUtilAsync(ctx, "/enable-device", id, name, L("“{0}” is re-enabled.", name)).ConfigureAwait(false);
    }
}
