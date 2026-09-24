using System.Management;
using System.Text.Json;
using PcPilot.Core.Platform;
using PcPilot.Core.Security;
using PcPilot.Core.Settings;

namespace PcPilot.Modules.Devices;

/// <summary>Périphérique Plug-and-Play tel qu'énuméré par WMI (Win32_PnPEntity).</summary>
public sealed class DeviceEntry
{
    public required string InstanceId { get; init; }
    public required string Name { get; init; }
    public string? PnpClass { get; init; }
    public string? Manufacturer { get; init; }
    public string? Status { get; init; }
    public int ErrorCode { get; init; }
    public bool Present { get; init; } = true;
    public string? Service { get; init; }

    public DeviceClassInfo Class => DeviceCatalog.ClassInfo(PnpClass);
    public bool IsDisabled => ErrorCode == 22;
    public bool HasProblem => !DeviceCatalog.IsBenign(ErrorCode);
    public (DeviceProtection Level, string? Reason) Protection => DeviceCatalog.Protection(PnpClass, InstanceId);

    /// <summary>L'identifiant respecte le format accepté par les actions (certains identifiants logiciels exotiques non).</summary>
    public bool IdIsActionable
    {
        get
        {
            try { Validate.DeviceInstanceId(InstanceId); return true; }
            catch (ValidationException) { return false; }
        }
    }
}

public sealed record DriverDetails(string? Version, DateTime? Date, string? Provider, string? Inf, bool? Signed, string? Signer);

/// <summary>Lecture des périphériques (WMI, lecture seule, sans élévation). À appeler hors du thread UI.</summary>
public static class DeviceInventory
{
    private const string Fields = "Name, Description, PNPClass, Manufacturer, Status, ConfigManagerErrorCode, PNPDeviceID, Present, Service";

    public static List<DeviceEntry> Load()
    {
        var rows = WmiQuery.Query("SELECT " + Fields + " FROM Win32_PnPEntity", timeoutSeconds: 30);
        var list = new List<DeviceEntry>(rows.Count);
        foreach (var r in rows)
        {
            if (ToEntry(r) is { } e) list.Add(e);
        }
        return list;
    }

    /// <summary>Recherche exacte d'un périphérique présent par identifiant d'instance (l'identifiant doit avoir été validé).</summary>
    public static DeviceEntry? Find(string instanceId)
    {
        var id = Validate.DeviceInstanceId(instanceId);
        var rows = WmiQuery.Query("SELECT " + Fields + " FROM Win32_PnPEntity WHERE PNPDeviceID = '" + WmiQuery.Escape(id) + "'");
        return rows.Select(ToEntry).FirstOrDefault(e => e is not null && string.Equals(e.InstanceId, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Périphériques en erreur (hors désactivation volontaire) et désactivés, pour le contrôle de santé.</summary>
    public static (List<DeviceEntry> Problems, int Disabled) Problems()
    {
        var rows = WmiQuery.Query("SELECT " + Fields + " FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0", timeoutSeconds: 20);
        var entries = rows.Select(ToEntry).OfType<DeviceEntry>().ToList();
        return ([.. entries.Where(e => e.HasProblem)], entries.Count(e => e.IsDisabled));
    }

    /// <summary>Pilote associé (Win32_PnPSignedDriver) : requête lente, à faire à la demande.</summary>
    public static DriverDetails? LoadDriver(string instanceId)
    {
        var id = Validate.DeviceInstanceId(instanceId);
        var rows = WmiQuery.Query("SELECT DeviceID, DriverVersion, DriverDate, DriverProviderName, InfName, IsSigned, Signer " +
                                  "FROM Win32_PnPSignedDriver WHERE DeviceID = '" + WmiQuery.Escape(id) + "'", timeoutSeconds: 30);
        var r = rows.FirstOrDefault(x => string.Equals(x.GetValueOrDefault("DeviceID") as string, id, StringComparison.OrdinalIgnoreCase));
        if (r is null) return null;
        DateTime? date = null;
        if (r.GetValueOrDefault("DriverDate") is string d && d.Length >= 14)
        {
            try { date = ManagementDateTimeConverter.ToDateTime(d); } catch (ArgumentOutOfRangeException) { }
        }
        return new DriverDetails(r.GetValueOrDefault("DriverVersion") as string, date, r.GetValueOrDefault("DriverProviderName") as string,
            r.GetValueOrDefault("InfName") as string, r.GetValueOrDefault("IsSigned") as bool?, r.GetValueOrDefault("Signer") as string);
    }

    private static DeviceEntry? ToEntry(Dictionary<string, object?> r)
    {
        if (r.GetValueOrDefault("PNPDeviceID") is not string id || id.Length == 0) return null;
        var name = r.GetValueOrDefault("Name") as string;
        if (string.IsNullOrWhiteSpace(name)) name = r.GetValueOrDefault("Description") as string;
        if (string.IsNullOrWhiteSpace(name)) name = "Périphérique inconnu";
        return new DeviceEntry
        {
            InstanceId = id,
            Name = name.Trim(),
            PnpClass = r.GetValueOrDefault("PNPClass") as string,
            Manufacturer = Clean(r.GetValueOrDefault("Manufacturer") as string),
            Status = r.GetValueOrDefault("Status") as string,
            ErrorCode = r.GetValueOrDefault("ConfigManagerErrorCode") switch { uint u => (int)u, int i => i, _ => 0 },
            Present = r.GetValueOrDefault("Present") as bool? ?? true,
            Service = r.GetValueOrDefault("Service") as string,
        };
    }

    /// <summary>Les fabricants génériques « (Standard system devices) » ne renseignent rien : on les masque.</summary>
    private static string? Clean(string? manufacturer) =>
        string.IsNullOrWhiteSpace(manufacturer) || manufacturer.StartsWith('(') ? null : manufacturer.Trim();
}

/// <summary>
/// Mémoire des périphériques désactivés par PC Pilot (préférences de l'interface) : permet de proposer « Réactiver »
/// même si le périphérique est débranché ou n'apparaît plus dans la liste.
/// </summary>
public static class DisabledDevicesStore
{
    private const string Key = "devices.disabled";

    public static Dictionary<string, string> Load()
    {
        try
        {
            if (AppHost.Settings.ModuleData.TryGetValue(Key, out var json) && json.Length > 0 &&
                JsonSerializer.Deserialize(json, CoreJson.Default.DictionaryStringString) is { } d)
                return new Dictionary<string, string>(d, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex) { Log.Warn("Devices", "liste des périphériques désactivés illisible : " + ex.Message); }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static void Set(string instanceId, string? name)
    {
        var d = Load();
        if (name is null) d.Remove(instanceId);
        else d[instanceId] = name;
        AppHost.Settings.ModuleData[Key] = JsonSerializer.Serialize(new Dictionary<string, string>(d), CoreJson.Default.DictionaryStringString);
        SettingsStore.Save();
    }
}
