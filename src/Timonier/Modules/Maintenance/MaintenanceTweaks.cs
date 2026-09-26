using System.Text.RegularExpressions;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Maintenance;

/// <summary>
/// Réglages déclaratifs de la maintenance : Windows Update (stratégies officielles de WindowsUpdate.admx) et
/// Assistant de stockage (valeurs de l'application Paramètres, HKCU). Aucun réglage ne désactive durablement les mises à jour.
/// </summary>
internal static partial class MaintenanceTweaks
{
    // Titres de groupe affichés ; la page compare TweakDefinition.Group à ces mêmes champs.
    public static readonly string GroupUpdates = L("Windows Update settings");
    public static readonly string GroupStorage = LC("feature name", "Storage Sense");

    private const string Cat = MaintenanceModule.Category;
    private const string WuPolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string AuPolicy = WuPolicy + @"\AU";
    private const string StoragePolicy = @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";

    private const long SmallDisk = 140L * 1024 * 1024 * 1024;

    [GeneratedRegex(@"^\d{2}H[12]$")]
    private static partial Regex DisplayVersionRx();

    public static IEnumerable<TweakDefinition> All()
    {
        // ---------------------------------------------------------------- Windows Update
        yield return Tweak.Toggle("maintenance.wu.autoreboot", L("Automatic restart while a user is signed in"),
                L("Allows Windows Update to restart the PC on its own to finish an installation while a user is signed in. When off (official “No auto-restart with logged on users for scheduled automatic updates installations” policy), Windows waits for you to restart yourself and reminds you with notifications: no unsaved work is lost."))
            .In(Cat, GroupUpdates)
            .Keywords(L("automatic restart, reboot, auto restart, NoAutoRebootWithLoggedOnUsers, restarts by itself, windows update"))
            .WhenOn(Reg.LmDel(AuPolicy, "NoAutoRebootWithLoggedOnUsers"))
            .WhenOff(Reg.LmDword(AuPolicy, "NoAutoRebootWithLoggedOnUsers", 1))
            .Labels(L("Allowed"), L("Blocked"))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.Off)
            .Requires(Requires.ProOrHigher)
            .Warning(L("Security fixes only really take effect after the restart: restart when Windows asks you to."))
            .Tags("office", "family")
            .Build();

        yield return Tweak.Toggle("maintenance.wu.otherproducts", L("Updates for other Microsoft products"),
                L("Has Windows Update install fixes for Office, .NET runtimes, Visual Studio and other Microsoft software. “Enforced” applies the official policy (“Install updates for other Microsoft products” checkbox); “User's choice” lets the “Receive updates for other Microsoft products” option in Settings decide."))
            .In(Cat, GroupUpdates)
            .Keywords(L("office, microsoft update, other products, AllowMUUpdateService, .net, office updates"))
            .WhenOn(Reg.LmDword(AuPolicy, "AllowMUUpdateService", 1))
            .WhenOff(Reg.LmDel(AuPolicy, "AllowMUUpdateService"))
            .Labels(L("Enforced"), L("User's choice (Settings)"))
            .WindowsDefault(TweakDefinition.Off)
            .Recommend(TweakDefinition.On)
            .Tags("office", "family")
            .Build();

        yield return Tweak.Toggle("maintenance.wu.drivers", L("Drivers through Windows Update"),
                L("Lets Windows Update automatically install device drivers (graphics, Wi-Fi, audio…). When off (“Do not include drivers with Windows Updates” policy), drivers no longer come from Windows Update: useful if a driver update caused a problem or if you install the manufacturer's drivers."))
            .In(Cat, GroupUpdates)
            .Keywords(L("drivers, ExcludeWUDriversInQualityUpdate, driver update, update drivers"))
            .WhenOn(Reg.LmDel(WuPolicy, "ExcludeWUDriversInQualityUpdate"))
            .WhenOff(Reg.LmDword(WuPolicy, "ExcludeWUDriversInQualityUpdate", 1))
            .Labels(L("Included"), L("Excluded"))
            .WindowsDefault(TweakDefinition.On)
            .Risk(RiskLevel.Moderate)
            .Warning(L("You'll have to update your drivers yourself (manufacturer's website): a new device may be left without a suitable driver."))
            .Build();

