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

        var cpu = string.IsNullOrWhiteSpace(p.CpuName) ? L("Unknown processor") : Regex.Replace(p.CpuName, @"\s+", " ").Replace("(R)", "").Replace("(TM)", "").Trim();
        var logical = Threads(p);
        var cpuText = Cores(p) is { } cores
            ? LP(cores, "{1} · {0} core, {2} threads", "{1} · {0} cores, {2} threads", cpu, logical)
            : LP(logical, "{1} · {0} logical processor", "{1} · {0} logical processors", cpu);
        facts.Add(IsLowEndCpu(p) || logical <= 2
            ? new("", L("Processor"), cpuText, L("Entry-level"), AdviceTone.Limit)
            : logical >= 12
                ? new("", L("Processor"), cpuText, L("Powerful"), AdviceTone.Good)
                : new("", L("Processor"), cpuText, null, AdviceTone.Neutral));

        var ram = p.RamGb > 0 ? L("{0} GB", p.RamGb.ToString("0.#", Loc.Culture)) : LC("feminine", "Unknown");
        facts.Add(p.RamGb switch
        {
            > 0 and < 6 => new("", L("RAM"), ram, L("Barely enough for Windows 11"), AdviceTone.Limit),
            >= 16 => new("", L("RAM"), ram, L("Comfortable"), AdviceTone.Good),
            _ => new("", L("RAM"), ram, p.RamGb > 0 ? L("Enough for everyday use") : null, AdviceTone.Neutral),
        });

        var disk = p.SystemDisk;
        var emmc = IsEmmc(disk);
        var diskText = disk is null ? L("Unknown") : $"{(emmc ? L("eMMC storage") : MediaLabel(disk.Media))}{(disk.SizeBytes > 0 ? " · " + Format.Bytes(disk.SizeBytes) : "")}{(disk.Model.Length > 0 ? " · " + disk.Model.Trim() : "")}";
        facts.Add(emmc ? new("", L("System disk"), diskText, L("Slower than a true SSD"), AdviceTone.Limit) : disk?.Media switch
        {
            DiskMedia.Hdd => new("", L("System disk"), diskText, L("Main cause of slowness"), AdviceTone.Limit),
            DiskMedia.Nvme => new("", L("System disk"), diskText, L("Very fast"), AdviceTone.Good),
            DiskMedia.Ssd => new("", L("System disk"), diskText, L("Fast"), AdviceTone.Good),
            _ => new("", L("System disk"), diskText, null, AdviceTone.Neutral),
        });

        if (p.Gpus.Count == 0)
        {
            facts.Add(new("", L("Graphics card"), LC("feminine", "Unknown"), null, AdviceTone.Neutral));
        }
        else
        {
            var names = string.Join(" + ", p.Gpus.Select(g => g.Name.Trim()).Distinct());
            var dedicated = p.Gpus.Any(g => !g.Integrated && g.Vendor is HardwareVendor.Nvidia or HardwareVendor.Amd);
            facts.Add(dedicated
                ? new("", L("Graphics card"), names, L("Dedicated: suited for gaming"), AdviceTone.Good)
                : new("", L("Graphics card"), names, L("Integrated: light gaming only"), AdviceTone.Neutral));
        }

        if (p.HasBattery || source?.HasBattery == true)
        {
            var state = source switch
            {
                null => L("Battery present"),
                { OnBattery: true } => source.BatterySaver
                    ? (source.BatteryPercent is { } b ? L("On battery ({0}%) · saver on", b) : L("On battery · saver on"))
                    : (source.BatteryPercent is { } b2 ? L("On battery ({0}%)", b2) : L("On battery")),
                _ => source.BatteryPercent is { } c ? L("Plugged in ({0}%)", c) : L("Plugged in"),
            };
            facts.Add(new("", L("Power"), state, L("Laptop: battery life matters"), AdviceTone.Neutral));
        }
        else
        {
            facts.Add(new("", L("Power"), L("AC power only (no battery)"), null, AdviceTone.Neutral));
        }
        return facts;
    }

    public static string MediaLabel(DiskMedia media) => media switch
    {
        DiskMedia.Hdd => L("Hard disk drive (HDD)"),
        DiskMedia.Ssd => "SSD",
        DiskMedia.Nvme => "SSD NVMe",
        _ => L("Unknown type"),
    };

    /// <summary>Résumé en une phrase du niveau de performance.</summary>
    public static string TierSummary(SystemProfile p) => p.Tier switch
    {
        PerformanceTier.Low => L("Modest PC: every resource counts. Timonier favors responsiveness over effects."),
        PerformanceTier.Medium => L("All-purpose PC: the original settings work well; a few targeted tweaks are enough."),
        PerformanceTier.High => L("High-performance PC: no need to sacrifice comfort; focus on games and power."),
        _ => LC("long form", "Scanning hardware…"),
    };

    /// <summary>Conseils concrets pour ce matériel (du plus utile au moins utile).</summary>
    public static List<HardwareTip> Tips(SystemProfile p)
    {
        var tips = new List<HardwareTip>();
        if (!p.HardwareLoaded && p.Tier == PerformanceTier.Unknown) return tips;

        if (p.SystemDiskIsHdd)
        {
            tips.Add(new("", L("The mechanical hard drive is the main bottleneck: replacing the system drive with an SSD does far more than any software setting.")));
            tips.Add(new("", L("Keep SysMain running: it preloads apps and partly makes up for the slow drive."), "perf.svc.sysmain"));
        }
        if (p.Tier == PerformanceTier.Low)
        {
            tips.Add(new("", L("Reduce visual effects (“Best performance” preset) for a snappier interface."), "perf.fx.preset"));
            tips.Add(new("", L("Turn off Xbox Game Bar game captures if you don't use them."), "perf.game.capture"));
        }
        if (p.RamGb is > 0 and <= 8)
        {
            tips.Add(new("", L("With {0} GB of memory, limit the programs launched at startup and the tabs you keep open: that's the first way to fight slowdowns.", p.RamGb.ToString("0.#", Loc.Culture))));
        }
        if (p.HasBattery)
        {
            tips.Add(new("", L("On battery, prefer the “Balanced” or “Best power efficiency” mode and let Windows limit background tasks."), "perf.power.throttling"));
        }
        if (p.Gpus.Any(g => !g.Integrated && g.Vendor is HardwareVendor.Nvidia or HardwareVendor.Amd))
        {
            tips.Add(new("", L("Dedicated graphics card: turn on hardware-accelerated GPU scheduling and Game Mode."), "perf.game.hags"));
        }
        if (!p.HasBattery && p.Tier == PerformanceTier.High)
        {
            tips.Add(new("", L("High-performance desktop PC: the “Ultimate Performance” plan makes sense for heavy workloads (video editing, computing), at the cost of higher power consumption.")));
        }
        if (IsEmmc(p.SystemDisk))
        {
            tips.Add(new("", L("eMMC storage: slow and small. Keep at least 15% free space and avoid installing large software; Windows updates need that space.")));
        }
        else if (p.SystemDisk?.Media is DiskMedia.Ssd or DiskMedia.Nvme)
        {
            tips.Add(new("", L("SSD: make sure TRIM stays active; classic defragmentation is unnecessary (Windows optimizes the SSD)."), "perf.storage.trim"));
        }
        if (tips.Count == 0)
            tips.Add(new("", L("Windows' original settings suit this PC. Adjust them to your needs below.")));
        return tips;
    }
}
