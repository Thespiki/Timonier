using System.Globalization;
using System.Text.RegularExpressions;
using PcPilot.Core.Platform;

namespace PcPilot.Modules.Performance;

/// <summary>Caractéristique matérielle affichée dans la fiche, avec son verdict (atout / limite / neutre).</summary>
public sealed record HardwareFact(string Glyph, string Label, string Value, string? Verdict, AdviceTone Tone);

/// <summary>Conseil adapté au matériel ; <see cref="TweakId"/> permet de mettre le réglage correspondant en évidence.</summary>
public sealed record HardwareTip(string Glyph, string Text, string? TweakId = null);

public enum AdviceTone { Neutral, Good, Limit }

/// <summary>
/// Analyse du profil matériel (lecture seule, calculs purs) : raisons du niveau de performance et conseils concrets.
/// Les critères reprennent ceux du calcul du niveau (<see cref="SystemProfile.Tier"/>).
/// </summary>
public static partial class HardwareAdvice
{
    [GeneratedRegex(@"Celeron|Pentium|Atom|Athlon (Silver|Gold)|\bN\d{3,4}\b|\bN\d{2}\b|A[46]-|E[12]-|Snapdragon 7c", RegexOptions.IgnoreCase)]
    private static partial Regex LowCpuRx();

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static bool IsLowEndCpu(SystemProfile p) => LowCpuRx().IsMatch(p.CpuName);

    /// <summary>
    /// Processeurs logiques. Le profil peut cumuler les valeurs WMI d'un lancement à l'autre (cache) : on retient
    /// le nombre du système d'exploitation dès que la valeur du profil est incohérente.
    /// </summary>
    public static int Threads(SystemProfile p) =>
        p.CpuThreads > 0 && p.CpuThreads <= Environment.ProcessorCount ? p.CpuThreads : Environment.ProcessorCount;

    public static int? Cores(SystemProfile p) => p.CpuCores > 0 && p.CpuCores <= Threads(p) ? p.CpuCores : null;

    /// <summary>Stockage eMMC (soudé, classé « SSD » par Windows mais bien plus lent).</summary>
    public static bool IsEmmc(DiskInfo? d) => d is not null &&
        (d.Model.Contains("MMC", StringComparison.OrdinalIgnoreCase) || d.Bus.Contains("MMC", StringComparison.OrdinalIgnoreCase));

    public static List<HardwareFact> Facts(SystemProfile p, PowerSource? source)
    {
        var facts = new List<HardwareFact>();

        var cpu = string.IsNullOrWhiteSpace(p.CpuName) ? "Processeur inconnu" : Regex.Replace(p.CpuName, @"\s+", " ").Replace("(R)", "").Replace("(TM)", "").Trim();
        var logical = Threads(p);
        var threads = Cores(p) is { } cores
            ? $" · {cores} cœurs, {logical} threads"
            : $" · {logical} processeurs logiques";
        facts.Add(IsLowEndCpu(p) || logical <= 2
            ? new("", "Processeur", cpu + threads, "Entrée de gamme", AdviceTone.Limit)
            : logical >= 12
                ? new("", "Processeur", cpu + threads, "Puissant", AdviceTone.Good)
                : new("", "Processeur", cpu + threads, null, AdviceTone.Neutral));

        var ram = p.RamGb > 0 ? p.RamGb.ToString("0.#", Fr) + " Go" : "Inconnue";
        facts.Add(p.RamGb switch
        {
            > 0 and < 6 => new("", "Mémoire vive", ram, "Juste pour Windows 11", AdviceTone.Limit),
            >= 16 => new("", "Mémoire vive", ram, "Confortable", AdviceTone.Good),
            _ => new("", "Mémoire vive", ram, p.RamGb > 0 ? "Suffisante pour un usage courant" : null, AdviceTone.Neutral),
        });

        var disk = p.SystemDisk;
        var emmc = IsEmmc(disk);
        var diskText = disk is null ? "Inconnu" : $"{(emmc ? "Mémoire eMMC" : MediaLabel(disk.Media))}{(disk.SizeBytes > 0 ? " · " + Format.Bytes(disk.SizeBytes) : "")}{(disk.Model.Length > 0 ? " · " + disk.Model.Trim() : "")}";
        facts.Add(emmc ? new("", "Disque système", diskText, "Plus lent qu'un vrai SSD", AdviceTone.Limit) : disk?.Media switch
        {
            DiskMedia.Hdd => new("", "Disque système", diskText, "Principal facteur de lenteur", AdviceTone.Limit),
            DiskMedia.Nvme => new("", "Disque système", diskText, "Très rapide", AdviceTone.Good),
            DiskMedia.Ssd => new("", "Disque système", diskText, "Rapide", AdviceTone.Good),
            _ => new("", "Disque système", diskText, null, AdviceTone.Neutral),
        });

        if (p.Gpus.Count == 0)
        {
            facts.Add(new("", "Carte graphique", "Inconnue", null, AdviceTone.Neutral));
        }
        else
        {
            var names = string.Join(" + ", p.Gpus.Select(g => g.Name.Trim()).Distinct());
            var dedicated = p.Gpus.Any(g => !g.Integrated && g.Vendor is HardwareVendor.Nvidia or HardwareVendor.Amd);
            facts.Add(dedicated
                ? new("", "Carte graphique", names, "Dédiée : adaptée aux jeux", AdviceTone.Good)
                : new("", "Carte graphique", names, "Intégrée : jeux légers uniquement", AdviceTone.Neutral));
        }

        if (p.HasBattery || source?.HasBattery == true)
        {
            var state = source switch
            {
                null => "Batterie présente",
                { OnBattery: true } => $"Sur batterie{Percent(source)}{(source.BatterySaver ? " · économiseur actif" : "")}",
                _ => $"Sur secteur{Percent(source)}",
            };
            facts.Add(new("", "Alimentation", state, "Portable : l'autonomie compte", AdviceTone.Neutral));
        }
        else
        {
            facts.Add(new("", "Alimentation", "Secteur uniquement (pas de batterie)", null, AdviceTone.Neutral));
        }
        return facts;
    }

