using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Devices;

/// <summary>
/// Blocages matériels pour tout le PC (stratégies machine, admin). Sources : RemovableStorage.admx,
/// DeviceInstallation.admx, DeviceSetup.admx, Camera.admx, AppPrivacy.admx, Sensors.admx (Microsoft Learn),
/// clé de service USBSTOR (article « Désactiver les périphériques de stockage USB ») et magasin de consentement
/// machine du gestionnaire d'accès aux capacités (interrupteurs « Accès à la caméra / au micro / localisation »
/// des Paramètres, partie « appareil »).
/// </summary>
public static class DevicesTweaks
{
    public static string GroupStorage => L("Removable storage");
    public static string GroupSensors => L("Camera, microphone and location");
    public static string GroupInstall => L("Device installation");

    private const string C = DevicesModule.Category;

    private const string UsbStor = @"SYSTEM\CurrentControlSet\Services\USBSTOR";
    private const string StoragePolicies = @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies";
    private const string Removable = @"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices";
    /// <summary>Classe « Disques amovibles » (GUID_DEVINTERFACE_DISK).</summary>
    private const string RemovableDisks = Removable + @"\{53f5630d-b6bf-11d0-94f2-00a0c91efb8b}";
    /// <summary>Classe « CD et DVD » (GUID_DEVINTERFACE_CDROM).</summary>
    private const string Optical = Removable + @"\{53f56308-b6bf-11d0-94f2-00a0c91efb8b}";
    /// <summary>Appareils portables Windows (WPD) : deux classes dans l'ADMX.</summary>
    private const string Wpd1 = Removable + @"\{6AC27878-A6FA-4155-BA85-F98F491D4F33}";
    private const string Wpd2 = Removable + @"\{F33FDC04-D1AC-4E8E-9A30-19BBD4B108AE}";

    private const string ConsentStore = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private const string CameraPolicy = @"SOFTWARE\Policies\Microsoft\Camera";
    private const string AppPrivacy = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
    private const string LocationPolicy = @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors";

    private const string DeviceInstall = @"SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions";
    private const string DeviceMetadata = @"SOFTWARE\Policies\Microsoft\Windows\Device Metadata";

