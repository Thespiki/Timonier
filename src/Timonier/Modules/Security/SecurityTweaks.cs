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

    public static string GroupRemote => L("Remote access and network sharing");
    public static string GroupMalware => L("Malware protection");
    public static string GroupSystem => L("System and sign-in protection");
    public static string GroupAsr => L("Attack surface reduction (ASR) rules");

    public static string[] Groups => [GroupMalware, GroupSystem, GroupRemote, GroupAsr];

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
        L("Requires Microsoft Defender to be the active antivirus (another antivirus is installed or Defender is turned off)."));

    private static readonly Requirement Smb1Installed = Requires.When(_ => RegistryAccess.KeyExists(RegHive.LocalMachine, Smb1ClientService),
        L("The SMBv1 protocol isn't installed on this PC (the default since Windows 10 1709): there's nothing to turn off."));

    public static IEnumerable<TweakDefinition> All() =>
        MalwareTweaks().Concat(SystemTweaks()).Concat(RemoteTweaks()).Concat(AsrTweaks());

    // =====================================================================================================
    // Logiciels malveillants
    // =====================================================================================================

    private static IEnumerable<TweakDefinition> MalwareTweaks()
    {
        yield return Tweak.Toggle("security.smartscreen.apps", L("SmartScreen for apps and files"),
                L("Checks the reputation of downloaded programs and files before they open and warns you if they're unknown or malicious. “Enforced” sets SmartScreen to “Warn” by policy (Windows Security then shows “managed by your organization”); the other position simply removes the policy and hands control back to Windows Security. Timonier never offers to turn off SmartScreen."))
            .In(C, GroupMalware)
            .Keywords(L("smartscreen, reputation, download, unknown app, defender smartscreen, filter"))
            .Tags("security", "family", "kiosk", "office")
            .Labels(L("Enforced (warn)"), L("Windows Security setting"))
            .WhenOn(Reg.LmDword(SystemPolicy, "EnableSmartScreen", 1), Reg.LmString(SystemPolicy, "ShellSmartScreenLevel", "Warn"))
            .WhenOff(Reg.LmDel(SystemPolicy, "EnableSmartScreen"), Reg.LmDel(SystemPolicy, "ShellSmartScreenLevel"))
            .Detect(() => RegistryAccess.ReadDword(RegHive.LocalMachine, SystemPolicy, "EnableSmartScreen") == 1 ? On : Off)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("security.defender.pua", L("Potentially unwanted app (PUA) blocking"),
                L("Microsoft Defender blocks “borderline” software: toolbars, cryptocurrency miners, adware installers, fake optimizers. “Enforced” turns on the PUAProtection policy; the other position removes the policy and leaves the choice to Windows Security (App & browser control › Reputation-based protection)."))
            .In(C, GroupMalware)
            .Keywords(L("pua, pup, adware, unwanted software, bloatware, defender, reputation"))
            .Tags("security", "family", "kiosk", "office")
            .Requires(DefenderActive)
            .Labels(L("Enforced"), L("Windows Security setting"))
            .WhenOn(Reg.LmDword(DefenderPolicy, "PUAProtection", 1))
            .WhenOff(Reg.LmDel(DefenderPolicy, "PUAProtection"))
            .Detect(() => RegistryAccess.ReadDword(RegHive.LocalMachine, DefenderPolicy, "PUAProtection") == 1 ? On : Off)
            .WindowsDefault(Off)
            .RecommendWhen(p => p.IsManaged ? null : On)
            .Build();

        yield return Tweak.Toggle("security.defender.cfa", L("Ransomware-protected folders"),
                L("Microsoft Defender Controlled folder access: only trusted apps can change Documents, Pictures, Videos, Music, Desktop and Favorites. Protects effectively against ransomware, but Windows also blocks unknown legitimate apps (games, photo editors, backup tools): you then have to allow them in Windows Security › Ransomware protection."))
            .In(C, GroupMalware)
            .Keywords(L("ransomware, controlled folder access, protected folders, cfa, defender"))
            .Tags("security", "family")
            .Risk(RiskLevel.Moderate)
            .Requires(DefenderActive)
            .Warning(L("Legitimate apps may be prevented from saving to your folders: Windows then shows an “Unauthorized changes blocked” notification. If Tamper Protection rejects the change, use Windows Security › Ransomware protection."))
            .WhenOn(Sys.Tool(SystemTool.PowerShell, true, L("Microsoft Defender: turns on Controlled folder access (Set-MpPreference)"),
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "Set-MpPreference -EnableControlledFolderAccess Enabled"))
            .WhenOff(Sys.Tool(SystemTool.PowerShell, true, L("Microsoft Defender: turns off Controlled folder access (Set-MpPreference)"),
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "Set-MpPreference -EnableControlledFolderAccess Disabled"))
            .Detect(DetectControlledFolderAccess)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("security.defender.networkprotection", L("Network protection (Microsoft Defender)"),
                L("Extends SmartScreen to all apps and all browsers: connections to known phishing, scam or malware domains are blocked. “Enforced” turns on the policy in block mode; the other position removes the policy (Windows default behavior: protection off)."))
            .In(C, GroupMalware)
            .Keywords(L("network protection, phishing, malicious domain, scam, defender, exploit guard, smartscreen"))
            .Tags("security", "family")
            .Risk(RiskLevel.Moderate)
            .Requires(DefenderActive)
            .Warning(L("A misclassified legitimate site can be blocked in all apps, not just in Edge."))
            .Labels(LC("feminine", "Enforced"), L("Windows setting"))
            .WhenOn(Reg.LmDword(NetworkProtectionPolicy, "EnableNetworkProtection", 1))
            .WhenOff(Reg.LmDel(NetworkProtectionPolicy, "EnableNetworkProtection"))
            .Detect(() => RegistryAccess.ReadDword(RegHive.LocalMachine, NetworkProtectionPolicy, "EnableNetworkProtection") == 1 ? On : Off)
            .WindowsDefault(Off)
            .Build();

        yield return Tweak.Toggle("security.autorun", L("Media AutoRun and AutoPlay"),
                L("When it's off, Windows no longer runs anything or suggests an action when you insert a CD/DVD, USB drive, disk or phone (NoDriveTypeAutoRun = 255, NoAutorun and “AutoPlay for non-volume devices” policies). Since Windows 7, autorun.inf no longer runs from a USB drive anyway; this setting removes the rest of the attack surface. You simply open your media from File Explorer."))
            .In(C, GroupMalware)
            .Keywords(L("autorun, autoplay, usb drive, usb stick, flash drive, cd, dvd, autorun.inf"))
            .Tags("security", "family", "kiosk", "office")
            .WhenOn(Reg.LmDel(PolExplorer, "NoDriveTypeAutoRun"), Reg.LmDel(PolExplorer, "NoAutorun"),
                    Reg.LmDel(PolWinExplorer, "NoAutoplayfornonVolume"))
            .WhenOff(Reg.LmDword(PolExplorer, "NoDriveTypeAutoRun", 255), Reg.LmDword(PolExplorer, "NoAutorun", 1),
                     Reg.LmDword(PolWinExplorer, "NoAutoplayfornonVolume", 1))
            .WindowsDefault(On)
            .Recommend(Off)
            .Build();

        yield return Tweak.Toggle("security.wsh", L("Windows Script Host (.vbs and .js scripts)"),
                L("Engine that runs VBScript and JScript scripts (.vbs, .vbe, .js, .wsf) on double-click. These files are a classic vector for booby-trapped attachments. Turning it off for the whole PC blocks these scripts; PowerShell and .bat files aren't affected. Microsoft has also started phasing out VBScript."))
            .In(C, GroupMalware)
            .Keywords(L("wsh, vbs, vbscript, jscript, wscript, cscript, script, attachment"))
            .Tags("security", "family", "kiosk")
            .Risk(RiskLevel.Moderate)
            .Warning(L("Some installers, corporate sign-in scripts or printer utilities use .vbs scripts: they'll show “Windows Script Host access is disabled on this machine”."))
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
        yield return Tweak.Action("security.uac.recommended", L("Restore the recommended UAC level"),
                L("Resets User Account Control (UAC) to its original setting: UAC on (EnableLUA = 1), consent prompt for non-Windows programs (ConsentPromptBehaviorAdmin = 5) shown on the secure desktop (PromptOnSecureDesktop = 1). Useful if software or a “tutorial” lowered UAC."))
            .In(C, GroupSystem)
            .Keywords(L("uac, user account control, elevation, admin prompt, secure desktop, enablelua"))
            .Tags("security", "family", "kiosk", "office")
            .Requires(Requires.When(_ => !SecurityProbe.UacStricterThanDefault(),
                L("The UAC prompt is already set stricter than the recommended level: use “Set UAC to the highest level” instead, which turns UAC and the secure desktop back on without lowering this setting.")))
            .Warning(L("If UAC was completely turned off (EnableLUA = 0), a restart is required."))
            .Run(Reg.LmDword(PolSystem, "EnableLUA", 1), Reg.LmDword(PolSystem, "ConsentPromptBehaviorAdmin", 5),
                 Reg.LmDword(PolSystem, "PromptOnSecureDesktop", 1))
            .Build();

        yield return Tweak.Action("security.uac.always", L("Set UAC to the highest level (“Always notify”)"),
                L("Asks for confirmation on the secure desktop for every elevation, including when you change Windows settings yourself (ConsentPromptBehaviorAdmin = 2). Safer for an administrator account used every day, but prompts are more frequent."))
            .In(C, GroupSystem)
            .Keywords(L("uac, always notify, highest level, maximum level, user account control, elevation"))
            .Tags("security", "family", "kiosk")
            .Run(Reg.LmDword(PolSystem, "EnableLUA", 1), Reg.LmDword(PolSystem, "ConsentPromptBehaviorAdmin", 2),
                 Reg.LmDword(PolSystem, "PromptOnSecureDesktop", 1))
            .Build();

        yield return Tweak.Toggle("security.lsa.ppl", L("Local Security Authority (LSA) protection"),
                L("Runs the LSASS process, which holds your sign-in credentials, as a protected process: password-stealing tools (like Mimikatz) can no longer read its memory. Timonier turns it on without a UEFI lock (RunAsPPL = 2) so it can still be turned off. Windows 11 turns it on by default on new compatible installations."))
            .In(C, GroupSystem)
            .Keywords(L("lsa, lsass, runasppl, credentials, mimikatz, protected process, password theft"))
            .Tags("security", "office")
            .Requires(Requires.Windows11_22H2)
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning(L("Authentication modules or third-party drivers not signed by Microsoft (old smart card readers, VPNs, anti-cheat software) may no longer load. If the protection was locked in UEFI (RunAsPPL = 1), turning it off here isn't enough."))
            .WhenOn(Reg.LmDword(Lsa, "RunAsPPL", 2))
            .WhenOff(Reg.LmDword(Lsa, "RunAsPPL", 0))
            .Detect(() => SecurityProbe.LsaProtectionConfigured() ? On : Off)
            .WindowsDefault(Off)
            .Recommend(On)
            .Build();

        yield return Tweak.Toggle("security.hvci", L("Memory integrity (core isolation)"),
                L("Uses virtualization (VBS) to prevent unsigned or malicious drivers from loading into the kernel. On by default on new compatible PCs running Windows 11. At restart, Windows refuses to turn it on if an incompatible driver is present (list shown in Windows Security › Device security)."))
            .In(C, GroupSystem)
            .Keywords(L("hvci, memory integrity, core isolation, kernel isolation, vbs, driver"))
            .Tags("security", "office")
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning(L("An old incompatible driver (device, anti-cheat, virtualization software) may stop working. On a low-end or older processor, a slight performance drop is possible."))
            .WhenOn(Reg.LmDword(Hvci, "Enabled", 1))
            .WhenOff(Reg.LmDword(Hvci, "Enabled", 0))
            .WindowsDefault(Off)
            .RecommendWhen(p => p.Tier == PerformanceTier.Low || p.IsVirtualMachine ? null : On)
            .Build();

        yield return Tweak.Toggle("security.logon.cad", L("Ctrl+Alt+Delete before sign-in"),
                L("Requires pressing Ctrl+Alt+Delete before typing your password. Windows always intercepts this key sequence: it guarantees you're typing your password on the real sign-in screen and not in an imitation shown by a program."))
            .In(C, GroupSystem)
            .Keywords(L("ctrl alt del, ctrl alt delete, disablecad, sign-in, login, sign-in screen, sas"))
            .Tags("security", "office", "kiosk")
            .Labels(L("Required"), L("Not required"))
            .Warning(L("On a tablet without a keyboard, you get this sequence with Windows + power button."))
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
        yield return Tweak.Toggle("security.rdp", L("Remote Desktop (incoming connections)"),
                L("Lets other devices sign in to this PC via the RDP protocol (port 3389). If you don't use it, leave it off: it's a frequent target of brute-force attacks. Turning it on here doesn't open the firewall: for a first-time setup, use Settings › System › Remote Desktop instead."))
            .In(C, GroupRemote)
            .Keywords(L("rdp, remote desktop, mstsc, 3389, remote control, remote access"))
            .Tags("security", "family", "kiosk")
            .Requires(Requires.ProOrHigher)
            .WhenOn(Reg.LmDword(TerminalServer, "fDenyTSConnections", 0))
            .WhenOff(Reg.LmDword(TerminalServer, "fDenyTSConnections", 1))
            .WindowsDefault(Off)
            .RecommendWhen(p => p.IsManaged ? null : Off)
            .Build();

        yield return Tweak.Toggle("security.remoteassistance", L("Windows Remote Assistance"),
                L("Lets someone you invite (invitation file or Easy Connect) see and control this PC with the old “Remote Assistance” tool (msra.exe). Rarely used today: the Quick Assist app, which works differently, isn't affected by this setting."))
            .In(C, GroupRemote)
            .Keywords(L("remote assistance, msra, easy connect, invitation, remote help"))
            .Tags("security", "family", "kiosk", "office")
            .WhenOn(Reg.LmDword(RemoteAssistance, "fAllowToGetHelp", 1))
            .WhenOff(Reg.LmDword(RemoteAssistance, "fAllowToGetHelp", 0))
            .WindowsDefault(On)
            .RecommendWhen(p => p.IsManaged ? null : Off)
            .Build();

        yield return Tweak.Toggle("security.remoteregistry", L("Remote Registry service"),
                L("Lets an administrator on another computer on the network read and change this PC's registry. Off by default on Windows 10 and 11; some corporate inventory tools need it."))
            .In(C, GroupRemote)
            .Keywords(L("remote registry, remoteregistry, service"))
            .Tags("security", "family", "kiosk")
            .WhenOn(Sys.Service("RemoteRegistry", ServiceStartKind.Manual))
            .WhenOff(Sys.Service("RemoteRegistry", ServiceStartKind.Disabled))
            .WindowsDefault(Off)
            .RecommendWhen(p => p.IsManaged ? null : Off)
            .Build();

        yield return Tweak.Toggle("security.llmnr", L("Multicast name resolution (LLMNR)"),
                L("Old protocol that asks the entire local network “who is this name?” when DNS fails. On public Wi-Fi, an attacker can answer instead of the real device and intercept credentials. Turning it off (EnableMulticast = 0 policy) is a classic recommendation; normal DNS resolution isn't affected."))
            .In(C, GroupRemote)
            .Keywords(L("llmnr, multicast, name resolution, responder, poisoning, spoofing"))
            .Tags("security", "office")
            .WhenOn(Reg.LmDel(DnsClientPolicy, "EnableMulticast"))
            .WhenOff(Reg.LmDword(DnsClientPolicy, "EnableMulticast", 0))
            .WindowsDefault(On)
            .RecommendWhen(p => p.IsManaged ? null : Off)
            .Build();

        yield return Tweak.Toggle("security.smb1", L("SMBv1 protocol (old file sharing)"),
                L("First version of the Windows file sharing protocol, obsolete and exploited by the WannaCry and NotPetya ransomware. Turns off the SMBv1 client (mrxsmb10 service) and server. To remove it completely, use the “Uninstall” button in the security status."))
            .In(C, GroupRemote)
            .Keywords(L("smb1, smbv1, cifs, file sharing, wannacry, nas"))
            .Tags("security", "office")
            .Requires(Smb1Installed)
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning(L("Very old NAS devices, routers or network printers/scanners that only speak SMBv1 will no longer be reachable."))
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

        yield return Tweak.Toggle("security.adminshares", L("Automatic administrative shares (C$, ADMIN$)"),
                L("Windows automatically shares each drive (C$…) and the Windows folder (ADMIN$) for remote administration. They're only accessible to administrators and, by default, not to local accounts over the network: removing them slightly reduces the attack surface on a personal PC."))
            .In(C, GroupRemote)
            .Keywords(L("administrative share, admin share, c$, admin$, autosharewks, hidden share"))
            .Tags("security", "kiosk")
            .Risk(RiskLevel.Moderate)
            .Effect(ApplyEffect.Reboot)
            .Warning(L("Network administration or backup tools that use \\\\PC\\C$ will stop working."))
            .WhenOn(Reg.LmDel(LanmanServerParams, "AutoShareWks"))
            .WhenOff(Reg.LmDword(LanmanServerParams, "AutoShareWks", 0))
            .WindowsDefault(On)
            .Build();
    }

    // =====================================================================================================
    // Règles ASR (Microsoft Defender) — mode avancé
    // =====================================================================================================

    private sealed record AsrRule(string Suffix, string Guid, string Title, string Description, string Keywords);

    private static readonly AsrRule[] AsrRules =
    [
        new("lsass", "9E6C4E1F-7D60-472F-BA1A-A39EF669E4B2", L("ASR: block credential stealing from LSASS"),
            L("Prevents untrusted programs from opening the memory of the LSASS process to steal passwords. A very unintrusive rule, recommended by Microsoft in block mode."), L("lsass, mimikatz, credentials")),
        new("drivers", "56A863A9-875E-4185-98A7-B882C64B5CE5", L("ASR: block abuse of exploited vulnerable signed drivers"),
            L("Prevents installing legitimate drivers that are known to be vulnerable, which attackers use to turn off kernel protections."), L("vulnerable driver, byovd, driver")),
        new("email", "BE9BA2D9-53EA-4CDC-84E5-9B1EEEE46550", L("ASR: block executable content from email and webmail"),
            L("Prevents programs and scripts opened directly from Outlook or webmail from running."), L("mail, email, attachment, outlook")),
        new("scriptdl", "D3E037E1-3EB8-44C8-A917-57927947596D", L("ASR: block JavaScript or VBScript from launching downloaded content"),
            L("Blocks scripts that download and then run a program, a common technique in booby-trapped attachments."),
            L("javascript, vbscript, download, dropper")),
        new("obfuscated", "5BEB7EFE-FD9A-4556-801D-275E5FFC04CC", L("ASR: block execution of potentially obfuscated scripts"),
            L("Detects scripts (PowerShell, VBScript, JavaScript) whose code is deliberately hidden. May flag some legitimate administration scripts: start with audit mode."), L("obfuscation, powershell, obfuscated script, hidden script")),
        new("officechild", "D4F940AB-401B-4EFC-AADC-AD5F3C50688A", L("ASR: block Office apps from creating child processes"),
            L("Word, Excel, PowerPoint… can no longer start other programs (the malicious macro technique). Some legitimate Office add-ins may be blocked."), L("office, macro, word, excel")),
        new("usb", "B2B3F03D-6A65-4F7B-A9C7-1C7EF74A9BA4", L("ASR: block unsigned processes that run from USB"),
            L("Unsigned or untrusted programs on removable media can no longer run."), L("usb, usb drive, flash drive, removable")),
        new("wmi", "E6DB77E5-3DF2-4CF1-B95A-636979351E5B", L("ASR: block persistence through WMI event subscription"),
            L("Prevents malware from restarting itself automatically through WMI event subscriptions."), L("wmi, persistence")),
    ];

    private static IEnumerable<TweakDefinition> AsrTweaks()
    {
        foreach (var rule in AsrRules)
        {
            yield return Tweak.Choice("security.asr." + rule.Suffix, rule.Title,
                    rule.Description + " " + L("“Audit” only logs events (Event Viewer › Windows Defender › Operational, event 1122): ideal for testing before blocking."))
                .In(C, GroupAsr)
                .Keywords(rule.Keywords, L("asr, attack surface reduction, exploit guard, defender"))
                .Tags("security")
                .Risk(RiskLevel.Advanced)
                .Requires(Requires.ProOrHigher.And(DefenderActive))
                .Warning(L("ASR rules only work if Microsoft Defender is the active antivirus. In block mode, a legitimate app may be prevented from acting: switch the rule back to audit if that happens."))
                .Option("off", L("Not configured"), Reg.LmDel(AsrRulesPolicy, rule.Guid))
                .Option("audit", L("Audit (log only)"),
                    Reg.LmDword(AsrPolicy, "ExploitGuard_ASR_Rules", 1), Reg.LmString(AsrRulesPolicy, rule.Guid, "2"))
                .Option("block", L("Block"),
                    Reg.LmDword(AsrPolicy, "ExploitGuard_ASR_Rules", 1), Reg.LmString(AsrRulesPolicy, rule.Guid, "1"))
                .WindowsDefault("off")
                .Build();
        }
    }
}
