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
    public static string SectionEssential => L("Protection essentielle");
    public static string SectionDevice => L("Appareil et démarrage");
    public static string SectionSurface => L("Surface d'attaque");
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
                items.Add(new SecItem(key, section, title, glyph, SecLevel.Unknown, L("Impossible à déterminer"),
                    L("La lecture de cet état a échoué sur ce PC."), 0));
            }
        }

        Add(() => Antivirus(), "antivirus", SectionEssential, L("Antivirus"), "\uEA18");
        Add(() => [Firewall()], "firewall", SectionEssential, L("Pare-feu"), "\uE774");
        Add(() => [Uac(profile)], "uac", SectionEssential, L("Contrôle de compte d'utilisateur (UAC)"), "\uE7EF");
        Add(() => [SmartScreen(profile)], "smartscreen", SectionEssential, "SmartScreen", "\uE890");
        Add(() => [Encryption(profile)], "encryption", SectionDevice, L("Chiffrement du disque système"), "\uE72E");
        Add(() => [SecureBoot()], "secureboot", SectionDevice, L("Démarrage sécurisé (Secure Boot)"), "\uE7E8");
        Add(() => [Tpm()], "tpm", SectionDevice, L("Puce de sécurité (TPM)"), "\uE950");
        Add(() => [MemoryIntegrity()], "hvci", SectionDevice, L("Intégrité de la mémoire"), "\uE9F5");
        Add(() => [LsaProtection(profile)], "lsa", SectionDevice, L("Protection LSA"), "\uE8D7");
        Add(() => [Smb1()], "smb1", SectionSurface, L("Protocole SMBv1"), "\uE968");
        Add(() => [RemoteDesktop(profile)], "rdp", SectionSurface, L("Bureau à distance"), "\uE7F4");
        Add(() => [RemoteAssistanceItem()], "remoteassistance", SectionSurface, L("Assistance à distance"), "\uE716");
        Add(() => [AutoRun()], "autorun", SectionSurface, L("Exécution automatique des supports"), "\uE88E");
        Add(() => [BuiltInAccounts()], "accounts", SectionSurface, L("Comptes intégrés Administrateur et Invité"), "\uE77B");
        Add(() => [FileExtensions()], "extensions", SectionSurface, L("Extensions de fichiers visibles"), "\uE8A5");

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
            return new HealthResult(HealthStatus.Unknown, L("État inconnu"), ex.Message);
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
        var openThreat = new SecFix(L("Ouvrir la protection")) { Uri = "windowsdefender://threat/" };
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
            var ageText = age switch { < 0 => "", 0 => L("définitions mises à jour aujourd'hui"), 1 => L("définitions d'hier"), _ => LP(age, "définitions vieilles de {0} jour", "définitions vieilles de {0} jours") };
            if (!realtime)
            {
                yield return new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Critical,
                    L("Protection en temps réel désactivée"),
                    L("Microsoft Defender est installé mais n'analyse plus les fichiers à l'ouverture. Windows la réactive automatiquement après un moment ; réactivez-la maintenant dans Sécurité Windows."), 25)
                { Fix = new SecFix(L("Réactiver")) { Uri = "windowsdefender://threatsettings/" } };
            }
            else if (age > 3)
            {
                yield return new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Warning,
                    L("Microsoft Defender actif, {0}", ageText),
                    L("Les définitions de virus devraient être mises à jour chaque jour par Windows Update. Lancez une mise à jour depuis Sécurité Windows › Protection contre les virus et menaces."), 25)
                { Fix = new SecFix(L("Mettre à jour")) { Uri = "windowsdefender://threat/" } };
            }
            else
            {
                yield return new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Good,
                    ageText.Length > 0 ? L("Microsoft Defender actif, {0}", ageText) : L("Microsoft Defender actif"),
                    L("Protection en temps réel active. Aucun antivirus supplémentaire n'est nécessaire pour un usage courant."), 25)
                { Fix = openThreat with { Label = L("Analyser") } };
            }

            var tamper = mp!.GetValueOrDefault("IsTamperProtected");
            if (tamper is bool tp)
            {
                yield return tp
                    ? new SecItem("tamper", SectionEssential, L("Protection contre les falsifications"), "\uE72E", SecLevel.Good, L("Activée"),
                        L("Empêche les logiciels malveillants (et les « optimiseurs ») de désactiver Microsoft Defender."), 5)
                    : new SecItem("tamper", SectionEssential, L("Protection contre les falsifications"), "\uE72E", SecLevel.Warning, L("Désactivée"),
                        L("Un programme malveillant exécuté en administrateur peut désactiver Defender. Activez-la dans Sécurité Windows › Paramètres de protection contre les virus et menaces."), 5)
                    { Fix = new SecFix(L("Activer")) { Uri = "windowsdefender://threatsettings/" } };
            }
            yield break;
        }

        if (thirdParty is not null)
        {
            yield return thirdParty.UpToDate
                ? new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Good, L("{0} actif", thirdParty.Name),
                    L("Un antivirus tiers est enregistré auprès de Sécurité Windows et signale être à jour ; Microsoft Defender se met alors en retrait automatiquement."), 25)
                : new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Warning, L("{0} : définitions à mettre à jour", thirdParty.Name),
                    L("L'antivirus installé signale des définitions de virus obsolètes. Ouvrez-le pour lancer une mise à jour."), 25)
                { Fix = new SecFix(L("Sécurité Windows")) { Uri = "windowsdefender://providers/" } };
            yield break;
        }

        if (mp is null && products.Count == 0)
        {
            yield return new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Unknown, L("État illisible"),
                L("Ni Sécurité Windows ni Microsoft Defender n'ont répondu. Ouvrez Sécurité Windows pour vérifier."), 25)
            { Fix = new SecFix(L("Vérifier")) { Uri = "windowsdefender://threat/" } };
            yield break;
        }

        yield return new SecItem("antivirus", SectionEssential, title, glyph, SecLevel.Critical, L("Aucun antivirus actif détecté"),
            L("Ni Microsoft Defender ni un antivirus tiers ne protège ce PC en temps réel. Réactivez Microsoft Defender dans Sécurité Windows (ou mettez à jour/réinstallez votre antivirus)."), 25)
        { Fix = new SecFix(L("Activer Defender")) { Uri = "windowsdefender://threat/" } };
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
        var title = L("Pare-feu"); const string glyph = "\uE774";
        (string Label, string Local, string Gpo)[] profiles =
        [
            (LC("network profile", "privé"), "StandardProfile", "PrivateProfile"),
            (LC("network profile", "public"), "PublicProfile", "PublicProfile"),
            (LC("network profile", "domaine"), "DomainProfile", "DomainProfile"),
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
            return new SecItem("firewall", SectionEssential, title, glyph, SecLevel.Good, L("Pare-feu Windows actif sur tous les réseaux"),
                thirdParty is not null
                    ? L("Les connexions entrantes non sollicitées sont bloquées sur les réseaux privés, publics et de domaine. {0} est également enregistré.", thirdParty.Name)
                    : L("Les connexions entrantes non sollicitées sont bloquées sur les réseaux privés, publics et de domaine."), 20)
            { Fix = new SecFix(L("Détails")) { Uri = "windowsdefender://network/" } };
        }
        if (thirdParty is not null)
        {
            return new SecItem("firewall", SectionEssential, title, glyph, SecLevel.Good, L("{0} actif", thirdParty.Name),
                L("Le pare-feu Windows est désactivé pour le réseau {0}, mais un pare-feu tiers actif est enregistré auprès de Sécurité Windows.", string.Join(", ", off)), 20);
        }
        var fix = byPolicy
            ? new SecFix(L("Ouvrir")) { Uri = "windowsdefender://network/" }
            : new SecFix(L("Réactiver"))
            {
                ActionId = FirewallEnableAction.ActionId,
                Confirm = L("Le pare-feu Windows va être réactivé pour les profils privé, public et domaine (les règles existantes sont conservées)."),
            };
        return new SecItem("firewall", SectionEssential, title, glyph, critical ? SecLevel.Critical : SecLevel.Warning,
            L("Désactivé pour le réseau {0}", string.Join(", ", off)),
            Sentences(byPolicy ? L("Une stratégie de groupe désactive le pare-feu : seul l'administrateur du PC peut la retirer.") : null,
                L("Sans pare-feu, les services de ce PC sont exposés aux autres appareils du réseau (Wi-Fi public en particulier).")), 20)
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
        var title = L("Contrôle de compte d'utilisateur (UAC)"); const string glyph = "\uE7EF";
        var lua = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.PolSystem, "EnableLUA") ?? 1;
        var cpba = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.PolSystem, "ConsentPromptBehaviorAdmin") ?? 5;
        var secure = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.PolSystem, "PromptOnSecureDesktop") ?? 1;
        // Ne jamais proposer une correction qui abaisse l'invite : « Toujours m'avertir » (2) est conservé par l'action
        // « niveau maximal » ; « identifiants » (1) n'a pas d'équivalent, sauf si l'UAC est entièrement désactivé.
        SecFix? fix = cpba switch
        {
            2 => new SecFix(L("Rétablir")) { TweakId = "security.uac.always", Option = TweakDefinition.Run },
            1 => lua == 0 ? new SecFix(L("Rétablir")) { TweakId = "security.uac.always", Option = TweakDefinition.Run } : null,
            _ => new SecFix(L("Rétablir")) { TweakId = "security.uac.recommended", Option = TweakDefinition.Run },
        };
        var account = profile.IsUserAdmin ? null : L("Vous utilisez un compte standard : c'est la configuration la plus sûre.");

        if (lua == 0)
            return new SecItem("uac", SectionEssential, title, glyph, SecLevel.Critical, L("UAC désactivé"),
                L("Tout programme lancé par un administrateur obtient tous les droits sans rien demander, et les applications du Microsoft Store ne fonctionnent plus correctement. Un redémarrage sera nécessaire après correction."), 15) { Fix = fix };
        if (cpba == 0)
            return new SecItem("uac", SectionEssential, title, glyph, SecLevel.Critical, L("Élévation sans aucune invite"),
                L("Les programmes obtiennent les droits administrateur sans confirmation (« Ne jamais m'avertir ») : un logiciel malveillant peut modifier tout le système en silence."), 15) { Fix = fix };
        if (secure == 0)
            return new SecItem("uac", SectionEssential, title, glyph, SecLevel.Warning, L("Invites hors du Bureau sécurisé"),
                L("Les invites UAC s'affichent sur le bureau normal, où un autre programme peut les imiter ou cliquer dessus à votre place."), 15)
            { Fix = fix };

        var level = cpba switch
        {
            2 => L("Niveau maximal : toujours m'avertir"),
            1 => L("Identifiants demandés sur le Bureau sécurisé"),
            5 => L("Niveau recommandé"),
            _ => L("Invite de confirmation active"),
        };
        return new SecItem("uac", SectionEssential, title, glyph, SecLevel.Good, level,
            Sentences(L("Chaque élévation de privilèges est confirmée sur le Bureau sécurisé."), account), 15);
    }

    // ====================================================================== SmartScreen

    private static SecItem SmartScreen(SystemProfile profile)
    {
        const string title = "SmartScreen", glyph = "\uE890";
        var fix = new SecFix(L("Activer")) { TweakId = "security.smartscreen.apps", Option = TweakDefinition.On };
        var policy = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.SystemPolicy, "EnableSmartScreen");
        var sac = SmartAppControlText(profile);

        if (policy == 0)
            return new SecItem("smartscreen", SectionEssential, title, glyph, SecLevel.Critical, L("Désactivé par une stratégie"),
                Sentences(L("Les programmes téléchargés ne sont plus vérifiés avant leur ouverture. Une stratégie (souvent posée par un outil « d'optimisation ») force cette désactivation."), sac), 8) { Fix = fix };
        if (policy == 1)
            return new SecItem("smartscreen", SectionEssential, title, glyph, SecLevel.Good, L("Imposé par stratégie"),
                Sentences(L("La réputation des programmes et fichiers téléchargés est vérifiée avant leur ouverture."), sac), 8);

        var local = RegistryAccess.ReadString(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled");
        if (string.Equals(local, "Off", StringComparison.OrdinalIgnoreCase))
            return new SecItem("smartscreen", SectionEssential, title, glyph, SecLevel.Warning, L("Vérification des applications désactivée"),
                Sentences(L("Les programmes téléchargés ne sont plus vérifiés avant leur ouverture."), sac), 8) { Fix = fix };
        return new SecItem("smartscreen", SectionEssential, title, glyph, SecLevel.Good, L("Actif"),
            Sentences(L("Windows avertit avant d'ouvrir un programme ou un fichier téléchargé inconnu ou malveillant."), sac), 8)
        { Fix = new SecFix(L("Détails")) { Uri = "windowsdefender://appbrowser/" } };
    }

    private static string SmartAppControlText(SystemProfile profile)
    {
        if (profile.Build < 22621) return "";
        return RegistryAccess.ReadDword(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState") switch
        {
            1 => L("Contrôle intelligent des applications : activé."),
            2 => L("Contrôle intelligent des applications : en évaluation."),
            _ => "",
        };
    }

    // ====================================================================== Chiffrement

    internal static SecItem Encryption(SystemProfile profile)
    {
        var title = L("Chiffrement du disque système"); const string glyph = "\uE72E";
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
            ? new SecFix(L("Chiffrement de l'appareil")) { Uri = "ms-settings:deviceencryption" }
            : new SecFix(L("Gérer BitLocker")) { Tool = SystemTool.Control, ToolArgs = ["/name", "Microsoft.BitLockerDriveEncryption"] };
        var mobile = profile.IsLaptopLike;
        var name = profile.IsHomeEdition ? L("Chiffrement de l'appareil") : "BitLocker";

        return value switch
        {
            1 or 6 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Good, L("{0} chiffré ({1})", drive, name),
                L("En cas de vol du PC ou du disque, vos fichiers restent illisibles sans votre session. Conservez la clé de récupération (compte Microsoft ou copie imprimée)."), 10),
            3 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Info, L("Chiffrement en cours"),
                L("Le chiffrement du disque se poursuit en arrière-plan ; vous pouvez utiliser le PC normalement."), 10),
            4 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Warning, L("Déchiffrement en cours"),
                L("Le disque système est en train d'être déchiffré : ses données ne seront plus protégées en cas de vol."), 10) { Fix = manage },
            5 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Warning, L("Protection suspendue"),
                L("BitLocker est suspendu (souvent pendant une mise à jour du firmware) : la clé est stockée en clair sur le disque. Reprenez la protection si aucune mise à jour n'est en cours."), 10) { Fix = manage },
            8 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Warning, L("En attente d'activation"),
                L("Le disque est préparé pour le chiffrement mais la protection n'est pas activée : connectez-vous avec un compte Microsoft ou activez la protection pour sauvegarder la clé de récupération."), 10) { Fix = manage },
            2 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Warning, L("{0} non chiffré", drive),
                Sentences(mobile ? L("Sur un PC portable, quiconque récupère le disque peut lire vos fichiers sans mot de passe.")
                        : L("Quiconque récupère le disque peut lire vos fichiers sans mot de passe."),
                    profile.SupportsBitLocker ? L("Activez BitLocker (clé de récupération à sauvegarder).")
                        : L("L'édition Famille ne propose que le « chiffrement de l'appareil », si le matériel le permet.")), mobile ? 10 : 6)
            { Fix = manage },
            0 => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Info, L("Non chiffrable"),
                L("Windows indique que ce volume ne peut pas être chiffré sur ce PC."), 0),
            _ => new SecItem("encryption", SectionDevice, title, glyph, SecLevel.Unknown, L("État inconnu"),
                L("L'état du chiffrement n'a pas pu être lu sans droits administrateur."), 10) { Fix = manage },
        };
    }

    // ====================================================================== Secure Boot / TPM

    internal static SecItem SecureBoot()
    {
        var title = L("Démarrage sécurisé (Secure Boot)"); const string glyph = "\uE7E8";
        var uefi = Native.IsUefiFirmware();
        var state = RegistryAccess.ReadDword(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        if (state == 1)
            return new SecItem("secureboot", SectionDevice, title, glyph, SecLevel.Good, L("Activé"),
                L("Seuls des chargeurs de démarrage signés peuvent se lancer : protège contre les « bootkits » qui s'installent avant Windows."), 6);
        if (!uefi)
            return new SecItem("secureboot", SectionDevice, title, glyph, SecLevel.Warning, L("Indisponible (démarrage BIOS hérité)"),
                L("Windows démarre en mode BIOS hérité (MBR) : Secure Boot nécessite une conversion du disque en GPT (outil mbr2gpt) puis le passage du firmware en mode UEFI. Opération délicate, à préparer avec une sauvegarde."), 6);
        if (state == 0)
            return new SecItem("secureboot", SectionDevice, title, glyph, SecLevel.Warning, L("Désactivé dans le firmware UEFI"),
                L("Le PC démarre en UEFI mais Secure Boot est désactivé. Il s'active dans les réglages UEFI (touche F2, F10, Échap ou Suppr au démarrage, section « Security » ou « Boot »). Aucun logiciel ne peut le faire à votre place."), 6);
        return new SecItem("secureboot", SectionDevice, title, glyph, SecLevel.Unknown, L("État inconnu"),
            L("Le firmware n'a pas communiqué l'état de Secure Boot."), 6);
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
        var title = L("Puce de sécurité (TPM)"); const string glyph = "\uE950";
        const uint TpmNotFound = 0x8028400F;
        uint rc;
        TpmDeviceInfo info;
        try { rc = Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<TpmDeviceInfo>(), out info); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new SecItem("tpm", SectionDevice, title, glyph, SecLevel.Unknown, L("État inconnu"), L("Le service TPM de Windows est indisponible."), 3);
        }
        if (rc == 0 && info.TpmVersion == 2)
            return new SecItem("tpm", SectionDevice, title, glyph, SecLevel.Good, L("TPM 2.0 présent"),
                L("Stocke les clés de BitLocker, de Windows Hello et vérifie l'intégrité du démarrage."), 3)
            { Fix = new SecFix(L("Détails")) { Uri = "windowsdefender://devicesecurity/" } };
        if (rc == 0 && info.TpmVersion == 1)
            return new SecItem("tpm", SectionDevice, title, glyph, SecLevel.Info, L("TPM 1.2 (ancienne version)"),
                L("Suffisant pour BitLocker, mais Windows 11 exige un TPM 2.0."), 3);
        if (rc == TpmNotFound)
            return new SecItem("tpm", SectionDevice, title, glyph, SecLevel.Warning, L("Aucun TPM détecté"),
                L("Aucune puce TPM n'est active. Sur beaucoup de PC, elle existe mais est désactivée dans le firmware (Intel PTT ou AMD fTPM dans les réglages UEFI)."), 3);
        return new SecItem("tpm", SectionDevice, title, glyph, SecLevel.Unknown, L("État inconnu"), L("Réponse inattendue du service TPM (0x{0:X8}).", rc), 3);
    }

    // ====================================================================== Isolation du noyau / LSA

    private static SecItem MemoryIntegrity()
    {
        var title = L("Intégrité de la mémoire"); const string glyph = "\uE9F5";
        bool? running = null;
        try
        {
            var row = WmiQuery.Query("SELECT SecurityServicesRunning FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard", 6).FirstOrDefault();
            if (row?.GetValueOrDefault("SecurityServicesRunning") is Array services)
                running = services.Cast<object>().Any(s => Convert.ToInt32(s) == 2);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException) { /* WMI Device Guard absent */ }

        var configured = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.Hvci, "Enabled") == 1;
        var fix = new SecFix(L("Activer")) { TweakId = "security.hvci", Option = TweakDefinition.On };
        if (running == true)
            return new SecItem("hvci", SectionDevice, title, glyph, SecLevel.Good, LC("state", "Active"),
                L("Le noyau refuse les pilotes non signés ou modifiés, même lancés par un administrateur."), 6);
        if (configured)
            return new SecItem("hvci", SectionDevice, title, glyph, SecLevel.Info, L("Activée, effective au prochain redémarrage"),
                L("Si elle reste inactive après redémarrage, un pilote incompatible l'en empêche : la liste est dans Sécurité Windows › Sécurité de l'appareil › Isolation du noyau."), 6)
            { Fix = new SecFix(L("Détails")) { Uri = "windowsdefender://devicesecurity/" } };
        return new SecItem("hvci", SectionDevice, title, glyph, SecLevel.Warning, L("Désactivée"),
            L("Un pilote malveillant ou vulnérable pourrait s'exécuter dans le noyau. Activez-la si vos périphériques sont compatibles (redémarrage nécessaire)."), 6) { Fix = fix };
    }

    public static bool LsaProtectionConfigured()
    {
        var ppl = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.Lsa, "RunAsPPL");
        if (ppl is { } v) return v is 1 or 2;
        return RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.Lsa, "RunAsPPLBoot") is 1 or 2;
    }

    private static SecItem LsaProtection(SystemProfile profile)
    {
        var title = L("Protection LSA"); const string glyph = "\uE8D7";
        if (LsaProtectionConfigured())
            return new SecItem("lsa", SectionDevice, title, glyph, SecLevel.Good, L("Activée"),
                L("Le processus qui détient vos identifiants de session est protégé contre la lecture de sa mémoire."), 4);
        return new SecItem("lsa", SectionDevice, title, glyph, SecLevel.Warning, L("Désactivée"),
            L("Un programme exécuté en administrateur peut extraire les identifiants de session de la mémoire (outils de type Mimikatz)."), 4)
        {
            Fix = profile.Build >= 22621
                ? new SecFix(L("Activer")) { TweakId = "security.lsa.ppl", Option = TweakDefinition.On }
                : new SecFix(L("Détails")) { Uri = "windowsdefender://devicesecurity/" },
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
        var title = L("Protocole SMBv1"); const string glyph = "\uE968";
        if (!RegistryAccess.KeyExists(RegHive.LocalMachine, SecurityTweaks.Smb1ClientService))
            return new SecItem("smb1", SectionSurface, title, glyph, SecLevel.Good, L("Non installé"),
                L("L'ancien protocole de partage exploité par WannaCry n'est pas présent sur ce PC."), 6);
        var uninstall = new SecFix(L("Désinstaller"))
        {
            ActionId = Smb1UninstallAction.ActionId,
            Confirm = L("La fonctionnalité Windows « Support de partage de fichiers SMB 1.0/CIFS » va être désinstallée. Les très anciens NAS, box ou imprimantes réseau qui ne parlent que SMBv1 ne seront plus accessibles. Un redémarrage est nécessaire. Vous pourrez la réinstaller depuis « Fonctionnalités facultatives »."),
        };
        return Smb1Enabled() == true
            ? new SecItem("smb1", SectionSurface, title, glyph, SecLevel.Warning, L("Installé et actif"),
                L("Protocole obsolète, sans chiffrement ni protection contre les attaques de type WannaCry. À désinstaller sauf si un très ancien appareil réseau en dépend."), 6) { Fix = uninstall }
            : new SecItem("smb1", SectionSurface, title, glyph, SecLevel.Good, L("Installé mais désactivé"),
                L("SMBv1 est désactivé ; vous pouvez aussi désinstaller complètement la fonctionnalité."), 6) { Fix = uninstall };
    }

    private static SecItem RemoteDesktop(SystemProfile profile)
    {
        var title = L("Bureau à distance"); const string glyph = "\uE7F4";
        if (profile.IsHomeEdition)
            return new SecItem("rdp", SectionSurface, title, glyph, SecLevel.Good, L("Non disponible sur l'édition Famille"),
                L("Ce PC ne peut pas recevoir de connexions Bureau à distance."), 5);
        var deny = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.TerminalServer, "fDenyTSConnections") ?? 1;
        if (deny != 0)
            return new SecItem("rdp", SectionSurface, title, glyph, SecLevel.Good, L("Désactivé"),
                L("Personne ne peut ouvrir de session sur ce PC à distance via RDP."), 5);
        var nla = RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.TerminalServer + @"\WinStations\RDP-Tcp", "UserAuthentication") ?? 1;
        return new SecItem("rdp", SectionSurface, title, glyph, SecLevel.Warning,
            nla == 1 ? L("Activé") : L("Activé sans authentification réseau (NLA)"),
            Sentences(L("Les connexions RDP entrantes sont autorisées. Si vous ne l'utilisez pas, désactivez-le ; sinon utilisez un mot de passe robuste et n'exposez jamais le port 3389 sur Internet."),
                nla == 1 ? null : L("Réactivez l'authentification au niveau du réseau.")), 5)
        { Fix = new SecFix(L("Désactiver")) { TweakId = "security.rdp", Option = TweakDefinition.Off } };
    }

    private static SecItem RemoteAssistanceItem()
    {
        var title = L("Assistance à distance"); const string glyph = "\uE716";
        var allowed = (RegistryAccess.ReadDword(RegHive.LocalMachine, SecurityTweaks.RemoteAssistance, "fAllowToGetHelp") ?? 1) == 1;
        return allowed
            ? new SecItem("remoteassistance", SectionSurface, title, glyph, SecLevel.Info, L("Autorisée (sur invitation)"),
                L("Une personne peut prendre le contrôle de ce PC si vous lui envoyez une invitation. Réglage d'origine de Windows ; désactivez-le si vous ne vous en servez pas."), 0)
            { Fix = new SecFix(L("Désactiver")) { TweakId = "security.remoteassistance", Option = TweakDefinition.Off } }
            : new SecItem("remoteassistance", SectionSurface, title, glyph, SecLevel.Good, L("Désactivée"),
                L("L'ancien outil d'assistance à distance (msra.exe) ne peut pas être utilisé pour prendre la main sur ce PC."), 0);
    }

    private static SecItem AutoRun()
    {
        var title = L("Exécution automatique des supports"); const string glyph = "\uE88E";
        static bool Disabled(RegHive hive) =>
            RegistryAccess.ReadDword(hive, SecurityTweaks.PolExplorer, "NoDriveTypeAutoRun") == 255
            || RegistryAccess.ReadDword(hive, SecurityTweaks.PolExplorer, "NoAutorun") == 1;
        if (Disabled(RegHive.LocalMachine) || Disabled(RegHive.CurrentUser))
            return new SecItem("autorun", SectionSurface, title, glyph, SecLevel.Good, L("Désactivée"),
                L("Rien ne s'exécute ni ne s'ouvre automatiquement à l'insertion d'une clé USB, d'un disque ou d'un CD."), 3);
        return new SecItem("autorun", SectionSurface, title, glyph, SecLevel.Info, L("Lecture automatique active"),
            L("Windows propose une action à l'insertion d'un support ; autorun.inf n'est plus exécuté depuis les clés USB depuis Windows 7, mais peut l'être depuis un CD/DVD."), 3)
        { Fix = new SecFix(L("Désactiver")) { TweakId = "security.autorun", Option = TweakDefinition.Off } };
    }

    private static SecItem BuiltInAccounts()
    {
        var title = L("Comptes intégrés Administrateur et Invité"); const string glyph = "\uE77B";
        var rows = WmiQuery.Query("SELECT Name, SID, Disabled FROM Win32_UserAccount WHERE LocalAccount = TRUE", timeoutSeconds: 10);
        string? admin = null, guest = null;
        foreach (var r in rows)
        {
            if (r.GetValueOrDefault("SID") is not string sid || r.GetValueOrDefault("Disabled") is not bool disabled || disabled) continue;
            if (sid.EndsWith("-500", StringComparison.Ordinal)) admin = r.GetValueOrDefault("Name") as string ?? L("Administrateur");
            else if (sid.EndsWith("-501", StringComparison.Ordinal)) guest = r.GetValueOrDefault("Name") as string ?? L("Invité");
        }
        if (admin is null && guest is null)
            return new SecItem("accounts", SectionSurface, title, glyph, SecLevel.Good, L("Désactivés"),
                L("Les comptes intégrés, cibles classiques des attaques (nom connu d'avance), sont désactivés."), 5);
        return new SecItem("accounts", SectionSurface, title, glyph, SecLevel.Warning,
            admin is not null && guest is not null ? L("Comptes activés : « {0} » et « {1} »", admin, guest) : L("Compte activé : « {0} »", admin ?? guest),
            Sentences(admin is not null ? L("Le compte Administrateur intégré n'est pas soumis à l'UAC et son nom est connu d'avance.") : null,
                guest is not null ? L("Le compte Invité permet d'ouvrir une session sans mot de passe.") : null,
                L("Désactivez-les s'ils ne sont pas indispensables.")), 5)
        { Fix = new SecFix(L("Gérer les comptes")) { PageId = "users" } };
    }

    private static SecItem FileExtensions()
    {
        var title = L("Extensions de fichiers visibles"); const string glyph = "\uE8A5";
        var hidden = (RegistryAccess.ReadDword(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt") ?? 1) != 0;
        return hidden
            ? new SecItem("extensions", SectionSurface, title, glyph, SecLevel.Warning, L("Masquées"),
                L("Un fichier « facture.pdf.exe » apparaît comme « facture.pdf » : afficher les extensions aide à repérer les pièces jointes piégées."), 2)
            { Fix = new SecFix(L("Afficher")) { TweakId = "custom.explorer.extensions", Option = TweakDefinition.On } }
            : new SecItem("extensions", SectionSurface, title, glyph, SecLevel.Good, L("Affichées"),
                L("Vous voyez le vrai type de chaque fichier (.exe, .pdf, .docx…)."), 2);
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
        if (!thread.Join(timeout)) throw new TimeoutException(L("Le shell Windows n'a pas répondu à temps."));
        if (error is not null) throw error;
        return result;
    }
}
