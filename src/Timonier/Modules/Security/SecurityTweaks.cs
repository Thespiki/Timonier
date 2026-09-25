using Microsoft.Win32;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Security;

/// <summary>
/// Réglages de renforcement de la sécurité. Règle d'or : aucun réglage ne permet de désactiver Defender, le pare-feu,
/// l'UAC, SmartScreen ou Secure Boot. Les réglages « imposés par stratégie » ont pour côté « Off » la suppression de la
/// stratégie (retour au choix fait dans Sécurité Windows), jamais une désactivation forcée.
/// </summary>
internal static class SecurityTweaks
{
    private const string C = SecurityModule.Category;
    private const string On = TweakDefinition.On;
    private const string Off = TweakDefinition.Off;

    public const string GroupRemote = "Accès à distance et partage réseau";
    public const string GroupMalware = "Protection contre les logiciels malveillants";
    public const string GroupSystem = "Protection du système et de l'ouverture de session";
    public const string GroupAsr = "Règles de réduction de la surface d'attaque (ASR)";

    public static readonly string[] Groups = [GroupMalware, GroupSystem, GroupRemote, GroupAsr];

    // --- Chemins de registre (documentés : Microsoft Learn / ADMX) ---
    internal const string PolExplorer = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    internal const string PolWinExplorer = @"SOFTWARE\Policies\Microsoft\Windows\Explorer";
    internal const string PolSystem = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    internal const string TerminalServer = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
    internal const string RemoteAssistance = @"SYSTEM\CurrentControlSet\Control\Remote Assistance";
    internal const string DnsClientPolicy = @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient";
    internal const string ScriptHost = @"SOFTWARE\Microsoft\Windows Script Host\Settings";
    internal const string DefenderPolicy = @"SOFTWARE\Policies\Microsoft\Windows Defender";
    internal const string DefenderKey = @"SOFTWARE\Microsoft\Windows Defender";
    internal const string ExploitGuardPolicy = DefenderPolicy + @"\Windows Defender Exploit Guard";
    internal const string AsrPolicy = ExploitGuardPolicy + @"\ASR";
    internal const string AsrRulesPolicy = AsrPolicy + @"\Rules";
    internal const string NetworkProtectionPolicy = ExploitGuardPolicy + @"\Network Protection";
    internal const string CfaPolicy = ExploitGuardPolicy + @"\Controlled Folder Access";
    internal const string CfaKey = DefenderKey + @"\Windows Defender Exploit Guard\Controlled Folder Access";
    internal const string SystemPolicy = @"SOFTWARE\Policies\Microsoft\Windows\System";
    internal const string Lsa = @"SYSTEM\CurrentControlSet\Control\Lsa";
    internal const string Hvci = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    internal const string LanmanServerParams = @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters";
    internal const string LanmanWorkstation = @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation";
    internal const string Smb1ClientService = @"SYSTEM\CurrentControlSet\Services\mrxsmb10";

    private static readonly Requirement DefenderActive = Requires.When(_ => SecurityProbe.DefenderLikelyActive(),
        "Nécessite que Microsoft Defender soit l'antivirus actif (un autre antivirus est installé ou Defender est désactivé).");

    private static readonly Requirement Smb1Installed = Requires.When(_ => RegistryAccess.KeyExists(RegHive.LocalMachine, Smb1ClientService),
        "Le protocole SMBv1 n'est pas installé sur ce PC (c'est le cas par défaut depuis Windows 10 1709) : il n'y a rien à désactiver.");

    public static IEnumerable<TweakDefinition> All() =>
        MalwareTweaks().Concat(SystemTweaks()).Concat(RemoteTweaks()).Concat(AsrTweaks());