    private static string Percent(PowerSource s) => s.BatteryPercent is { } v ? $" ({v} %)" : "";

    public static string MediaLabel(DiskMedia media) => media switch
    {
        DiskMedia.Hdd => "Disque dur (HDD)",
        DiskMedia.Ssd => "SSD",
        DiskMedia.Nvme => "SSD NVMe",
        _ => "Type inconnu",
    };

    /// <summary>Résumé en une phrase du niveau de performance.</summary>
    public static string TierSummary(SystemProfile p) => p.Tier switch
    {
        PerformanceTier.Low => "PC modeste : chaque ressource compte. PC Pilot privilégie la réactivité plutôt que les effets.",
        PerformanceTier.Medium => "PC polyvalent : les réglages d'origine conviennent ; quelques ajustements ciblés suffisent.",
        PerformanceTier.High => "PC performant : inutile de sacrifier le confort, concentrez-vous sur les jeux et l'alimentation.",
        _ => "Analyse du matériel en cours…",
    };

    /// <summary>Conseils concrets pour ce matériel (du plus utile au moins utile).</summary>
    public static List<HardwareTip> Tips(SystemProfile p)
    {
        var tips = new List<HardwareTip>();
        if (!p.HardwareLoaded && p.Tier == PerformanceTier.Unknown) return tips;

        if (p.SystemDiskIsHdd)
        {
            tips.Add(new("", "Le disque dur mécanique est le principal frein : remplacer le disque système par un SSD apporte " +
                                   "bien plus que n'importe quel réglage logiciel."));
            tips.Add(new("", "Gardez SysMain actif : il précharge les applications et compense en partie la lenteur du disque.", "perf.svc.sysmain"));
        }
        if (p.Tier == PerformanceTier.Low)
        {
            tips.Add(new("", "Réduisez les effets visuels (préréglage « Meilleures performances ») pour une interface plus vive.", "perf.fx.preset"));
            tips.Add(new("", "Désactivez les captures de jeu de la Xbox Game Bar si vous ne vous en servez pas.", "perf.game.capture"));
        }
        if (p.RamGb is > 0 and <= 8)
        {
            tips.Add(new("", $"Avec {p.RamGb.ToString("0.#", Fr)} Go de mémoire, limitez les programmes lancés au démarrage " +
                                   "et les onglets ouverts : c'est le premier levier contre les ralentissements."));
        }
        if (p.HasBattery)
        {
            tips.Add(new("", "Sur batterie, préférez le mode « Équilibré » ou « Meilleure efficacité énergétique » et laissez " +
                                   "Windows limiter les tâches en arrière-plan.", "perf.power.throttling"));
        }
        if (p.Gpus.Any(g => !g.Integrated && g.Vendor is HardwareVendor.Nvidia or HardwareVendor.Amd))
        {
            tips.Add(new("", "Carte graphique dédiée : activez la planification GPU à accélération matérielle et le Mode Jeu.", "perf.game.hags"));
        }
        if (!p.HasBattery && p.Tier == PerformanceTier.High)
        {
            tips.Add(new("", "PC fixe performant : le plan « Performances optimales » est pertinent pour les charges lourdes " +
                                   "(montage, calcul), au prix d'une consommation plus élevée."));
        }
        if (IsEmmc(p.SystemDisk))
        {
            tips.Add(new("", "Stockage eMMC : lent et de petite capacité. Gardez au moins 15 % d'espace libre et évitez d'installer " +
                                   "de gros logiciels ; les mises à jour de Windows en ont besoin."));
        }
        else if (p.SystemDisk?.Media is DiskMedia.Ssd or DiskMedia.Nvme)
        {
            tips.Add(new("", "Disque SSD : vérifiez que TRIM reste actif ; la défragmentation classique est inutile (Windows optimise le SSD).", "perf.storage.trim"));
        }
        if (tips.Count == 0)
            tips.Add(new("", "Les réglages d'origine de Windows sont adaptés à ce PC. Ajustez selon vos usages ci-dessous."));
        return tips;
    }
}
