using PcPilot.Core.Catalog;
using PcPilot.Core.Platform;
using PcPilot.Core.Search;

namespace PcPilot.Modules.Security;

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
        r.AddCategory(new CategoryInfo(Category, "Sécurité", Glyph,
            "Antivirus, pare-feu, UAC, chiffrement, Secure Boot et renforcement de Windows."));

        r.AddTweaks(SecurityTweaks.All());

        r.AddAction(new FirewallBlockAction());
        r.AddAction(new FirewallUnblockAction());
        r.AddAction(new FirewallEnableAction());
        r.AddAction(new Smb1UninstallAction());

        r.AddPage(new PageInfo(PageId, "Sécurité", Glyph, NavSection.Control, 10, () => new SecurityPage())
        {
            CategoryId = Category,
            Description = "État de sécurité et score, pare-feu par application, renforcement de Windows.",
            Keywords = ["sécurité", "antivirus", "defender", "pare-feu", "firewall", "uac", "bitlocker", "chiffrement", "secure boot",
                        "tpm", "smartscreen", "ransomware", "renforcement", "hardening"],
        });

        // Contrôles de santé : exécutés dans l'interface, hors du thread UI, en lecture seule.
        r.AddHealthCheck(HealthCheck.Sync("security.antivirus", "Antivirus", Glyph, PageId,
            () => SecurityProbe.SafeHealth(SecurityProbe.AntivirusMain)));
        r.AddHealthCheck(HealthCheck.Sync("security.firewall", "Pare-feu", "", PageId,
            () => SecurityProbe.SafeHealth(SecurityProbe.Firewall)));
        r.AddHealthCheck(HealthCheck.Sync("security.encryption", "Chiffrement du disque", "", PageId,
            () => SecurityProbe.SafeHealth(() => SecurityProbe.Encryption(AppHost.Profile))));
        r.AddHealthCheck(HealthCheck.Sync("security.secureboot", "Démarrage sécurisé", "", PageId,
            () => SecurityProbe.SafeHealth(SecurityProbe.SecureBoot)));

        r.AddQuickAction(new QuickAction("security.open-windows-security", "Ouvrir Sécurité Windows", Glyph,
            "Antivirus, pare-feu, contrôle des applications et sécurité de l'appareil.",
            () => { ProcessRunner.OpenSettingsUri("windowsdefender:"); return Task.CompletedTask; })
        {
            Keywords = ["sécurité windows", "windows security", "defender", "antivirus", "centre de sécurité"],
            Order = 30,
        });
        r.AddQuickAction(new QuickAction("security.defender-quickscan", "Analyse antivirus rapide", "",
            "Ouvre Protection contre les virus et menaces de Sécurité Windows pour lancer une analyse rapide.",
            () => { ProcessRunner.OpenSettingsUri("windowsdefender://threat/"); return Task.CompletedTask; })
        {
            Keywords = ["analyse", "scan", "antivirus", "virus", "analyse rapide", "quick scan", "defender"],
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
            Title = "Score de sécurité",
            Subtitle = "Bilan pondéré : antivirus, pare-feu, UAC, chiffrement, Secure Boot…",
            Glyph = Glyph,
            Keywords = ["score securite", "bilan securite", "audit securite", "etat securite", "suis-je protege"],
            PageId = PageId,
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "security.firewall-block",
            Title = "Bloquer Internet pour une application",
            Subtitle = "Crée une règle de pare-feu qui coupe l'accès réseau d'un programme",
            Glyph = "",
            Keywords = ["bloquer internet", "bloquer application", "pare-feu application", "firewall block", "empecher connexion", "hors ligne"],
            PageId = PageId,
            PageParameter = "section:firewall",
            Boost = 0.1,
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "security.hardening",
            Title = "Renforcement de la sécurité de Windows",
            Subtitle = "SMBv1, Bureau à distance, AutoRun, LSA, intégrité de la mémoire, règles ASR…",
            Glyph = "",
            Keywords = ["renforcement", "hardening", "durcissement", "securiser windows"],
            PageId = PageId,
            PageParameter = "section:hardening",
        });

        AddUri(r, "security.win.virus", "Protection contre les virus et menaces", "Sécurité Windows : analyses, mises à jour des définitions",
            "windowsdefender://threat/", ["virus", "menaces", "analyse", "definitions"]);
        AddUri(r, "security.win.firewall", "Pare-feu et protection du réseau", "Sécurité Windows : état du pare-feu par réseau",
            "windowsdefender://network/", ["pare-feu", "firewall", "reseau"]);
        AddUri(r, "security.win.device", "Sécurité de l'appareil", "Isolation du noyau, processeur de sécurité (TPM), démarrage sécurisé",
            "windowsdefender://devicesecurity/", ["isolation noyau", "tpm", "securite appareil", "device security"]);
        AddUri(r, "security.win.appbrowser", "Contrôle des applications et du navigateur", "SmartScreen, protection fondée sur la réputation, protection contre les exploits",
            "windowsdefender://appbrowser/", ["smartscreen", "reputation", "exploit protection", "controle applications"]);
        AddUri(r, "security.win.history", "Historique de protection", "Menaces détectées et actions de Microsoft Defender",
            "windowsdefender://history/", ["historique protection", "quarantaine", "menaces detectees"]);
        r.AddSearchEntry(new SearchEntry
        {
            Id = "security.win.firewall-advanced",
            Title = "Pare-feu Windows avec fonctions avancées",
            Subtitle = "Console MMC des règles de pare-feu (wf.msc)",
            Glyph = "",
            Keywords = ["wf.msc", "regles pare-feu", "firewall avance", "advanced firewall"],
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
