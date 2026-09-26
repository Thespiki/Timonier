using System.IO;
using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Model;
using Timonier.UI.Services;

namespace Timonier.Modules.Dashboard;

/// <summary>
/// Contrôles de santé propres au tableau de bord. Lecture seule, rapides, sans élévation ; exécutés hors du thread UI.
/// </summary>
internal static class DashboardHealth
{
    private const string SessionManager = @"SYSTEM\CurrentControlSet\Control\Session Manager";
    private const string CbsRebootPending = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string WuRebootRequired = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";

    public static HealthResult DiskSpace()
    {
        var root = LiveMetrics.SystemDriveRoot;
        DriveInfo drive;
        try
        {
            drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0) return new HealthResult(HealthStatus.Unknown, L("System drive unreadable"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new HealthResult(HealthStatus.Unknown, L("System drive unreadable"), ex.Message);
        }

        var free = drive.AvailableFreeSpace;
        var total = drive.TotalSize;
        var ratio = (double)free / total;
        var name = drive.Name.TrimEnd('\\');
        var summary = L("{0} free of {1} ({2}) on {3}", Format.Bytes(free), Format.Bytes(total), Format.Percent(ratio), name);
        if (ratio < 0.05)
            return new HealthResult(HealthStatus.Critical, summary,
                L("The system drive is almost full: Windows updates may fail and the PC slows down. Empty the Recycle Bin, delete temporary files or uninstall apps you don't use."));
        if (ratio < 0.10)
            return new HealthResult(HealthStatus.Warning, summary,
                L("Less than 10% free space: Windows needs room for its updates, virtual memory and temporary files."));
        return new HealthResult(HealthStatus.Good, summary);
    }

    public static HealthResult Uptime()
    {
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var summary = L("Up for {0}", Format.Duration(up));
        if (up.TotalDays > 7)
            return new HealthResult(HealthStatus.Info, L("Up for {0}: consider restarting", Format.Duration(up)),
                L("A restart finishes installing updates and frees up memory. With Windows Fast startup, “Shut down” doesn't reset this counter: only “Restart” does."));
        return new HealthResult(HealthStatus.Good, summary);
    }

    public static HealthResult PendingReboot()
    {
        var reasons = new List<string>();
        var strong = false;
        if (RegistryAccess.KeyExists(RegHive.LocalMachine, CbsRebootPending)) { reasons.Add(L("Windows component installation")); strong = true; }
        if (RegistryAccess.KeyExists(RegHive.LocalMachine, WuRebootRequired)) { reasons.Add(L("Windows updates")); strong = true; }
        if (RegistryAccess.Read(RegHive.LocalMachine, SessionManager, "PendingFileRenameOperations") is string[] { Length: > 0 } ops
            && ops.Any(o => !string.IsNullOrWhiteSpace(o)))
            reasons.Add(L("pending file replacements"));
        if (SystemEffects.RebootPending) { reasons.Add(LC("mid-sentence", "Timonier settings")); strong = true; }

        if (reasons.Count == 0) return new HealthResult(HealthStatus.Good, L("No restart pending"));
        var detail = L("Reasons: {0}.", string.Join(", ", reasons));
        return strong
            ? new HealthResult(HealthStatus.Warning, L("Restart required to finish changes"), detail)
            : new HealthResult(HealthStatus.Info, L("Files will be replaced at the next restart"),
                L("{0} Often left behind by an installer or a driver; not urgent.", detail));
    }

    public static HealthResult DiskHealth()
    {
        var p = AppHost.Profile;
        if (p is null || !p.HardwareLoaded) return new HealthResult(HealthStatus.Unknown, LC("long form", "Scanning hardware…"));
        var disks = p.Disks;
        if (disks.Count == 0) return new HealthResult(HealthStatus.Unknown, L("No physical disk detected"));

        static string Label(DiskInfo d) => string.IsNullOrWhiteSpace(d.Model) ? L("Disk") : d.Model.Trim();
        var bad = disks.Where(d => d.Health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase)).ToList();
        var warn = disks.Where(d => d.Health.Equals("Warning", StringComparison.OrdinalIgnoreCase)).ToList();
        var good = disks.Count(d => d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase));
        if (bad.Count > 0)
            return new HealthResult(HealthStatus.Critical, L("{0} reports a failure", Label(bad[0])),
                L("Windows reports an unhealthy disk: back up your data right away and plan to replace it."));
        if (warn.Count > 0)
            return new HealthResult(HealthStatus.Warning, L("{0} reports a warning", Label(warn[0])),
                L("The disk reports a degraded state (wear, errors): back up your data and keep an eye on it."));
        if (good == 0) return new HealthResult(HealthStatus.Unknown, L("Disk status not reported by Windows"));
        return new HealthResult(HealthStatus.Good,
            disks.Count == 1 ? L("Disk healthy") : LP(good, "{0} of {1} disks healthy", "{0} of {1} disks healthy", disks.Count),
            string.Join(" · ", disks.Select(d => $"{Label(d)} ({Format.Bytes(d.SizeBytes)})")));
    }
}
