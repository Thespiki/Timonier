using System.Runtime.InteropServices;
using System.Management;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Security;

internal enum SecLevel { Good, Info, Warning, Critical, Unknown }

/// <summary>Correction proposée pour un élément de l'état de sécurité (une seule des cibles est renseignée).</summary>
internal sealed record SecFix(string Label)
{
    public string? TweakId { get; init; }
    public string? Option { get; init; }
    public string? ActionId { get; init; }
    public string? Uri { get; init; }
    public string? PageId { get; init; }
    public SystemTool? Tool { get; init; }
    public string[] ToolArgs { get; init; } = [];
    /// <summary>Texte de confirmation pour une action (les réglages génèrent leur propre texte).</summary>
    public string? Confirm { get; init; }
}

internal sealed record SecItem(string Key, string Section, string Title, string Glyph, SecLevel Level, string Status, string Detail, int Weight)
{
    public SecFix? Fix { get; init; }
}

internal sealed record SecReport(IReadOnlyList<SecItem> Items, int Score, DateTime At)
{
    public int WarningCount => Items.Count(i => i.Level == SecLevel.Warning);
    public int CriticalCount => Items.Count(i => i.Level == SecLevel.Critical);
}

/// <summary>
/// Lecture seule de l'état de sécurité (WMI, registre, COM Shell, TBS). Aucune élévation, aucune écriture.
/// Toutes les sondes s'exécutent hors du thread UI ; chacune est isolée (une erreur donne un état « inconnu »).
/// </summary>
internal static partial class SecurityProbe
{
    // Titres de section affichés ; SecItem.Section et Sections passent par les mêmes appels L(...).
    public static string SectionEssential => L("Essential protection");
    public static string SectionDevice => L("Device and startup");
    public static string SectionSurface => L("Attack surface");
    public static string[] Sections => [SectionEssential, SectionDevice, SectionSurface];

    /// <summary>Assemble des phrases complètes (déjà traduites) séparées par une espace, en ignorant les vides.</summary>
    private static string Sentences(params string?[] parts) => string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private const string SecurityCenter = @"root\SecurityCenter2";
    private const string DefenderWmi = @"root\Microsoft\Windows\Defender";
    private const string FirewallPolicyRoot = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
    private const string FirewallGpoRoot = @"SOFTWARE\Policies\Microsoft\WindowsFirewall";

    // ====================================================================== Rapport complet

    public static SecReport Collect(SystemProfile profile, CancellationToken ct = default)
    {
        var items = new List<SecItem>();
        void Add(Func<IEnumerable<SecItem>> probe, string key, string section, string title, string glyph)
        {
            ct.ThrowIfCancellationRequested();
            try { items.AddRange(probe()); }
            catch (Exception ex)
            {
                Log.Warn("Security", $"sonde {key} : {ex.Message}");
                items.Add(new SecItem(key, section, title, glyph, SecLevel.Unknown, L("Can't be determined"),
                    L("Reading this status failed on this PC."), 0));
            }
        }

        Add(() => Antivirus(), "antivirus", SectionEssential, L("Antivirus"), "\uEA18");
        Add(() => [Firewall()], "firewall", SectionEssential, L("Firewall"), "\uE774");
        Add(() => [Uac(profile)], "uac", SectionEssential, L("User Account Control (UAC)"), "\uE7EF");
        Add(() => [SmartScreen(profile)], "smartscreen", SectionEssential, "SmartScreen", "\uE890");
        Add(() => [Encryption(profile)], "encryption", SectionDevice, L("System drive encryption"), "\uE72E");
        Add(() => [SecureBoot()], "secureboot", SectionDevice, LC("with English term", "Secure Boot"), "\uE7E8");
        Add(() => [Tpm()], "tpm", SectionDevice, L("Security chip (TPM)"), "\uE950");
        Add(() => [MemoryIntegrity()], "hvci", SectionDevice, L("Memory integrity"), "\uE9F5");
        Add(() => [LsaProtection(profile)], "lsa", SectionDevice, L("LSA protection"), "\uE8D7");
        Add(() => [Smb1()], "smb1", SectionSurface, L("SMBv1 protocol"), "\uE968");
        Add(() => [RemoteDesktop(profile)], "rdp", SectionSurface, L("Remote Desktop"), "\uE7F4");
        Add(() => [RemoteAssistanceItem()], "remoteassistance", SectionSurface, L("Remote Assistance"), "\uE716");
        Add(() => [AutoRun()], "autorun", SectionSurface, L("Media AutoRun"), "\uE88E");
        Add(() => [BuiltInAccounts()], "accounts", SectionSurface, L("Built-in Administrator and Guest accounts"), "\uE77B");
        Add(() => [FileExtensions()], "extensions", SectionSurface, L("File name extensions visible"), "\uE8A5");

        return new SecReport(items, ComputeScore(items), DateTime.Now);
    }

