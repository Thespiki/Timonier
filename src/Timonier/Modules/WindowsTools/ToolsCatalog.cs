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
        (ToolGroup.Admin, L("Consoles d'administration"), ""),
        (ToolGroup.Diagnostic, L("Diagnostic"), ""),
        (ToolGroup.System, L("Système"), ""),
        (ToolGroup.ControlPanel, L("Panneau de configuration"), ""),
        (ToolGroup.Folders, L("Dossiers spéciaux"), ""),
        (ToolGroup.Accessories, L("Accessoires"), ""),
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
        yield return Msc("devmgmt", "devmgmt.msc", L("Gestionnaire de périphériques"),
            L("Matériel détecté, pilotes, périphériques en erreur ou désactivés."), "",
            L("gestionnaire de peripheriques, device manager, pilotes, drivers, materiel"));
        yield return Msc("diskmgmt", "diskmgmt.msc", L("Gestion des disques"),
            L("Partitions, lettres de lecteur, formatage, extension ou réduction de volumes."), "",
            L("partition, partitionner, formater, lettre de lecteur, disk management, volume"));
        yield return Msc("compmgmt", "compmgmt.msc", L("Gestion de l'ordinateur"),
            L("Console qui regroupe journaux, dossiers partagés, utilisateurs, disques et services."), "",
            L("computer management, gestion ordinateur"));
        yield return Msc("eventvwr", "eventvwr.msc", L("Observateur d'événements"),
            L("Journaux d'erreurs et d'avertissements de Windows et des applications."), "",
            L("event viewer, journaux, logs, erreurs windows, evenements"));
        yield return Msc("services", "services.msc", L("Services"),
            L("Console classique de tous les services Windows (démarrage, état, compte)."), "",
            inSearch: false, keywords: [L("services windows")]);
        yield return Msc("taskschd", "taskschd.msc", L("Planificateur de tâches"),
            L("Tâches planifiées de Windows et des applications."), "",
            inSearch: false, keywords: [L("task scheduler")]);
        yield return Msc("wf", "wf.msc", L("Pare-feu avec fonctions avancées"),
            L("Règles entrantes et sortantes du Pare-feu Windows Defender, profils, journalisation."), "",
            inSearch: false, keywords: [L("firewall, regles pare-feu")]);
        yield return Msc("certmgr", "certmgr.msc", L("Certificats (utilisateur actuel)"),
            L("Certificats personnels et autorités de confiance de votre compte."), "",
            L("certificat, certificate, autorite de certification"));
        yield return Msc("certlm", "certlm.msc", L("Certificats (ordinateur local)"),
            L("Certificats de l'ordinateur, utilisés par les services et tous les comptes."), "",
            L("certificat machine, certificate, local machine"));
        yield return Msc("fsmgmt", "fsmgmt.msc", L("Dossiers partagés"),
            L("Partages de ce PC, sessions ouvertes et fichiers utilisés à distance."), "",
            L("partages, shared folders, smb, partage reseau"));
        yield return Msc("comexp", "comexp.msc", L("Services de composants"),
            L("Applications COM+, configuration DCOM et coordinateur de transactions distribuées."), "",
            L("com+, dcom, dcomcnfg, component services"));
        yield return Msc("tpm", "tpm.msc", L("Gestion du module TPM"),
            L("État, fabricant et version de la puce de sécurité TPM."), "",
            L("tpm, puce de securite, trusted platform module"));
        yield return Msc("gpedit", "gpedit.msc", L("Éditeur de stratégie de groupe locale"),
            L("Stratégies de l'ordinateur et de l'utilisateur (modèles d'administration)."), "",
            proOnly: true, keywords: [L("gpedit, group policy, strategie de groupe, gpo")]);
        yield return Msc("secpol", "secpol.msc", L("Stratégie de sécurité locale"),
            L("Stratégies de mot de passe, d'audit, droits des utilisateurs, options de sécurité."), "",
            proOnly: true, keywords: [L("secpol, local security policy, politique de securite")]);
        yield return Msc("lusrmgr", "lusrmgr.msc", L("Utilisateurs et groupes locaux"),
            L("Comptes locaux, groupes (Administrateurs, Utilisateurs…) et leurs membres."), "",
            proOnly: true, inSearch: false, keywords: [L("lusrmgr, local users and groups, groupes, comptes locaux")]);
        yield return Msc("printmanagement", "printmanagement.msc", L("Gestion de l'impression"),
            L("Imprimantes, pilotes d'impression, files d'attente et ports."), "",
            proOnly: true, keywords: [L("print management, pilotes imprimante, file d'attente")]);

        // ---------------------------------------------------------------- Diagnostic
        yield return Exe("msinfo32", SystemTool.Msinfo32, "msinfo32", L("Informations système"),
            L("Inventaire complet : matériel, pilotes, BIOS, démarrage sécurisé, composants."), ToolGroup.Diagnostic, "",
            keywords: [L("msinfo32, msinfo, system information, configuration materielle, bios")]);
        yield return Exe("resmon", SystemTool.Resmon, "resmon", L("Moniteur de ressources"),
            L("Processeur, mémoire, disque et réseau par processus, en temps réel."), ToolGroup.Diagnostic, "",
            keywords: [L("resource monitor, resmon, utilisation disque, activite reseau")]);
        yield return Exe("perfmon", SystemTool.Perfmon, "perfmon", L("Analyseur de performances"),
            L("Compteurs de performances et ensembles de collecteurs de données."), ToolGroup.Diagnostic, "",
            admin: true, keywords: [L("performance monitor, perfmon, compteurs")]);
        yield return Exe("reliability", SystemTool.Perfmon, "perfmon /rel", L("Moniteur de fiabilité"),
            L("Historique jour par jour des plantages, erreurs et installations."), ToolGroup.Diagnostic, "",
            args: ["/rel"], keywords: [L("reliability monitor, historique de fiabilite, plantages, crash")]);
        yield return Exe("taskmgr", SystemTool.Taskmgr, "taskmgr", L("Gestionnaire des tâches"),
            L("Processus, performances, applications au démarrage et services."), ToolGroup.Diagnostic, "",
            keywords: [L("task manager, processus, ctrl maj echap")]);
        yield return Exe("dxdiag", SystemTool.Dxdiag, "dxdiag", L("Outil de diagnostic DirectX"),
            L("Carte graphique, pilotes, son et fonctionnalités DirectX."), ToolGroup.Diagnostic, "",
            keywords: [L("directx, dxdiag, carte graphique, diagnostic graphique")]);
        yield return Exe("mdsched", SystemTool.Mdsched, "mdsched", L("Diagnostic de la mémoire Windows"),
            L("Teste la mémoire vive au prochain redémarrage (vous choisissez quand redémarrer)."), ToolGroup.Diagnostic, "",
            admin: true, keywords: [L("test memoire, test ram, memory diagnostic, mdsched")]);

        // ---------------------------------------------------------------- Système
        yield return Exe("msconfig", SystemTool.Msconfig, "msconfig", L("Configuration du système"),
            L("Démarrage sélectif, options de démarrage (mode sans échec), services."), ToolGroup.System, "",
            admin: true, keywords: [L("msconfig, mode sans echec, safe mode, demarrage selectif, boot")]);
        yield return Exe("regedit", SystemTool.Regedit, "regedit", L("Éditeur du Registre"),
            L("Modification avancée du Registre : une erreur peut empêcher Windows de démarrer."), ToolGroup.System, "",
            admin: true, keywords: [L("regedit, registre, registry, base de registre")]);
        yield return Exe("sysadvanced", SystemTool.SystemPropertiesAdvanced, "SystemPropertiesAdvanced", L("Paramètres système avancés"),
            L("Variables d'environnement, profils utilisateur, démarrage et récupération."), ToolGroup.System, "",
            admin: true, keywords: [L("variables d'environnement, environment variables, path, proprietes systeme, sysdm")]);
        yield return Exe("sysperformance", SystemTool.SystemPropertiesPerformance, "SystemPropertiesPerformance", L("Options de performances"),
            L("Effets visuels, mémoire virtuelle (fichier d'échange), prévention de l'exécution des données."), ToolGroup.System, "",
            admin: true, inSearch: false, keywords: [L("memoire virtuelle, fichier d'echange, pagefile, virtual memory, effets visuels")]);
        yield return Exe("sysprotection", SystemTool.SystemPropertiesProtection, "SystemPropertiesProtection", L("Protection du système"),
            L("Activer la protection, créer ou configurer les points de restauration."), ToolGroup.System, "",
            admin: true, keywords: [L("point de restauration, restore point, protection du systeme, creer un point")]);
        yield return Exe("rstrui", SystemTool.Rstrui, "rstrui", L("Restauration du système"),
            L("Revenir à un point de restauration antérieur (vos fichiers ne sont pas touchés)."), ToolGroup.System, "",
            admin: true, inSearch: false, keywords: [L("restauration systeme, system restore, rstrui, revenir en arriere")]);
        yield return Sys32("computername", "SystemPropertiesComputerName.exe", L("Nom de l'ordinateur et domaine"),
            L("Renommer le PC, rejoindre un groupe de travail ou un domaine."), ToolGroup.System, "",
            admin: true, keywords: [L("nom du pc, renommer le pc, groupe de travail, workgroup, domaine")]);
        yield return Sys32("sysremote", "SystemPropertiesRemote.exe", L("Utilisation à distance"),
            L("Assistance à distance et Bureau à distance (Propriétés système)."), ToolGroup.System, "",
            admin: true, keywords: [L("assistance a distance, remote assistance, bureau a distance, rdp")]);
        yield return Exe("netplwiz", SystemTool.Netplwiz, "netplwiz", L("Comptes d'utilisateurs (avancé)"),
            L("Liste des comptes, appartenance aux groupes, gestion avancée des mots de passe."), ToolGroup.System, "",
            admin: true, inSearch: false, keywords: [L("netplwiz, control userpasswords2, comptes utilisateurs")]);
        yield return Exe("cleanmgr", SystemTool.CleanMgr, "cleanmgr", L("Nettoyage de disque"),
            L("Fichiers temporaires, miniatures, anciennes mises à jour (« Nettoyer les fichiers système »)."), ToolGroup.System, "",
            inSearch: false, keywords: [L("cleanmgr, nettoyage de disque, disk cleanup, liberer de l'espace")]);
        yield return Sys32("dfrgui", "dfrgui.exe", L("Optimiser les lecteurs"),
            L("Défragmentation des disques durs, TRIM des SSD et planification."), ToolGroup.System, "",
            admin: true, keywords: [L("defragmenter, defragmentation, defrag, trim, optimiser les lecteurs")]);
        yield return Sys32("optionalfeatures", "optionalfeatures.exe", L("Fonctionnalités de Windows"),
            L("Activer ou désactiver .NET 3.5, Hyper-V, WSL, Sandbox, clients SMB…"), ToolGroup.System, "",
            admin: true, keywords: [L("fonctionnalites windows, windows features, hyper-v, wsl, sandbox, net framework 3.5")]);
        yield return Sys32("recoverydrive", "RecoveryDrive.exe", L("Créer un lecteur de récupération"),
            L("Clé USB de secours pour réparer ou réinstaller Windows."), ToolGroup.System, "",
            admin: true, keywords: [L("lecteur de recuperation, recovery drive, cle de secours, cle usb de reparation")]);
        yield return Exe("winver", SystemTool.Winver, "winver", L("À propos de Windows"),
            L("Version, build exacte et édition de Windows installées."), ToolGroup.System, "",
            keywords: [L("winver, version de windows, build, numero de version")]);

        // ---------------------------------------------------------------- Panneau de configuration
        yield return Cpl("programs", "Microsoft.ProgramsAndFeatures", L("Programmes et fonctionnalités"),
            L("Liste classique des programmes installés : désinstaller, modifier, réparer."), "",
            L("appwiz, ajout suppression de programmes, desinstaller un programme"));
        yield return Cpl("power", "Microsoft.PowerOptions", L("Options d'alimentation"),
            L("Modes de gestion de l'alimentation classiques et paramètres avancés."), "",
            inSearch: false, keywords: [L("powercfg.cpl, mode de gestion de l'alimentation, parametres d'alimentation avances")]);
        yield return Cpl("netcenter", "Microsoft.NetworkAndSharingCenter", L("Centre Réseau et partage"),
            L("État des connexions, paramètres de partage avancés, propriétés des cartes."), "",
            L("network and sharing center, partage reseau, centre reseau"));
        yield return new WinTool
        {
            Key = "ncpa", Title = L("Connexions réseau"), Group = ToolGroup.ControlPanel, Glyph = "", Command = "ncpa.cpl",
            Description = L("Cartes réseau : propriétés IPv4/IPv6, activation, diagnostic."),
            Launch = () => ToolLauncher.Launch(SystemTool.Control, "ncpa.cpl"),
            IsAvailable = () => SystemTools.IsAvailable(SystemTool.Control), InSearch = false,
        };
        yield return Cpl("devprinters", "Microsoft.DevicesAndPrinters", L("Périphériques et imprimantes"),
            L("Vue classique des appareils (peut s'ouvrir dans Paramètres sur les versions récentes)."), "",
            L("devices and printers, imprimantes"));
        yield return Cpl("useraccounts", "Microsoft.UserAccounts", L("Comptes d'utilisateurs"),
            L("Type de compte, contrôle de compte d'utilisateur, variables d'environnement du compte."), "",
            L("user accounts, comptes"));
        yield return Cpl("credentials", "Microsoft.CredentialManager", L("Gestionnaire d'identification"),
            L("Identifiants Windows et Web enregistrés (partages réseau, applications)."), "",
            L("credential manager, mots de passe enregistres, identifiants, coffre"));
        yield return Cpl("sound", "Microsoft.Sound", L("Son (panneau classique)"),
            L("Périphériques de lecture et d'enregistrement, formats, sons système."), "",
            L("mmsys.cpl, peripherique de lecture, enregistrement, sons systeme"));
        yield return Cpl("mouse", "Microsoft.Mouse", L("Propriétés de la souris"),
            L("Boutons, schémas de pointeurs, vitesse, roulette."), "",
            L("main.cpl, pointeurs, double clic, roulette"));
        yield return Cpl("keyboard", "Microsoft.Keyboard", L("Propriétés du clavier"),
            L("Délai et vitesse de répétition des touches, clignotement du curseur."), "",
            L("repetition des touches, delai de repetition, clignotement"));
        yield return Cpl("folders", "Microsoft.FolderOptions", L("Options de l'Explorateur de fichiers"),
            L("Affichage, fichiers cachés, extensions, navigation."), "", inSearch: false);
        yield return Cpl("datetime", "Microsoft.DateAndTime", L("Date et heure (panneau classique)"),
            L("Horloges supplémentaires, synchronisation avec un serveur de temps Internet."), "",
            L("timedate.cpl, horloges supplementaires, serveur de temps, ntp"));
        yield return Cpl("region", "Microsoft.RegionAndLanguage", L("Région"),
            L("Formats de date et de nombre, langue des programmes non Unicode, copie des paramètres."), "",
            L("intl.cpl, format de date, separateur decimal, unicode, parametres regionaux"));
        yield return Cpl("backup7", "Microsoft.BackupAndRestore", L("Sauvegarder et restaurer (Windows 7)"),
            L("Image système et sauvegardes classiques (fonction héritée, toujours disponible)."), "",
            L("image systeme, system image, sauvegarde windows 7"));
        yield return Cpl("filehistory", "Microsoft.FileHistory", L("Historique des fichiers"),
            L("Copies automatiques de vos fichiers sur un disque externe ou réseau."), "",
            L("file history, versions precedentes, sauvegarde fichiers"));
        yield return Cpl("troubleshooting", "Microsoft.Troubleshooting", L("Dépannage"),
            L("Utilitaires de résolution des problèmes (peut rediriger vers Paramètres)."), "",
            L("troubleshooting, resolution des problemes"));
        yield return Cpl("firewall", "Microsoft.WindowsFirewall", L("Pare-feu Windows Defender"),
            L("État par profil, applications autorisées, restauration des paramètres par défaut."), "",
            L("firewall.cpl, pare-feu, applications autorisees"));
        yield return Cpl("admintools", "Microsoft.AdministrativeTools", L("Outils Windows"),
            L("Dossier qui regroupe tous les outils d'administration de Windows."), "",
            L("outils d'administration, administrative tools, windows tools"));
        yield return Cpl("security", "Microsoft.ActionCenter", L("Sécurité et maintenance"),
            L("Messages de sécurité, maintenance automatique, historique des problèmes."), "",
            L("security and maintenance, maintenance automatique, rapports de problemes"));
        yield return Cpl("indexing", "Microsoft.IndexingOptions", L("Options d'indexation"),
            L("Emplacements indexés par la recherche, reconstruction de l'index."), "",
            L("indexation, index de recherche, reconstruire l'index, indexing"));
        yield return Cpl("internet", "Microsoft.InternetOptions", L("Options Internet"),
            L("Paramètres Internet hérités (proxy, zones de sécurité) encore utilisés par certaines applications."), "",
            L("inetcpl, internet options, zones de securite"));
        yield return Cpl("colormgmt", "Microsoft.ColorManagement", L("Gestion des couleurs"),
            L("Profils de couleur (ICC) des écrans et des imprimantes."), "",
            L("profil icc, profil colorimetrique, color management"));
        yield return Cpl("easeofaccess", "Microsoft.EaseOfAccessCenter", L("Centre Options d'ergonomie"),
            L("Options d'accessibilité classiques : clavier, souris, affichage."), "",
            L("ergonomie, ease of access center"));
        yield return Cpl("storagespaces", "Microsoft.StorageSpaces", L("Espaces de stockage"),
            L("Regrouper plusieurs disques en un volume, avec ou sans redondance."), "",
            L("storage spaces, pool de stockage, raid"));
        yield return Cpl("bitlocker", "Microsoft.BitLockerDriveEncryption", L("Chiffrement de lecteur BitLocker"),
            L("Activer BitLocker, sauvegarder la clé de récupération, BitLocker To Go."), "",
            proOnly: true, keywords: [L("bitlocker, cle de recuperation, chiffrement du disque")]);
        yield return Sys32("odbc", "odbcad32.exe", L("Sources de données ODBC (64 bits)"),
            L("Connexions de bases de données utilisées par certains logiciels de gestion."), ToolGroup.ControlPanel, "",
            admin: true, keywords: [L("odbc, odbcad32, source de donnees, dsn, base de donnees")]);
        yield return Sys32("mobility", "mblctr.exe", L("Centre de mobilité Windows"),
            L("Raccourcis pour ordinateurs portables : luminosité, volume, batterie, écran externe."), ToolGroup.ControlPanel, "",
            keywords: [L("mblctr, mobility center, ordinateur portable, portable")]);

        // ---------------------------------------------------------------- Dossiers spéciaux
        yield return Folder("startup", "shell:startup", L("Dossier Démarrage (votre compte)"),
            L("Raccourcis lancés à l'ouverture de votre session."), "",
            L("dossier demarrage, startup folder, lancement automatique"));
        yield return Folder("commonstartup", "shell:common startup", L("Dossier Démarrage (tous les comptes)"),
            L("Raccourcis lancés à l'ouverture de session de chaque utilisateur."), "",
            L("dossier demarrage commun, all users startup"));
        yield return Folder("appsfolder", "shell:appsfolder", L("Toutes les applications"),
            L("Applications classiques et du Store, avec possibilité de créer des raccourcis."), "",
            L("appsfolder, liste des applications, raccourci application store"));
        yield return Folder("programs", "shell:programs", L("Raccourcis du menu Démarrer"),
            L("Dossier des raccourcis de votre menu Démarrer (Programmes)."), "",
            L("start menu programs, raccourcis demarrer"));
        yield return Folder("sendto", "shell:sendto", L("Menu « Envoyer vers »"),
            L("Ajoutez ou retirez des destinations du menu « Envoyer vers »."), "",
            L("envoyer vers, send to, sendto"));
        yield return Folder("recent", "shell:recent", L("Éléments récents"),
            L("Raccourcis vers les fichiers ouverts récemment."), "",
            L("fichiers recents, recent items, recents"));
        yield return Folder("appdata", "shell:appdata", "AppData (Roaming)",
            L("Données et paramètres des applications de votre compte."), "",
            L("appdata, roaming, donnees d'application"));
        yield return new WinTool
        {
            Key = "temp", Title = L("Fichiers temporaires (%TEMP%)"), Group = ToolGroup.Folders, Glyph = "", Command = "%TEMP%",
            Description = L("Dossier temporaire de votre compte, souvent volumineux."),
            Keywords = [L("temp, fichiers temporaires, dossier temporaire, tmp")],
            Launch = () => ProcessRunner.OpenFolder(Path.GetTempPath()),
            IsAvailable = () => Directory.Exists(Path.GetTempPath()),
        };
        yield return Folder("godmode", "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}", L("Mode Dieu"),
            L("Toutes les tâches du Panneau de configuration réunies dans une seule liste."), "",
            L("mode dieu, god mode, godmode, tous les parametres, toutes les taches"));

        // ---------------------------------------------------------------- Accessoires
        yield return Exe("charmap", SystemTool.Charmap, "charmap", L("Table des caractères"),
            L("Copier des caractères spéciaux, symboles et accents de toutes les polices."), ToolGroup.Accessories, "",
            keywords: [L("charmap, caracteres speciaux, symboles, character map")]);
        yield return Exe("osk", SystemTool.Osk, "osk", L("Clavier visuel"),
            L("Clavier à l'écran, utilisable à la souris ou au toucher."), ToolGroup.Accessories, "",
            keywords: [L("clavier visuel, clavier a l'ecran, on-screen keyboard, osk")]);
        yield return Exe("magnify", SystemTool.Magnify, "magnify", L("Loupe"),
            L("Agrandit une partie de l'écran (Windows + Échap pour quitter)."), ToolGroup.Accessories, "",
            keywords: [L("loupe, magnifier, zoom ecran")]);
        yield return Exe("mstsc", SystemTool.Mstsc, "mstsc", L("Connexion Bureau à distance"),
            L("Se connecter à un autre PC par le protocole Bureau à distance (RDP)."), ToolGroup.Accessories, "",
            keywords: [L("mstsc, rdp, remote desktop connection, bureau a distance")]);
        yield return Sys32("msra", "msra.exe", L("Assistance à distance Windows"),
            L("Inviter une personne de confiance à vous aider, ou aider quelqu'un."), ToolGroup.Accessories, "",
            keywords: [L("msra, assistance a distance, remote assistance, aide a distance")]);
        yield return Sys32("sndvol", "sndvol.exe", L("Mélangeur de volume (classique)"),
            L("Volume de chaque application et périphérique audio."), ToolGroup.Accessories, "",
            keywords: [L("sndvol, volume mixer, melangeur")]);
        yield return Sys32("cttune", "cttune.exe", L("Ajusteur de texte ClearType"),
            L("Rendre le texte plus net à l'écran, en quelques étapes."), ToolGroup.Accessories, "",
            keywords: [L("cleartype, texte flou, lissage des polices")]);
        yield return Sys32("dccw", "dccw.exe", L("Calibrer les couleurs de l'écran"),
            L("Assistant de réglage du gamma, de la luminosité et du contraste."), ToolGroup.Accessories, "",
            admin: true, keywords: [L("calibrage, calibrer ecran, gamma, dccw, calibration")]);
        yield return Sys32("eudcedit", "eudcedit.exe", L("Éditeur de caractères privés"),
            L("Dessiner vos propres caractères (logos, symboles) utilisables dans toutes les polices."), ToolGroup.Accessories, "",
            admin: true, keywords: [L("eudcedit, eudc, caractere personnalise, private character editor")]);
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
        Command = "control /name " + canonicalName, Keywords = [canonicalName, L("panneau de configuration"), .. keywords ?? []],
        ProOnly = proOnly, InSearch = inSearch,
        Launch = () => ToolLauncher.Launch(SystemTool.Control, "/name", canonicalName),
        IsAvailable = () => SystemTools.IsAvailable(SystemTool.Control),
    };

    private static WinTool Folder(string key, string shellPath, string title, string description, string glyph, params string[] keywords) => new()
    {
        Key = "folder." + key, Title = title, Description = description, Group = ToolGroup.Folders, Glyph = glyph,
        Command = shellPath.StartsWith("shell:::", StringComparison.Ordinal) ? "shell:::{ED7BA470…}" : shellPath,
        Keywords = [shellPath, L("dossier"), .. keywords],
        Launch = () => ToolLauncher.Launch(SystemTool.Explorer, shellPath),
        IsAvailable = () => SystemTools.IsAvailable(SystemTool.Explorer),
    };
}