        if (TargetVersionTweak() is { } target) yield return target;

        yield return Tweak.Toggle("maintenance.wu.metered", L("Download over metered connections"),
                L("Allows updates to download automatically over a connection set as metered (phone hotspot, 4G/5G plan). By default, Windows avoids these downloads to save your data plan. Official “Allow updates to be downloaded automatically over metered connections” policy."))
            .In(Cat, GroupUpdates)
            .Keywords(L("metered connection, metered, data plan, 4g, 5g, hotspot, tethering, mobile data"))
            .WhenOn(Reg.LmDword(WuPolicy, "AllowAutoWindowsUpdateDownloadOverMeteredNetwork", 1))
            .WhenOff(Reg.LmDel(WuPolicy, "AllowAutoWindowsUpdateDownloadOverMeteredNetwork"))
            .Labels(L("Allowed"), L("Avoided"))
            .WindowsDefault(TweakDefinition.Off)
            .RecommendWhen(p => p.IsLaptopLike ? TweakDefinition.Off : null)
            .Build();

        // ---------------------------------------------------------------- Assistant de stockage
        yield return Tweak.Toggle("maintenance.storage.enabled", LC("feature name", "Storage Sense"),
                L("Frees up space automatically: apps' temporary files and, depending on the choices below, the Recycle Bin and Downloads. It runs at the chosen frequency, or when disk space gets low. An organization policy can enforce this setting."))
            .In(Cat, GroupStorage)
            .Keywords(L("storage sense, disk space, automatic cleanup, free up space, storage"))
            .WhenOn(Reg.CuDword(StoragePolicy, "01", 1))
            .WhenOff(Reg.CuDword(StoragePolicy, "01", 0))
            .WindowsDefault(TweakDefinition.Off)
            .RecommendWhen(p => p.Tier == PerformanceTier.Low || p.SystemDisk is { } d && d.SizeBytes > 0 && d.SizeBytes <= SmallDisk ? TweakDefinition.On : null)
            .Tags("lowend", "family", "office")
            .Build();

        yield return Tweak.Choice("maintenance.storage.cadence", L("Storage Sense frequency"),
                L("When Storage Sense runs (if it's on). “Low disk space”: only when the disk is filling up."))
            .In(Cat, GroupStorage)
            .Keywords(L("storage sense, frequency, schedule, every week, every month"))
            .Option("lowspace", L("Low disk space"), Reg.CuDword(StoragePolicy, "2048", 0))
            .Option("daily", L("Every day"), Reg.CuDword(StoragePolicy, "2048", 1))
            .Option("weekly", L("Every week"), Reg.CuDword(StoragePolicy, "2048", 7))
            .Option("monthly", L("Every month"), Reg.CuDword(StoragePolicy, "2048", 30))
            .WindowsDefault("lowspace")
            .RecommendWhen(p => p.Tier == PerformanceTier.Low || p.SystemDisk is { } d && d.SizeBytes > 0 && d.SizeBytes <= SmallDisk ? "weekly" : null)
            .Tags("lowend")
            .Build();

        yield return Tweak.Toggle("maintenance.storage.tempfiles", L("Clean up apps' temporary files"),
                L("When it runs, Storage Sense deletes temporary files that apps no longer use."))
            .In(Cat, GroupStorage)
            .Keywords(L("storage sense, temporary files, temp, automatic cleanup"))
            .WhenOn(Reg.CuDword(StoragePolicy, "04", 1))
            .WhenOff(Reg.CuDword(StoragePolicy, "04", 0))
            .WindowsDefault(TweakDefinition.On)
            .Recommend(TweakDefinition.On)
            .Tags("lowend")
            .Build();

