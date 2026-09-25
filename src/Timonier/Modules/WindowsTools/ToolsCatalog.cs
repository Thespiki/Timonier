using Timonier.Core.Platform;

namespace Timonier.Modules.WindowsTools;

internal enum ToolGroup { Admin, Diagnostic, System, ControlPanel, Folders, Accessories }

/// <summary>Outil Windows lancé avec des arguments constants.</summary>
internal sealed class WinTool
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required ToolGroup Group { get; init; }
    public required string Glyph { get; init; }
    /// <summary>Commande affichée à titre indicatif (ex. « devmgmt.msc »).</summary>
    public required string Command { get; init; }
    public required Action Launch { get; init; }
    public required Func<bool> IsAvailable { get; init; }
    public string[] Keywords { get; init; } = [];
    /// <summary>Demande les droits d'administrateur (invite UAC) à l'ouverture ou pour modifier quoi que ce soit.</summary>
    public bool Admin { get; init; }
    /// <summary>Uniquement sur les éditions Professionnel, Éducation et Entreprise.</summary>
    public bool ProOnly { get; init; }
    /// <summary>Faux si un autre module indexe déjà cet outil dans la recherche (évite les doublons).</summary>
    public bool InSearch { get; init; } = true;

    public string Id => "wintools.tool." + Key;
}

/// <summary>Catalogue des outils d'administration, panneaux classiques et dossiers spéciaux.</summary>
internal static class ToolsCatalog
{
    public static readonly (ToolGroup Group, string Title, string Glyph)[] Groups =
    [
        (ToolGroup.Admin, "Consoles d'administration", ""),
        (ToolGroup.Diagnostic, "Diagnostic", ""),
        (ToolGroup.System, "Système", ""),
        (ToolGroup.ControlPanel, "Panneau de configuration", ""),
        (ToolGroup.Folders, "Dossiers spéciaux", ""),
        (ToolGroup.Accessories, "Accessoires", ""),
    ];

    public static string GroupTitle(ToolGroup g) => Array.Find(Groups, x => x.Group == g).Title;

    private static readonly Lazy<IReadOnlyList<WinTool>> AvailableTools = new(() =>
        [.. All().Where(t => (!t.ProOnly || WinInfo.IsProOrHigher) && SafeAvailable(t))]);

    /// <summary>Outils présents sur ce PC et adaptés à l'édition (calculé une seule fois, simples tests d'existence de fichiers).</summary>
    public static IReadOnlyList<WinTool> Available => AvailableTools.Value;

    private static bool SafeAvailable(WinTool t)
    {
        try { return t.IsAvailable(); }
        catch { return false; }
    }