    /// <summary>Score pondéré : bon/info = 100 %, à vérifier = 40 %, critique = 0 ; les états inconnus sont exclus.</summary>
    public static int ComputeScore(IEnumerable<SecItem> items)
    {
        double earned = 0, total = 0;
        foreach (var i in items.Where(i => i.Weight > 0 && i.Level != SecLevel.Unknown))
        {
            total += i.Weight;
            earned += i.Weight * (i.Level switch { SecLevel.Warning => 0.4, SecLevel.Critical => 0.0, _ => 1.0 });
        }
        return total <= 0 ? 0 : (int)Math.Round(100 * earned / total);
    }

    public static HealthResult ToHealth(SecItem item) => new(item.Level switch
    {
        SecLevel.Good => HealthStatus.Good,
        SecLevel.Info => HealthStatus.Info,
        SecLevel.Warning => HealthStatus.Warning,
        SecLevel.Critical => HealthStatus.Critical,
        _ => HealthStatus.Unknown,
    }, item.Status, item.Detail);

    public static HealthResult SafeHealth(Func<SecItem> probe)
    {
        try { return ToHealth(probe()); }
        catch (Exception ex)
        {
            Log.Warn("Security", "contrôle de santé : " + ex.Message);
            return new HealthResult(HealthStatus.Unknown, L("Unknown state"), ex.Message);
        }
    }

    // ====================================================================== Antivirus

    private sealed record AvProduct(string Name, bool Enabled, bool UpToDate);

    /// <summary>Décodage de productState (SecurityCenter2) : octet central = état du moteur, octet bas = définitions.</summary>
    private static AvProduct DecodeProduct(string name, uint state)
    {
        var engine = (state >> 8) & 0xFF;
        var enabled = engine is 0x10 or 0x11;
        var upToDate = (state & 0xFF) == 0x00;
        return new AvProduct(name, enabled, upToDate);
    }

    private static List<AvProduct> SecurityCenterProducts(string className)
    {
        try
        {
            return [.. WmiQuery.Query($"SELECT displayName, productState FROM {className}", SecurityCenter, 8)
                .Select(r => DecodeProduct(r.GetValueOrDefault("displayName") as string ?? "?", Convert.ToUInt32(r.GetValueOrDefault("productState") ?? 0u)))];
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            return [];
        }
    }

    private static bool IsMicrosoftDefender(string name) => name.Contains("Defender", StringComparison.OrdinalIgnoreCase);

    internal static SecItem AntivirusMain() => Antivirus().First();

    private static IEnumerable<SecItem> Antivirus()
    {
        var title = L("Antivirus"); const string glyph = "\uEA18";
        var openThreat = new SecFix(L("Open protection")) { Uri = "windowsdefender://threat/" };
        var products = SecurityCenterProducts("AntiVirusProduct");
        var thirdParty = products.FirstOrDefault(p => !IsMicrosoftDefender(p.Name) && p.Enabled);

        Dictionary<string, object?>? mp = null;
        try { mp = WmiQuery.Query("SELECT * FROM MSFT_MpComputerStatus", DefenderWmi, 8).FirstOrDefault(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException) { /* Defender absent ou désactivé */ }

        var mode = mp?.GetValueOrDefault("AMRunningMode") as string ?? "";
        var defenderPrimary = mp is not null && Bool(mp, "AntivirusEnabled") && Bool(mp, "AMServiceEnabled")
                              && (mode.Length == 0 || mode.Equals("Normal", StringComparison.OrdinalIgnoreCase));

        if (defenderPrimary)
        {
            var realtime = Bool(mp!, "RealTimeProtectionEnabled");
            var age = mp!.GetValueOrDefault("AntivirusSignatureAge") is { } a ? Convert.ToInt32(a) : -1;
            var ageText = age switch { < 0 => "", 0 => L("definitions updated today"), 1 => L("definitions from yesterday"), _ => LP(age, "definitions {0} day old", "definitions {0} days old") };
            if (!realtime)
            {
                yield return new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Critical,
                    L("Real-time protection off"),
                    L("Microsoft Defender is installed but no longer scans files when they're opened. Windows turns it back on automatically after a while; turn it back on now in Windows Security."), 25)
                { Fix = new SecFix(L("Re-enable")) { Uri = "windowsdefender://threatsettings/" } };
            }
            else if (age > 3)
            {
                yield return new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Warning,
                    L("Microsoft Defender active, {0}", ageText),
                    L("Virus definitions should be updated every day by Windows Update. Start an update from Windows Security › Virus & threat protection."), 25)
                { Fix = new SecFix(L("Update")) { Uri = "windowsdefender://threat/" } };
            }
            else
            {
                yield return new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Good,
                    ageText.Length > 0 ? L("Microsoft Defender active, {0}", ageText) : L("Microsoft Defender active"),
                    L("Real-time protection on. No additional antivirus is needed for everyday use."), 25)
                { Fix = openThreat with { Label = L("Scan") } };
            }

