using System.Text.RegularExpressions;
using Timonier.Core.Localization;
using Timonier.Core.Platform;

namespace Timonier.Modules.Performance;

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

        var cpu = string.IsNullOrWhiteSpace(p.CpuName) ? L("Processeur inconnu") : Regex.Replace(p.CpuName, @"\s+", " ").Replace("(R)", "").Replace("(TM)", "").Trim();
        var logical = Threads(p);
        var cpuText = Cores(p) is { } cores
            ? LP(cores, "{1} · {0} cœur, {2} threads", "{1} · {0} cœurs, {2} threads", cpu, logical)
            : LP(logical, "{1} · {0} processeur logique", "{1} · {0} processeurs logiques", cpu);
        facts.Add(IsLowEndCpu(p) || logical <= 2
            ? new("", L("Processeur"), cpuText, L("Entrée de gamme"), AdviceTone.Limit)
            : logical >= 12
                ? new("", L("Processeur"), cpuText, L("Puissant"), AdviceTone.Good)
                : new("", L("Processeur"), cpuText, null, AdviceTone.Neutral));

        var ram = p.RamGb > 0 ? L("{0} Go", p.RamGb.ToString("0.#", Loc.Culture)) : L("Inconnue");
        facts.Add(p.RamGb switch
        {
            > 0 and < 6 => new("", L("Mémoire vive"), ram, L("Juste pour Windows 11"), AdviceTone.Limit),
            >= 16 => new("", L("Mémoire vive"), ram, L("Confortable"), AdviceTone.Good),
            _ => new("", L("Mémoire vive"), ram, p.RamGb > 0 ? L("Suffisante pour un usage courant") : null, AdviceTone.Neutral),
        });

        var disk = p.SystemDisk;
        var emmc = IsEmmc(disk);
        var diskText = disk is null ? L("Inconnu") : $"{(emmc ? L("Mémoire eMMC") : MediaLabel(disk.Media))}{(disk.SizeBytes > 0 ? " · " + Format.Bytes(disk.SizeBytes) : "")}{(disk.Model.Length > 0 ? " · " + disk.Model.Trim() : "")}";
        facts.Add(emmc ? new("", L("Disque système"), diskText, L("Plus lent qu'un vrai SSD"), AdviceTone.Limit) : disk?.Media switch
        {
            DiskMedia.Hdd => new("", L("Disque système"), diskText, L("Principal facteur de lenteur"), AdviceTone.Limit),
            DiskMedia.Nvme => new("", L("Disque système"), diskText, L("Très rapide"), AdviceTone.Good),
            DiskMedia.Ssd => new("", L("Disque système"), diskText, L("Rapide"), AdviceTone.Good),
            _ => new("", L("Disque système"), diskText, null, AdviceTone.Neutral),
        });

        if (p.Gpus.Count == 0)
        {
            facts.Add(new("", L("Carte graphique"), L("Inconnue"), null, AdviceTone.Neutral));
        }
        else
        {
            var names = string.Join(" + ", p.Gpus.Select(g => g.Name.Trim()).Distinct());
            var dedicated = p.Gpus.Any(g => !g.Integrated && g.Vendor is HardwareVendor.Nvidia or HardwareVendor.Amd);
            facts.Add(dedicated
                ? new("", L("Carte graphique"), names, L("Dédiée : adaptée aux jeux"), AdviceTone.Good)
                : new("", L("Carte graphique"), names, L("Intégrée : jeux légers uniquement"), AdviceTone.Neutral));
        }

        if (p.HasBattery || source?.HasBattery == true)
        {
            var state = source switch
            {
                null => L("Batterie présente"),
                { OnBattery: true } => source.BatterySaver
                    ? (source.BatteryPercent is { } b ? L("Sur batterie ({0} %) · économiseur actif", b) : L("Sur batterie · économiseur actif"))
                    : (source.BatteryPercent is { } b2 ? L("Sur batterie ({0} %)", b2) : L("Sur batterie")),
                _ => source.BatteryPercent is { } c ? L("Sur secteur ({0} %)", c) : L("Sur secteur"),
            };
            facts.Add(new("", L("Alimentation"), state, L("Portable : l'autonomie compte"), AdviceTone.Neutral));
        }
        else
        {
            facts.Add(new("", L("Alimentation"), L("Secteur uniquement (pas de batterie)"), null, AdviceTone.Neutral));
        }
        return facts;
    }

    public static string MediaLabel(DiskMedia media) => media switch
    {
        DiskMedia.Hdd => L("Disque dur (HDD)"),
        DiskMedia.Ssd => "SSD",
        DiskMedia.Nvme => "SSD NVMe",
        _ => L("Type inconnu"),
    };

    /// <summary>Résumé en une phrase du niveau de performance.</summary>
    public static string TierSummary(SystemProfile p) => p.Tier switch
    {
        PerformanceTier.Low => L("PC modeste : chaque ressource compte. Timonier privilégie la réactivité plutôt que les effets."),
        PerformanceTier.Medium => L("PC polyvalent : les réglages d'origine conviennent ; quelques ajustements ciblés suffisent."),
        PerformanceTier.High => L("PC performant : inutile de sacrifier le confort, concentrez-vous sur les jeux et l'alimentation."),
        _ => L("Analyse du matériel en cours…"),
    };

    /// <summary>Conseils concrets pour ce matériel (du plus utile au moins utile).</summary>
    public static List<HardwareTip> Tips(SystemProfile p)
    {
        var tips = new List<HardwareTip>();
        if (!p.HardwareLoaded && p.Tier == PerformanceTier.Unknown) return tips;

        if (p.SystemDiskIsHdd)
        {
            tips.Add(new("", L("Le disque dur mécanique est le principal frein : remplacer le disque système par un SSD apporte bien plus que n'importe quel réglage logiciel.")));
            tips.Add(new("", L("Gardez SysMain actif : il précharge les applications et compense en partie la lenteur du disque."), "perf.svc.sysmain"));
        }
        if (p.Tier == PerformanceTier.Low)
        {
            tips.Add(new("", L("Réduisez les effets visuels (préréglage « Meilleures performances ») pour une interface plus vive."), "perf.fx.preset"));
            tips.Add(new("", L("Désactivez les captures de jeu de la Xbox Game Bar si vous ne vous en servez pas."), "perf.game.capture"));
        }
        if (p.RamGb is > 0 and <= 8)
        {
            tips.Add(new("", L("Avec {0} Go de mémoire, limitez les programmes lancés au démarrage et les onglets ouverts : c'est le premier levier contre les ralentissements.", p.RamGb.ToString("0.#", Loc.Culture))));
        }
        if (p.HasBattery)
        {
            tips.Add(new("", L("Sur batterie, préférez le mode « Équilibré » ou « Meilleure efficacité énergétique » et laissez Windows limiter les tâches en arrière-plan."), "perf.power.throttling"));
        }
        if (p.Gpus.Any(g => !g.Integrated && g.Vendor is HardwareVendor.Nvidia or HardwareVendor.Amd))
        {
            tips.Add(new("", L("Carte graphique dédiée : activez la planification GPU à accélération matérielle et le Mode Jeu."), "perf.game.hags"));
        }
        if (!p.HasBattery && p.Tier == PerformanceTier.High)
        {
            tips.Add(new("", L("PC fixe performant : le plan « Performances optimales » est pertinent pour les charges lourdes (montage, calcul), au prix d'une consommation plus élevée.")));
        }
        if (IsEmmc(p.SystemDisk))
        {
            tips.Add(new("", L("Stockage eMMC : lent et de petite capacité. Gardez au moins 15 % d'espace libre et évitez d'installer de gros logiciels ; les mises à jour de Windows en ont besoin.")));
        }
        else if (p.SystemDisk?.Media is DiskMedia.Ssd or DiskMedia.Nvme)
        {
            tips.Add(new("", L("Disque SSD : vérifiez que TRIM reste actif ; la défragmentation classique est inutile (Windows optimise le SSD)."), "perf.storage.trim"));
        }
        if (tips.Count == 0)
            tips.Add(new("", L("Les réglages d'origine de Windows sont adaptés à ce PC. Ajustez selon vos usages ci-dessous.")));
        return tips;
    }
}