    private static IEnumerable<WinTool> All()
    {
        // ---------------------------------------------------------------- Consoles MMC
        yield return Msc("devmgmt", "devmgmt.msc", "Gestionnaire de périphériques",
            "Matériel détecté, pilotes, périphériques en erreur ou désactivés.", "",
            "gestionnaire de peripheriques", "device manager", "pilotes", "drivers", "materiel");
        yield return Msc("diskmgmt", "diskmgmt.msc", "Gestion des disques",
            "Partitions, lettres de lecteur, formatage, extension ou réduction de volumes.", "",
            "partition", "partitionner", "formater", "lettre de lecteur", "disk management", "volume");
        yield return Msc("compmgmt", "compmgmt.msc", "Gestion de l'ordinateur",
            "Console qui regroupe journaux, dossiers partagés, utilisateurs, disques et services.", "",
            "computer management", "gestion ordinateur");
        yield return Msc("eventvwr", "eventvwr.msc", "Observateur d'événements",
            "Journaux d'erreurs et d'avertissements de Windows et des applications.", "",
            "event viewer", "journaux", "logs", "erreurs windows", "evenements");
        yield return Msc("services", "services.msc", "Services",
            "Console classique de tous les services Windows (démarrage, état, compte).", "",
            inSearch: false, keywords: ["services windows"]);
        yield return Msc("taskschd", "taskschd.msc", "Planificateur de tâches",
            "Tâches planifiées de Windows et des applications.", "",
            inSearch: false, keywords: ["task scheduler"]);
        yield return Msc("wf", "wf.msc", "Pare-feu avec fonctions avancées",
            "Règles entrantes et sortantes du Pare-feu Windows Defender, profils, journalisation.", "",
            inSearch: false, keywords: ["firewall", "regles pare-feu"]);
        yield return Msc("certmgr", "certmgr.msc", "Certificats (utilisateur actuel)",
            "Certificats personnels et autorités de confiance de votre compte.", "",
            "certificat", "certificate", "autorite de certification");
        yield return Msc("certlm", "certlm.msc", "Certificats (ordinateur local)",
            "Certificats de l'ordinateur, utilisés par les services et tous les comptes.", "",
            "certificat machine", "certificate", "local machine");
        yield return Msc("fsmgmt", "fsmgmt.msc", "Dossiers partagés",
            "Partages de ce PC, sessions ouvertes et fichiers utilisés à distance.", "",
            "partages", "shared folders", "smb", "partage reseau");
        yield return Msc("comexp", "comexp.msc", "Services de composants",
            "Applications COM+, configuration DCOM et coordinateur de transactions distribuées.", "",
            "com+", "dcom", "dcomcnfg", "component services");
        yield return Msc("tpm", "tpm.msc", "Gestion du module TPM",
            "État, fabricant et version de la puce de sécurité TPM.", "",
            "tpm", "puce de securite", "trusted platform module");
        yield return Msc("gpedit", "gpedit.msc", "Éditeur de stratégie de groupe locale",
            "Stratégies de l'ordinateur et de l'utilisateur (modèles d'administration).", "",
            proOnly: true, keywords: ["gpedit", "group policy", "strategie de groupe", "gpo"]);
        yield return Msc("secpol", "secpol.msc", "Stratégie de sécurité locale",
            "Stratégies de mot de passe, d'audit, droits des utilisateurs, options de sécurité.", "",
            proOnly: true, keywords: ["secpol", "local security policy", "politique de securite"]);
        yield return Msc("lusrmgr", "lusrmgr.msc", "Utilisateurs et groupes locaux",
            "Comptes locaux, groupes (Administrateurs, Utilisateurs…) et leurs membres.", "",
            proOnly: true, inSearch: false, keywords: ["lusrmgr", "local users and groups", "groupes", "comptes locaux"]);
        yield return Msc("printmanagement", "printmanagement.msc", "Gestion de l'impression",
            "Imprimantes, pilotes d'impression, files d'attente et ports.", "",
            proOnly: true, keywords: ["print management", "pilotes imprimante", "file d'attente"]);

        // ---------------------------------------------------------------- Diagnostic
        yield return Exe("msinfo32", SystemTool.Msinfo32, "msinfo32", "Informations système",
            "Inventaire complet : matériel, pilotes, BIOS, démarrage sécurisé, composants.", ToolGroup.Diagnostic, "",
            keywords: ["msinfo32", "msinfo", "system information", "configuration materielle", "bios"]);
        yield return Exe("resmon", SystemTool.Resmon, "resmon", "Moniteur de ressources",
            "Processeur, mémoire, disque et réseau par processus, en temps réel.", ToolGroup.Diagnostic, "",
            keywords: ["resource monitor", "resmon", "utilisation disque", "activite reseau"]);
        yield return Exe("perfmon", SystemTool.Perfmon, "perfmon", "Analyseur de performances",
            "Compteurs de performances et ensembles de collecteurs de données.", ToolGroup.Diagnostic, "",
            admin: true, keywords: ["performance monitor", "perfmon", "compteurs"]);
        yield return Exe("reliability", SystemTool.Perfmon, "perfmon /rel", "Moniteur de fiabilité",
            "Historique jour par jour des plantages, erreurs et installations.", ToolGroup.Diagnostic, "",
            args: ["/rel"], keywords: ["reliability monitor", "historique de fiabilite", "plantages", "crash"]);
        yield return Exe("taskmgr", SystemTool.Taskmgr, "taskmgr", "Gestionnaire des tâches",
            "Processus, performances, applications au démarrage et services.", ToolGroup.Diagnostic, "",
            keywords: ["task manager", "processus", "ctrl maj echap"]);
        yield return Exe("dxdiag", SystemTool.Dxdiag, "dxdiag", "Outil de diagnostic DirectX",
            "Carte graphique, pilotes, son et fonctionnalités DirectX.", ToolGroup.Diagnostic, "",
            keywords: ["directx", "dxdiag", "carte graphique", "diagnostic graphique"]);
        yield return Exe("mdsched", SystemTool.Mdsched, "mdsched", "Diagnostic de la mémoire Windows",
            "Teste la mémoire vive au prochain redémarrage (vous choisissez quand redémarrer).", ToolGroup.Diagnostic, "",
            admin: true, keywords: ["test memoire", "test ram", "memory diagnostic", "mdsched"]);

        // ---------------------------------------------------------------- Système
        yield return Exe("msconfig", SystemTool.Msconfig, "msconfig", "Configuration du système",
            "Démarrage sélectif, options de démarrage (mode sans échec), services.", ToolGroup.System, "",
            admin: true, keywords: ["msconfig", "mode sans echec", "safe mode", "demarrage selectif", "boot"]);
        yield return Exe("regedit", SystemTool.Regedit, "regedit", "Éditeur du Registre",
            "Modification avancée du Registre : une erreur peut empêcher Windows de démarrer.", ToolGroup.System, "",
            admin: true, keywords: ["regedit", "registre", "registry", "base de registre"]);
        yield return Exe("sysadvanced", SystemTool.SystemPropertiesAdvanced, "SystemPropertiesAdvanced", "Paramètres système avancés",
            "Variables d'environnement, profils utilisateur, démarrage et récupération.", ToolGroup.System, "",
            admin: true, keywords: ["variables d'environnement", "environment variables", "path", "proprietes systeme", "sysdm"]);
        yield return Exe("sysperformance", SystemTool.SystemPropertiesPerformance, "SystemPropertiesPerformance", "Options de performances",
            "Effets visuels, mémoire virtuelle (fichier d'échange), prévention de l'exécution des données.", ToolGroup.System, "",
            admin: true, inSearch: false, keywords: ["memoire virtuelle", "fichier d'echange", "pagefile", "virtual memory", "effets visuels"]);
        yield return Exe("sysprotection", SystemTool.SystemPropertiesProtection, "SystemPropertiesProtection", "Protection du système",
            "Activer la protection, créer ou configurer les points de restauration.", ToolGroup.System, "",
            admin: true, keywords: ["point de restauration", "restore point", "protection du systeme", "creer un point"]);
        yield return Exe("rstrui", SystemTool.Rstrui, "rstrui", "Restauration du système",
            "Revenir à un point de restauration antérieur (vos fichiers ne sont pas touchés).", ToolGroup.System, "",
            admin: true, inSearch: false, keywords: ["restauration systeme", "system restore", "rstrui", "revenir en arriere"]);
        yield return Sys32("computername", "SystemPropertiesComputerName.exe", "Nom de l'ordinateur et domaine",
            "Renommer le PC, rejoindre un groupe de travail ou un domaine.", ToolGroup.System, "",
            admin: true, keywords: ["nom du pc", "renommer le pc", "groupe de travail", "workgroup", "domaine"]);
        yield return Sys32("sysremote", "SystemPropertiesRemote.exe", "Utilisation à distance",
            "Assistance à distance et Bureau à distance (Propriétés système).", ToolGroup.System, "",
            admin: true, keywords: ["assistance a distance", "remote assistance", "bureau a distance", "rdp"]);
        yield return Exe("netplwiz", SystemTool.Netplwiz, "netplwiz", "Comptes d'utilisateurs (avancé)",
            "Liste des comptes, appartenance aux groupes, gestion avancée des mots de passe.", ToolGroup.System, "",
            admin: true, inSearch: false, keywords: ["netplwiz", "control userpasswords2", "comptes utilisateurs"]);
        yield return Exe("cleanmgr", SystemTool.CleanMgr, "cleanmgr", "Nettoyage de disque",
            "Fichiers temporaires, miniatures, anciennes mises à jour (« Nettoyer les fichiers système »).", ToolGroup.System, "",
            inSearch: false, keywords: ["cleanmgr", "nettoyage de disque", "disk cleanup", "liberer de l'espace"]);
        yield return Sys32("dfrgui", "dfrgui.exe", "Optimiser les lecteurs",
            "Défragmentation des disques durs, TRIM des SSD et planification.", ToolGroup.System, "",
            admin: true, keywords: ["defragmenter", "defragmentation", "defrag", "trim", "optimiser les lecteurs"]);
        yield return Sys32("optionalfeatures", "optionalfeatures.exe", "Fonctionnalités de Windows",
            "Activer ou désactiver .NET 3.5, Hyper-V, WSL, Sandbox, clients SMB…", ToolGroup.System, "",
            admin: true, keywords: ["fonctionnalites windows", "windows features", "hyper-v", "wsl", "sandbox", "net framework 3.5"]);
        yield return Sys32("recoverydrive", "RecoveryDrive.exe", "Créer un lecteur de récupération",
            "Clé USB de secours pour réparer ou réinstaller Windows.", ToolGroup.System, "",
            admin: true, keywords: ["lecteur de recuperation", "recovery drive", "cle de secours", "cle usb de reparation"]);
        yield return Exe("winver", SystemTool.Winver, "winver", "À propos de Windows",
            "Version, build exacte et édition de Windows installées.", ToolGroup.System, "",
            keywords: ["winver", "version de windows", "build", "numero de version"]);

        // ---------------------------------------------------------------- Panneau de configuration
        yield return Cpl("programs", "Microsoft.ProgramsAndFeatures", "Programmes et fonctionnalités",
            "Liste classique des programmes installés : désinstaller, modifier, réparer.", "",
            "appwiz", "ajout suppression de programmes", "desinstaller un programme");
        yield return Cpl("power", "Microsoft.PowerOptions", "Options d'alimentation",
            "Modes de gestion de l'alimentation classiques et paramètres avancés.", "",
            inSearch: false, keywords: ["powercfg.cpl", "mode de gestion de l'alimentation", "parametres d'alimentation avances"]);
        yield return Cpl("netcenter", "Microsoft.NetworkAndSharingCenter", "Centre Réseau et partage",
            "État des connexions, paramètres de partage avancés, propriétés des cartes.", "",
            "network and sharing center", "partage reseau", "centre reseau");
        yield return new WinTool
        {
            Key = "ncpa", Title = "Connexions réseau", Group = ToolGroup.ControlPanel, Glyph = "", Command = "ncpa.cpl",
            Description = "Cartes réseau : propriétés IPv4/IPv6, activation, diagnostic.",
            Launch = () => ToolLauncher.Launch(SystemTool.Control, "ncpa.cpl"),
            IsAvailable = () => SystemTools.IsAvailable(SystemTool.Control), InSearch = false,
        };
        yield return Cpl("devprinters", "Microsoft.DevicesAndPrinters", "Périphériques et imprimantes",
            "Vue classique des appareils (peut s'ouvrir dans Paramètres sur les versions récentes).", "",
            "devices and printers", "imprimantes");
        yield return Cpl("useraccounts", "Microsoft.UserAccounts", "Comptes d'utilisateurs",
            "Type de compte, contrôle de compte d'utilisateur, variables d'environnement du compte.", "",
            "user accounts", "comptes");
        yield return Cpl("credentials", "Microsoft.CredentialManager", "Gestionnaire d'identification",
            "Identifiants Windows et Web enregistrés (partages réseau, applications).", "",
            "credential manager", "mots de passe enregistres", "identifiants", "coffre");
        yield return Cpl("sound", "Microsoft.Sound", "Son (panneau classique)",
            "Périphériques de lecture et d'enregistrement, formats, sons système.", "",
            "mmsys.cpl", "peripherique de lecture", "enregistrement", "sons systeme");
        yield return Cpl("mouse", "Microsoft.Mouse", "Propriétés de la souris",
            "Boutons, schémas de pointeurs, vitesse, roulette.", "",
            "main.cpl", "pointeurs", "double clic", "roulette");
        yield return Cpl("keyboard", "Microsoft.Keyboard", "Propriétés du clavier",
            "Délai et vitesse de répétition des touches, clignotement du curseur.", "",
            "repetition des touches", "delai de repetition", "clignotement");
        yield return Cpl("folders", "Microsoft.FolderOptions", "Options de l'Explorateur de fichiers",
            "Affichage, fichiers cachés, extensions, navigation.", "", inSearch: false);
        yield return Cpl("datetime", "Microsoft.DateAndTime", "Date et heure (panneau classique)",
            "Horloges supplémentaires, synchronisation avec un serveur de temps Internet.", "",
            "timedate.cpl", "horloges supplementaires", "serveur de temps", "ntp");
        yield return Cpl("region", "Microsoft.RegionAndLanguage", "Région",
            "Formats de date et de nombre, langue des programmes non Unicode, copie des paramètres.", "",
            "intl.cpl", "format de date", "separateur decimal", "unicode", "parametres regionaux");
        yield return Cpl("backup7", "Microsoft.BackupAndRestore", "Sauvegarder et restaurer (Windows 7)",
            "Image système et sauvegardes classiques (fonction héritée, toujours disponible).", "",
            "image systeme", "system image", "sauvegarde windows 7");
        yield return Cpl("filehistory", "Microsoft.FileHistory", "Historique des fichiers",
            "Copies automatiques de vos fichiers sur un disque externe ou réseau.", "",
            "file history", "versions precedentes", "sauvegarde fichiers");
        yield return Cpl("troubleshooting", "Microsoft.Troubleshooting", "Dépannage",
            "Utilitaires de résolution des problèmes (peut rediriger vers Paramètres).", "",
            "troubleshooting", "resolution des problemes");
        yield return Cpl("firewall", "Microsoft.WindowsFirewall", "Pare-feu Windows Defender",
            "État par profil, applications autorisées, restauration des paramètres par défaut.", "",
            "firewall.cpl", "pare-feu", "applications autorisees");
        yield return Cpl("admintools", "Microsoft.AdministrativeTools", "Outils Windows",
            "Dossier qui regroupe tous les outils d'administration de Windows.", "",
            "outils d'administration", "administrative tools", "windows tools");
        yield return Cpl("security", "Microsoft.ActionCenter", "Sécurité et maintenance",
            "Messages de sécurité, maintenance automatique, historique des problèmes.", "",
            "security and maintenance", "maintenance automatique", "rapports de problemes");
        yield return Cpl("indexing", "Microsoft.IndexingOptions", "Options d'indexation",
            "Emplacements indexés par la recherche, reconstruction de l'index.", "",
            "indexation", "index de recherche", "reconstruire l'index", "indexing");
        yield return Cpl("internet", "Microsoft.InternetOptions", "Options Internet",
            "Paramètres Internet hérités (proxy, zones de sécurité) encore utilisés par certaines applications.", "",
            "inetcpl", "internet options", "zones de securite");
        yield return Cpl("colormgmt", "Microsoft.ColorManagement", "Gestion des couleurs",
            "Profils de couleur (ICC) des écrans et des imprimantes.", "",
            "profil icc", "profil colorimetrique", "color management");
        yield return Cpl("easeofaccess", "Microsoft.EaseOfAccessCenter", "Centre Options d'ergonomie",
            "Options d'accessibilité classiques : clavier, souris, affichage.", "",
            "ergonomie", "ease of access center");
        yield return Cpl("storagespaces", "Microsoft.StorageSpaces", "Espaces de stockage",
            "Regrouper plusieurs disques en un volume, avec ou sans redondance.", "",
            "storage spaces", "pool de stockage", "raid");
        yield return Cpl("bitlocker", "Microsoft.BitLockerDriveEncryption", "Chiffrement de lecteur BitLocker",
            "Activer BitLocker, sauvegarder la clé de récupération, BitLocker To Go.", "",
            proOnly: true, keywords: ["bitlocker", "cle de recuperation", "chiffrement du disque"]);
        yield return Sys32("odbc", "odbcad32.exe", "Sources de données ODBC (64 bits)",
            "Connexions de bases de données utilisées par certains logiciels de gestion.", ToolGroup.ControlPanel, "",
            admin: true, keywords: ["odbc", "odbcad32", "source de donnees", "dsn", "base de donnees"]);
        yield return Sys32("mobility", "mblctr.exe", "Centre de mobilité Windows",
            "Raccourcis pour ordinateurs portables : luminosité, volume, batterie, écran externe.", ToolGroup.ControlPanel, "",
            keywords: ["mblctr", "mobility center", "ordinateur portable", "portable"]);

        // ---------------------------------------------------------------- Dossiers spéciaux
        yield return Folder("startup", "shell:startup", "Dossier Démarrage (votre compte)",
            "Raccourcis lancés à l'ouverture de votre session.", "",
            "dossier demarrage", "startup folder", "lancement automatique");
        yield return Folder("commonstartup", "shell:common startup", "Dossier Démarrage (tous les comptes)",
            "Raccourcis lancés à l'ouverture de session de chaque utilisateur.", "",
            "dossier demarrage commun", "all users startup");
        yield return Folder("appsfolder", "shell:appsfolder", "Toutes les applications",
            "Applications classiques et du Store, avec possibilité de créer des raccourcis.", "",
            "appsfolder", "liste des applications", "raccourci application store");
        yield return Folder("programs", "shell:programs", "Raccourcis du menu Démarrer",
            "Dossier des raccourcis de votre menu Démarrer (Programmes).", "",
            "start menu programs", "raccourcis demarrer");
        yield return Folder("sendto", "shell:sendto", "Menu « Envoyer vers »",
            "Ajoutez ou retirez des destinations du menu « Envoyer vers ».", "",
            "envoyer vers", "send to", "sendto");
        yield return Folder("recent", "shell:recent", "Éléments récents",
            "Raccourcis vers les fichiers ouverts récemment.", "",
            "fichiers recents", "recent items", "recents");
        yield return Folder("appdata", "shell:appdata", "AppData (Roaming)",
            "Données et paramètres des applications de votre compte.", "",
            "appdata", "roaming", "donnees d'application");
        yield return new WinTool
        {
            Key = "temp", Title = "Fichiers temporaires (%TEMP%)", Group = ToolGroup.Folders, Glyph = "", Command = "%TEMP%",
            Description = "Dossier temporaire de votre compte, souvent volumineux.",
            Keywords = ["temp", "fichiers temporaires", "dossier temporaire", "tmp"],
            Launch = () => ProcessRunner.OpenFolder(Path.GetTempPath()),
            IsAvailable = () => Directory.Exists(Path.GetTempPath()),
        };
        yield return Folder("godmode", "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}", "Mode Dieu",
            "Toutes les tâches du Panneau de configuration réunies dans une seule liste.", "",
            "mode dieu", "god mode", "godmode", "tous les parametres", "toutes les taches");

        // ---------------------------------------------------------------- Accessoires
        yield return Exe("charmap", SystemTool.Charmap, "charmap", "Table des caractères",
            "Copier des caractères spéciaux, symboles et accents de toutes les polices.", ToolGroup.Accessories, "",
            keywords: ["charmap", "caracteres speciaux", "symboles", "character map"]);
        yield return Exe("osk", SystemTool.Osk, "osk", "Clavier visuel",
            "Clavier à l'écran, utilisable à la souris ou au toucher.", ToolGroup.Accessories, "",
            keywords: ["clavier visuel", "clavier a l'ecran", "on-screen keyboard", "osk"]);
        yield return Exe("magnify", SystemTool.Magnify, "magnify", "Loupe",
            "Agrandit une partie de l'écran (Windows + Échap pour quitter).", ToolGroup.Accessories, "",
            keywords: ["loupe", "magnifier", "zoom ecran"]);
        yield return Exe("mstsc", SystemTool.Mstsc, "mstsc", "Connexion Bureau à distance",
            "Se connecter à un autre PC par le protocole Bureau à distance (RDP).", ToolGroup.Accessories, "",
            keywords: ["mstsc", "rdp", "remote desktop connection", "bureau a distance"]);
        yield return Sys32("msra", "msra.exe", "Assistance à distance Windows",
            "Inviter une personne de confiance à vous aider, ou aider quelqu'un.", ToolGroup.Accessories, "",
            keywords: ["msra", "assistance a distance", "remote assistance", "aide a distance"]);
        yield return Sys32("sndvol", "sndvol.exe", "Mélangeur de volume (classique)",
            "Volume de chaque application et périphérique audio.", ToolGroup.Accessories, "",
            keywords: ["sndvol", "volume mixer", "melangeur"]);
        yield return Sys32("cttune", "cttune.exe", "Ajusteur de texte ClearType",
            "Rendre le texte plus net à l'écran, en quelques étapes.", ToolGroup.Accessories, "",
            keywords: ["cleartype", "texte flou", "lissage des polices"]);
        yield return Sys32("dccw", "dccw.exe", "Calibrer les couleurs de l'écran",
            "Assistant de réglage du gamma, de la luminosité et du contraste.", ToolGroup.Accessories, "",
            admin: true, keywords: ["calibrage", "calibrer ecran", "gamma", "dccw", "calibration"]);
        yield return Sys32("eudcedit", "eudcedit.exe", "Éditeur de caractères privés",
            "Dessiner vos propres caractères (logos, symboles) utilisables dans toutes les polices.", ToolGroup.Accessories, "",
            admin: true, keywords: ["eudcedit", "eudc", "caractere personnalise", "private character editor"]);
    }

