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
        ["Display"] = new("Display", "Cartes graphiques", "", 10),
        ["Monitor"] = new("Monitor", "Écrans", "", 11),
        ["MEDIA"] = new("MEDIA", "Contrôleurs audio, vidéo et jeu", "", 20),
        ["AudioEndpoint"] = new("AudioEndpoint", "Entrées et sorties audio", "", 21),
        ["Camera"] = new("Camera", "Caméras", "", 22),
        ["Image"] = new("Image", "Périphériques d'image", "", 23),
        ["Net"] = new("Net", "Cartes réseau", "", 30),
        ["Bluetooth"] = new("Bluetooth", "Bluetooth", "", 31),
        ["Keyboard"] = new("Keyboard", "Claviers", "", 40),
        ["Mouse"] = new("Mouse", "Souris et pavés tactiles", "", 41),
        ["HIDClass"] = new("HIDClass", "Périphériques d'interface utilisateur (HID)", "", 42),
        ["Biometric"] = new("Biometric", "Biométrie (Windows Hello)", "", 43),
        ["SmartCardReader"] = new("SmartCardReader", "Lecteurs de cartes à puce", "", 44),
        ["Printer"] = new("Printer", "Imprimantes", "", 50),
        ["PrintQueue"] = new("PrintQueue", "Files d'impression", "", 51),
        ["WPD"] = new("WPD", "Appareils portables (téléphones, lecteurs)", "", 52),
        ["USB"] = new("USB", "Contrôleurs USB", "", 60),
        ["USBDevice"] = new("USBDevice", "Périphériques USB", "", 61),
        ["Ports"] = new("Ports", "Ports (COM et LPT)", "", 62),
        ["DiskDrive"] = new("DiskDrive", "Lecteurs de disque", "", 70),
        ["CDROM"] = new("CDROM", "Lecteurs de CD/DVD", "", 71),
        ["SCSIAdapter"] = new("SCSIAdapter", "Contrôleurs de stockage", "", 72),
        ["HDC"] = new("HDC", "Contrôleurs IDE/SATA", "", 73),
        ["Volume"] = new("Volume", "Volumes de stockage", "", 74),
        ["VolumeSnapshot"] = new("VolumeSnapshot", "Clichés instantanés de volume", "", 75),
        ["SDHost"] = new("SDHost", "Lecteurs de cartes mémoire", "", 76),
        ["Sensor"] = new("Sensor", "Capteurs", "", 80),
        ["Battery"] = new("Battery", "Batteries et alimentation", "", 81),
        ["Processor"] = new("Processor", "Processeurs", "", 90),
        ["System"] = new("System", "Périphériques système", "", 91),
        ["Computer"] = new("Computer", "Ordinateur", "", 92),
        ["Firmware"] = new("Firmware", "Micrologiciel", "", 93),
        ["SecurityDevices"] = new("SecurityDevices", "Périphériques de sécurité (TPM)", "", 94),
        ["SoftwareDevice"] = new("SoftwareDevice", "Périphériques logiciels", "", 95),
        ["SoftwareComponent"] = new("SoftwareComponent", "Composants logiciels", "", 96),
        ["Extension"] = new("Extension", "Extensions de pilotes", "", 97),
    };

    private static readonly DeviceClassInfo Unknown = new("", "Autres périphériques", "", 200);

    public static DeviceClassInfo ClassInfo(string? pnpClass) =>
        string.IsNullOrWhiteSpace(pnpClass) ? Unknown
        : Classes.TryGetValue(pnpClass, out var info) ? info
        : new DeviceClassInfo(pnpClass, pnpClass, "", 150);

    // ------------------------------------------------------------------ Codes d'erreur

    /// <summary>Explication (et conseil) pour un code ConfigManagerErrorCode. Null pour 0 (fonctionne correctement).</summary>
    public static (string Summary, string Advice)? Problem(int code) => code switch
    {
        0 => null,
        1 => ("Périphérique mal configuré (code 1).", "Mettez à jour le pilote depuis Windows Update ou le site du fabricant."),
        3 => ("Pilote endommagé ou mémoire insuffisante (code 3).", "Fermez des applications, puis réinstallez le pilote si le problème persiste."),
        10 => ("Le périphérique ne peut pas démarrer (code 10).", "Pilote inadapté ou matériel défaillant : réinstallez le pilote du fabricant, débranchez et rebranchez le périphérique."),
        12 => ("Ressources insuffisantes (code 12).", "Conflit de ressources avec un autre périphérique : désactivez un périphérique inutilisé ou mettez à jour le BIOS/UEFI."),
        14 => ("Redémarrage nécessaire (code 14).", "Redémarrez le PC pour que ce périphérique fonctionne."),
        16 => ("Ressources non identifiées (code 16).", "Mettez à jour le pilote ; contactez le fabricant si le problème persiste."),
        18 => ("Pilotes à réinstaller (code 18).", "Réinstallez le pilote (Windows Update, section Mises à jour facultatives, ou site du fabricant)."),
        19 => ("Configuration du registre incomplète ou endommagée (code 19).", "Désinstallez le périphérique depuis le Gestionnaire de périphériques puis redémarrez."),
        21 => ("Suppression en cours (code 21).", "Windows retire ce périphérique : patientez quelques secondes puis actualisez."),
        22 => ("Périphérique désactivé (code 22).", "Il a été désactivé volontairement : réactivez-le si vous en avez besoin."),
        24 => ("Périphérique absent ou pilote incomplet (code 24).", "Vérifiez le branchement ; réinstallez le pilote si le périphérique est bien connecté."),
        28 => ("Aucun pilote installé (code 28).", "Installez le pilote : Windows Update (Mises à jour facultatives) ou site du fabricant du PC."),
        29 => ("Désactivé par le micrologiciel (code 29).", "Ce périphérique est désactivé dans le BIOS/UEFI : il faut le réactiver dans l'utilitaire de configuration du PC."),
        31 => ("Pilote impossible à charger (code 31).", "Réinstallez ou mettez à jour le pilote."),
        32 => ("Service du pilote désactivé (code 32).", "Le service de ce pilote est désactivé : réinstallez le pilote pour le rétablir."),
        33 or 34 or 35 or 36 => ($"Problème de ressources matérielles (code {code}).", "Mettez à jour le BIOS/UEFI du PC ou contactez le fabricant."),
        37 => ("Échec d'initialisation du pilote (code 37).", "Réinstallez le pilote du fabricant."),
        38 => ("Ancienne instance du pilote encore en mémoire (code 38).", "Redémarrez le PC."),
        39 => ("Pilote endommagé ou manquant (code 39).", "Réinstallez le pilote ; si le problème persiste, un logiciel (antivirus, filtre) peut être en cause."),
        40 => ("Informations du service de pilote invalides (code 40).", "Réinstallez le pilote."),
        41 => ("Pilote chargé mais matériel introuvable (code 41).", "Débranchez et rebranchez le périphérique, ou réinstallez le pilote."),
        42 => ("Périphérique en double détecté (code 42).", "Redémarrez le PC."),
        43 => ("Arrêté car il a signalé des problèmes (code 43).", "Souvent une panne matérielle ou un pilote défaillant : mettez à jour le pilote, testez un autre port USB."),
        44 => ("Arrêté par une application ou un service (code 44).", "Redémarrez le PC."),
        45 => ("Périphérique non connecté (code 45).", "Il a déjà été branché mais ne l'est plus : rien à faire s'il est débranché volontairement."),
        46 => ("Arrêt de Windows en cours (code 46).", "Le périphérique sera disponible au prochain démarrage."),
        47 => ("Préparé pour un retrait sécurisé (code 47).", "Débranchez-le puis rebranchez-le."),
        48 => ("Pilote bloqué (code 48).", "Ce pilote est incompatible avec cette version de Windows : installez une version récente du fabricant."),
        49 => ("Registre système trop volumineux (code 49).", "Désinstallez les périphériques qui ne sont plus utilisés, puis redémarrez."),
        50 => ("Propriétés non appliquées (code 50).", "Redémarrez le PC."),
        51 => ("En attente d'un autre périphérique (code 51).", "Il démarrera quand le périphérique dont il dépend sera prêt."),
        52 => ("Signature du pilote non vérifiée (code 52).", "Installez un pilote signé depuis Windows Update ou le site du fabricant."),
        53 => ("Réservé au débogueur du noyau (code 53).", "Désactivez le débogage du noyau si ce n'est pas volontaire."),
        54 => ("Échec, réinitialisation en cours (code 54).", "Patientez puis actualisez ; redémarrez si le problème persiste."),
        _ => ($"Problème signalé par Windows (code {code}).", "Consultez les propriétés du périphérique dans le Gestionnaire de périphériques."),
    };

    /// <summary>Code « normal » à ne pas compter comme une panne (désactivation volontaire, périphérique débranché).</summary>
    public static bool IsBenign(int code) => code is 0 or 22 or 45;

    // ------------------------------------------------------------------ Protection

    /// <summary>Classes que Timonier refuse de désactiver, avec la raison affichée à l'utilisateur.</summary>
    public static readonly IReadOnlyList<(string Class, string Title, string Reason)> ProtectedClasses =
    [
        ("System", "Périphériques système", "Bus, horloges, contrôleurs d'interruptions, ACPI : les désactiver peut empêcher Windows de démarrer."),
        ("Computer", "Ordinateur", "Représente la carte mère elle-même."),
        ("Processor", "Processeurs", "Indispensables au fonctionnement du PC."),
        ("SCSIAdapter", "Contrôleurs de stockage", "Le disque système (NVMe, RAID…) en dépend : le PC ne démarrerait plus."),
        ("HDC", "Contrôleurs IDE/SATA", "Le disque système peut en dépendre : le PC ne démarrerait plus."),
        ("DiskDrive", "Disques internes et disque système", "Seuls les disques USB externes qui ne contiennent pas Windows peuvent être désactivés."),
        ("Volume", "Volumes de stockage", "Partitions utilisées par Windows ; utilisez plutôt la gestion des disques."),
        ("VolumeSnapshot", "Clichés instantanés", "Utilisés par la restauration du système et les sauvegardes."),
        ("SecurityDevices", "TPM et sécurité", "Le TPM protège BitLocker, Windows Hello et le démarrage sécurisé."),
        ("Firmware", "Micrologiciel", "Composants du BIOS/UEFI : risque de dysfonctionnement au démarrage."),
        ("Battery", "Batteries et adaptateur secteur", "Windows ne saurait plus gérer la charge ni l'arrêt en cas de batterie faible."),
        ("SoftwareDevice", "Périphériques logiciels", "Composants virtuels gérés par Windows lui-même."),
        ("SoftwareComponent", "Composants logiciels", "Modules logiciels rattachés aux pilotes, sans matériel propre."),
        ("Extension", "Extensions de pilotes", "Compléments de configuration, sans matériel propre."),
        ("LegacyDriver", "Pilotes hérités", "Pilotes non Plug-and-Play : à gérer comme des services."),
    ];

    /// <summary>Classes dont la désactivation est confirmée par le processus administrateur (risque de perdre le contrôle du PC).</summary>
    public static readonly IReadOnlyList<(string Class, string Why)> SensitiveClasses =
    [
        ("Keyboard", "le clavier ne répondra plus"),
        ("Mouse", "la souris ou le pavé tactile ne répondra plus"),
        ("HIDClass", "claviers, souris, écrans tactiles ou boutons peuvent en dépendre"),
        ("Display", "l'affichage passera sur le pilote de base de Windows (basse résolution, sans accélération)"),
        ("Net", "la connexion réseau ou Internet sera coupée"),
        ("USB", "tous les périphériques branchés sur ce contrôleur ou ce concentrateur seront coupés, y compris clavier et souris"),
        ("Bluetooth", "les claviers, souris et écouteurs Bluetooth seront déconnectés"),
        ("Biometric", "la connexion par Windows Hello (empreinte, visage) ne fonctionnera plus"),
        ("SmartCardReader", "la connexion par carte à puce ne fonctionnera plus"),
        ("SDHost", "les cartes mémoire ne seront plus lues et, sur les PC à stockage eMMC, le disque interne peut en dépendre"),
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
                : (DeviceProtection.Protected, "Disque interne : Timonier ne désactive que les disques USB externes.");
        }
        foreach (var (c, _, reason) in ProtectedClasses)
            if (c.Equals(cls, StringComparison.OrdinalIgnoreCase)) return (DeviceProtection.Protected, reason);
        // Pilotes ACPI de batterie / adaptateur secteur parfois rangés hors de la classe Battery.
        if (instanceId.StartsWith(@"ACPI\PNP0C0A", StringComparison.OrdinalIgnoreCase) ||
            instanceId.StartsWith(@"ACPI\ACPI0003", StringComparison.OrdinalIgnoreCase))
            return (DeviceProtection.Protected, "Batterie ou adaptateur secteur : Windows doit pouvoir gérer l'alimentation.");
        foreach (var (c, why) in SensitiveClasses)
            if (c.Equals(cls, StringComparison.OrdinalIgnoreCase)) return (DeviceProtection.Sensitive, "Attention : " + why + ".");
        if (cls.Length == 0)
            return (DeviceProtection.Sensitive, "Classe inconnue : Timonier ne peut pas évaluer le rôle de ce périphérique.");
        return (DeviceProtection.None, null);
    }
}
