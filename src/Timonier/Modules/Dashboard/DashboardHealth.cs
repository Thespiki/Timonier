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
            if (!drive.IsReady || drive.TotalSize <= 0) return new HealthResult(HealthStatus.Unknown, "Lecteur système illisible");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new HealthResult(HealthStatus.Unknown, "Lecteur système illisible", ex.Message);
        }

        var free = drive.AvailableFreeSpace;
        var total = drive.TotalSize;
        var ratio = (double)free / total;
        var name = drive.Name.TrimEnd('\\');
        var summary = $"{Format.Bytes(free)} libres sur {Format.Bytes(total)} ({Format.Percent(ratio)}) sur {name}";
        if (ratio < 0.05)
            return new HealthResult(HealthStatus.Critical, summary,
                "Le lecteur système est presque plein : les mises à jour de Windows peuvent échouer et le PC ralentit. " +
                "Videz la corbeille, supprimez les fichiers temporaires ou désinstallez des applications inutilisées.");
        if (ratio < 0.10)
            return new HealthResult(HealthStatus.Warning, summary,
                "Moins de 10 % d'espace libre : Windows a besoin de place pour ses mises à jour, la mémoire virtuelle et les fichiers temporaires.");
        return new HealthResult(HealthStatus.Good, summary);
    }

    public static HealthResult Uptime()
    {
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var summary = "Allumé depuis " + Format.Duration(up);
        if (up.TotalDays > 7)
            return new HealthResult(HealthStatus.Info, summary + " : pensez à redémarrer",
                "Un redémarrage termine l'installation des mises à jour et libère la mémoire. Avec le démarrage rapide de Windows, " +
                "« Arrêter » ne remet pas ce compteur à zéro : seul « Redémarrer » le fait.");
        return new HealthResult(HealthStatus.Good, summary);
    }

    public static HealthResult PendingReboot()
    {
        var reasons = new List<string>();
        var strong = false;
        if (RegistryAccess.KeyExists(RegHive.LocalMachine, CbsRebootPending)) { reasons.Add("installation de composants Windows"); strong = true; }
        if (RegistryAccess.KeyExists(RegHive.LocalMachine, WuRebootRequired)) { reasons.Add("mises à jour Windows"); strong = true; }
        if (RegistryAccess.Read(RegHive.LocalMachine, SessionManager, "PendingFileRenameOperations") is string[] { Length: > 0 } ops
            && ops.Any(o => !string.IsNullOrWhiteSpace(o)))
            reasons.Add("remplacement de fichiers en attente");
        if (SystemEffects.RebootPending) { reasons.Add("réglages de Timonier"); strong = true; }

        if (reasons.Count == 0) return new HealthResult(HealthStatus.Good, "Aucun redémarrage en attente");
        var detail = "Motifs : " + string.Join(", ", reasons) + ".";
        return strong
            ? new HealthResult(HealthStatus.Warning, "Redémarrage nécessaire pour terminer des modifications", detail)
            : new HealthResult(HealthStatus.Info, "Des fichiers seront remplacés au prochain redémarrage",
                detail + " Souvent laissé par un installateur ou un pilote ; sans urgence.");
    }

    public static HealthResult DiskHealth()
    {
        var p = AppHost.Profile;
        if (p is null || !p.HardwareLoaded) return new HealthResult(HealthStatus.Unknown, "Analyse du matériel en cours…");
        var disks = p.Disks;
        if (disks.Count == 0) return new HealthResult(HealthStatus.Unknown, "Aucun disque physique détecté");

        static string Label(DiskInfo d) => string.IsNullOrWhiteSpace(d.Model) ? "Disque" : d.Model.Trim();
        var bad = disks.Where(d => d.Health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase)).ToList();
        var warn = disks.Where(d => d.Health.Equals("Warning", StringComparison.OrdinalIgnoreCase)).ToList();
        var good = disks.Count(d => d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase));
        if (bad.Count > 0)
            return new HealthResult(HealthStatus.Critical, $"{Label(bad[0])} signale une défaillance",
                "Windows indique un disque en mauvaise santé : sauvegardez vos données sans attendre et prévoyez son remplacement.");
        if (warn.Count > 0)
            return new HealthResult(HealthStatus.Warning, $"{Label(warn[0])} signale un avertissement",
                "Le disque remonte un état dégradé (usure, erreurs) : sauvegardez vos données et surveillez son évolution.");
        if (good == 0) return new HealthResult(HealthStatus.Unknown, "État des disques non communiqué par Windows");
        return new HealthResult(HealthStatus.Good,
            disks.Count == 1 ? "Disque en bonne santé" : $"{good} disques sur {disks.Count} en bonne santé",
            string.Join(" · ", disks.Select(d => $"{Label(d)} ({Format.Bytes(d.SizeBytes)})")));
    }
}