    // ------------------------------------------------------------------ fabriques

    private static WinTool Msc(string key, string msc, string title, string description, string glyph, params string[] keywords) =>
        Msc(key, msc, title, description, glyph, false, true, keywords);

    private static WinTool Msc(string key, string msc, string title, string description, string glyph,
        bool proOnly = false, bool inSearch = true, string[]? keywords = null) => new()
    {
        Key = key, Title = title, Description = description, Group = ToolGroup.Admin, Glyph = glyph, Command = msc,
        Keywords = [msc, Path.GetFileNameWithoutExtension(msc), .. keywords ?? []],
        Admin = true, ProOnly = proOnly, InSearch = inSearch,
        Launch = () => ToolLauncher.LaunchConsole(msc),
        IsAvailable = () => File.Exists(ToolLauncher.MscPath(msc)) && SystemTools.IsAvailable(SystemTool.Mmc),
    };

    private static WinTool Exe(string key, SystemTool tool, string command, string title, string description, ToolGroup group,
        string glyph, bool admin = false, string[]? args = null, string[]? keywords = null, bool inSearch = true)
    {
        var a = args ?? [];
        return new WinTool
        {
            Key = key, Title = title, Description = description, Group = group, Glyph = glyph, Command = command,
            Keywords = keywords ?? [], Admin = admin, InSearch = inSearch,
            Launch = () => ToolLauncher.Launch(tool, a),
            IsAvailable = () => SystemTools.IsAvailable(tool),
        };
    }

