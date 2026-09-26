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
            if (!drive.IsReady || drive.TotalSize <= 0) return new HealthResult(HealthStatus.Unknown, L("Lecteur système illisible"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new HealthResult(HealthStatus.Unknown, L("Lecteur système illisible"), ex.Message);
        }

        var free = drive.AvailableFreeSpace;
        var total = drive.TotalSize;
        var ratio = (double)free / total;
        var name = drive.Name.TrimEnd('\\');
        var summary = L("{0} libres sur {1} ({2}) sur {3}", Format.Bytes(free), Format.Bytes(total), Format.Percent(ratio), name);
        if (ratio < 0.05)
            return new HealthResult(HealthStatus.Critical, summary,
                L("Le lecteur système est presque plein : les mises à jour de Windows peuvent échouer et le PC ralentit. Videz la corbeille, supprimez les fichiers temporaires ou désinstallez des applications inutilisées."));
        if (ratio < 0.10)
            return new HealthResult(HealthStatus.Warning, summary,
                L("Moins de 10 % d'espace libre : Windows a besoin de place pour ses mises à jour, la mémoire virtuelle et les fichiers temporaires."));
        return new HealthResult(HealthStatus.Good, summary);
    }

    public static HealthResult Uptime()
    {
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var summary = L("Allumé depuis {0}", Format.Duration(up));
        if (up.TotalDays > 7)
            return new HealthResult(HealthStatus.Info, L("Allumé depuis {0} : pensez à redémarrer", Format.Duration(up)),
                L("Un redémarrage termine l'installation des mises à jour et libère la mémoire. Avec le démarrage rapide de Windows, « Arrêter » ne remet pas ce compteur à zéro : seul « Redémarrer » le fait."));
        return new HealthResult(HealthStatus.Good, summary);
    }

    public static HealthResult PendingReboot()
    {
        var reasons = new List<string>();
        var strong = false;
        if (RegistryAccess.KeyExists(RegHive.LocalMachine, CbsRebootPending)) { reasons.Add(L("installation de composants Windows")); strong = true; }
        if (RegistryAccess.KeyExists(RegHive.LocalMachine, WuRebootRequired)) { reasons.Add(L("mises à jour Windows")); strong = true; }
        if (RegistryAccess.Read(RegHive.LocalMachine, SessionManager, "PendingFileRenameOperations") is string[] { Length: > 0 } ops
            && ops.Any(o => !string.IsNullOrWhiteSpace(o)))
            reasons.Add(L("remplacement de fichiers en attente"));
        if (SystemEffects.RebootPending) { reasons.Add(L("réglages de Timonier")); strong = true; }

        if (reasons.Count == 0) return new HealthResult(HealthStatus.Good, L("Aucun redémarrage en attente"));
        var detail = L("Motifs : {0}.", string.Join(", ", reasons));
        return strong
            ? new HealthResult(HealthStatus.Warning, L("Redémarrage nécessaire pour terminer des modifications"), detail)
            : new HealthResult(HealthStatus.Info, L("Des fichiers seront remplacés au prochain redémarrage"),
                L("{0} Souvent laissé par un installateur ou un pilote ; sans urgence.", detail));
    }

    public static HealthResult DiskHealth()
    {
        var p = AppHost.Profile;
        if (p is null || !p.HardwareLoaded) return new HealthResult(HealthStatus.Unknown, L("Analyse du matériel en cours…"));
        var disks = p.Disks;
        if (disks.Count == 0) return new HealthResult(HealthStatus.Unknown, L("Aucun disque physique détecté"));

        static string Label(DiskInfo d) => string.IsNullOrWhiteSpace(d.Model) ? L("Disque") : d.Model.Trim();
        var bad = disks.Where(d => d.Health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase)).ToList();
        var warn = disks.Where(d => d.Health.Equals("Warning", StringComparison.OrdinalIgnoreCase)).ToList();
        var good = disks.Count(d => d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase));
        if (bad.Count > 0)
            return new HealthResult(HealthStatus.Critical, L("{0} signale une défaillance", Label(bad[0])),
                L("Windows indique un disque en mauvaise santé : sauvegardez vos données sans attendre et prévoyez son remplacement."));
        if (warn.Count > 0)
            return new HealthResult(HealthStatus.Warning, L("{0} signale un avertissement", Label(warn[0])),
                L("Le disque remonte un état dégradé (usure, erreurs) : sauvegardez vos données et surveillez son évolution."));
        if (good == 0) return new HealthResult(HealthStatus.Unknown, L("État des disques non communiqué par Windows"));
        return new HealthResult(HealthStatus.Good,
            disks.Count == 1 ? L("Disque en bonne santé") : LP(good, "{0} disque sur {1} en bonne santé", "{0} disques sur {1} en bonne santé", disks.Count),
            string.Join(" · ", disks.Select(d => $"{Label(d)} ({Format.Bytes(d.SizeBytes)})")));
    }
}
