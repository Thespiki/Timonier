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
    public const string GroupStorage = "Stockage amovible";
    public const string GroupSensors = "Caméra, micro et position";
    public const string GroupInstall = "Installation de périphériques";

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
        yield return Tweak.Toggle("devices.block.usbstorage", "Clés USB et disques USB externes",
                "Autorise ou bloque l'accès aux clés USB, disques durs externes et cartes mémoire pour tous les comptes du PC. " +
                "Le blocage désactive le pilote de stockage USB (USBSTOR) et applique la stratégie « Disques amovibles : refuser l'accès en lecture ». " +
                "Claviers, souris, imprimantes, casques et la recharge par USB continuent de fonctionner.")
            .In(C, GroupStorage)
            .Keywords("usb", "cle usb", "clé usb", "disque externe", "stockage amovible", "usbstor", "carte sd", "fuite de donnees", "bloquer usb")
            .Tags("family", "kiosk", "security", "office")
            .Labels("Autorisé", "Bloqué")
            .Risk(RiskLevel.Moderate)
            .Warning("Une clé déjà branchée reste accessible jusqu'à ce qu'elle soit débranchée ou jusqu'au prochain redémarrage. " +
                     "Vous ne pourrez plus utiliser de clé USB, y compris pour une réinstallation ou une sauvegarde, tant que le blocage est actif.")
            .WhenOn(Reg.LmDword(UsbStor, "Start", 3), Reg.LmDel(RemovableDisks, "Deny_Read"))
            .WhenOff(Reg.LmDword(UsbStor, "Start", 4), Reg.LmDword(RemovableDisks, "Deny_Read", 1))
            .Detect(() => Dword(UsbStor, "Start") == 4 || Dword(RemovableDisks, "Deny_Read") == 1 ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.usbwrite", "Écriture sur les clés USB et disques amovibles",
                "Bloquée, les clés USB et disques amovibles passent en lecture seule : on peut ouvrir et copier leurs fichiers, " +
                "mais rien y enregistrer ni y supprimer. Applique la stratégie « Disques amovibles : refuser l'accès en écriture » " +
                "et la protection en écriture du stockage USB (StorageDevicePolicies).")
            .In(C, GroupStorage)
            .Keywords("lecture seule", "protection ecriture", "write protect", "writeprotect", "usb", "cle usb", "copie de fichiers")
            .Tags("kiosk", "security", "office")
            .Labels("Autorisée", "Bloquée")
            .Warning("Prend effet au prochain branchement de chaque clé ou disque.")
            .WhenOn(Reg.LmDel(RemovableDisks, "Deny_Write"), Reg.LmDel(StoragePolicies, "WriteProtect"))
            .WhenOff(Reg.LmDword(RemovableDisks, "Deny_Write", 1), Reg.LmDword(StoragePolicies, "WriteProtect", 1))
            .Detect(() => Dword(RemovableDisks, "Deny_Write") == 1 || Dword(StoragePolicies, "WriteProtect") == 1 ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.optical", "Lecteurs de CD et DVD",
                "Autorise ou bloque la lecture et la gravure des CD, DVD et Blu-ray pour tous les comptes " +
                "(stratégies « CD et DVD : refuser l'accès en lecture / en écriture »). Sans effet si le PC n'a pas de lecteur optique.")
            .In(C, GroupStorage)
            .Keywords("cd", "dvd", "blu ray", "lecteur optique", "graveur", "cdrom")
            .Tags("kiosk", "security")
            .Labels("Autorisé", "Bloqué")
            .WhenOn(Reg.LmDel(Optical, "Deny_Read"), Reg.LmDel(Optical, "Deny_Write"))
            .WhenOff(Reg.LmDword(Optical, "Deny_Read", 1), Reg.LmDword(Optical, "Deny_Write", 1))
            .Detect(() => Dword(Optical, "Deny_Read") == 1 || Dword(Optical, "Deny_Write") == 1 ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.wpd", "Téléphones, tablettes et appareils photo (MTP)",
                "Autorise ou bloque l'accès aux fichiers des smartphones, liseuses, lecteurs multimédias et appareils photo branchés en USB " +
                "(appareils portables Windows, protocoles MTP/PTP), pour tous les comptes. La recharge par USB n'est pas concernée.")
            .In(C, GroupStorage)
            .Keywords("telephone", "smartphone", "android", "iphone", "mtp", "ptp", "wpd", "appareil photo", "transfert photos")
            .Tags("family", "kiosk", "security", "office")
            .Labels("Autorisé", "Bloqué")
            .Warning("Prend effet au prochain branchement de l'appareil.")
            .WhenOn(Reg.LmDel(Wpd1, "Deny_Read"), Reg.LmDel(Wpd1, "Deny_Write"), Reg.LmDel(Wpd2, "Deny_Read"), Reg.LmDel(Wpd2, "Deny_Write"))
            .WhenOff(Reg.LmDword(Wpd1, "Deny_Read", 1), Reg.LmDword(Wpd1, "Deny_Write", 1), Reg.LmDword(Wpd2, "Deny_Read", 1), Reg.LmDword(Wpd2, "Deny_Write", 1))
            .Detect(() => Dword(Wpd1, "Deny_Read") == 1 || Dword(Wpd2, "Deny_Read") == 1 || Dword(Wpd1, "Deny_Write") == 1 ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.allremovable", "Tous les stockages amovibles (verrou global)",
                "Stratégie « Toutes les classes de stockage amovible : refuser tout accès ». Bloquée, elle interdit d'un coup clés USB, " +
                "disques externes, cartes mémoire, CD/DVD, disquettes et appareils portables, et prime sur les réglages ci-dessus.")
            .In(C, GroupStorage)
            .Keywords("stockage amovible", "deny all", "tout bloquer", "usb", "cd", "carte sd", "verrouillage")
            .Tags("kiosk", "security")
            .Labels("Autorisé", "Bloqué")
            .Risk(RiskLevel.Moderate)
            .Warning("Aucun support amovible ne sera lisible tant que ce verrou est actif, y compris pour installer un pilote ou sauvegarder des fichiers.")
            .WhenOn(Reg.LmDel(Removable, "Deny_All"))
            .WhenOff(Reg.LmDword(Removable, "Deny_All", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();

        // ------------------------------------------------------------------ Caméra, micro et position (tout le PC)
        yield return Tweak.Toggle("devices.block.camera", "Caméra pour tous les comptes",
                "Bloquée, aucune application ni aucun compte ne peut utiliser la caméra (interrupteur « Accès à la caméra » de l'appareil " +
                "et stratégie « Autoriser l'utilisation de la caméra »). Diffère des autorisations par application de la page Confidentialité, " +
                "qui ne concernent que votre compte. Windows Hello par reconnaissance faciale peut cesser de fonctionner.")
            .In(C, GroupSensors)
            .Keywords("camera", "caméra", "webcam", "video", "espionnage", "visioconference", "bloquer camera")
            .Tags("family", "kiosk", "security")
            .Labels("Autorisée", "Bloquée")
            .Requires(Requires.Camera)
            .WhenOn(Reg.LmString(ConsentStore + @"\webcam", "Value", "Allow"), Reg.LmDel(CameraPolicy, "AllowCamera"))
            .WhenOff(Reg.LmString(ConsentStore + @"\webcam", "Value", "Deny"), Reg.LmDword(CameraPolicy, "AllowCamera", 0))
            .Detect(() => Dword(CameraPolicy, "AllowCamera") == 0 || Str(ConsentStore + @"\webcam", "Value") == "Deny" ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.microphone", "Microphone pour tous les comptes",
                "Bloqué, aucun compte ne peut accorder l'accès au micro : l'interrupteur « Accès au microphone » de l'appareil est coupé " +
                "et la stratégie « Autoriser les applications Windows à accéder au microphone » force le refus. Les appels et la dictée " +
                "ne captent plus le son. Les autorisations par application de votre compte se règlent dans la page Confidentialité.")
            .In(C, GroupSensors)
            .Keywords("micro", "microphone", "audio", "enregistrement", "espionnage", "ecoute", "bloquer micro")
            .Tags("family", "kiosk", "security")
            .Labels("Autorisé", "Bloqué")
            .WhenOn(Reg.LmString(ConsentStore + @"\microphone", "Value", "Allow"), Reg.LmDel(AppPrivacy, "LetAppsAccessMicrophone"))
            .WhenOff(Reg.LmString(ConsentStore + @"\microphone", "Value", "Deny"), Reg.LmDword(AppPrivacy, "LetAppsAccessMicrophone", 2))
            .Detect(() => Dword(AppPrivacy, "LetAppsAccessMicrophone") == 2 || Str(ConsentStore + @"\microphone", "Value") == "Deny" ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        yield return Tweak.Toggle("devices.block.location", "Services de localisation du PC",
                "Coupés, ni Windows ni aucune application ne peut déterminer la position du PC (interrupteur « Services de localisation » " +
                "de l'appareil et stratégie « Désactiver l'emplacement »). La météo, les cartes, le fuseau horaire automatique et " +
                "« Localiser mon appareil » ne peuvent plus utiliser la position.")
            .In(C, GroupSensors)
            .Keywords("localisation", "position", "gps", "location", "geolocalisation", "lfsvc", "emplacement")
            .Tags("family", "kiosk", "security")
            .Labels("Activés", "Coupés")
            .WhenOn(Reg.LmString(ConsentStore + @"\location", "Value", "Allow"), Reg.LmDel(LocationPolicy, "DisableLocation"))
            .WhenOff(Reg.LmString(ConsentStore + @"\location", "Value", "Deny"), Reg.LmDword(LocationPolicy, "DisableLocation", 1))
            .Detect(() => Dword(LocationPolicy, "DisableLocation") == 1 || Str(ConsentStore + @"\location", "Value") == "Deny" ? TweakDefinition.Off : TweakDefinition.On)
            .WindowsDefault(TweakDefinition.On)
            .Build();

        // ------------------------------------------------------------------ Installation de périphériques
        yield return Tweak.Choice("devices.install.restrict", "Installation de nouveaux périphériques",
                "Empêche Windows d'installer des périphériques jamais branchés sur ce PC (stratégies « Empêcher l'installation de " +
                "périphériques amovibles » ou « … non décrits par d'autres paramètres »). Les périphériques déjà installés continuent de fonctionner.")
            .In(C, GroupInstall)
            .Keywords("installation peripherique", "nouveau peripherique", "device installation", "denyunspecified", "denyremovabledevices", "pilote", "restriction")
            .Tags("kiosk", "security")
            .Risk(RiskLevel.Advanced)
            .Warning("Un nouveau clavier, une nouvelle souris ou une clé USB jamais branchée ne fonctionneront plus, même en remplacement " +
                     "d'un appareil en panne. L'option « tout nouveau périphérique » bloque aussi les mises à jour de pilotes proposées par " +
                     "Windows Update. Revenez sur « Autorisée » avant de changer de matériel.")
            .OptionWithHelp("allow", "Autorisée", "Comportement normal de Windows.",
                Reg.LmDel(DeviceInstall, "DenyRemovableDevices"), Reg.LmDel(DeviceInstall, "DenyUnspecified"))
            .OptionWithHelp("removable", "Bloquée pour les périphériques amovibles", "Tout nouvel appareil USB, Bluetooth, carte mémoire… est refusé.",
                Reg.LmDword(DeviceInstall, "DenyRemovableDevices", 1), Reg.LmDel(DeviceInstall, "DenyUnspecified"))
            .OptionWithHelp("all", "Bloquée pour tout nouveau périphérique", "Aucun nouveau matériel ni nouveau pilote ne peut être installé.",
                Reg.LmDel(DeviceInstall, "DenyRemovableDevices"), Reg.LmDword(DeviceInstall, "DenyUnspecified", 1))
            .WindowsDefault("allow")
            .Build();

        yield return Tweak.Toggle("devices.install.metadata", "Applications et icônes des fabricants",
                "Quand un périphérique est branché, Windows peut télécharger depuis Internet les icônes personnalisées et les informations " +
                "(métadonnées) fournies par son fabricant, ainsi que l'application compagnon associée. Bloqué, les pilotes s'installent " +
                "toujours, mais sans ces compléments.")
            .In(C, GroupInstall)
            .Keywords("metadonnees", "device metadata", "application fabricant", "icones peripheriques", "logiciel constructeur", "bloatware")
            .Tags("office", "kiosk")
            .Labels("Autorisées", "Bloquées")
            .WhenOn(Reg.LmDel(DeviceMetadata, "PreventDeviceMetadataFromNetwork"))
            .WhenOff(Reg.LmDword(DeviceMetadata, "PreventDeviceMetadataFromNetwork", 1))
            .WindowsDefault(TweakDefinition.On)
            .Build();
    }

    private static int? Dword(string key, string name) => RegistryAccess.ReadDword(RegHive.LocalMachine, key, name);
    private static string? Str(string key, string name) => RegistryAccess.ReadString(RegHive.LocalMachine, key, name);
}
