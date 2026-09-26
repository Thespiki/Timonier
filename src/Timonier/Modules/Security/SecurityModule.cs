using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Security;

/// <summary>
/// Sécurité : état de sécurité (antivirus, pare-feu, UAC, chiffrement, Secure Boot…), score pondéré, blocage réseau
/// d'applications par le pare-feu et réglages de renforcement. Déclarations uniquement : appelé aussi dans le broker.
/// </summary>
public sealed class SecurityModule : IModule
{
    public const string Category = "security";
    public const string PageId = "security";
    internal const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, L("Security"), Glyph,
            L("Antivirus, firewall, UAC, encryption, Secure Boot and Windows hardening.")));

        r.AddTweaks(SecurityTweaks.All());

        r.AddAction(new FirewallBlockAction());
        r.AddAction(new FirewallUnblockAction());
        r.AddAction(new FirewallEnableAction());
        r.AddAction(new Smb1UninstallAction());

        r.AddPage(new PageInfo(PageId, L("Security"), Glyph, NavSection.Control, 10, () => new SecurityPage())
        {
            CategoryId = Category,
            Description = L("Security status and score, per-app firewall, Windows hardening."),
            Keywords = [L("security, antivirus, defender, firewall, uac, bitlocker, encryption, secure boot, tpm, smartscreen, ransomware, hardening")],
        });

        // Contrôles de santé : exécutés dans l'interface, hors du thread UI, en lecture seule.
        r.AddHealthCheck(HealthCheck.Sync("security.antivirus", L("Antivirus"), Glyph, PageId,
            () => SecurityProbe.SafeHealth(SecurityProbe.AntivirusMain)));
        r.AddHealthCheck(HealthCheck.Sync("security.firewall", L("Firewall"), "", PageId,
            () => SecurityProbe.SafeHealth(SecurityProbe.Firewall)));
        r.AddHealthCheck(HealthCheck.Sync("security.encryption", L("Drive encryption"), "", PageId,
            () => SecurityProbe.SafeHealth(() => SecurityProbe.Encryption(AppHost.Profile))));
        r.AddHealthCheck(HealthCheck.Sync("security.secureboot", L("Secure Boot"), "", PageId,
            () => SecurityProbe.SafeHealth(SecurityProbe.SecureBoot)));

        r.AddQuickAction(new QuickAction("security.open-windows-security", L("Open Windows Security"), Glyph,
            L("Antivirus, firewall, app control and device security."),
            () => { ProcessRunner.OpenSettingsUri("windowsdefender:"); return Task.CompletedTask; })
        {
            Keywords = [L("windows security, defender, antivirus, security center")],
            Order = 30,
        });
        r.AddQuickAction(new QuickAction("security.defender-quickscan", L("Quick antivirus scan"), "",
            L("Opens Virus & threat protection in Windows Security to run a quick scan."),
            () => { ProcessRunner.OpenSettingsUri("windowsdefender://threat/"); return Task.CompletedTask; })
        {
            Keywords = [L("scan, antivirus, virus, quick scan, defender, malware")],
            Order = 40,
        });

        RegisterSearchEntries(r);

        Synonyms.AddGroup("uac", "controle de compte utilisateur", "elevation", "invite administrateur");
        Synonyms.AddGroup("smb1", "smbv1", "cifs", "smb 1");
        Synonyms.AddGroup("asr", "reduction surface attaque", "attack surface reduction", "exploit guard");
        Synonyms.AddGroup("integrite memoire", "hvci", "isolation noyau", "core isolation", "memory integrity");
        Synonyms.AddGroup("secure boot", "demarrage securise", "uefi");
        Synonyms.AddGroup("tpm", "puce securite", "module plateforme securisee");
        Synonyms.AddGroup("bloquer internet", "bloquer application", "block app", "empecher connexion", "couper internet");
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        r.AddSearchEntry(new SearchEntry
        {
            Id = "security.score",
            Title = L("Security score"),
            Subtitle = L("Weighted assessment: antivirus, firewall, UAC, encryption, Secure Boot…"),
            Glyph = Glyph,
            Keywords = [L("security score, security check, security audit, security status, am i protected")],
            PageId = PageId,
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "security.firewall-block",
            Title = L("Block internet for an app"),
            Subtitle = L("Creates a firewall rule that cuts off a program's network access"),
            Glyph = "",
            Keywords = [L("block internet, block app, app firewall, firewall block, prevent connection, offline")],
            PageId = PageId,
            PageParameter = "section:firewall",
            Boost = 0.1,
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "security.hardening",
            Title = L("Windows security hardening"),
            Subtitle = L("SMBv1, Remote Desktop, AutoRun, LSA, memory integrity, ASR rules…"),
            Glyph = "",
            Keywords = [L("hardening, harden, secure windows, security hardening")],
            PageId = PageId,
            PageParameter = "section:hardening",
        });

        AddUri(r, "security.win.virus", L("Virus & threat protection"), L("Windows Security: scans, definition updates"),
            "windowsdefender://threat/", [L("virus, threats, scan, definitions, malware")]);
        AddUri(r, "security.win.firewall", L("Firewall & network protection"), L("Windows Security: firewall status per network"),
            "windowsdefender://network/", [L("firewall, network")]);
        AddUri(r, "security.win.device", L("Device security"), L("Core isolation, security processor (TPM), Secure Boot"),
            "windowsdefender://devicesecurity/", [L("core isolation, tpm, device security")]);
        AddUri(r, "security.win.appbrowser", L("App & browser control"), L("SmartScreen, reputation-based protection, exploit protection"),
            "windowsdefender://appbrowser/", [L("smartscreen, reputation, exploit protection, app control")]);
        AddUri(r, "security.win.history", L("Protection history"), L("Threats detected and actions taken by Microsoft Defender"),
            "windowsdefender://history/", [L("protection history, quarantine, detected threats")]);
        r.AddSearchEntry(new SearchEntry
        {
            Id = "security.win.firewall-advanced",
            Title = L("Windows Defender Firewall with Advanced Security"),
            Subtitle = L("MMC console for firewall rules (wf.msc)"),
            Glyph = "",
            Keywords = [L("wf.msc, firewall rules, advanced firewall")],
            Kind = SearchEntryKind.Tool,
            Execute = () => ProcessRunner.Launch(SystemTool.Mmc, Path.Combine(Environment.SystemDirectory, "wf.msc")),
        });
    }

    private static void AddUri(ModuleRegistry r, string id, string title, string subtitle, string uri, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id,
            Title = title,
            Subtitle = subtitle,
            Glyph = Glyph,
            Keywords = keywords,
            Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri(uri),
        });
}
