namespace Timonier.Modules.Devices;

/// <summary>Niveau de protection d'un périphérique vis-à-vis de la désactivation par Timonier.</summary>
public enum DeviceProtection
{
    /// <summary>Désactivation possible après une confirmation simple.</summary>
    None,
    /// <summary>Désactivation possible, mais confirmée par le processus administrateur (clavier, souris, réseau…).</summary>
    Sensitive,
    /// <summary>Timonier refuse de désactiver ce périphérique (composant indispensable au démarrage ou à la sécurité).</summary>
    Protected,
}

/// <summary>Classe de périphériques Plug-and-Play : nom français, icône, ordre d'affichage.</summary>
public sealed record DeviceClassInfo(string Key, string Title, string Glyph, int Order);

/// <summary>
/// Connaissances statiques sur les périphériques : noms des classes, explications des codes d'erreur du Gestionnaire
/// de périphériques, et règles de protection. Code pur (sans E/S) : utilisable dans l'interface comme dans le broker.
/// </summary>
public static class DeviceCatalog
{
    private static readonly Dictionary<string, DeviceClassInfo> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Display"] = new("Display", L("Graphics cards"), "", 10),
        ["Monitor"] = new("Monitor", L("Monitors"), "", 11),
        ["MEDIA"] = new("MEDIA", L("Sound, video and game controllers"), "", 20),
        ["AudioEndpoint"] = new("AudioEndpoint", L("Audio inputs and outputs"), "", 21),
        ["Camera"] = new("Camera", L("Cameras"), "", 22),
        ["Image"] = new("Image", L("Imaging devices"), "", 23),
        ["Net"] = new("Net", L("Network adapters"), "", 30),
        ["Bluetooth"] = new("Bluetooth", "Bluetooth", "", 31),
        ["Keyboard"] = new("Keyboard", L("Keyboards"), "", 40),
        ["Mouse"] = new("Mouse", L("Mice and touchpads"), "", 41),
        ["HIDClass"] = new("HIDClass", L("Human Interface Devices (HID)"), "", 42),
        ["Biometric"] = new("Biometric", L("Biometrics (Windows Hello)"), "", 43),
        ["SmartCardReader"] = new("SmartCardReader", L("Smart card readers"), "", 44),
        ["Printer"] = new("Printer", L("Printers"), "", 50),
        ["PrintQueue"] = new("PrintQueue", L("Print queues"), "", 51),
        ["WPD"] = new("WPD", L("Portable devices (phones, players)"), "", 52),
        ["USB"] = new("USB", L("USB controllers"), "", 60),
        ["USBDevice"] = new("USBDevice", L("USB devices"), "", 61),
        ["Ports"] = new("Ports", L("Ports (COM and LPT)"), "", 62),
        ["DiskDrive"] = new("DiskDrive", L("Disk drives"), "", 70),
        ["CDROM"] = new("CDROM", L("CD/DVD drives"), "", 71),
        ["SCSIAdapter"] = new("SCSIAdapter", L("Storage controllers"), "", 72),
        ["HDC"] = new("HDC", L("IDE/SATA controllers"), "", 73),
        ["Volume"] = new("Volume", L("Storage volumes"), "", 74),
        ["VolumeSnapshot"] = new("VolumeSnapshot", L("Volume shadow copies"), "", 75),
        ["SDHost"] = new("SDHost", L("Memory card readers"), "", 76),
        ["Sensor"] = new("Sensor", L("Sensors"), "", 80),
        ["Battery"] = new("Battery", L("Batteries and power"), "", 81),
        ["Processor"] = new("Processor", L("Processors"), "", 90),
        ["System"] = new("System", L("System devices"), "", 91),
        ["Computer"] = new("Computer", L("Computer"), "", 92),
        ["Firmware"] = new("Firmware", L("Firmware"), "", 93),
        ["SecurityDevices"] = new("SecurityDevices", L("Security devices (TPM)"), "", 94),
        ["SoftwareDevice"] = new("SoftwareDevice", L("Software devices"), "", 95),
        ["SoftwareComponent"] = new("SoftwareComponent", L("Software components"), "", 96),
        ["Extension"] = new("Extension", L("Driver extensions"), "", 97),
    };

    private static readonly DeviceClassInfo Unknown = new("", L("Other devices"), "", 200);

    public static DeviceClassInfo ClassInfo(string? pnpClass) =>
        string.IsNullOrWhiteSpace(pnpClass) ? Unknown
        : Classes.TryGetValue(pnpClass, out var info) ? info
        : new DeviceClassInfo(pnpClass, pnpClass, "", 150);

    // ------------------------------------------------------------------ Codes d'erreur

    /// <summary>Explication (et conseil) pour un code ConfigManagerErrorCode. Null pour 0 (fonctionne correctement).</summary>
    public static (string Summary, string Advice)? Problem(int code) => code switch
    {
        0 => null,
        1 => (L("Device not configured correctly (code 1)."), L("Update the driver from Windows Update or the manufacturer's website.")),
        3 => (L("Driver corrupted or not enough memory (code 3)."), L("Close some apps, then reinstall the driver if the problem persists.")),
        10 => (L("The device can't start (code 10)."), L("Unsuitable driver or faulty hardware: reinstall the manufacturer's driver, then unplug the device and plug it back in.")),
        12 => (L("Not enough resources (code 12)."), L("Resource conflict with another device: disable an unused device or update the BIOS/UEFI.")),
        14 => (L("Restart required (code 14)."), L("Restart the PC for this device to work.")),
        16 => (L("Unidentified resources (code 16)."), L("Update the driver; contact the manufacturer if the problem persists.")),
        18 => (L("Drivers need to be reinstalled (code 18)."), L("Reinstall the driver (Windows Update, Optional updates section, or the manufacturer's website).")),
        19 => (L("Registry configuration incomplete or corrupted (code 19)."), L("Uninstall the device from Device Manager, then restart.")),
        21 => (L("Removal in progress (code 21)."), L("Windows is removing this device: wait a few seconds, then refresh.")),
        22 => (L("Device disabled (code 22)."), L("It was disabled on purpose: re-enable it if you need it.")),
        24 => (L("Device missing or driver incomplete (code 24)."), L("Check the connection; reinstall the driver if the device is properly connected.")),
        28 => (L("No driver installed (code 28)."), L("Install the driver: Windows Update (Optional updates) or the PC manufacturer's website.")),
        29 => (L("Disabled by the firmware (code 29)."), L("This device is disabled in the BIOS/UEFI: you need to re-enable it in the PC's setup utility.")),
        31 => (L("Driver can't be loaded (code 31)."), L("Reinstall or update the driver.")),
        32 => (L("Driver service disabled (code 32)."), L("This driver's service is disabled: reinstall the driver to restore it.")),
        33 or 34 or 35 or 36 => (L("Hardware resource problem (code {0}).", code), L("Update the PC's BIOS/UEFI or contact the manufacturer.")),
        37 => (L("Driver initialization failed (code 37)."), L("Reinstall the manufacturer's driver.")),
        38 => (L("Previous instance of the driver still in memory (code 38)."), L("Restart the PC.")),
        39 => (L("Driver corrupted or missing (code 39)."), L("Reinstall the driver; if the problem persists, some software (antivirus, filter) may be the cause.")),
        40 => (L("Invalid driver service information (code 40)."), L("Reinstall the driver.")),
        41 => (L("Driver loaded but hardware not found (code 41)."), L("Unplug the device and plug it back in, or reinstall the driver.")),
        42 => (L("Duplicate device detected (code 42)."), L("Restart the PC.")),
        43 => (L("Stopped because it reported problems (code 43)."), L("Often a hardware failure or a faulty driver: update the driver, try another USB port.")),
        44 => (L("Stopped by an app or a service (code 44)."), L("Restart the PC.")),
        45 => (L("Device not connected (code 45)."), L("It was connected before but isn't anymore: nothing to do if you unplugged it on purpose.")),
        46 => (L("Windows is shutting down (code 46)."), L("The device will be available at the next startup.")),
        47 => (L("Prepared for safe removal (code 47)."), L("Unplug it, then plug it back in.")),
        48 => (L("Driver blocked (code 48)."), L("This driver isn't compatible with this version of Windows: install a recent version from the manufacturer.")),
        49 => (L("System registry too large (code 49)."), L("Uninstall devices you no longer use, then restart.")),
        50 => (L("Properties not applied (code 50)."), L("Restart the PC.")),
        51 => (L("Waiting for another device (code 51)."), L("It will start when the device it depends on is ready.")),
        52 => (L("Driver signature not verified (code 52)."), L("Install a signed driver from Windows Update or the manufacturer's website.")),
        53 => (L("Reserved for the kernel debugger (code 53)."), L("Turn off kernel debugging if it isn't intentional.")),
        54 => (L("Failed, reset in progress (code 54)."), L("Wait, then refresh; restart if the problem persists.")),
        _ => (L("Problem reported by Windows (code {0}).", code), L("Check the device's properties in Device Manager.")),
    };

    /// <summary>Code « normal » à ne pas compter comme une panne (désactivation volontaire, périphérique débranché).</summary>
    public static bool IsBenign(int code) => code is 0 or 22 or 45;

    // ------------------------------------------------------------------ Protection

    /// <summary>Classes que Timonier refuse de désactiver, avec la raison affichée à l'utilisateur.</summary>
    public static readonly IReadOnlyList<(string Class, string Title, string Reason)> ProtectedClasses =
    [
        ("System", L("System devices"), L("Buses, clocks, interrupt controllers, ACPI: disabling them can prevent Windows from starting.")),
        ("Computer", L("Computer"), L("Represents the motherboard itself.")),
        ("Processor", L("Processors"), L("Essential for the PC to work.")),
        ("SCSIAdapter", L("Storage controllers"), L("The system disk (NVMe, RAID…) depends on them: the PC would no longer start.")),
        ("HDC", L("IDE/SATA controllers"), L("The system disk may depend on them: the PC would no longer start.")),
        ("DiskDrive", L("Internal disks and system disk"), L("Only external USB drives that don't contain Windows can be disabled.")),
        ("Volume", L("Storage volumes"), L("Partitions used by Windows; use Disk Management instead.")),
        ("VolumeSnapshot", L("Shadow copies"), L("Used by System Restore and backups.")),
        ("SecurityDevices", L("TPM and security"), L("The TPM protects BitLocker, Windows Hello and Secure Boot.")),
        ("Firmware", L("Firmware"), L("BIOS/UEFI components: risk of startup problems.")),
        ("Battery", L("Batteries and AC adapter"), L("Windows could no longer manage charging or shut down when the battery is low.")),
        ("SoftwareDevice", L("Software devices"), L("Virtual components managed by Windows itself.")),
        ("SoftwareComponent", L("Software components"), L("Software modules attached to drivers, with no hardware of their own.")),
        ("Extension", L("Driver extensions"), L("Configuration add-ons, with no hardware of their own.")),
        ("LegacyDriver", L("Legacy drivers"), L("Non-Plug and Play drivers: manage them as services.")),
    ];

    /// <summary>Classes dont la désactivation est confirmée par le processus administrateur (risque de perdre le contrôle du PC).</summary>
    public static readonly IReadOnlyList<(string Class, string Why)> SensitiveClasses =
    [
        ("Keyboard", L("the keyboard will stop responding")),
        ("Mouse", L("the mouse or touchpad will stop responding")),
        ("HIDClass", L("keyboards, mice, touchscreens or buttons may depend on it")),
        ("Display", L("the display will fall back to the Windows basic driver (low resolution, no acceleration)")),
        ("Net", L("the network or internet connection will be cut")),
        ("USB", L("all devices plugged into this controller or hub will be cut off, including the keyboard and mouse")),
        ("Bluetooth", L("Bluetooth keyboards, mice and headphones will be disconnected")),
        ("Biometric", L("Windows Hello sign-in (fingerprint, face) will stop working")),
        ("SmartCardReader", L("smart card sign-in will stop working")),
        ("SDHost", L("memory cards will no longer be read and, on PCs with eMMC storage, the internal disk may depend on it")),
    ];

    /// <summary>
    /// Classement d'un périphérique d'après sa classe et son identifiant (sans E/S). Le broker complète ce contrôle
    /// (existence, disque système) avant d'agir : cette fonction sert aussi à l'interface pour griser les boutons.
    /// </summary>
    public static (DeviceProtection Level, string? Reason) Protection(string? pnpClass, string instanceId)
    {
        var cls = pnpClass ?? "";
        if (cls.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase))
        {
            return instanceId.StartsWith(@"USBSTOR\", StringComparison.OrdinalIgnoreCase)
                ? (DeviceProtection.None, null)
                : (DeviceProtection.Protected, L("Internal disk: Timonier only disables external USB drives."));
        }
        foreach (var (c, _, reason) in ProtectedClasses)
            if (c.Equals(cls, StringComparison.OrdinalIgnoreCase)) return (DeviceProtection.Protected, reason);
        // Pilotes ACPI de batterie / adaptateur secteur parfois rangés hors de la classe Battery.
        if (instanceId.StartsWith(@"ACPI\PNP0C0A", StringComparison.OrdinalIgnoreCase) ||
            instanceId.StartsWith(@"ACPI\ACPI0003", StringComparison.OrdinalIgnoreCase))
            return (DeviceProtection.Protected, L("Battery or AC adapter: Windows must be able to manage power."));
        foreach (var (c, why) in SensitiveClasses)
            if (c.Equals(cls, StringComparison.OrdinalIgnoreCase)) return (DeviceProtection.Sensitive, L("Warning: {0}.", why));
        if (cls.Length == 0)
            return (DeviceProtection.Sensitive, L("Unknown class: Timonier can't assess what this device does."));
        return (DeviceProtection.None, null);
    }
}
