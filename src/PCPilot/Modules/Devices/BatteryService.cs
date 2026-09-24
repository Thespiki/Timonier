using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using PcPilot.Core.Catalog;
using PcPilot.Core.Platform;

namespace PcPilot.Modules.Devices;

/// <summary>État instantané de l'alimentation (GetSystemPowerStatus).</summary>
public sealed record PowerNow(bool HasBattery, bool OnAc, bool Charging, int? Percent, TimeSpan? Remaining, bool Saver);

/// <summary>Une batterie décrite par le rapport de powercfg.</summary>
public sealed record BatteryPack(string Name, string? Manufacturer, string? Chemistry, long DesignMwh, long FullMwh, int? Cycles)
{
    /// <summary>Usure (0–1) : part de la capacité d'origine perdue. Null si la capacité d'origine est inconnue.</summary>
    public double? Wear => DesignMwh > 0 && FullMwh > 0 ? Math.Clamp(1 - (double)FullMwh / DesignMwh, 0, 1) : null;
}

/// <summary>
/// Batterie : charge en direct, capacités (WinRT, instantané) et rapport détaillé de powercfg (cycles, fabricant),
/// qui fonctionne sans élévation. Tout est en lecture seule ; les fichiers générés restent dans le cache de PC Pilot.
/// </summary>
public static partial class BatteryService
{
    public static string CacheDir => Path.Combine(AppPaths.LocalData, "cache");

    // ------------------------------------------------------------------ Instantané

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static PowerNow Now()
    {
        if (!GetSystemPowerStatus(out var s)) return new PowerNow(false, true, false, null, null, false);
        var noBattery = (s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255;
        return new PowerNow(
            HasBattery: !noBattery,
            OnAc: s.ACLineStatus == 1,
            Charging: (s.BatteryFlag & 8) != 0,
            Percent: s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null,
            Remaining: s.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(s.BatteryLifeTime) : null,
            Saver: s.SystemStatusFlag == 1);
    }

    /// <summary>Capacités agrégées de toutes les batteries (WinRT, rapide, sans élévation).</summary>
    public static (long DesignMwh, long FullMwh)? Capacities()
    {
        try
        {
            var report = Windows.Devices.Power.Battery.AggregateBattery.GetReport();
            if (report.Status == Windows.System.Power.BatteryStatus.NotPresent) return null;
            return (report.DesignCapacityInMilliwattHours ?? 0, report.FullChargeCapacityInMilliwattHours ?? 0);
        }
        catch (Exception ex)
        {
            Log.Warn("Devices", "capacité batterie (WinRT) : " + ex.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------ Rapport powercfg

    /// <summary>Génère le rapport XML de powercfg dans le cache puis l'analyse. Null si indisponible.</summary>
    public static async Task<List<BatteryPack>?> LoadReportAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(CacheDir);
        var file = Path.Combine(CacheDir, "battery-report.xml");
        try
        {
            var r = await ProcessRunner.RunAsync(SystemTool.PowerCfg, ["/batteryreport", "/xml", "/output", file],
                new RunOptions { Timeout = TimeSpan.FromSeconds(60) }, ct).ConfigureAwait(false);
            if (!r.Success || !File.Exists(file)) return null;
            return Parse(XDocument.Load(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or FileNotFoundException)
        {
            Log.Warn("Devices", "rapport de batterie : " + ex.Message);
            return null;
        }
    }

    private static List<BatteryPack> Parse(XDocument doc)
    {
        XNamespace ns = "http://schemas.microsoft.com/battery/2012";
        var list = new List<BatteryPack>();
        var i = 0;
        foreach (var b in doc.Descendants(ns + "Batteries").Elements(ns + "Battery"))
        {
            i++;
            string? S(string n) => b.Element(ns + n)?.Value is { Length: > 0 } v ? v.Trim() : null;
            long L(string n) => long.TryParse(S(n), out var v) ? v : 0;
            var cycles = int.TryParse(S("CycleCount"), out var c) ? c : (int?)null;
            list.Add(new BatteryPack($"Batterie {i}",S("Manufacturer"), Chemistry(S("Chemistry")), L("DesignCapacity"), L("FullChargeCapacity"), cycles));
        }
        return list;
    }

    private static string? Chemistry(string? raw) => raw?.ToUpperInvariant() switch
    {
        null or "" => null,
        "LION" or "LI-I" or "LI" => "Lithium-ion",
        "LIP" or "LIPO" => "Lithium-polymère",
        "NIMH" => "Nickel-métal-hydrure",
        "PBAC" => "Plomb-acide",
        _ => null, // valeur non standard (certains micrologiciels écrivent n'importe quoi) : on ne l'affiche pas
    };

    /// <summary>Génère le rapport HTML complet de Windows dans le cache et l'ouvre dans le navigateur par défaut.</summary>
    public static async Task<(bool Ok, string Message)> OpenHtmlReportAsync()
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var file = Path.Combine(CacheDir, "battery-report.html");
            var r = await ProcessRunner.RunAsync(SystemTool.PowerCfg, ["/batteryreport", "/output", file],
                new RunOptions { Timeout = TimeSpan.FromSeconds(60) }).ConfigureAwait(true);
            if (!r.Success || !File.Exists(file)) return (false, "Windows n'a pas pu générer le rapport de batterie.");
            // Fichier créé par PC Pilot dans son propre cache : ouverture par l'application associée aux .html.
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
            return (true, "Rapport de batterie ouvert dans le navigateur.");
        }
        catch (Exception ex)
        {
            Log.Warn("Devices", "rapport HTML de batterie : " + ex.Message);
            return (false, "Impossible d'ouvrir le rapport de batterie.");
        }
    }

    // ------------------------------------------------------------------ Santé

    public static HealthResult Health()
    {
        var caps = Capacities();
        if (caps is null) return new HealthResult(HealthStatus.Good, "Pas de batterie : rien à surveiller.");
        var (design, full) = caps.Value;
        if (design <= 0 || full <= 0) return new HealthResult(HealthStatus.Unknown, "Capacité de la batterie non communiquée par le micrologiciel.");
        var wear = Math.Clamp(1 - (double)full / design, 0, 1);
        var detail = $"Capacité actuelle {full / 1000.0:0.#} Wh sur {design / 1000.0:0.#} Wh d'origine.";
        return wear switch
        {
            > 0.40 => new HealthResult(HealthStatus.Warning, $"Batterie usée à {Format.Percent(wear)} : autonomie nettement réduite.", detail + " Un remplacement peut être envisagé."),
            > 0.20 => new HealthResult(HealthStatus.Info, $"Batterie usée à {Format.Percent(wear)}.", detail),
            _ => new HealthResult(HealthStatus.Good, $"Batterie en bon état (usure {Format.Percent(wear)}).", detail),
        };
    }

    public static string WearBrush(double wear) => wear > 0.40 ? "Pp.Danger" : wear > 0.20 ? "Pp.Warning" : "Pp.Success";
}