    public static IEnumerable<TweakDefinition> All()
    {
        // ------------------------------------------------------------------ Stockage amovible
        yield return Tweak.Toggle("devices.block.usbstorage", L("USB drives and external USB disks"),
                L("Allows or blocks access to USB drives, external hard drives and memory cards for all accounts on the PC. Blocking disables the USB storage driver (USBSTOR) and applies the “Removable Disks: Deny read access” policy. Keyboards, mice, printers, headsets and USB charging keep working."))
            .In(C, GroupStorage)
            .Keywords(L("usb, usb drive, usb stick, flash drive, external drive, removable storage, usbstor, sd card, data leak, block usb"))
            .Tags("family", "kiosk", "security", "office")
            .Labels(L("Allowed"), L("Blocked"))
            .Risk(RiskLevel.Moderate)
            .Warning(L("A drive that is already plugged in stays accessible until it's unplugged or until the next restart. You won't be able to use any USB drive, including for a reinstall or a backup, while the block is on."))
            .WhenOn(Reg.LmDword(UsbStor, "Start", 3), Reg.LmDel(RemovableDisks, "Deny_Read"))
            .WhenOff(Reg.LmDword(UsbStor, "Start", 4), Reg.LmDword(RemovableDisks, "Deny_Read", 1))
            .Detect(() => Dword(UsbStor, "Start") == 4 || Dword(RemovableDisks, "Deny_Read") == 1 ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.usbwrite", L("Writing to USB drives and removable disks"),
                L("When blocked, USB drives and removable disks become read-only: you can open and copy their files, but not save or delete anything on them. Applies the “Removable Disks: Deny write access” policy and USB storage write protection (StorageDevicePolicies)."))
            .In(C, GroupStorage)
            .Keywords(L("read-only, write protection, write protect, writeprotect, usb, usb drive, file copy"))
            .Tags("kiosk", "security", "office")
            .Labels(LC("feminine", "Allowed"), LC("feminine", "Blocked"))
            .Warning(L("Takes effect the next time each drive or disk is plugged in."))
            .WhenOn(Reg.LmDel(RemovableDisks, "Deny_Write"), Reg.LmDel(StoragePolicies, "WriteProtect"))
            .WhenOff(Reg.LmDword(RemovableDisks, "Deny_Write", 1), Reg.LmDword(StoragePolicies, "WriteProtect", 1))
            .Detect(() => Dword(RemovableDisks, "Deny_Write") == 1 || Dword(StoragePolicies, "WriteProtect") == 1 ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.optical", L("CD and DVD drives"),
                L("Allows or blocks reading and burning CDs, DVDs and Blu-ray discs for all accounts (“CD and DVD: Deny read access / Deny write access” policies). No effect if the PC has no optical drive."))
            .In(C, GroupStorage)
            .Keywords(L("cd, dvd, blu-ray, optical drive, burner, cdrom"))
            .Tags("kiosk", "security")
            .Labels(L("Allowed"), L("Blocked"))
            .WhenOn(Reg.LmDel(Optical, "Deny_Read"), Reg.LmDel(Optical, "Deny_Write"))
            .WhenOff(Reg.LmDword(Optical, "Deny_Read", 1), Reg.LmDword(Optical, "Deny_Write", 1))
            .Detect(() => Dword(Optical, "Deny_Read") == 1 || Dword(Optical, "Deny_Write") == 1 ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.wpd", L("Phones, tablets and cameras (MTP)"),
                L("Allows or blocks access to files on smartphones, e-readers, media players and cameras connected via USB (Windows portable devices, MTP/PTP protocols), for all accounts. USB charging isn't affected."))
            .In(C, GroupStorage)
            .Keywords(L("phone, smartphone, android, iphone, mtp, ptp, wpd, camera, photo transfer"))
            .Tags("family", "kiosk", "security", "office")
            .Labels(L("Allowed"), L("Blocked"))
            .Warning(L("Takes effect the next time the device is plugged in."))
            .WhenOn(Reg.LmDel(Wpd1, "Deny_Read"), Reg.LmDel(Wpd1, "Deny_Write"), Reg.LmDel(Wpd2, "Deny_Read"), Reg.LmDel(Wpd2, "Deny_Write"))
            .WhenOff(Reg.LmDword(Wpd1, "Deny_Read", 1), Reg.LmDword(Wpd1, "Deny_Write", 1), Reg.LmDword(Wpd2, "Deny_Read", 1), Reg.LmDword(Wpd2, "Deny_Write", 1))
            .Detect(() => Dword(Wpd1, "Deny_Read") == 1 || Dword(Wpd2, "Deny_Read") == 1 || Dword(Wpd1, "Deny_Write") == 1 ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.allremovable", L("All removable storage (global lock)"),
                L("“All Removable Storage classes: Deny all access” policy. When blocked, it prohibits USB drives, external disks, memory cards, CDs/DVDs, floppy disks and portable devices all at once, and takes precedence over the settings above."))
            .In(C, GroupStorage)
            .Keywords(L("removable storage, deny all, block all, usb, cd, sd card, lockdown"))
            .Tags("kiosk", "security")
            .Labels(L("Allowed"), L("Blocked"))
            .Risk(RiskLevel.Moderate)
            .Warning(L("No removable media can be read while this lock is on, including to install a driver or back up files."))
            .WhenOn(Reg.LmDel(Removable, "Deny_All"))
            .WhenOff(Reg.LmDword(Removable, "Deny_All", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        // ------------------------------------------------------------------ Caméra, micro et position (tout le PC)
        yield return Tweak.Toggle("devices.block.camera", L("Camera for all accounts"),
                L("When blocked, no app or account can use the camera (the device's “Camera access” switch and the “Allow Use of Camera” policy). This differs from the per-app permissions on the Privacy page, which only apply to your account. Windows Hello facial recognition may stop working."))
            .In(C, GroupSensors)
            .Keywords(L("camera, webcam, video, spying, video call, videoconference, block camera, privacy"))
            .Tags("family", "kiosk", "security")
            .Labels(LC("feminine", "Allowed"), LC("feminine", "Blocked"))
            .Requires(Requires.Camera)
            .WhenOn(Reg.LmString(ConsentStore + @"\webcam", "Value", "Allow"), Reg.LmDel(CameraPolicy, "AllowCamera"))
            .WhenOff(Reg.LmString(ConsentStore + @"\webcam", "Value", "Deny"), Reg.LmDword(CameraPolicy, "AllowCamera", 0))
            .Detect(() => Dword(CameraPolicy, "AllowCamera") == 0 || Str(ConsentStore + @"\webcam", "Value") == "Deny" ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.microphone", L("Microphone for all accounts"),
                L("When blocked, no account can grant access to the microphone: the device's “Microphone access” switch is off and the “Let Windows apps access the microphone” policy forces denial. Calls and dictation no longer pick up sound. Per-app permissions for your account are set on the Privacy page."))
            .In(C, GroupSensors)
            .Keywords(L("mic, microphone, audio, recording, spying, eavesdropping, block microphone, privacy"))
            .Tags("family", "kiosk", "security")
            .Labels(L("Allowed"), L("Blocked"))
            .WhenOn(Reg.LmString(ConsentStore + @"\microphone", "Value", "Allow"), Reg.LmDel(AppPrivacy, "LetAppsAccessMicrophone"))
            .WhenOff(Reg.LmString(ConsentStore + @"\microphone", "Value", "Deny"), Reg.LmDword(AppPrivacy, "LetAppsAccessMicrophone", 2))
            .Detect(() => Dword(AppPrivacy, "LetAppsAccessMicrophone") == 2 || Str(ConsentStore + @"\microphone", "Value") == "Deny" ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.location", L("PC location services"),
                L("When off, neither Windows nor any app can determine the PC's location (the device's “Location services” switch and the “Turn off location” policy). Weather, maps, automatic time zone and “Find my device” can no longer use the location."))
            .In(C, GroupSensors)
            .Keywords(L("location, position, gps, geolocation, lfsvc, location services"))
            .Tags("family", "kiosk", "security")
            .Labels(LC("plural (services)", "On"), LC("plural (services)", "Off"))
            .WhenOn(Reg.LmString(ConsentStore + @"\location", "Value", "Allow"), Reg.LmDel(LocationPolicy, "DisableLocation"))
            .WhenOff(Reg.LmString(ConsentStore + @"\location", "Value", "Deny"), Reg.LmDword(LocationPolicy, "DisableLocation", 1))
            .Detect(() => Dword(LocationPolicy, "DisableLocation") == 1 || Str(ConsentStore + @"\location", "Value") == "Deny" ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        // ------------------------------------------------------------------ Installation de périphériques
        yield return Tweak.Choice("devices.install.restrict", L("Installation of new devices"),
                L("Prevents Windows from installing devices that have never been plugged into this PC (“Prevent installation of removable devices” or “…not described by other policy settings” policies). Devices already installed keep working."))
            .In(C, GroupInstall)
            .Keywords(L("device installation, new device, denyunspecified, denyremovabledevices, driver, restriction, block devices"))
            .Tags("kiosk", "security")
            .Risk(RiskLevel.Advanced)
            .Warning(L("A new keyboard, a new mouse or a USB drive that has never been plugged in will no longer work, even to replace a broken device. The “all new devices” option also blocks driver updates offered by Windows Update. Switch back to “Allowed” before changing hardware."))
            .OptionWithHelp("allow", LC("feminine", "Allowed"), L("Normal Windows behavior."),
                Reg.LmDel(DeviceInstall, "DenyRemovableDevices"), Reg.LmDel(DeviceInstall, "DenyUnspecified"))
            .OptionWithHelp("removable", L("Blocked for removable devices"), L("Any new USB or Bluetooth device, memory card… is refused."),
                Reg.LmDword(DeviceInstall, "DenyRemovableDevices", 1), Reg.LmDel(DeviceInstall, "DenyUnspecified"))
            .OptionWithHelp("all", L("Blocked for all new devices"), L("No new hardware or new driver can be installed."),
                Reg.LmDel(DeviceInstall, "DenyRemovableDevices"), Reg.LmDword(DeviceInstall, "DenyUnspecified", 1))
            .WindowsDefault("allow")
            .Build();

        yield return Tweak.Toggle("devices.install.metadata", L("Manufacturer apps and icons"),
                L("When a device is plugged in, Windows can download from the internet the custom icons and information (metadata) provided by its manufacturer, as well as the associated companion app. When blocked, drivers still install, but without these extras."))
            .In(C, GroupInstall)
            .Keywords(L("metadata, device metadata, manufacturer app, device icons, oem software, bloatware"))
            .Tags("office", "kiosk")
            .Labels(LC("feminine plural", "Allowed"), LC("feminine plural", "Blocked"))
            .WhenOn(Reg.LmDel(DeviceMetadata, "PreventDeviceMetadataFromNetwork"))
            .WhenOff(Reg.LmDword(DeviceMetadata, "PreventDeviceMetadataFromNetwork", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();
    }

    private static int? Dword(string key, string name) => RegistryAccess.ReadDword(RegHive.LocalMachine, key, name);
    private static string? Str(string key, string name) => RegistryAccess.ReadString(RegHive.LocalMachine, key, name);
}