    // =====================================================================================================
    // Logiciels malveillants
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> MalwareTweaks()
    {
        yield return Tweak.Toggle("security.smartscreen.apps", "SmartScreen pour les applications et fichiers",
                "Vérifie la réputation des programmes et fichiers téléchargés avant leur ouverture et avertit s'ils sont inconnus " +
                "ou malveillants. « Imposé » fixe SmartScreen sur « Avertir » par stratégie (Sécurité Windows affiche alors « géré par " +
                "votre organisation ») ; l'autre position supprime simplement la stratégie et rend la main à Sécurité Windows. " +
                "Timonier ne propose jamais de désactiver SmartScreen.")
            .In(C, GroupMalware)
            .Keywords("smartscreen", "réputation", "téléchargement", "application inconnue", "defender smartscreen", "filtre")
            .Tags("security", "family", "kiosk", "office")
            .Labels("Imposé (avertir)", "Réglage de Sécurité Windows")
            .WhenOn(Reg.LmDword(SystemPolicy, "EnableSmartScreen", 1), Reg.LmString(SystemPolicy, "ShellSmartScreenLevel", "Warn"))
            .WhenOff(Reg.LmDel(SystemPolicy, "EnableSmartScreen"), Reg.LmDel(SystemPolicy, "ShellSmartScreenLevel"))
            .Detect(() => RegistryAccess.ReadDword(RegHive.LocalMachine, SystemPolicy, "EnableSmartScreen") == 1 ? On : Off)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("security.defender.pua", "Blocage des applications potentiellement indésirables (PUA)",
                "Microsoft Defender bloque les logiciels « limites » : barres d'outils, mineurs de cryptomonnaie, installateurs " +
                "publicitaires, faux optimiseurs. « Imposé » active la stratégie PUAProtection ; l'autre position supprime la stratégie " +
                "et laisse le choix à Sécurité Windows (Contrôle des applications › Protection fondée sur la réputation).")
            .In(C, GroupMalware)
            .Keywords("pua", "pup", "adware", "logiciel indésirable", "bloatware", "defender", "réputation")
            .Tags("security", "family", "kiosk", "office")
            .Requires(DefenderActive)
            .Labels("Imposé", "Réglage de Sécurité Windows")
            .WhenOn(Reg.LmDword(DefenderPolicy, "PUAProtection", 1))
            .WhenOff(Reg.LmDel(DefenderPolicy, "PUAProtection"))
            .Detect(() => RegistryAccess.ReadDword(RegHive.LocalMachine, DefenderPolicy, "PUAProtection") == 1 ? On : Off)
            .WindowsDefault(Off)
            .RecommendWhen(p => p.IsManaged ? null : On)
            .Build();

        yield return Tweak.Toggle("security.defender.cfa", "Dossiers protégés contre les rançongiciels",
                "Contrôle d'accès aux dossiers de Microsoft Defender : seules les applications de confiance peuvent modifier Documents, " +
                "Images, Vidéos, Musique, Bureau et Favoris. Protège efficacement contre les rançongiciels, mais Windows bloque aussi " +
                "des applications légitimes inconnues (jeux, éditeurs photo, outils de sauvegarde) : il faut alors les autoriser dans " +
                "Sécurité Windows › Protection contre les rançongiciels.")
            .In(C, GroupMalware)
            .Keywords("ransomware", "rançongiciel", "controlled folder access", "dossiers protégés", "cfa", "defender")
            .Tags("security", "family")
            .Risk(RiskLevel.Moderate)
            .Requires(DefenderActive)
            .Warning("Des applications légitimes pourront être empêchées d'enregistrer dans vos dossiers : Windows affiche alors une " +
                     "notification « Modification non autorisée bloquée ». Si la protection contre les falsifications refuse le changement, " +
                     "utilisez Sécurité Windows › Protection contre les rançongiciels.")
            .WhenOn(Sys.Tool(SystemTool.PowerShell, true, "Microsoft Defender : active le contrôle d'accès aux dossiers (Set-MpPreference)",
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "Set-MpPreference -EnableControlledFolderAccess Enabled"))
            .WhenOff(Sys.Tool(SystemTool.PowerShell, true, "Microsoft Defender : désactive le contrôle d'accès aux dossiers (Set-MpPreference)",
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "Set-MpPreference -EnableControlledFolderAccess Disabled"))
            .Detect(DetectControlledFolderAccess)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("security.defender.networkprotection", "Protection du réseau (Microsoft Defender)",
                "Étend SmartScreen à toutes les applications et à tous les navigateurs : les connexions vers des domaines d'hameçonnage, " +
                "d'arnaque ou de logiciels malveillants connus sont bloquées. « Imposée » active la stratégie en mode blocage ; l'autre " +
                "position supprime la stratégie (comportement par défaut de Windows : protection inactive).")
            .In(C, GroupMalware)
            .Keywords("network protection", "hameçonnage", "phishing", "domaine malveillant", "defender", "exploit guard")
            .Tags("security", "family")
            .Risk(RiskLevel.Moderate)
            .Requires(DefenderActive)
            .Warning("Un site légitime mal classé peut être bloqué dans toutes les applications, pas seulement dans Edge.")
            .Labels("Imposée", "Réglage de Windows")
            .WhenOn(Reg.LmDword(NetworkProtectionPolicy, "EnableNetworkProtection", 1))
            .WhenOff(Reg.LmDel(NetworkProtectionPolicy, "EnableNetworkProtection"))
            .Detect(() => RegistryAccess.ReadDword(RegHive.LocalMachine, NetworkProtectionPolicy, "EnableNetworkProtection") == 1 ? On : Off)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("security.autorun", "Exécution et lecture automatiques des supports",
                "Quand c'est désactivé, Windows n'exécute plus rien et ne propose plus d'action à l'insertion d'un CD/DVD, d'une clé USB, " +
                "d'un disque ou d'un téléphone (stratégies NoDriveTypeAutoRun = 255, NoAutorun et « lecture automatique pour les " +
                "périphériques sans volume »). Depuis Windows 7, autorun.inf n'est déjà plus exécuté depuis une clé USB ; ce réglage " +
                "supprime le reste de la surface d'attaque. Vous ouvrez simplement vos supports depuis l'Explorateur.")
            .In(C, GroupMalware)
            .Keywords("autorun", "autoplay", "exécution automatique", "lecture automatique", "clé usb", "cd", "autorun.inf")
            .Tags("security", "family", "kiosk", "office")
            .WhenOn(Reg.LmDel(PolExplorer, "NoDriveTypeAutoRun"), Reg.LmDel(PolExplorer, "NoAutorun"),
                    Reg.LmDel(PolWinExplorer, "NoAutoplayfornonVolume"))
            .WhenOff(Reg.LmDword(PolExplorer, "NoDriveTypeAutoRun", 255), Reg.LmDword(PolExplorer, "NoAutorun", 1),
                     Reg.LmDword(PolWinExplorer, "NoAutoplayfornonVolume", 1))
            .WindowsDefault(On)
            .Recommend(Off)
            .Build();

        yield return Tweak.Toggle("security.wsh", "Windows Script Host (scripts .vbs et .js)",
                "Moteur qui exécute les scripts VBScript et JScript (.vbs, .vbe, .js, .wsf) par double-clic. Ces fichiers sont un vecteur " +
                "classique de pièces jointes piégées. Le désactiver pour tout le PC bloque ces scripts ; PowerShell et les fichiers .bat " +
                "ne sont pas concernés. Microsoft a d'ailleurs engagé le retrait progressif de VBScript.")
            .In(C, GroupMalware)
            .Keywords("wsh", "vbs", "vbscript", "jscript", "wscript", "cscript", "script", "pièce jointe")
            .Tags("security", "family", "kiosk")
            .Risk(RiskLevel.Moderate)
            .Warning("Certains installateurs, scripts d'ouverture de session d'entreprise ou utilitaires d'imprimante utilisent des " +
                     "scripts .vbs : ils afficheront « L'accès à Windows Script Host est désactivé sur cet ordinateur ».")
            .WhenOn(Reg.LmDel(ScriptHost, "Enabled"))
            .WhenOff(Reg.LmDword(ScriptHost, "Enabled", 0))
            .WindowsDefault(On)
            .Build();
    }

