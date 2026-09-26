using System.Globalization;
using System.Runtime.InteropServices;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Maintenance;

/// <summary>État de Windows Update, lu sans droits administrateur (COM Microsoft.Update.AutoUpdate + registre).</summary>
internal sealed class UpdateStatus
{
    public DateTime? LastInstall { get; init; }
    public DateTime? LastSearch { get; init; }
    public bool ComFailed { get; init; }
    public bool RebootPending { get; init; }
    public DateTime? PausedUntil { get; init; }
    public bool PauseBlocked { get; init; }
    public int? ActiveStart { get; init; }
    public int? ActiveEnd { get; init; }
    public bool SmartActiveHours { get; init; }
    public bool ManagedByPolicy { get; init; }

    public bool IsPaused => PausedUntil is { } u && u > DateTime.Now;

    /// <summary>Lecture complète (quelques dizaines de ms) : à appeler hors du thread UI.</summary>
    public static UpdateStatus Read()
    {
        DateTime? install = null, search = null;
        var failed = false;
        object? au = null;
        try
        {
            var type = Type.GetTypeFromProgID("Microsoft.Update.AutoUpdate");
            if (type is null) failed = true;
            else
            {
                au = Activator.CreateInstance(type);
                dynamic results = ((dynamic)au!).Results;
                install = AsLocal((object?)results.LastInstallationSuccessDate);
                search = AsLocal((object?)results.LastSearchSuccessDate);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException)
        {
            Log.Warn("Maintenance", "état Windows Update : " + ex.Message);
            failed = true;
        }
        finally
        {
            if (au is not null && Marshal.IsComObject(au)) Marshal.FinalReleaseComObject(au);
        }

        DateTime? paused = null;
        if (RegistryAccess.ReadString(RegHive.LocalMachine, WuKeys.UxSettings, "PauseUpdatesExpiryTime") is { } expiry &&
            DateTime.TryParse(expiry, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var e))
            paused = e.ToLocalTime();

        return new UpdateStatus
        {
            LastInstall = install,
            LastSearch = search,
            ComFailed = failed,
            RebootPending = CleanupCatalog.RebootPending(),
            PausedUntil = paused,
            PauseBlocked = WuKeys.PauseBlockedByPolicy,
            ActiveStart = RegistryAccess.ReadDword(RegHive.LocalMachine, WuKeys.UxSettings, "ActiveHoursStart"),
            ActiveEnd = RegistryAccess.ReadDword(RegHive.LocalMachine, WuKeys.UxSettings, "ActiveHoursEnd"),
            SmartActiveHours = RegistryAccess.ReadDword(RegHive.LocalMachine, WuKeys.UxSettings, "SmartActiveHoursState") == 1,
            ManagedByPolicy = RegistryAccess.KeyExists(RegHive.LocalMachine, WuKeys.Policy + @"\AU") &&
                              RegistryAccess.ReadDword(RegHive.LocalMachine, WuKeys.Policy + @"\AU", "NoAutoUpdate") == 1,
        };
    }

    /// <summary>Les dates de l'agent Windows Update sont en UTC ; une date vide (jamais) est renvoyée comme null.</summary>
    private static DateTime? AsLocal(object? value) => value is DateTime d && d.Year > 2000
        ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime()
        : null;

    public HealthResult ToHealth()
    {
        if (ComFailed || LastInstall is null)
            return new HealthResult(HealthStatus.Unknown, L("Date de la dernière mise à jour inconnue"),
                L("L'agent Windows Update n'a pas indiqué de dernière installation réussie."));
        var days = (int)(DateTime.Now - LastInstall.Value).TotalDays;
        var last = days <= 0 ? L("Dernière mise à jour installée aujourd'hui")
            : days == 1 ? L("Dernière mise à jour installée hier")
            : LP(days, "Dernière mise à jour installée il y a {0} jour", "Dernière mise à jour installée il y a {0} jours");
        var detail = IsPaused ? L("Mises à jour suspendues jusqu'au {0}.", Format.Day(PausedUntil!.Value)) : null;
        if (RebootPending)
            detail = detail is null ? L("Un redémarrage est nécessaire pour terminer l'installation.")
                : L("Un redémarrage est nécessaire pour terminer l'installation. {0}", detail);
        return days switch
        {
            > 60 => new HealthResult(HealthStatus.Critical, LP(days, "Aucune mise à jour installée depuis {0} jour", "Aucune mise à jour installée depuis {0} jours"), detail ?? L("Ce PC ne reçoit plus les correctifs de sécurité : lancez une recherche de mises à jour.")),
            > 30 => new HealthResult(HealthStatus.Warning, last, detail ?? L("Pensez à rechercher les mises à jour.")),
            _ => new HealthResult(RebootPending ? HealthStatus.Info : HealthStatus.Good, last, detail),
        };
    }
}