        yield return Tweak.Choice("maintenance.storage.recyclebin", L("Automatically empty the Recycle Bin"),
                L("Permanently deletes items that have been in the Recycle Bin longer than the chosen period (when Storage Sense runs)."))
            .In(Cat, GroupStorage)
            .Keywords(L("recycle bin, storage sense, empty recycle bin, trash"))
            .Option("never", L("Never"), Reg.CuDword(StoragePolicy, "08", 0))
            .Option("d1", L("After 1 day"), Reg.CuDword(StoragePolicy, "08", 1), Reg.CuDword(StoragePolicy, "256", 1))
            .Option("d14", L("After 14 days"), Reg.CuDword(StoragePolicy, "08", 1), Reg.CuDword(StoragePolicy, "256", 14))
            .Option("d30", L("After 30 days"), Reg.CuDword(StoragePolicy, "08", 1), Reg.CuDword(StoragePolicy, "256", 30))
            .Option("d60", L("After 60 days"), Reg.CuDword(StoragePolicy, "08", 1), Reg.CuDword(StoragePolicy, "256", 60))
            .WindowsDefault("d30")
            .Tags("lowend")
            .Build();

        yield return Tweak.Choice("maintenance.storage.downloads", L("Automatically clean up Downloads"),
                L("Deletes files in the Downloads folder that haven't been opened for the chosen period (when Storage Sense runs)."))
            .In(Cat, GroupStorage)
            .Keywords(L("downloads, downloads folder, storage sense, automatic cleanup"))
            .Option("never", L("Never"), Reg.CuDword(StoragePolicy, "32", 0))
            .Option("d14", L("After 14 days"), Reg.CuDword(StoragePolicy, "32", 1), Reg.CuDword(StoragePolicy, "512", 14))
            .Option("d30", L("After 30 days"), Reg.CuDword(StoragePolicy, "32", 1), Reg.CuDword(StoragePolicy, "512", 30))
            .Option("d60", L("After 60 days"), Reg.CuDword(StoragePolicy, "32", 1), Reg.CuDword(StoragePolicy, "512", 60))
            .WindowsDefault("never")
            .Risk(RiskLevel.Moderate)
            .Warning(L("The affected files are deleted without going to the Recycle Bin: move anything you want to keep somewhere else."))
            .Build();
    }

    /// <summary>
    /// « Rester sur la version actuelle » : TargetReleaseVersion/ProductVersion/TargetReleaseVersionInfo (Windows Update pour les entreprises).
    /// La version affichée (ex. 25H2) est lue dans le registre à l'enregistrement : lecture instantanée, identique dans le broker.
    /// </summary>
    private static TweakDefinition? TargetVersionTweak()
    {
        const string cv = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var display = RegistryAccess.ReadString(RegHive.LocalMachine, cv, "DisplayVersion");
        if (display is null || !DisplayVersionRx().IsMatch(display)) return null;
        var build = int.TryParse(RegistryAccess.ReadString(RegHive.LocalMachine, cv, "CurrentBuildNumber"), out var b) ? b : 0;
        if (build == 0) return null;
        var product = build >= 22000 ? "Windows 11" : "Windows 10";

        return Tweak.Toggle("maintenance.wu.targetversion", L("Lock to {0} {1}", product, display),
                L("Blocks feature updates (the new yearly version of Windows) and keeps this PC on {0} {1}. Security updates and monthly fixes keep installing normally. Official “Select the target Feature Update version” policy (Windows Update for Business).", product, display))
            .In(Cat, GroupUpdates)
            .Keywords(L("target version, TargetReleaseVersion, block upgrade, feature update, stay on version, 24h2, 25h2, upgrade"))
            .WhenOn(Reg.LmDword(WuPolicy, "TargetReleaseVersion", 1),
                    Reg.LmString(WuPolicy, "ProductVersion", product),
                    Reg.LmString(WuPolicy, "TargetReleaseVersionInfo", display))
            .WhenOff(Reg.LmDel(WuPolicy, "TargetReleaseVersion"),
                     Reg.LmDel(WuPolicy, "ProductVersion"),
                     Reg.LmDel(WuPolicy, "TargetReleaseVersionInfo"))
            .Labels(L("Locked"), L("Unlocked"))
            .WindowsDefault(TweakDefinition.Off)
            .Risk(RiskLevel.Moderate)
            .Requires(Requires.ProOrHigher)
            .Warning(L("Each version is only supported for a limited time (24 months for an end-of-year release on the Pro edition, 36 months on Enterprise/Education). Remove this lock before support for {0} ends, or this PC will stop receiving security fixes.", display))
            .Build();
    }
}