    private static string? DetectControlledFolderAccess()
    {
        // La stratégie l'emporte sur la préférence locale (1 = activé, 2 = audit, 3/4 = protection des disques).
        var value = RegistryAccess.ReadDword(RegHive.LocalMachine, CfaPolicy, "EnableControlledFolderAccess")
                    ?? RegistryAccess.ReadDword(RegHive.LocalMachine, CfaKey, "EnableControlledFolderAccess")
                    ?? 0;
        return value == 1 ? On : Off;
    }

    // =====================================================================================================
    // Système et ouverture de session
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> SystemTweaks()
    {
        yield return Tweak.Action("security.uac.recommended", "Rétablir le niveau UAC recommandé",
                "Remet le Contrôle de compte d'utilisateur (UAC) à son réglage d'origine : UAC actif (EnableLUA = 1), invite de " +
                "consentement pour les programmes non Windows (ConsentPromptBehaviorAdmin = 5) affichée sur le Bureau sécurisé " +
                "(PromptOnSecureDesktop = 1). Utile si un logiciel ou un « tutoriel » a abaissé l'UAC.")
            .In(C, GroupSystem)
            .Keywords("uac", "contrôle de compte", "élévation", "invite admin", "bureau sécurisé", "enablelua")
            .Tags("security", "family", "kiosk", "office")
            .Requires(Requires.When(_ => !SecurityProbe.UacStricterThanDefault(),
                "L'invite UAC est déjà réglée plus strictement que le niveau recommandé : utilisez plutôt « Passer l'UAC au niveau " +
                "maximal », qui réactive l'UAC et le Bureau sécurisé sans abaisser ce réglage."))
            .Warning("Si l'UAC était complètement désactivé (EnableLUA = 0), un redémarrage est nécessaire.")
            .Run(Reg.LmDword(PolSystem, "EnableLUA", 1), Reg.LmDword(PolSystem, "ConsentPromptBehaviorAdmin", 5),
                 Reg.LmDword(PolSystem, "PromptOnSecureDesktop", 1))
            .Build();

        yield return Tweak.Action("security.uac.always", "Passer l'UAC au niveau maximal (« Toujours m'avertir »)",
                "Demande une confirmation sur le Bureau sécurisé pour toute élévation, y compris quand vous modifiez vous-même des " +
                "paramètres Windows (ConsentPromptBehaviorAdmin = 2). Plus sûr pour un compte administrateur utilisé au quotidien, " +
                "mais les invites sont plus fréquentes.")
            .In(C, GroupSystem)
            .Keywords("uac", "toujours m'avertir", "niveau maximal", "contrôle de compte", "élévation")
            .Tags("security", "family", "kiosk")
            .Run(Reg.LmDword(PolSystem, "EnableLUA", 1), Reg.LmDword(PolSystem, "ConsentPromptBehaviorAdmin", 2),
                 Reg.LmDword(PolSystem, "PromptOnSecureDesktop", 1))
            .Build();

        yield return Tweak.Toggle("security.lsa.ppl", "Protection de l'autorité de sécurité locale (LSA)",
                "Exécute le processus LSASS, qui détient vos identifiants de session, en processus protégé : les outils de vol de mots " +
                "de passe (type Mimikatz) ne peuvent plus lire sa mémoire. Timonier l'active sans verrou UEFI (RunAsPPL = 2) pour " +
                "qu'elle reste désactivable. Windows 11 l'active par défaut sur les nouvelles installations compatibles.")
            .In(C, GroupSystem)
            .Keywords("lsa", "lsass", "runasppl", "identifiants", "mimikatz", "credential", "processus protégé")
            .Tags("security", "office")
            .Requires(Requires.Windows11_22H2)
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning("Des modules d'authentification ou pilotes tiers non signés par Microsoft (anciens lecteurs de cartes à puce, VPN, " +
                     "logiciels anti-triche) peuvent ne plus se charger. Si la protection a été verrouillée en UEFI (RunAsPPL = 1), " +
                     "la désactiver ici ne suffit pas.")
            .WhenOn(Reg.LmDword(Lsa, "RunAsPPL", 2))
            .WhenOff(Reg.LmDword(Lsa, "RunAsPPL", 0))
            .Detect(() => SecurityProbe.LsaProtectionConfigured() ? On : Off)
            .WindowsDefault(Off)
            .Recommend(On)
            .Build();

        yield return Tweak.Toggle("security.hvci", "Intégrité de la mémoire (isolation du noyau)",
                "Utilise la virtualisation (VBS) pour empêcher le chargement de pilotes non signés ou malveillants dans le noyau. " +
                "Activée par défaut sur les PC neufs compatibles sous Windows 11. Au redémarrage, Windows refuse de l'activer si un " +
                "pilote incompatible est présent (liste visible dans Sécurité Windows › Sécurité de l'appareil).")
            .In(C, GroupSystem)
            .Keywords("hvci", "intégrité mémoire", "isolation du noyau", "core isolation", "vbs", "memory integrity", "pilote")
            .Tags("security", "office")
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning("Un pilote ancien incompatible (périphérique, anti-triche, logiciel de virtualisation) peut cesser de fonctionner. " +
                     "Sur un processeur modeste ou ancien, une légère baisse de performances est possible.")
            .WhenOn(Reg.LmDword(Hvci, "Enabled", 1))
            .WhenOff(Reg.LmDword(Hvci, "Enabled", 0))
            .WindowsDefault(Off)
            .RecommendWhen(p => p.Tier == PerformanceTier.Low || p.IsVirtualMachine ? null : On)
            .Build();

        yield return Tweak.Toggle("security.logon.cad", "Ctrl+Alt+Suppr avant l'ouverture de session",
                "Exige d'appuyer sur Ctrl+Alt+Suppr avant de saisir son mot de passe. Cette séquence est toujours interceptée par " +
                "Windows : elle garantit que vous tapez votre mot de passe dans le véritable écran de connexion et pas dans une " +
                "imitation affichée par un programme.")
            .In(C, GroupSystem)
            .Keywords("ctrl alt suppr", "ctrl alt del", "disablecad", "ouverture de session", "écran de connexion", "sas")
            .Tags("security", "office", "kiosk")
            .Labels("Exigé", "Non exigé")
            .Warning("Sur une tablette sans clavier, la séquence s'obtient avec Windows + bouton d'alimentation.")
            .WhenOn(Reg.LmDword(PolSystem, "DisableCAD", 0))
            .WhenOff(Reg.LmDel(PolSystem, "DisableCAD"))
            .WindowsDefault(Off)
            .Build();
    }