    private static WinTool Sys32(string key, string exe, string title, string description, ToolGroup group, string glyph,
        bool admin = false, string[]? keywords = null) => new()
    {
        Key = key, Title = title, Description = description, Group = group, Glyph = glyph,
        Command = Path.GetFileNameWithoutExtension(exe), Keywords = keywords ?? [], Admin = admin,
        Launch = () => ToolLauncher.LaunchSystem32(exe),
        IsAvailable = () => ToolLauncher.System32FileExists(exe),
    };

    private static WinTool Cpl(string key, string canonicalName, string title, string description, string glyph, params string[] keywords) =>
        Cpl(key, canonicalName, title, description, glyph, false, true, keywords);

    private static WinTool Cpl(string key, string canonicalName, string title, string description, string glyph,
        bool proOnly = false, bool inSearch = true, string[]? keywords = null) => new()
    {
        Key = "cpl." + key, Title = title, Description = description, Group = ToolGroup.ControlPanel, Glyph = glyph,
        Command = "control /name " + canonicalName, Keywords = [canonicalName, "panneau de configuration", .. keywords ?? []],
        ProOnly = proOnly, InSearch = inSearch,
        Launch = () => ToolLauncher.Launch(SystemTool.Control, "/name", canonicalName),
        IsAvailable = () => SystemTools.IsAvailable(SystemTool.Control),
    };

    private static WinTool Folder(string key, string shellPath, string title, string description, string glyph, params string[] keywords) => new()
    {
        Key = "folder." + key, Title = title, Description = description, Group = ToolGroup.Folders, Glyph = glyph,
        Command = shellPath.StartsWith("shell:::", StringComparison.Ordinal) ? "shell:::{ED7BA470…}" : shellPath,
        Keywords = [shellPath, "dossier", .. keywords],
        Launch = () => ToolLauncher.Launch(SystemTool.Explorer, shellPath),
        IsAvailable = () => SystemTools.IsAvailable(SystemTool.Explorer),
    };
}