            var tamper = mp!.GetValueOrDefault("IsTamperProtected");
            if (tamper is bool tp)
            {
                yield return tp
                    ? new SecItem("tamper", SectionEssential, L("Tamper Protection"), "\uE72E", SecLevel.Good, LC("feminine", "On"),
                        L("Prevents malware (and “optimizers”) from turning off Microsoft Defender."), 5)
                    : new SecItem("tamper", SectionEssential, L("Tamper Protection"), "\uE72E", SecLevel.Warning, L("Disabled"),
                        L("A malicious program running as administrator can turn off Defender. Turn it on in Windows Security › Virus & threat protection settings."), 5)
                    { Fix = new SecFix(L("Enable")) { Uri = "windowsdefender://threatsettings/" } };
            }
            yield break;
        }

        if (thirdParty is not null)
        {
            yield return thirdParty.UpToDate
                ? new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Good, L("{0} active", thirdParty.Name),
                    L("A third-party antivirus is registered with Windows Security and reports being up to date; Microsoft Defender then steps aside automatically."), 25)
                : new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Warning, L("{0}: definitions need updating", thirdParty.Name),
                    L("The installed antivirus reports outdated virus definitions. Open it to run an update."), 25)
                { Fix = new SecFix(L("Windows Security")) { Uri = "windowsdefender://providers/" } };
            yield break;
        }

        if (mp is null && products.Count == 0)
        {
            yield return new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Unknown, L("Status unreadable"),
                L("Neither Windows Security nor Microsoft Defender responded. Open Windows Security to check."), 25)
            { Fix = new SecFix(L("Check")) { Uri = "windowsdefender://threat/" } };
            yield break;
        }

        yield return new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Critical, L("No active antivirus detected"),
            L("Neither Microsoft Defender nor a third-party antivirus is protecting this PC in real time. Turn Microsoft Defender back on in Windows Security (or update/reinstall your antivirus)."), 25)
        { Fix = new SecFix(L("Turn on Defender")) { Uri = "windowsdefender://threat/" } };
    }

    private static bool Bool(Dictionary<string, object?> row, string key) => row.GetValueOrDefault(key) is true;

    /// <summary>Estimation rapide (registre, sans WMI) : Defender est l'antivirus actif. Utilisé par les conditions des réglages.</summary>
    public static bool DefenderLikelyActive()
    {
        var now = Environment.TickCount64;
        if (now - _defenderCheckedAt < 30_000) return _defenderActive;
        var running = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.DefenderKey, "IsServiceRunning");
        var disabled = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.DefenderKey, "DisableAntiVirus") == 1
                       || RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.DefenderPolicy, "DisableAntiSpyware") == 1;
        var passive = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.DefenderKey, "PassiveMode") is 1 or 2;
        _defenderActive = running != 0 && !disabled && !passive;
        _defenderCheckedAt = now;
        return _defenderActive;
    }

    private static long _defenderCheckedAt = long.MinValue / 2;
    private static bool _defenderActive;

    // ====================================================================== Pare-feu

    internal static SecItem Firewall()
    {
        var title = L("Firewall"); const string glyph = "\uE774";
        (string Label, string Local, string Gpo)[] profiles =
        [
            (LC("network profile", "private"), "StandardProfile", "PrivateProfile"),
            (LC("network profile", "public"), "PublicProfile", "PublicProfile"),
            (LC("network profile", "domain"), "DomainProfile", "DomainProfile"),
        ];
        var off = new List<string>();
        var byPolicy = false;
        var critical = false; // pare-feu coupé sur un profil privé ou public (id stable, pas le libellé traduit)
        foreach (var (label, local, gpo) in profiles)
        {
            var policy = RegistryAccess.ReadDword(RegHive.LocalMachine, FirewallGpoRoot + "\\" + gpo, "EnableFirewall");
            var value = policy ?? RegistryAccess.ReadDword(RegHive.LocalMachine, FirewallPolicyRoot + "\\" + local, "EnableFirewall") ?? 1;
            if (value == 0)
            {
                off.Add(label);
                byPolicy |= policy == 0;
                critical |= local != "DomainProfile";
            }
        }

        var thirdParty = SecurityCenterProducts("FirewallProduct").FirstOrDefault(p => !IsMicrosoftDefender(p.Name) && p.Enabled);
        if (off.Count == 0)
        {
            return new SecItem("firewall", SectionEssential, title, glyph, SecLevel.Good, L("Windows Firewall on for all networks"),
                thirdParty is not null
                    ? L("Unsolicited incoming connections are blocked on private, public and domain networks. {0} is also registered.", thirdParty.Name)
                    : L("Unsolicited incoming connections are blocked on private, public and domain networks."), 20)
            { Fix = new SecFix(L("Details")) { Uri = "windowsdefender://network/" } };
        }
        if (thirdParty is not null)
        {
            return new SecItem("firewall", SectionEssential, title, glyph, SecLevel.Good, L("{0} active", thirdParty.Name),
                L("Windows Firewall is off for the {0} network, but an active third-party firewall is registered with Windows Security.", string.Join(", ", off)), 20);
        }
        var fix = byPolicy
            ? new SecFix(L("Open")) { Uri = "windowsdefender://network/" }
            : new SecFix(L("Re-enable"))
            {
                ActionId = FirewallEnableAction.ActionId,
                Confirm = L("Windows Firewall will be turned back on for the private, public and domain profiles (existing rules are kept)."),
            };
        return new SecItem("firewall", SectionEssential, title, glyph, critical ? SecLevel.Critical : SecLevel.Warning,
            L("Off for the {0} network", string.Join(", ", off)),
            Sentences(byPolicy ? L("A Group Policy turns off the firewall: only the PC's administrator can remove it.") : null,
                L("Without a firewall, this PC's services are exposed to other devices on the network (especially on public Wi-Fi).")), 20)
        { Fix = fix };
    }

    // ====================================================================== UAC

    /// <summary>
    /// L'invite administrateur est déjà plus stricte que le niveau recommandé (1 = identifiants, 2 = toujours m'avertir) :
    /// « Rétablir le niveau recommandé » (ConsentPromptBehaviorAdmin = 5) l'abaisserait, quel que soit l'état d'EnableLUA
    /// ou du Bureau sécurisé.
    /// </summary>
    public static bool UacStricterThanDefault() =>
        RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.PolSystem, "ConsentPromptBehaviorAdmin") is 1 or 2;

    private static SecItem Uac(SystemProfile profile)
    {
        var title = L("User Account Control (UAC)"); const string glyph = "\uE7EF";
        var lua = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.PolSystem, "EnableLUA") ?? 1;
        var cpba = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.PolSystem, "ConsentPromptBehaviorAdmin") ?? 5;
        var secure = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.PolSystem, "PromptOnSecureDesktop") ?? 1;
        // Ne jamais proposer une correction qui abaisse l'invite : « Toujours m'avertir » (2) est conservé par l'action
        // « niveau maximal » ; « identifiants » (1) n'a pas d'équivalent, sauf si l'UAC est entièrement désactivé.
        SecFix? fix = cpba switch
        {
            2 => new SecFix(L("Restore")) { TweakId = "security.uac.always", Option = TweakDefinition.Run },
            1 => lua == 0 ? new SecFix(L("Restore")) { TweakId = "security.uac.always", Option = TweakDefinition.Run } : null,
            _ => new SecFix(L("Restore")) { TweakId = "security.uac.recommended", Option = TweakDefinition.Run },
        };
        var account = profile.IsUserAdmin ? null : L("You're using a standard account: that's the safest setup.");

        if (lua == 0)
            return new SecItem("uac", SectionEssential, title, glyph, SecLevel.Critical, L("UAC turned off"),
                L("Any program started by an administrator gets full rights without asking, and Microsoft Store apps no longer work properly. A restart will be required after the fix."), 15) { Fix = fix };
        if (cpba == 0)
            return new SecItem("uac", SectionEssential, title, glyph, SecLevel.Critical, L("Elevation without any prompt"),
                L("Programs get administrator rights without confirmation (“Never notify”): malware can silently change the entire system."), 15) { Fix = fix };
        if (secure == 0)
            return new SecItem("uac", SectionEssential, title, glyph, SecLevel.Warning, L("Prompts outside the secure desktop"),
                L("UAC prompts appear on the normal desktop, where another program can mimic them or click them for you."), 15)
            { Fix = fix };

        var level = cpba switch
        {
            2 => L("Highest level: always notify me"),
            1 => L("Credentials requested on the secure desktop"),
            5 => L("Recommended level"),
            _ => L("Confirmation prompt on"),
        };
        return new SecItem("uac", SectionEssential, title, glyph, SecLevel.Good, level,
            Sentences(L("Every privilege elevation is confirmed on the secure desktop."), account), 15);
    }

    // ====================================================================== SmartScreen

    private static SecItem SmartScreen(SystemProfile profile)
    {
        const string title = "SmartScreen", glyph = "\uE890";
        var fix = new SecFix(L("Enable")) { TweakId = "security.smartscreen.apps", Option = TweakDefinition.On };
        var policy = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.SystemPolicy, "EnableSmartScreen");
        var sac = SmartAppControlText(profile);

        if (policy == 0)
            return new SecItem("smartscreen", SectionEssential, title, glyph, SecLevel.Critical, L("Turned off by a policy"),
                Sentences(L("Downloaded programs are no longer checked before they open. A policy (often set by an “optimization” tool) forces this off."), sac), 8) { Fix = fix };
        if (policy == 1)
            return new SecItem("smartscreen", SectionEssential, title, glyph, SecLevel.Good, L("Enforced by policy"),
                Sentences(L("The reputation of downloaded programs and files is checked before they open."), sac), 8);

        var local = RegistryAccess.ReadString(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled");
        if (string.Equals(local, "Off", StringComparison.OrdinalIgnoreCase))
            return new SecItem("smartscreen", SectionEssential, title, glyph, SecLevel.Warning, L("App checking turned off"),
                Sentences(L("Downloaded programs are no longer checked before they open."), sac), 8) { Fix = fix };
        return new SecItem("smartscreen", SectionEssential, title, glyph, SecLevel.Good, L("Active"),
            Sentences(L("Windows warns you before opening an unknown or malicious downloaded program or file."), sac), 8)
        { Fix = new SecFix(L("Details")) { Uri = "windowsdefender://appbrowser/" } };
    }

    private static string SmartAppControlText(SystemProfile profile)
    {
        if (profile.Build < 22621) return "";
        return RegistryAccess.ReadDword(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState") switch
        {
            1 => L("Smart App Control: on."),
            2 => L("Smart App Control: evaluation mode."),
            _ => "",
        };
    }

    // ====================================================================== Chiffrement

    internal static SecItem Encryption(SystemProfile profile)
    {
        var title = L("System drive encryption"); const string glyph = "\uE72E";
        var drive = (Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\").TrimEnd('\\');
        var value = StaThread.Run<int?>(() =>
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return null;
            object shell = Activator.CreateInstance(type)!;
            try
            {
                dynamic? folder = ((dynamic)shell).NameSpace(drive + "\\");
                if (folder is null) return null;
                object? raw = folder.Self.ExtendedProperty("System.Volume.BitLockerProtection");
                if (raw is null) return null;
                return Convert.ToInt32(raw);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }, TimeSpan.FromSeconds(6));

        SecFix manage = profile.IsHomeEdition
            ? new SecFix(L("Device encryption")) { Uri = "ms-settings:deviceencryption" }
            : new SecFix(L("Manage BitLocker")) { Tool = SystemTool.Control, ToolArgs = ["/name", "Microsoft.BitLockerDriveEncryption"] };
        var mobile = profile.IsLaptopLike;
        var name = profile.IsHomeEdition ? L("Device encryption") : "BitLocker";

        return value switch
        {
            1 or 6 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Good, L("{0} encrypted ({1})", drive, name),
                L("If the PC or drive is stolen, your files stay unreadable without your sign-in. Keep the recovery key (in your Microsoft account or on a printed copy)."), 10),
            3 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Info, L("Encryption in progress"),
                L("Drive encryption continues in the background; you can use the PC normally."), 10),
            4 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Warning, L("Decryption in progress"),
                L("The system drive is being decrypted: its data will no longer be protected if it's stolen."), 10) { Fix = manage },
            5 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Warning, L("Protection suspended"),
                L("BitLocker is suspended (often during a firmware update): the key is stored unencrypted on the drive. Resume protection if no update is in progress."), 10) { Fix = manage },
            8 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Warning, L("Waiting for activation"),
                L("The drive is prepared for encryption but protection isn't turned on: sign in with a Microsoft account or turn on protection to back up the recovery key."), 10) { Fix = manage },
            2 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Warning, L("{0} not encrypted", drive),
                Sentences(mobile ? L("On a laptop, anyone who gets hold of the drive can read your files without a password.")
                        : L("Anyone who gets hold of the drive can read your files without a password."),
                    profile.SupportsBitLocker ? L("Turn on BitLocker (and back up the recovery key).")
                        : L("The Home edition only offers “device encryption”, if the hardware supports it.")), mobile ? 10 : 6)
            { Fix = manage },
            0 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Info, L("Can't be encrypted"),
                L("Windows reports that this volume can't be encrypted on this PC."), 0),
            _ => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Unknown, L("Unknown state"),
                L("The encryption status couldn't be read without administrator rights."), 10) { Fix = manage },
        };
    }

    // ====================================================================== Secure Boot / TPM

    internal static SecItem SecureBoot()
    {
        var title = LC("with English term", "Secure Boot"); const string glyph = "\uE7E8";
        var uefi = Native.IsUefiFirmware();
        var state = RegistryAccess.ReadDword(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        if (state == 1)
            return new SecItem("secureboot", SectionDevice, title, glyph, SecLevel.Good, L("On"),
                L("Only signed boot loaders can run: protects against “bootkits” that install themselves before Windows."), 6);
        if (!uefi)
            return new SecItem("secureboot", SectionDevice, title, glyph, SecLevel.Warning, L("Unavailable (legacy BIOS boot)"),
                L("Windows boots in legacy BIOS mode (MBR): Secure Boot requires converting the disk to GPT (mbr2gpt tool) and then switching the firmware to UEFI mode. This is a delicate operation; prepare for it with a backup."), 6);
        if (state == 0)
            return new SecItem("secureboot", SectionDevice, title, glyph, SecLevel.Warning, L("Turned off in UEFI firmware"),
                L("The PC boots in UEFI mode but Secure Boot is turned off. You turn it on in the UEFI settings (press F2, F10, Esc or Delete at startup, “Security” or “Boot” section). No software can do it for you."), 6);
        return new SecItem("secureboot", SectionDevice, title, glyph, SecLevel.Unknown, L("Unknown state"),
            L("The firmware didn't report the Secure Boot status."), 6);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TpmDeviceInfo
    {
        public uint StructVersion;
        public uint TpmVersion;
        public uint TpmInterfaceType;
        public uint TpmImpRevision;
    }

    [LibraryImport("tbs.dll")]
    private static partial uint Tbsi_GetDeviceInfo(uint size, out TpmDeviceInfo info);

    private static SecItem Tpm()
    {
        var title = L("Security chip (TPM)"); const string glyph = "\uE950";
        const uint TpmNotFound = 0x8028400F;
        uint rc;
        TpmDeviceInfo info;
        try { rc = Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<TpmDeviceInfo>(), out info); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new SecItem("tpm", SectionDevice, title, glyph, SecLevel.Unknown, L("Unknown state"), L("The Windows TPM service is unavailable."), 3);
        }
        if (rc == 0 && info.TpmVersion == 2)
            return new SecItem("tpm", SectionDevice, title, glyph, SecLevel.Good, L("TPM 2.0 present"),
                L("Stores BitLocker and Windows Hello keys and verifies boot integrity."), 3)
            { Fix = new SecFix(L("Details")) { Uri = "windowsdefender://devicesecurity/" } };
        if (rc == 0 && info.TpmVersion == 1)
            return new SecItem("tpm", SectionDevice, title, glyph, SecLevel.Info, L("TPM 1.2 (older version)"),
                L("Enough for BitLocker, but Windows 11 requires TPM 2.0."), 3);
        if (rc == TpmNotFound)
            return new SecItem("tpm", SectionDevice, title, glyph, SecLevel.Warning, L("No TPM detected"),
                L("No TPM chip is active. On many PCs, it exists but is turned off in the firmware (Intel PTT or AMD fTPM in the UEFI settings)."), 3);
        return new SecItem("tpm", SectionDevice, title, glyph, SecLevel.Unknown, L("Unknown state"), L("Unexpected response from the TPM service (0x{0:X8}).", rc), 3);
    }

    // ====================================================================== Isolation du noyau / LSA

    private static SecItem MemoryIntegrity()
    {
        var title = L("Memory integrity"); const string glyph = "\uE9F5";
        bool? running = null;
        try
        {
            var row = WmiQuery.Query("SELECT SecurityServicesRunning FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard", 6).FirstOrDefault();
            if (row?.GetValueOrDefault("SecurityServicesRunning") is Array services)
                running = services.Cast<object>().Any(s => Convert.ToInt32(s) == 2);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException) { /* WMI Device Guard absent */ }

        var configured = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.Hvci, "Enabled") == 1;
        var fix = new SecFix(L("Enable")) { TweakId = "security.hvci", Option = TweakDefinition.On };
        if (running == true)
            return new SecItem("hvci", SectionDevice, title, glyph, SecLevel.Good, LC("state", "Active"),
                L("The kernel refuses unsigned or modified drivers, even when started by an administrator."), 6);
        if (configured)
            return new SecItem("hvci", SectionDevice, title, glyph, SecLevel.Info, L("On, takes effect at next restart"),
                L("If it's still inactive after restarting, an incompatible driver is blocking it: the list is in Windows Security › Device security › Core isolation."), 6)
            { Fix = new SecFix(L("Details")) { Uri = "windowsdefender://devicesecurity/" } };
        return new SecItem("hvci", SectionDevice, title, glyph, SecLevel.Warning, L("Disabled"),
            L("A malicious or vulnerable driver could run in the kernel. Turn it on if your devices are compatible (restart required)."), 6) { Fix = fix };
    }

    public static bool LsaProtectionConfigured()
    {
        var ppl = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.Lsa, "RunAsPPL");
        if (ppl is { } v) return v is 1 or 2;
        return RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.Lsa, "RunAsPPLBoot") is 1 or 2;
    }

    private static SecItem LsaProtection(SystemProfile profile)
    {
        var title = L("LSA protection"); const string glyph = "\uE8D7";
        if (LsaProtectionConfigured())
            return new SecItem("lsa", SectionDevice, title, glyph, SecLevel.Good, LC("feminine", "On"),
                L("The process that holds your sign-in credentials is protected against memory reads."), 4);
        return new SecItem("lsa", SectionDevice, title, glyph, SecLevel.Warning, L("Disabled"),
            L("A program running as administrator can extract sign-in credentials from memory (Mimikatz-type tools)."), 4)
        {
            Fix = profile.Build >= 22621
                ? new SecFix(L("Enable")) { TweakId = "security.lsa.ppl", Option = TweakDefinition.On }
                : new SecFix(L("Details")) { Uri = "windowsdefender://devicesecurity/" },
        };
    }

    // ====================================================================== Surface d'attaque

    /// <summary>true = SMBv1 (client ou serveur) actif ; false = installé mais désactivé ou absent ; null = inconnu.</summary>
    public static bool? Smb1Enabled()
    {
        if (!RegistryAccess.KeyExists(RegHive.LocalMachine, SecurityTweaks.Smb1ClientService)) return false;
        var start = ServiceConfig.ReadStart("mrxsmb10");
        var server = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.LanmanServerParams, "SMB1") ?? 1;
        return start != ServiceStartKind.Disabled || server != 0;
    }

    private static SecItem Smb1()
    {
        var title = L("SMBv1 protocol"); const string glyph = "\uE968";
        if (!RegistryAccess.KeyExists(RegHive.LocalMachine, SecurityTweaks.Smb1ClientService))
            return new SecItem("smb1", SectionSurface, title, glyph, SecLevel.Good, L("Not installed"),
                L("The old sharing protocol exploited by WannaCry isn't present on this PC."), 6);
        var uninstall = new SecFix(L("Uninstall"))
        {
            ActionId = Smb1UninstallAction.ActionId,
            Confirm = L("The Windows feature “SMB 1.0/CIFS File Sharing Support” will be uninstalled. Very old NAS devices, routers or network printers that only speak SMBv1 will no longer be reachable. A restart is required. You can reinstall it from “Optional features”."),
        };
        return Smb1Enabled() == true
            ? new SecItem("smb1", SectionSurface, title, glyph, SecLevel.Warning, L("Installed and active"),
                L("Obsolete protocol, with no encryption and no protection against WannaCry-type attacks. Uninstall it unless a very old network device depends on it."), 6) { Fix = uninstall }
            : new SecItem("smb1", SectionSurface, title, glyph, SecLevel.Good, L("Installed but turned off"),
                L("SMBv1 is turned off; you can also uninstall the feature completely."), 6) { Fix = uninstall };
    }

    private static SecItem RemoteDesktop(SystemProfile profile)
    {
        var title = L("Remote Desktop"); const string glyph = "\uE7F4";
        if (profile.IsHomeEdition)
            return new SecItem("rdp", SectionSurface, title, glyph, SecLevel.Good, L("Not available on the Home edition"),
                L("This PC can't receive Remote Desktop connections."), 5);
        var deny = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.TerminalServer, "fDenyTSConnections") ?? 1;
        if (deny != 0)
            return new SecItem("rdp", SectionSurface, title, glyph, SecLevel.Good, L("Off"),
                L("No one can sign in to this PC remotely via RDP."), 5);
        var nla = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.TerminalServer + @"\WinStations\RDP-Tcp", "UserAuthentication") ?? 1;
        return new SecItem("rdp", SectionSurface, title, glyph, SecLevel.Warning,
            nla == 1 ? L("On") : L("On without Network Level Authentication (NLA)"),
            Sentences(L("Incoming RDP connections are allowed. If you don't use it, turn it off; otherwise use a strong password and never expose port 3389 to the internet."),
                nla == 1 ? null : L("Turn Network Level Authentication back on.")), 5)
        { Fix = new SecFix(L("Disable")) { TweakId = "security.rdp", Option = TweakDefinition.Off } };
    }

    private static SecItem RemoteAssistanceItem()
    {
        var title = L("Remote Assistance"); const string glyph = "\uE716";
        var allowed = (RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.RemoteAssistance, "fAllowToGetHelp") ?? 1) == 1;
        return allowed
            ? new SecItem("remoteassistance", SectionSurface, title, glyph, SecLevel.Info, L("Allowed (by invitation)"),
                L("Someone can take control of this PC if you send them an invitation. Original Windows setting; turn it off if you don't use it."), 0)
            { Fix = new SecFix(L("Disable")) { TweakId = "security.remoteassistance", Option = TweakDefinition.Off } }
            : new SecItem("remoteassistance", SectionSurface, title, glyph, SecLevel.Good, L("Disabled"),
                L("The old Remote Assistance tool (msra.exe) can't be used to take control of this PC."), 0);
    }

    private static SecItem AutoRun()
    {
        var title = L("Media AutoRun"); const string glyph = "\uE88E";
        static bool Disabled(RegHive hive) =>
            RegistryAccess.ReadDword(hive, SecurityTweaks.PolExplorer, "NoDriveTypeAutoRun") == 255
            || RegistryAccess.ReadDword(hive, SecurityTweaks.PolExplorer, "NoAutorun") == 1;
        if (Disabled(RegHive.LocalMachine) || Disabled(RegHive.CurrentUser))
            return new SecItem("autorun", SectionSurface, title, glyph, SecLevel.Good, L("Disabled"),
                L("Nothing runs or opens automatically when you insert a USB drive, disk or CD."), 3);
        return new SecItem("autorun", SectionSurface, title, glyph, SecLevel.Info, L("AutoPlay on"),
            L("Windows suggests an action when media is inserted; autorun.inf hasn't run from USB drives since Windows 7, but can still run from a CD/DVD."), 3)
        { Fix = new SecFix(L("Disable")) { TweakId = "security.autorun", Option = TweakDefinition.Off } };
    }

    private static SecItem BuiltInAccounts()
    {
        var title = L("Built-in Administrator and Guest accounts"); const string glyph = "\uE77B";
        var rows = WmiQuery.Query("SELECT Name, SID, Disabled FROM Win32_UserAccount WHERE LocalAccount = TRUE", timeoutSeconds: 10);
        string? admin = null, guest = null;
        foreach (var r in rows)
        {
            if (r.GetValueOrDefault("SID") is not string sid || r.GetValueOrDefault("Disabled") is not bool disabled || disabled) continue;
            if (sid.EndsWith("-500", StringComparison.Ordinal)) admin = r.GetValueOrDefault("Name") as string ?? L("Administrator");
            else if (sid.EndsWith("-501", StringComparison.Ordinal)) guest = r.GetValueOrDefault("Name") as string ?? L("Guest");
        }
        if (admin is null && guest is null)
            return new SecItem("accounts", SectionSurface, title, glyph, SecLevel.Good, LC("plural", "Disabled"),
                L("The built-in accounts, classic attack targets (their names are known in advance), are turned off."), 5);
        return new SecItem("accounts", SectionSurface, title, glyph, SecLevel.Warning,
            admin is not null && guest is not null ? L("Accounts enabled: “{0}” and “{1}”", admin, guest) : L("Account enabled: “{0}”", admin ?? guest),
            Sentences(admin is not null ? L("The built-in Administrator account isn't subject to UAC and its name is known in advance.") : null,
                guest is not null ? L("The Guest account lets anyone sign in without a password.") : null,
                L("Turn them off if they aren't essential.")), 5)
        { Fix = new SecFix(L("Manage accounts")) { PageId = "users" } };
    }

    private static SecItem FileExtensions()
    {
        var title = L("File name extensions visible"); const string glyph = "\uE8A5";
        var hidden = (RegistryAccess.ReadDword(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt") ?? 1) != 0;
        return hidden
            ? new SecItem("extensions", SectionSurface, title, glyph, SecLevel.Warning, LC("feminine plural", "Hidden"),
                L("A file named “invoice.pdf.exe” shows up as “invoice.pdf”: showing extensions helps you spot booby-trapped attachments."), 2)
            { Fix = new SecFix(L("Show")) { TweakId = "custom.explorer.extensions", Option = TweakDefinition.On } }
            : new SecItem("extensions", SectionSurface, title, glyph, SecLevel.Good, LC("feminine plural", "Shown"),
                L("You see each file's real type (.exe, .pdf, .docx…)."), 2);
    }
}

/// <summary>Exécute un appel COM « apartment » (Shell) sur un thread STA dédié, avec délai maximal.</summary>
internal static class StaThread
{
    public static T? Run<T>(Func<T> func, TimeSpan timeout)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true, Name = "Timonier.Security.STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(timeout)) throw new TimeoutException(L("The Windows shell didn't respond in time."));
        if (error is not null) throw error;
        return result;
    }
}
