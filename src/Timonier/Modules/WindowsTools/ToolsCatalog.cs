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
        (ToolGroup.Admin, L("Admin consoles"), ""),
        (ToolGroup.Diagnostic, L("Diagnostics"), ""),
        (ToolGroup.System, L("System"), ""),
        (ToolGroup.ControlPanel, L("Control Panel"), ""),
        (ToolGroup.Folders, L("Special folders"), ""),
        (ToolGroup.Accessories, L("Accessories"), ""),
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
        yield return Msc("devmgmt", "devmgmt.msc", L("Device Manager"),
            L("Detected hardware, drivers, devices with errors or disabled."), "",
            L("device manager, drivers, hardware"));
        yield return Msc("diskmgmt", "diskmgmt.msc", L("Disk Management"),
            L("Partitions, drive letters, formatting, extending or shrinking volumes."), "",
            L("partition, format, drive letter, disk management, volume"));
        yield return Msc("compmgmt", "compmgmt.msc", L("Computer Management"),
            L("Console that brings together logs, shared folders, users, disks and services."), "",
            L("computer management"));
        yield return Msc("eventvwr", "eventvwr.msc", L("Event Viewer"),
            L("Error and warning logs from Windows and apps."), "",
            L("event viewer, event logs, logs, windows errors, events"));
        yield return Msc("services", "services.msc", L("Services"),
            L("Classic console for all Windows services (startup, status, account)."), "",
            inSearch: false, keywords: [L("windows services, services")]);
        yield return Msc("taskschd", "taskschd.msc", L("Task Scheduler"),
            L("Scheduled tasks from Windows and apps."), "",
            inSearch: false, keywords: [L("task scheduler, scheduled tasks")]);
        yield return Msc("wf", "wf.msc", L("Firewall with Advanced Security"),
            L("Inbound and outbound rules of Windows Defender Firewall, profiles, logging."), "",
            inSearch: false, keywords: [L("firewall, firewall rules")]);
        yield return Msc("certmgr", "certmgr.msc", L("Certificates (current user)"),
            L("Personal certificates and trusted authorities for your account."), "",
            L("certificate, certificate authority"));
        yield return Msc("certlm", "certlm.msc", L("Certificates (local computer)"),
            L("Computer certificates, used by services and all accounts."), "",
            L("machine certificate, certificate, local machine"));
        yield return Msc("fsmgmt", "fsmgmt.msc", L("Shared Folders"),
            L("Shares on this PC, open sessions and files used remotely."), "",
            L("shares, shared folders, smb, network share"));
        yield return Msc("comexp", "comexp.msc", L("Component Services"),
            L("COM+ applications, DCOM configuration and Distributed Transaction Coordinator."), "",
            L("com+, dcom, dcomcnfg, component services"));
        yield return Msc("tpm", "tpm.msc", L("TPM Management"),
            L("Status, manufacturer and version of the TPM security chip."), "",
            L("tpm, security chip, trusted platform module"));
        yield return Msc("gpedit", "gpedit.msc", L("Local Group Policy Editor"),
            L("Computer and user policies (administrative templates)."), "",
            proOnly: true, keywords: [L("gpedit, group policy, gpo")]);
        yield return Msc("secpol", "secpol.msc", L("Local Security Policy"),
            L("Password and audit policies, user rights, security options."), "",
            proOnly: true, keywords: [L("secpol, local security policy, security policy")]);
        yield return Msc("lusrmgr", "lusrmgr.msc", L("Local Users and Groups"),
            L("Local accounts, groups (Administrators, Users…) and their members."), "",
            proOnly: true, inSearch: false, keywords: [L("lusrmgr, local users and groups, groups, local accounts")]);
        yield return Msc("printmanagement", "printmanagement.msc", L("Print Management"),
            L("Printers, printer drivers, queues and ports."), "",
            proOnly: true, keywords: [L("print management, printer drivers, print queue")]);

        // ---------------------------------------------------------------- Diagnostic
        yield return Exe("msinfo32", SystemTool.Msinfo32, "msinfo32", L("System Information"),
            L("Full inventory: hardware, drivers, BIOS, Secure Boot, components."), ToolGroup.Diagnostic, "",
            keywords: [L("msinfo32, msinfo, system information, hardware configuration, bios")]);
        yield return Exe("resmon", SystemTool.Resmon, "resmon", L("Resource Monitor"),
            L("CPU, memory, disk and network per process, in real time."), ToolGroup.Diagnostic, "",
            keywords: [L("resource monitor, resmon, disk usage, network activity")]);
        yield return Exe("perfmon", SystemTool.Perfmon, "perfmon", L("Performance Monitor"),
            L("Performance counters and data collector sets."), ToolGroup.Diagnostic, "",
            admin: true, keywords: [L("performance monitor, perfmon, counters")]);
        yield return Exe("reliability", SystemTool.Perfmon, "perfmon /rel", L("Reliability Monitor"),
            L("Day-by-day history of crashes, errors and installations."), ToolGroup.Diagnostic, "",
            args: ["/rel"], keywords: [L("reliability monitor, reliability history, crashes, crash")]);
        yield return Exe("taskmgr", SystemTool.Taskmgr, "taskmgr", L("Task Manager"),
            L("Processes, performance, startup apps and services."), ToolGroup.Diagnostic, "",
            keywords: [L("task manager, processes, ctrl shift esc")]);
        yield return Exe("dxdiag", SystemTool.Dxdiag, "dxdiag", L("DirectX Diagnostic Tool"),
            L("Graphics card, drivers, sound and DirectX features."), ToolGroup.Diagnostic, "",
            keywords: [L("directx, dxdiag, graphics card, gpu, graphics diagnostics")]);
        yield return Exe("mdsched", SystemTool.Mdsched, "mdsched", L("Windows Memory Diagnostic"),
            L("Tests RAM at the next restart (you choose when to restart)."), ToolGroup.Diagnostic, "",
            admin: true, keywords: [L("memory test, ram test, memory diagnostic, mdsched")]);

        // ---------------------------------------------------------------- Système
        yield return Exe("msconfig", SystemTool.Msconfig, "msconfig", L("System Configuration"),
            L("Selective startup, boot options (safe mode), services."), ToolGroup.System, "",
            admin: true, keywords: [L("msconfig, safe mode, selective startup, boot")]);
        yield return Exe("regedit", SystemTool.Regedit, "regedit", L("Registry Editor"),
            L("Advanced Registry editing: a mistake can prevent Windows from starting."), ToolGroup.System, "",
            admin: true, keywords: [L("regedit, registry, registry editor")]);
        yield return Exe("sysadvanced", SystemTool.SystemPropertiesAdvanced, "SystemPropertiesAdvanced", L("Advanced system settings"),
            L("Environment variables, user profiles, startup and recovery."), ToolGroup.System, "",
            admin: true, keywords: [L("environment variables, path, system properties, sysdm")]);
        yield return Exe("sysperformance", SystemTool.SystemPropertiesPerformance, "SystemPropertiesPerformance", L("Performance Options"),
            L("Visual effects, virtual memory (paging file), Data Execution Prevention."), ToolGroup.System, "",
            admin: true, inSearch: false, keywords: [L("virtual memory, paging file, pagefile, swap file, visual effects")]);
        yield return Exe("sysprotection", SystemTool.SystemPropertiesProtection, "SystemPropertiesProtection", L("System Protection"),
            L("Turn on protection, create or configure restore points."), ToolGroup.System, "",
            admin: true, keywords: [L("restore point, system protection, create a restore point")]);
        yield return Exe("rstrui", SystemTool.Rstrui, "rstrui", L("System Restore"),
            L("Go back to an earlier restore point (your files aren't affected)."), ToolGroup.System, "",
            admin: true, inSearch: false, keywords: [L("system restore, rstrui, roll back, go back")]);
        yield return Sys32("computername", "SystemPropertiesComputerName.exe", L("Computer name and domain"),
            L("Rename the PC, join a workgroup or a domain."), ToolGroup.System, "",
            admin: true, keywords: [L("pc name, computer name, rename pc, workgroup, domain")]);
        yield return Sys32("sysremote", "SystemPropertiesRemote.exe", L("Remote access"),
            L("Remote Assistance and Remote Desktop (System Properties)."), ToolGroup.System, "",
            admin: true, keywords: [L("remote assistance, remote desktop, rdp")]);
        yield return Exe("netplwiz", SystemTool.Netplwiz, "netplwiz", L("User Accounts (advanced)"),
            L("Account list, group membership, advanced password management."), ToolGroup.System, "",
            admin: true, inSearch: false, keywords: [L("netplwiz, control userpasswords2, user accounts")]);
        yield return Exe("cleanmgr", SystemTool.CleanMgr, "cleanmgr", L("Disk Cleanup"),
            L("Temporary files, thumbnails, old updates (“Clean up system files”)."), ToolGroup.System, "",
            inSearch: false, keywords: [L("cleanmgr, disk cleanup, free up space")]);
        yield return Sys32("dfrgui", "dfrgui.exe", L("Optimize Drives"),
            L("Hard drive defragmentation, SSD TRIM and scheduling."), ToolGroup.System, "",
            admin: true, keywords: [L("defragment, defragmentation, defrag, trim, optimize drives")]);
        yield return Sys32("optionalfeatures", "optionalfeatures.exe", L("Windows Features"),
            L("Turn .NET 3.5, Hyper-V, WSL, Sandbox, SMB clients… on or off."), ToolGroup.System, "",
            admin: true, keywords: [L("windows features, hyper-v, wsl, sandbox, net framework 3.5")]);
        yield return Sys32("recoverydrive", "RecoveryDrive.exe", L("Create a recovery drive"),
            L("Rescue USB drive to repair or reinstall Windows."), ToolGroup.System, "",
            admin: true, keywords: [L("recovery drive, rescue drive, usb repair drive")]);
        yield return Exe("winver", SystemTool.Winver, "winver", L("About Windows"),
            L("Exact version, build and edition of Windows installed."), ToolGroup.System, "",
            keywords: [L("winver, windows version, build, version number")]);

        // ---------------------------------------------------------------- Panneau de configuration
        yield return Cpl("programs", "Microsoft.ProgramsAndFeatures", L("Programs and Features"),
            L("Classic list of installed programs: uninstall, change, repair."), "",
            L("appwiz, add remove programs, uninstall a program"));
        yield return Cpl("power", "Microsoft.PowerOptions", L("Power Options"),
            L("Classic power plans and advanced settings."), "",
            inSearch: false, keywords: [L("powercfg.cpl, power plan, advanced power settings")]);
        yield return Cpl("netcenter", "Microsoft.NetworkAndSharingCenter", L("Network and Sharing Center"),
            L("Connection status, advanced sharing settings, adapter properties."), "",
            L("network and sharing center, network sharing, network center"));
        yield return new WinTool
        {
            Key = "ncpa", Title = L("Network Connections"), Group = ToolGroup.ControlPanel, Glyph = "", Command = "ncpa.cpl",
            Description = L("Network adapters: IPv4/IPv6 properties, enabling, diagnostics."),
            Launch = () => ToolLauncher.Launch(SystemTool.Control, "ncpa.cpl"),
            IsAvailable = () => SystemTools.IsAvailable(SystemTool.Control), InSearch = false,
        };
        yield return Cpl("devprinters", "Microsoft.DevicesAndPrinters", L("Devices and Printers"),
            L("Classic view of devices (may open in Settings on recent versions)."), "",
            L("devices and printers, printers"));
        yield return Cpl("useraccounts", "Microsoft.UserAccounts", L("User Accounts"),
            L("Account type, User Account Control, account environment variables."), "",
            L("user accounts, accounts"));
        yield return Cpl("credentials", "Microsoft.CredentialManager", L("Credential Manager"),
            L("Saved Windows and web credentials (network shares, apps)."), "",
            L("credential manager, saved passwords, credentials, vault"));
        yield return Cpl("sound", "Microsoft.Sound", L("Sound (classic panel)"),
            L("Playback and recording devices, formats, system sounds."), "",
            L("mmsys.cpl, playback device, recording, system sounds"));
        yield return Cpl("mouse", "Microsoft.Mouse", L("Mouse Properties"),
            L("Buttons, pointer schemes, speed, wheel."), "",
            L("main.cpl, pointers, double-click, wheel, scroll wheel"));
        yield return Cpl("keyboard", "Microsoft.Keyboard", L("Keyboard Properties"),
            L("Key repeat delay and rate, cursor blink rate."), "",
            L("key repeat, repeat delay, cursor blink, blink rate"));
        yield return Cpl("folders", "Microsoft.FolderOptions", L("File Explorer Options"),
            L("View, hidden files, extensions, navigation."), "", inSearch: false);
        yield return Cpl("datetime", "Microsoft.DateAndTime", L("Date and Time (classic panel)"),
            L("Additional clocks, sync with an internet time server."), "",
            L("timedate.cpl, additional clocks, time server, ntp"));
        yield return Cpl("region", "Microsoft.RegionAndLanguage", L("Region"),
            L("Date and number formats, language for non-Unicode programs, copying settings."), "",
            L("intl.cpl, date format, decimal separator, unicode, regional settings"));
        yield return Cpl("backup7", "Microsoft.BackupAndRestore", L("Backup and Restore (Windows 7)"),
            L("System image and classic backups (legacy feature, still available)."), "",
            L("system image, windows 7 backup"));
        yield return Cpl("filehistory", "Microsoft.FileHistory", L("File History"),
            L("Automatic copies of your files to an external or network drive."), "",
            L("file history, previous versions, file backup"));
        yield return Cpl("troubleshooting", "Microsoft.Troubleshooting", L("Troubleshooting"),
            L("Troubleshooters (may redirect to Settings)."), "",
            L("troubleshooting, troubleshoot"));
        yield return Cpl("firewall", "Microsoft.WindowsFirewall", L("Windows Defender Firewall"),
            L("Status per profile, allowed apps, restoring default settings."), "",
            L("firewall.cpl, firewall, allowed apps"));
        yield return Cpl("admintools", "Microsoft.AdministrativeTools", L("Windows Tools"),
            L("Folder that gathers all Windows administrative tools."), "",
            L("administrative tools, windows tools"));
        yield return Cpl("security", "Microsoft.ActionCenter", L("Security and Maintenance"),
            L("Security messages, automatic maintenance, problem history."), "",
            L("security and maintenance, automatic maintenance, problem reports"));
        yield return Cpl("indexing", "Microsoft.IndexingOptions", L("Indexing Options"),
            L("Locations indexed by search, rebuilding the index."), "",
            L("indexing, search index, rebuild index"));
        yield return Cpl("internet", "Microsoft.InternetOptions", L("Internet Options"),
            L("Legacy internet settings (proxy, security zones) still used by some apps."), "",
            L("inetcpl, internet options, security zones"));
        yield return Cpl("colormgmt", "Microsoft.ColorManagement", L("Color Management"),
            L("Color profiles (ICC) for displays and printers."), "",
            L("icc profile, color profile, color management"));
        yield return Cpl("easeofaccess", "Microsoft.EaseOfAccessCenter", L("Ease of Access Center"),
            L("Classic accessibility options: keyboard, mouse, display."), "",
            L("ease of access, ease of access center, accessibility"));
        yield return Cpl("storagespaces", "Microsoft.StorageSpaces", L("Storage Spaces"),
            L("Combine several drives into one volume, with or without redundancy."), "",
            L("storage spaces, storage pool, raid"));
        yield return Cpl("bitlocker", "Microsoft.BitLockerDriveEncryption", L("BitLocker Drive Encryption"),
            L("Turn on BitLocker, back up the recovery key, BitLocker To Go."), "",
            proOnly: true, keywords: [L("bitlocker, recovery key, drive encryption")]);
        yield return Sys32("odbc", "odbcad32.exe", L("ODBC Data Sources (64-bit)"),
            L("Database connections used by some business software."), ToolGroup.ControlPanel, "",
            admin: true, keywords: [L("odbc, odbcad32, data source, dsn, database")]);
        yield return Sys32("mobility", "mblctr.exe", L("Windows Mobility Center"),
            L("Shortcuts for laptops: brightness, volume, battery, external display."), ToolGroup.ControlPanel, "",
            keywords: [L("mblctr, mobility center, laptop, notebook")]);

        // ---------------------------------------------------------------- Dossiers spéciaux
        yield return Folder("startup", "shell:startup", L("Startup folder (your account)"),
            L("Shortcuts launched when you sign in."), "",
            L("startup folder, autostart, run at startup"));
        yield return Folder("commonstartup", "shell:common startup", L("Startup folder (all accounts)"),
            L("Shortcuts launched when any user signs in."), "",
            L("common startup folder, all users startup"));
        yield return Folder("appsfolder", "shell:appsfolder", L("All apps"),
            L("Classic and Store apps, with the option to create shortcuts."), "",
            L("appsfolder, app list, store app shortcut"));
        yield return Folder("programs", "shell:programs", L("Start menu shortcuts"),
            L("Folder with your Start menu shortcuts (Programs)."), "",
            L("start menu programs, start shortcuts"));
        yield return Folder("sendto", "shell:sendto", L("“Send to” menu"),
            L("Add or remove destinations from the “Send to” menu."), "",
            L("send to, sendto"));
        yield return Folder("recent", "shell:recent", L("Recent items"),
            L("Shortcuts to recently opened files."), "",
            L("recent files, recent items, recent"));
        yield return Folder("appdata", "shell:appdata", "AppData (Roaming)",
            L("App data and settings for your account."), "",
            L("appdata, roaming, application data"));
        yield return new WinTool
        {
            Key = "temp", Title = L("Temporary files (%TEMP%)"), Group = ToolGroup.Folders, Glyph = "", Command = "%TEMP%",
            Description = L("Your account's temporary folder, often large."),
            Keywords = [L("temp, temporary files, temp folder, tmp")],
            Launch = () => ProcessRunner.OpenFolder(Path.GetTempPath()),
            IsAvailable = () => Directory.Exists(Path.GetTempPath()),
        };
        yield return Folder("godmode", "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}", L("God Mode"),
            L("All Control Panel tasks gathered in a single list."), "",
            L("god mode, godmode, all settings, all tasks"));

        // ---------------------------------------------------------------- Accessoires
        yield return Exe("charmap", SystemTool.Charmap, "charmap", L("Character Map"),
            L("Copy special characters, symbols and accents from any font."), ToolGroup.Accessories, "",
            keywords: [L("charmap, special characters, symbols, character map")]);
        yield return Exe("osk", SystemTool.Osk, "osk", L("On-Screen Keyboard"),
            L("On-screen keyboard you can use with a mouse or by touch."), ToolGroup.Accessories, "",
            keywords: [L("on-screen keyboard, virtual keyboard, osk")]);
        yield return Exe("magnify", SystemTool.Magnify, "magnify", L("Magnifier"),
            L("Magnifies part of the screen (Windows + Esc to exit)."), ToolGroup.Accessories, "",
            keywords: [L("magnifier, screen zoom, zoom")]);
        yield return Exe("mstsc", SystemTool.Mstsc, "mstsc", L("Remote Desktop Connection"),
            L("Connect to another PC using the Remote Desktop Protocol (RDP)."), ToolGroup.Accessories, "",
            keywords: [L("mstsc, rdp, remote desktop connection, remote desktop")]);
        yield return Sys32("msra", "msra.exe", L("Windows Remote Assistance"),
            L("Invite someone you trust to help you, or help someone."), ToolGroup.Accessories, "",
            keywords: [L("msra, remote assistance, remote help")]);
        yield return Sys32("sndvol", "sndvol.exe", L("Volume Mixer (classic)"),
            L("Volume for each app and audio device."), ToolGroup.Accessories, "",
            keywords: [L("sndvol, volume mixer, mixer")]);
        yield return Sys32("cttune", "cttune.exe", L("ClearType Text Tuner"),
            L("Make on-screen text sharper, in a few steps."), ToolGroup.Accessories, "",
            keywords: [L("cleartype, blurry text, font smoothing")]);
        yield return Sys32("dccw", "dccw.exe", L("Calibrate display color"),
            L("Wizard for adjusting gamma, brightness and contrast."), ToolGroup.Accessories, "",
            admin: true, keywords: [L("calibration, calibrate display, gamma, dccw, color calibration")]);
        yield return Sys32("eudcedit", "eudcedit.exe", L("Private Character Editor"),
            L("Draw your own characters (logos, symbols) usable in all fonts."), ToolGroup.Accessories, "",
            admin: true, keywords: [L("eudcedit, eudc, custom character, private character editor")]);
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
        Command = "control /name " + canonicalName, Keywords = [canonicalName, LC("search keywords", "control panel"), .. keywords ?? []],
        ProOnly = proOnly, InSearch = inSearch,
        Launch = () => ToolLauncher.Launch(SystemTool.Control, "/name", canonicalName),
        IsAvailable = () => SystemTools.IsAvailable(SystemTool.Control),
    };

    private static WinTool Folder(string key, string shellPath, string title, string description, string glyph, params string[] keywords) => new()
    {
        Key = "folder." + key, Title = title, Description = description, Group = ToolGroup.Folders, Glyph = glyph,
        Command = shellPath.StartsWith("shell:::", StringComparison.Ordinal) ? "shell:::{ED7BA470…}" : shellPath,
        Keywords = [shellPath, L("folder"), .. keywords],
        Launch = () => ToolLauncher.Launch(SystemTool.Explorer, shellPath),
        IsAvailable = () => SystemTools.IsAvailable(SystemTool.Explorer),
    };
}
