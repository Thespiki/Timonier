using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Search;

namespace Timonier.Modules.Maintenance;

/// <summary>
/// Maintenance : nettoyage, réparation de Windows (SFC, DISM, chkdsk…), points de restauration, Windows Update et
/// journal des erreurs. Déclarations uniquement : appelé aussi dans le broker (aucune UI, aucun accès lent).
/// </summary>
public sealed class MaintenanceModule : IModule
{
    public const string Category = "maintenance";
    public const string PageId = "maintenance";
    internal const string Glyph = "";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, L("Maintenance"), Glyph,
            L("Cleanup, Windows repair, restore points, Windows Update and error log.")));

        r.AddTweaks(MaintenanceTweaks.All());

        r.AddAction(new CleanupRunAction());
        r.AddAction(new RepairSfcAction());
        r.AddAction(RepairDismAction.RestoreHealth());
        r.AddAction(RepairDismAction.ComponentCleanup());
        r.AddAction(new ChkdskScanAction());
        r.AddAction(new TimeResyncAction());
        r.AddAction(new RestorePointCreateAction());
        r.AddAction(new RestorePointListAction());
        r.AddAction(new RestoreEnableAction());
        r.AddAction(new UpdatesPauseAction());
        r.AddAction(new UpdatesResumeAction());
        r.AddAction(new ActiveHoursAction());

        r.AddPage(new PageInfo(PageId, L("Maintenance"), Glyph, NavSection.Tools, 30, () => new MaintenancePage())
        {
            CategoryId = Category,
            Description = L("Disk cleanup, Windows repair, restore points, Windows Update and error log."),
            Keywords = [L("maintenance, cleanup, repair, sfc, dism, chkdsk, restore point, windows update, error log, disk space, temp files, recycle bin")],
        });

        // Contrôles de santé : lecture seule, exécutés hors du thread UI dans l'interface uniquement.
        r.AddHealthCheck(HealthCheck.Sync("maintenance.updates", "Windows Update", "", PageId, () => UpdateStatus.Read().ToHealth()));
        r.AddHealthCheck(HealthCheck.Sync("maintenance.junk", L("Junk files"), "", PageId, QuickClean.EstimateHealth));

        r.AddQuickAction(new QuickAction("maintenance.quick-clean", L("Quick cleanup"), "",
            L("Deletes your account's temporary files older than 24 h and empties the Recycle Bin, after confirmation."),
            QuickClean.RunAsync)
        {
            Keywords = [L("cleanup, clean, temp files, temporary files, recycle bin, free up space")],
            Order = 30,
        });
        r.AddQuickAction(new QuickAction("maintenance.restore-point", L("Create a restore point"), "",
            L("Saves the current state of Windows so you can go back to it if something goes wrong (description “Timonier”)."),
            QuickClean.CreateRestorePointAsync)
        {
            Keywords = [L("restore point, system backup, restore, system restore")],
            Order = 35,
            RequiresAdmin = true,
        });
        r.AddQuickAction(new QuickAction("maintenance.sfc", L("Check system files"), "",
            L("Runs SFC /scannow to detect and repair damaged Windows files (10 to 30 minutes)."),
            () => { AppHost.Navigator.Navigate(PageId, "run:sfc"); return Task.CompletedTask; })
        {
            Keywords = [L("sfc, scannow, repair windows, system files, corruption, corrupted files")],
            Order = 60,
            RequiresAdmin = true,
        });

        RegisterSearchEntries(r);

        Synonyms.AddGroup("sfc", "scannow", "verification fichiers systeme", "fichiers systeme corrompus");
        Synonyms.AddGroup("dism", "restorehealth", "reparer image", "image windows");
        Synonyms.AddGroup("point de restauration", "restore point", "restauration systeme", "protection systeme");
        Synonyms.AddGroup("nettoyage", "nettoyer", "cleanup", "menage", "liberer espace", "fichiers inutiles");
        Synonyms.AddGroup("corbeille", "recycle bin");
        Synonyms.AddGroup("fichiers temporaires", "temp", "tmp", "temporaires");
        Synonyms.AddGroup("journal des evenements", "event log", "observateur evenements", "journal erreurs");
        Synonyms.AddGroup("ecran bleu", "bsod", "bugcheck", "plantage systeme");
        Synonyms.AddGroup("assistant de stockage", "storage sense");
        Synonyms.AddGroup("chkdsk", "verifier disque", "erreurs disque", "check disk");
    }

    private static void RegisterSearchEntries(ModuleRegistry r)
    {
        Section(r, "cleanup", L("Disk cleanup"), L("Temporary files, Recycle Bin, caches, error reports, Windows Update cache"), "",
            [L("cleanup, disk space, temp files, temporary files, cache, free up space, disk full")]);
        Section(r, "repair", L("Repair Windows"), L("SFC, DISM, disk scan, icon cache, Microsoft Store, time"), "",
            [L("repair, fix, sfc, dism, chkdsk, corrupted windows, troubleshooting")]);
        Section(r, "restore", L("Restore points"), L("Create, list, turn on system protection"), "",
            [L("restore point, system restore, go back, roll back, restore")]);
        Section(r, "updates", L("Windows Update: pause and active hours"), L("Status, pause for 1 to 5 weeks, active hours, settings"), "",
            [L("windows update, updates, pause, pause updates, active hours, restart")]);
        Section(r, "events", L("Error log"), L("Errors and critical events from the last 7 days, explained"), "",
            [L("log, errors, events, event viewer, crash, kernel-power, blue screen, bsod")]);

        Feature(r, "maintenance.tool.dism", L("Repair Windows image (DISM)"), "DISM /Online /Cleanup-Image /RestoreHealth", "repair",
            [L("dism, restorehealth, repair image, component store, repair windows")]);
        Feature(r, "maintenance.tool.chkdsk", L("Scan system disk (chkdsk)"), L("Online file system scan, no restart needed"), "repair",
            [L("chkdsk, disk errors, check disk, file system, scan disk")]);
        Feature(r, "maintenance.tool.iconcache", L("Reset icon cache"), L("Wrong or blank icons and thumbnails"), "repair",
            [L("icons, icon cache, blank icons, white icons, thumbnails, iconcache")]);
        Feature(r, "maintenance.tool.wsreset", L("Reset Microsoft Store cache"), L("WSReset: the Store won't open or no longer downloads"), "repair",
            [L("microsoft store, wsreset, store stuck, store cache")]);
        Feature(r, "maintenance.tool.time", L("Resync time"), L("Clock is off: sync with the time server"), "repair",
            [L("time, clock, w32tm, sync time, time sync, wrong time")]);

        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.win.storage", Title = L("Storage (Windows Settings)"), Subtitle = L("Space used by category and Storage Sense"),
            Glyph = "", Keywords = [L("storage, disk space, storage sense, free space")], Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:storagesense"),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.win.update", Title = L("Windows Update (Windows Settings)"), Subtitle = L("Check for and install updates"),
            Glyph = "", Keywords = [L("windows update, check for updates, updates")], Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:windowsupdate-action"),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.win.recovery", Title = L("Recovery (Windows Settings)"), Subtitle = L("Reset this PC, advanced startup"),
            Glyph = "", Keywords = [L("reset, reset pc, advanced startup, recovery, reinstall windows")], Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:recovery"),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.tool.cleanmgr", Title = L("Disk Cleanup (Windows tool)"), Subtitle = L("Opens the classic cleanmgr tool"),
            Glyph = "", Keywords = [L("cleanmgr, disk cleanup, clean disk")], Kind = SearchEntryKind.Tool,
            Execute = () => MaintUi.OpenTool(SystemTool.CleanMgr),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.tool.rstrui", Title = L("System Restore (Windows tool)"), Subtitle = L("Go back to a restore point"),
            Glyph = "", Keywords = [L("rstrui, system restore, restore system")], Kind = SearchEntryKind.Tool,
            Execute = () => MaintUi.OpenTool(SystemTool.Rstrui),
        });
    }

    private static void Section(ModuleRegistry r, string key, string title, string subtitle, string glyph, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.section." + key, Title = title, Subtitle = subtitle, Glyph = glyph, Keywords = keywords,
            PageId = PageId, PageParameter = "section:" + key,
        });

    private static void Feature(ModuleRegistry r, string id, string title, string subtitle, string section, string[] keywords) =>
        r.AddSearchEntry(new SearchEntry
        {
            Id = id, Title = title, Subtitle = subtitle, Glyph = "", Keywords = keywords,
            PageId = PageId, PageParameter = "section:" + section,
        });
}
