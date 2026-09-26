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
        ["Display"] = new("Display", L("Cartes graphiques"), "", 10),
        ["Monitor"] = new("Monitor", L("Écrans"), "", 11),
        ["MEDIA"] = new("MEDIA", L("Contrôleurs audio, vidéo et jeu"), "", 20),
        ["AudioEndpoint"] = new("AudioEndpoint", L("Entrées et sorties audio"), "", 21),
        ["Camera"] = new("Camera", L("Caméras"), "", 22),
        ["Image"] = new("Image", L("Périphériques d'image"), "", 23),
        ["Net"] = new("Net", L("Cartes réseau"), "", 30),
        ["Bluetooth"] = new("Bluetooth", "Bluetooth", "", 31),
        ["Keyboard"] = new("Keyboard", L("Claviers"), "", 40),
        ["Mouse"] = new("Mouse", L("Souris et pavés tactiles"), "", 41),
        ["HIDClass"] = new("HIDClass", L("Périphériques d'interface utilisateur (HID)"), "", 42),
        ["Biometric"] = new("Biometric", L("Biométrie (Windows Hello)"), "", 43),
        ["SmartCardReader"] = new("SmartCardReader", L("Lecteurs de cartes à puce"), "", 44),
        ["Printer"] = new("Printer", L("Imprimantes"), "", 50),
        ["PrintQueue"] = new("PrintQueue", L("Files d'impression"), "", 51),
        ["WPD"] = new("WPD", L("Appareils portables (téléphones, lecteurs)"), "", 52),
        ["USB"] = new("USB", L("Contrôleurs USB"), "", 60),
        ["USBDevice"] = new("USBDevice", L("Périphériques USB"), "", 61),
        ["Ports"] = new("Ports", L("Ports (COM et LPT)"), "", 62),
        ["DiskDrive"] = new("DiskDrive", L("Lecteurs de disque"), "", 70),
        ["CDROM"] = new("CDROM", L("Lecteurs de CD/DVD"), "", 71),
        ["SCSIAdapter"] = new("SCSIAdapter", L("Contrôleurs de stockage"), "", 72),
        ["HDC"] = new("HDC", L("Contrôleurs IDE/SATA"), "", 73),
        ["Volume"] = new("Volume", L("Volumes de stockage"), "", 74),
        ["VolumeSnapshot"] = new("VolumeSnapshot", L("Clichés instantanés de volume"), "", 75),
        ["SDHost"] = new("SDHost", L("Lecteurs de cartes mémoire"), "", 76),
        ["Sensor"] = new("Sensor", L("Capteurs"), "", 80),
        ["Battery"] = new("Battery", L("Batteries et alimentation"), "", 81),
        ["Processor"] = new("Processor", L("Processeurs"), "", 90),
        ["System"] = new("System", L("Périphériques système"), "", 91),
        ["Computer"] = new("Computer", L("Ordinateur"), "", 92),
        ["Firmware"] = new("Firmware", L("Micrologiciel"), "", 93),
        ["SecurityDevices"] = new("SecurityDevices", L("Périphériques de sécurité (TPM)"), "", 94),
        ["SoftwareDevice"] = new("SoftwareDevice", L("Périphériques logiciels"), "", 95),
        ["SoftwareComponent"] = new("SoftwareComponent", L("Composants logiciels"), "", 96),
        ["Extension"] = new("Extension", L("Extensions de pilotes"), "", 97),
    };

    private static readonly DeviceClassInfo Unknown = new("", L("Autres périphériques"), "", 200);

    public static DeviceClassInfo ClassInfo(string? pnpClass) =>
        string.IsNullOrWhiteSpace(pnpClass) ? Unknown
        : Classes.TryGetValue(pnpClass, out var info) ? info
        : new DeviceClassInfo(pnpClass, pnpClass, "", 150);

    // ------------------------------------------------------------------ Codes d'erreur

    /// <summary>Explication (et conseil) pour un code ConfigManagerErrorCode. Null pour 0 (fonctionne correctement).</summary>
    public static (string Summary, string Advice)? Problem(int code) => code switch
    {
        0 => null,
        1 => (L("Périphérique mal configuré (code 1)."), L("Mettez à jour le pilote depuis Windows Update ou le site du fabricant.")),
        3 => (L("Pilote endommagé ou mémoire insuffisante (code 3)."), L("Fermez des applications, puis réinstallez le pilote si le problème persiste.")),
        10 => (L("Le périphérique ne peut pas démarrer (code 10)."), L("Pilote inadapté ou matériel défaillant : réinstallez le pilote du fabricant, débranchez et rebranchez le périphérique.")),
        12 => (L("Ressources insuffisantes (code 12)."), L("Conflit de ressources avec un autre périphérique : désactivez un périphérique inutilisé ou mettez à jour le BIOS/UEFI.")),
        14 => (L("Redémarrage nécessaire (code 14)."), L("Redémarrez le PC pour que ce périphérique fonctionne.")),
        16 => (L("Ressources non identifiées (code 16)."), L("Mettez à jour le pilote ; contactez le fabricant si le problème persiste.")),
        18 => (L("Pilotes à réinstaller (code 18)."), L("Réinstallez le pilote (Windows Update, section Mises à jour facultatives, ou site du fabricant).")),
        19 => (L("Configuration du registre incomplète ou endommagée (code 19)."), L("Désinstallez le périphérique depuis le Gestionnaire de périphériques puis redémarrez.")),
        21 => (L("Suppression en cours (code 21)."), L("Windows retire ce périphérique : patientez quelques secondes puis actualisez.")),
        22 => (L("Périphérique désactivé (code 22)."), L("Il a été désactivé volontairement : réactivez-le si vous en avez besoin.")),
        24 => (L("Périphérique absent ou pilote incomplet (code 24)."), L("Vérifiez le branchement ; réinstallez le pilote si le périphérique est bien connecté.")),
        28 => (L("Aucun pilote installé (code 28)."), L("Installez le pilote : Windows Update (Mises à jour facultatives) ou site du fabricant du PC.")),
        29 => (L("Désactivé par le micrologiciel (code 29)."), L("Ce périphérique est désactivé dans le BIOS/UEFI : il faut le réactiver dans l'utilitaire de configuration du PC.")),
        31 => (L("Pilote impossible à charger (code 31)."), L("Réinstallez ou mettez à jour le pilote.")),
        32 => (L("Service du pilote désactivé (code 32)."), L("Le service de ce pilote est désactivé : réinstallez le pilote pour le rétablir.")),
        33 or 34 or 35 or 36 => (L("Problème de ressources matérielles (code {0}).", code), L("Mettez à jour le BIOS/UEFI du PC ou contactez le fabricant.")),
        37 => (L("Échec d'initialisation du pilote (code 37)."), L("Réinstallez le pilote du fabricant.")),
        38 => (L("Ancienne instance du pilote encore en mémoire (code 38)."), L("Redémarrez le PC.")),
        39 => (L("Pilote endommagé ou manquant (code 39)."), L("Réinstallez le pilote ; si le problème persiste, un logiciel (antivirus, filtre) peut être en cause.")),
        40 => (L("Informations du service de pilote invalides (code 40)."), L("Réinstallez le pilote.")),
        41 => (L("Pilote chargé mais matériel introuvable (code 41)."), L("Débranchez et rebranchez le périphérique, ou réinstallez le pilote.")),
        42 => (L("Périphérique en double détecté (code 42)."), L("Redémarrez le PC.")),
        43 => (L("Arrêté car il a signalé des problèmes (code 43)."), L("Souvent une panne matérielle ou un pilote défaillant : mettez à jour le pilote, testez un autre port USB.")),
        44 => (L("Arrêté par une application ou un service (code 44)."), L("Redémarrez le PC.")),
        45 => (L("Périphérique non connecté (code 45)."), L("Il a déjà été branché mais ne l'est plus : rien à faire s'il est débranché volontairement.")),
        46 => (L("Arrêt de Windows en cours (code 46)."), L("Le périphérique sera disponible au prochain démarrage.")),
        47 => (L("Préparé pour un retrait sécurisé (code 47)."), L("Débranchez-le puis rebranchez-le.")),
        48 => (L("Pilote bloqué (code 48)."), L("Ce pilote est incompatible avec cette version de Windows : installez une version récente du fabricant.")),
        49 => (L("Registre système trop volumineux (code 49)."), L("Désinstallez les périphériques qui ne sont plus utilisés, puis redémarrez.")),
        50 => (L("Propriétés non appliquées (code 50)."), L("Redémarrez le PC.")),
        51 => (L("En attente d'un autre périphérique (code 51)."), L("Il démarrera quand le périphérique dont il dépend sera prêt.")),
        52 => (L("Signature du pilote non vérifiée (code 52)."), L("Installez un pilote signé depuis Windows Update ou le site du fabricant.")),
        53 => (L("Réservé au débogueur du noyau (code 53)."), L("Désactivez le débogage du noyau si ce n'est pas volontaire.")),
        54 => (L("Échec, réinitialisation en cours (code 54)."), L("Patientez puis actualisez ; redémarrez si le problème persiste.")),
        _ => (L("Problème signalé par Windows (code {0}).", code), L("Consultez les propriétés du périphérique dans le Gestionnaire de périphériques.")),
    };

    /// <summary>Code « normal » à ne pas compter comme une panne (désactivation volontaire, périphérique débranché).</summary>
    public static bool IsBenign(int code) => code is 0 or 22 or 45;

    // ------------------------------------------------------------------ Protection

    /// <summary>Classes que Timonier refuse de désactiver, avec la raison affichée à l'utilisateur.</summary>
    public static readonly IReadOnlyList<(string Class, string Title, string Reason)> ProtectedClasses =
    [
        ("System", L("Périphériques système"), L("Bus, horloges, contrôleurs d'interruptions, ACPI : les désactiver peut empêcher Windows de démarrer.")),
        ("Computer", L("Ordinateur"), L("Représente la carte mère elle-même.")),
        ("Processor", L("Processeurs"), L("Indispensables au fonctionnement du PC.")),
        ("SCSIAdapter", L("Contrôleurs de stockage"), L("Le disque système (NVMe, RAID…) en dépend : le PC ne démarrerait plus.")),
        ("HDC", L("Contrôleurs IDE/SATA"), L("Le disque système peut en dépendre : le PC ne démarrerait plus.")),
        ("DiskDrive", L("Disques internes et disque système"), L("Seuls les disques USB externes qui ne contiennent pas Windows peuvent être désactivés.")),
        ("Volume", L("Volumes de stockage"), L("Partitions utilisées par Windows ; utilisez plutôt la gestion des disques.")),
        ("VolumeSnapshot", L("Clichés instantanés"), L("Utilisés par la restauration du système et les sauvegardes.")),
        ("SecurityDevices", L("TPM et sécurité"), L("Le TPM protège BitLocker, Windows Hello et le démarrage sécurisé.")),
        ("Firmware", L("Micrologiciel"), L("Composants du BIOS/UEFI : risque de dysfonctionnement au démarrage.")),
        ("Battery", L("Batteries et adaptateur secteur"), L("Windows ne saurait plus gérer la charge ni l'arrêt en cas de batterie faible.")),
        ("SoftwareDevice", L("Périphériques logiciels"), L("Composants virtuels gérés par Windows lui-même.")),
        ("SoftwareComponent", L("Composants logiciels"), L("Modules logiciels rattachés aux pilotes, sans matériel propre.")),
        ("Extension", L("Extensions de pilotes"), L("Compléments de configuration, sans matériel propre.")),
        ("LegacyDriver", L("Pilotes hérités"), L("Pilotes non Plug-and-Play : à gérer comme des services.")),
    ];

    /// <summary>Classes dont la désactivation est confirmée par le processus administrateur (risque de perdre le contrôle du PC).</summary>
    public static readonly IReadOnlyList<(string Class, string Why)> SensitiveClasses =
    [
        ("Keyboard", L("le clavier ne répondra plus")),
        ("Mouse", L("la souris ou le pavé tactile ne répondra plus")),
        ("HIDClass", L("claviers, souris, écrans tactiles ou boutons peuvent en dépendre")),
        ("Display", L("l'affichage passera sur le pilote de base de Windows (basse résolution, sans accélération)")),
        ("Net", L("la connexion réseau ou Internet sera coupée")),
        ("USB", L("tous les périphériques branchés sur ce contrôleur ou ce concentrateur seront coupés, y compris clavier et souris")),
        ("Bluetooth", L("les claviers, souris et écouteurs Bluetooth seront déconnectés")),
        ("Biometric", L("la connexion par Windows Hello (empreinte, visage) ne fonctionnera plus")),
        ("SmartCardReader", L("la connexion par carte à puce ne fonctionnera plus")),
        ("SDHost", L("les cartes mémoire ne seront plus lues et, sur les PC à stockage eMMC, le disque interne peut en dépendre")),
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
                : (DeviceProtection.Protected, L("Disque interne : Timonier ne désactive que les disques USB externes."));
        }
        foreach (var (c, _, reason) in ProtectedClasses)
            if (c.Equals(cls, StringComparison.OrdinalIgnoreCase)) return (DeviceProtection.Protected, reason);
        // Pilotes ACPI de batterie / adaptateur secteur parfois rangés hors de la classe Battery.
        if (instanceId.StartsWith(@"ACPI\PNP0C0A", StringComparison.OrdinalIgnoreCase) ||
            instanceId.StartsWith(@"ACPI\ACPI0003", StringComparison.OrdinalIgnoreCase))
            return (DeviceProtection.Protected, L("Batterie ou adaptateur secteur : Windows doit pouvoir gérer l'alimentation."));
        foreach (var (c, why) in SensitiveClasses)
            if (c.Equals(cls, StringComparison.OrdinalIgnoreCase)) return (DeviceProtection.Sensitive, L("Attention : {0}.", why));
        if (cls.Length == 0)
            return (DeviceProtection.Sensitive, L("Classe inconnue : Timonier ne peut pas évaluer le rôle de ce périphérique."));
        return (DeviceProtection.None, null);
    }
}