    // =====================================================================================================
    // Accès à distance et réseau
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> RemoteTweaks()
    {
        yield return Tweak.Toggle("security.rdp", "Bureau à distance (connexions entrantes)",
                "Autorise d'autres appareils à ouvrir une session sur ce PC via le protocole RDP (port 3389). Si vous ne l'utilisez " +
                "pas, laissez-le désactivé : c'est une cible fréquente d'attaques par force brute. Activer ici n'ouvre pas le pare-feu : " +
                "pour une première configuration, passez plutôt par Paramètres › Système › Bureau à distance.")
            .In(C, GroupRemote)
            .Keywords("rdp", "bureau à distance", "remote desktop", "mstsc", "3389", "prise de contrôle")
            .Tags("security", "family", "kiosk")
            .Requires(Requires.ProOrHigher)
            .WhenOn(Reg.LmDword(TerminalServer, "fDenyTSConnections", 0))
            .WhenOff(Reg.LmDword(TerminalServer, "fDenyTSConnections", 1))
            .WindowsDefault(Off)
            .RecommendWhen(p => p.IsManaged ? null : Off)
            .Build();

        yield return Tweak.Toggle("security.remoteassistance", "Assistance à distance Windows",
                "Permet à une personne que vous invitez (fichier d'invitation ou Easy Connect) de voir et contrôler ce PC avec l'ancien " +
                "outil « Assistance à distance » (msra.exe). Rarement utilisé aujourd'hui : l'application Assistance rapide, qui " +
                "fonctionne autrement, n'est pas concernée par ce réglage.")
            .In(C, GroupRemote)
            .Keywords("assistance à distance", "remote assistance", "msra", "easy connect", "invitation", "aide à distance")
            .Tags("security", "family", "kiosk", "office")
            .WhenOn(Reg.LmDword(RemoteAssistance, "fAllowToGetHelp", 1))
            .WhenOff(Reg.LmDword(RemoteAssistance, "fAllowToGetHelp", 0))
            .WindowsDefault(On)
            .RecommendWhen(p => p.IsManaged ? null : Off)
            .Build();

        yield return Tweak.Toggle("security.remoteregistry", "Service Registre à distance",
                "Permet à un administrateur d'un autre ordinateur du réseau de lire et modifier le registre de ce PC. Désactivé par " +
                "défaut sur Windows 10 et 11 ; certains outils d'inventaire d'entreprise en ont besoin.")
            .In(C, GroupRemote)
            .Keywords("remote registry", "registre à distance", "remoteregistry", "service")
            .Tags("security", "family", "kiosk")
            .WhenOn(Sys.Service("RemoteRegistry", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("RemoteRegistry", ServiceStartKind.Disabled))
            .WindowsDefault(Off)
            .RecommendWhen(p => p.IsManaged ? null : Off)
            .Build();

        yield return Tweak.Toggle("security.llmnr", "Résolution de noms multidiffusion (LLMNR)",
                "Protocole ancien qui demande à tout le réseau local « qui est ce nom ? » quand le DNS échoue. Sur un Wi-Fi public, un " +
                "attaquant peut répondre à la place du vrai appareil et intercepter des identifiants. Le désactiver (stratégie " +
                "EnableMulticast = 0) est une recommandation classique ; la résolution DNS normale n'est pas affectée.")
            .In(C, GroupRemote)
            .Keywords("llmnr", "multicast", "multidiffusion", "résolution de noms", "responder", "empoisonnement")
            .Tags("security", "office")
            .WhenOn(Reg.LmDel(DnsClientPolicy, "EnableMulticast"))
            .WhenOff(Reg.LmDword(DnsClientPolicy, "EnableMulticast", 0))
            .WindowsDefault(On)
            .RecommendWhen(p => p.IsManaged ? null : Off)
            .Build();

        yield return Tweak.Toggle("security.smb1", "Protocole SMBv1 (partage de fichiers ancien)",
                "Première version du protocole de partage de fichiers Windows, obsolète et exploitée par les rançongiciels WannaCry et " +
                "NotPetya. Désactive le client (service mrxsmb10) et le serveur SMBv1. Pour le supprimer complètement, utilisez le bouton " +
                "« Désinstaller » de l'état de sécurité.")
            .In(C, GroupRemote)
            .Keywords("smb1", "smbv1", "cifs", "partage de fichiers", "wannacry", "nas")
            .Tags("security", "office")
            .Requires(Smb1Installed)
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning("Les très anciens NAS, box ou imprimantes/scanners réseau qui ne parlent que SMBv1 ne seront plus accessibles.")
            .WhenOn(Sys.Service("mrxsmb10", ServiceStartKind.Automatic),
                    Reg.Lm(LanmanWorkstation, "DependOnService", RegistryValueKind.MultiString, new[] { "Bowser", "MRxSmb10", "MRxSmb20", "NSI" }),
                    Reg.LmDel(LanmanServerParams, "SMB1"))
            .WhenOff(Sys.Service("mrxsmb10", ServiceStartKind.Disabled),
                     Reg.Lm(LanmanWorkstation, "DependOnService", RegistryValueKind.MultiString, new[] { "Bowser", "MRxSmb20", "NSI" }),
                     Reg.LmDword(LanmanServerParams, "SMB1", 0))
            .Detect(() => SecurityProbe.Smb1Enabled() switch { true => On, false => Off, null => null })
            .WindowsDefault(Off)
            .Recommend(Off)
            .Build();

        yield return Tweak.Toggle("security.adminshares", "Partages administratifs automatiques (C$, ADMIN$)",
                "Windows partage automatiquement chaque disque (C$…) et le dossier Windows (ADMIN$) pour l'administration à distance. " +
                "Ils ne sont accessibles qu'aux administrateurs et, par défaut, pas aux comptes locaux via le réseau : les supprimer " +
                "réduit un peu la surface d'attaque sur un PC personnel.")
            .In(C, GroupRemote)
            .Keywords("partage administratif", "c$", "admin$", "autosharewks", "partage caché")
            .Tags("security", "kiosk")
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning("Les outils d'administration ou de sauvegarde réseau qui utilisent \\\\PC\\C$ ne fonctionneront plus.")
            .WhenOn(Reg.LmDel(LanmanServerParams, "AutoShareWks"))
            .WhenOff(Reg.LmDword(LanmanServerParams, "AutoShareWks", 0))
            .WindowsDefault(On)
            .Build();
    }

    // =====================================================================================================
    // Règles ASR (Microsoft Defender) — mode avancé
    // =====================================================================================================

    private sealed record AsrRule(string Suffix, string Guid, string Title, string Description, string[] Keywords);

    private static readonly AsrRule[] AsrRules =
    [
        new("lsass", "9E6C4E1F-7D60-472F-BA1A-A39EF669E4B2", "ASR : bloquer le vol d'identifiants dans LSASS",
            "Empêche les programmes non fiables d'ouvrir la mémoire du processus LSASS pour y voler des mots de passe. Règle très peu " +
            "intrusive, recommandée par Microsoft en blocage.", ["lsass", "mimikatz", "identifiants"]),
        new("drivers", "56A863A9-875E-4185-98A7-B882C64B5CE5", "ASR : bloquer l'abus de pilotes signés vulnérables",
            "Empêche l'installation de pilotes légitimes mais connus pour être vulnérables, utilisés par les attaquants pour " +
            "désactiver les protections du noyau.", ["pilote vulnérable", "byovd", "driver"]),
        new("email", "BE9BA2D9-53EA-4CDC-84E5-9B1EEEE46550", "ASR : bloquer le contenu exécutable des e-mails et webmails",
            "Empêche l'exécution de programmes et scripts ouverts directement depuis Outlook ou un webmail.", ["mail", "pièce jointe", "outlook"]),
        new("scriptdl", "D3E037E1-3EB8-44C8-A917-57927947596D", "ASR : empêcher JavaScript et VBScript de lancer du contenu téléchargé",
            "Bloque les scripts qui téléchargent puis exécutent un programme, technique fréquente des pièces jointes piégées.",
            ["javascript", "vbscript", "téléchargement", "dropper"]),
        new("obfuscated", "5BEB7EFE-FD9A-4556-801D-275E5FFC04CC", "ASR : bloquer les scripts potentiellement obscurcis",
            "Détecte les scripts (PowerShell, VBScript, JavaScript) dont le code est volontairement masqué. Peut signaler certains " +
            "scripts d'administration légitimes : commencez par l'audit.", ["obfuscation", "powershell", "script masqué"]),
        new("officechild", "D4F940AB-401B-4EFC-AADC-AD5F3C50688A", "ASR : empêcher les applications Office de créer des processus enfants",
            "Word, Excel, PowerPoint… ne peuvent plus lancer d'autres programmes (technique des macros malveillantes). Certains " +
            "compléments Office légitimes peuvent être bloqués.", ["office", "macro", "word", "excel"]),
        new("usb", "B2B3F03D-6A65-4F7B-A9C7-1C7EF74A9BA4", "ASR : bloquer les processus non signés lancés depuis une clé USB",
            "Les programmes non signés ou non fiables présents sur un support amovible ne peuvent plus être exécutés.", ["usb", "clé usb", "amovible"]),
        new("wmi", "E6DB77E5-3DF2-4CF1-B95A-636979351E5B", "ASR : bloquer la persistance via les abonnements WMI",
            "Empêche les logiciels malveillants de se relancer automatiquement grâce aux abonnements aux événements WMI.", ["wmi", "persistance"]),
    ];

    private static IEnumerable<TweakDefinition> AsrTweaks()
    {
        foreach (var rule in AsrRules)
        {
            yield return Tweak.Choice("security.asr." + rule.Suffix, rule.Title,
                    rule.Description + " « Audit » se contente de journaliser (Observateur d'événements › Windows Defender › " +
                    "Operational, événement 1122) : idéal pour tester avant de bloquer.")
                .In(C, GroupAsr)
                .Keywords([.. rule.Keywords, "asr", "attack surface reduction", "réduction de la surface d'attaque", "exploit guard", "defender"])
                .Tags("security")
                .Risk(RiskLevel.Advanced)
                .Requires(Requires.ProOrHigher.And(DefenderActive))
                .Warning("Les règles ASR ne fonctionnent que si Microsoft Defender est l'antivirus actif. En mode blocage, une " +
                         "application légitime peut être empêchée d'agir : repassez la règle en audit si c'est le cas.")
                .Option("off", "Non configurée", Reg.LmDel(AsrRulesPolicy, rule.Guid))
                .Option("audit", "Audit (journal uniquement)",
                    Reg.LmDword(AsrPolicy, "ExploitGuard_ASR_Rules", 1), Reg.LmString(AsrRulesPolicy, rule.Guid, "2"))
                .Option("block", "Bloquer",
                    Reg.LmDword(AsrPolicy, "ExploitGuard_ASR_Rules", 1), Reg.LmString(AsrRulesPolicy, rule.Guid, "1"))
                .WindowsDefault("off")
                .Build();
        }
    }
}
