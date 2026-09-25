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
        r.AddCategory(new CategoryInfo(Category, "Maintenance", Glyph,
            "Nettoyage, réparation de Windows, points de restauration, Windows Update et journal des erreurs."));

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

        r.AddPage(new PageInfo(PageId, "Maintenance", Glyph, NavSection.Tools, 30, () => new MaintenancePage())
        {
            CategoryId = Category,
            Description = "Nettoyage du disque, réparation de Windows, points de restauration, Windows Update et journal des erreurs.",
            Keywords = ["maintenance", "nettoyage", "nettoyer", "réparer", "réparation", "sfc", "dism", "chkdsk", "point de restauration",
                        "windows update", "mises à jour", "journal", "erreurs", "espace disque", "fichiers temporaires", "corbeille"],
        });

        // Contrôles de santé : lecture seule, exécutés hors du thread UI dans l'interface uniquement.
        r.AddHealthCheck(HealthCheck.Sync("maintenance.updates", "Windows Update", "", PageId, () => UpdateStatus.Read().ToHealth()));
        r.AddHealthCheck(HealthCheck.Sync("maintenance.junk", "Fichiers inutiles", "", PageId, QuickClean.EstimateHealth));

        r.AddQuickAction(new QuickAction("maintenance.quick-clean", "Nettoyage rapide", "",
            "Supprime les fichiers temporaires de plus de 24 h de votre compte et vide la corbeille, après confirmation.",
            QuickClean.RunAsync)
        {
            Keywords = ["nettoyage", "nettoyer", "fichiers temporaires", "corbeille", "libérer de l'espace", "clean", "temp"],
            Order = 30,
        });
        r.AddQuickAction(new QuickAction("maintenance.restore-point", "Créer un point de restauration", "",
            "Enregistre l'état actuel de Windows pour pouvoir y revenir en cas de problème (description « Timonier »).",
            QuickClean.CreateRestorePointAsync)
        {
            Keywords = ["point de restauration", "restore point", "sauvegarde système", "restauration"],
            Order = 35,
            RequiresAdmin = true,
        });
        r.AddQuickAction(new QuickAction("maintenance.sfc", "Vérifier les fichiers système", "",
            "Lance SFC /scannow pour détecter et réparer les fichiers de Windows endommagés (10 à 30 minutes).",
            () => { AppHost.Navigator.Navigate(PageId, "run:sfc"); return Task.CompletedTask; })
        {
            Keywords = ["sfc", "scannow", "réparer windows", "fichiers système", "corruption"],
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
        Section(r, "cleanup", "Nettoyage du disque", "Fichiers temporaires, corbeille, caches, rapports d'erreurs, cache Windows Update", "",
            ["nettoyage", "espace disque", "fichiers temporaires", "cache", "liberer espace", "disque plein"]);
        Section(r, "repair", "Réparer Windows", "SFC, DISM, analyse du disque, cache des icônes, Microsoft Store, heure", "",
            ["reparer", "reparation", "sfc", "dism", "chkdsk", "windows corrompu", "depannage"]);
        Section(r, "restore", "Points de restauration", "Créer, lister, activer la protection du système", "",
            ["point de restauration", "restauration systeme", "revenir en arriere", "restore"]);
        Section(r, "updates", "Windows Update : pause et heures d'activité", "État, suspendre 1 à 5 semaines, heures d'activité, réglages", "",
            ["windows update", "mises a jour", "suspendre", "pause", "heures activite", "redemarrage"]);
        Section(r, "events", "Journal des erreurs", "Erreurs et événements critiques des 7 derniers jours, expliqués", "",
            ["journal", "erreurs", "evenements", "event viewer", "plantage", "kernel-power", "ecran bleu"]);

        Feature(r, "maintenance.tool.dism", "Réparer l'image de Windows (DISM)", "DISM /Online /Cleanup-Image /RestoreHealth", "repair",
            ["dism", "restorehealth", "reparer image", "magasin de composants"]);
        Feature(r, "maintenance.tool.chkdsk", "Analyser le disque système (chkdsk)", "Analyse en ligne du système de fichiers, sans redémarrage", "repair",
            ["chkdsk", "erreurs disque", "check disk", "systeme de fichiers"]);
        Feature(r, "maintenance.tool.iconcache", "Réinitialiser le cache des icônes", "Icônes et miniatures incorrectes ou vides", "repair",
            ["icones", "cache icones", "icones blanches", "miniatures", "iconcache"]);
        Feature(r, "maintenance.tool.wsreset", "Réinitialiser le cache du Microsoft Store", "WSReset : le Store ne s'ouvre pas ou ne télécharge plus", "repair",
            ["microsoft store", "wsreset", "store bloque", "cache store"]);
        Feature(r, "maintenance.tool.time", "Resynchroniser l'heure", "Horloge décalée : synchronisation avec le serveur de temps", "repair",
            ["heure", "horloge", "w32tm", "synchroniser heure", "time sync"]);

        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.win.storage", Title = "Stockage (Paramètres Windows)", Subtitle = "Espace utilisé par catégorie et Assistant de stockage",
            Glyph = "", Keywords = ["stockage", "storage", "espace disque", "storage sense"], Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:storagesense"),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.win.update", Title = "Windows Update (Paramètres Windows)", Subtitle = "Rechercher et installer les mises à jour",
            Glyph = "", Keywords = ["windows update", "rechercher mises a jour", "check for updates"], Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:windowsupdate-action"),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.win.recovery", Title = "Récupération (Paramètres Windows)", Subtitle = "Réinitialiser ce PC, démarrage avancé",
            Glyph = "", Keywords = ["reinitialiser", "reset pc", "demarrage avance", "recuperation", "recovery"], Kind = SearchEntryKind.WindowsSetting,
            Execute = () => ProcessRunner.OpenSettingsUri("ms-settings:recovery"),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.tool.cleanmgr", Title = "Nettoyage de disque (outil Windows)", Subtitle = "Ouvre l'outil classique cleanmgr",
            Glyph = "", Keywords = ["cleanmgr", "nettoyage de disque", "disk cleanup"], Kind = SearchEntryKind.Tool,
            Execute = () => MaintUi.OpenTool(SystemTool.CleanMgr),
        });
        r.AddSearchEntry(new SearchEntry
        {
            Id = "maintenance.tool.rstrui", Title = "Restauration du système (outil Windows)", Subtitle = "Revenir à un point de restauration",
            Glyph = "", Keywords = ["rstrui", "restaurer le systeme", "system restore"], Kind = SearchEntryKind.Tool,
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
