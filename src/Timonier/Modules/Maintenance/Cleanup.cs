using System.Diagnostics;
using System.IO.Enumeration;
using Timonier.Core.Platform;

namespace Timonier.Modules.Maintenance;

internal enum CleanupKind { Files, RecycleBin, DeliveryOptimization }

/// <summary>Dossier nettoyé par une catégorie. Les chemins sont CONSTRUITS DANS LE CODE (jamais reçus en paramètre).</summary>
internal sealed record CleanupTarget(string Root, string Pattern, bool Recursive, TimeSpan MinAge);

/// <summary>Catégorie de nettoyage : clé de liste blanche, textes, droits nécessaires et dossiers ciblés.</summary>
internal sealed record CleanupCategory(string Key, string Title, string Description, string Glyph, bool Admin, CleanupKind Kind)
{
    public Func<IReadOnlyList<CleanupTarget>> Targets { get; init; } = () => [];
    public bool DefaultSelected { get; init; } = true;
    /// <summary>Précision affichée sous la catégorie (conséquence à connaître).</summary>
    public string? Note { get; init; }
}

/// <summary>Résultat d'analyse ou de nettoyage d'une catégorie.</summary>
internal sealed class CleanupStats
{
    public long Bytes;
    public long Files;
    public long Skipped;
    /// <summary>Énumération interrompue (délai dépassé) : les chiffres sont un minimum.</summary>
    public bool Partial;
    /// <summary>Dossier illisible sans droits administrateur (taille inconnue).</summary>
    public bool AccessDenied;
    public string? Note;
}

internal static class CleanupCatalog
{
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public static string ExplorerCacheDir => Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer");
    public static string DeliveryOptimizationCacheDir =>
        Path.Combine(Windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache");

    public static readonly IReadOnlyList<CleanupCategory> All =
    [
        new("usertemp", "Fichiers temporaires de votre compte",
            "Dossier %TEMP% : fichiers laissés par les installations et les applications, non modifiés depuis plus de 24 heures.",
            "\uE8B7", Admin: false, CleanupKind.Files)
        {
            Targets = () => UserTempDir() is { } temp ? [new CleanupTarget(temp, "*", true, Day)] : [],
        },
        new("recyclebin", "Corbeille",
            "Fichiers supprimés de tous les lecteurs. Une fois vidée, la corbeille ne peut plus être restaurée.",
            "\uE74D", Admin: false, CleanupKind.RecycleBin)
        {
            DefaultSelected = false,
            Note = "Suppression définitive : vérifiez qu'elle ne contient rien d'important.",
        },
        new("thumbcache", "Cache des miniatures",
            "Aperçus d'images et de vidéos de l'Explorateur (thumbcache_*.db). Windows les recrée au besoin ; les fichiers utilisés par l'Explorateur sont ignorés.",
            "\uE91B", Admin: false, CleanupKind.Files)
        {
            Targets = () => [new CleanupTarget(ExplorerCacheDir, "thumbcache_*.db", false, TimeSpan.Zero)],
        },
        new("userdumps", "Rapports de plantage de vos applications",
            "Vidages mémoire (%LOCALAPPDATA%\\CrashDumps) et rapports d'erreurs Windows de votre compte, déjà envoyés ou archivés.",
            "\uE7BA", Admin: false, CleanupKind.Files)
        {
            Targets = () =>
            [
                new CleanupTarget(Path.Combine(LocalAppData, "CrashDumps"), "*", true, TimeSpan.Zero),
                new CleanupTarget(Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportArchive"), "*", true, TimeSpan.Zero),
                new CleanupTarget(Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportQueue"), "*", true, TimeSpan.Zero),
            ],
        },
        new("wintemp", "Fichiers temporaires de Windows",
            "Dossier C:\\Windows\\Temp : fichiers de plus de 24 heures laissés par les installations et les services.",
            "\uE8B7", Admin: true, CleanupKind.Files)
        {
            Targets = () => [new CleanupTarget(Path.Combine(Windows, "Temp"), "*", true, Day)],
        },
        new("wucache", "Téléchargements de Windows Update",
            "Fichiers d'installation déjà téléchargés (SoftwareDistribution\\Download). Windows les télécharge à nouveau si nécessaire ; les fichiers en cours d'utilisation sont ignorés.",
            "\uE896", Admin: true, CleanupKind.Files)
        {
            Targets = () => [new CleanupTarget(Path.Combine(Windows, @"SoftwareDistribution\Download"), "*", true, TimeSpan.Zero)],
            Note = "Ignoré tant qu'un redémarrage de mise à jour est en attente.",
        },
        new("docache", "Cache de l'Optimisation de la distribution",
            "Copies de mises à jour conservées pour les partager avec d'autres PC. Vidé avec la commande officielle Delete-DeliveryOptimizationCache.",
            "\uE895", Admin: true, CleanupKind.DeliveryOptimization),
        new("sysdumps", "Vidages mémoire du système",
            "Fichiers créés après un écran bleu (C:\\Windows\\Minidump et MEMORY.DMP). Utiles uniquement pour diagnostiquer un plantage : gardez-les si vous enquêtez sur un écran bleu.",
            "\uE7BA", Admin: true, CleanupKind.Files)
        {
            Targets = () =>
            [
                new CleanupTarget(Path.Combine(Windows, "Minidump"), "*.dmp", false, TimeSpan.Zero),
                new CleanupTarget(Windows, "MEMORY.DMP", false, TimeSpan.Zero),
            ],
            DefaultSelected = false,
        },
        new("wer", "Rapports d'erreurs Windows (système)",
            "Rapports de problèmes archivés ou en attente (ProgramData\\Microsoft\\Windows\\WER). L'historique de fiabilité sera moins détaillé.",
            "\uE9D9", Admin: true, CleanupKind.Files)
        {
            Targets = () =>
            [
                new CleanupTarget(Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive"), "*", true, TimeSpan.Zero),
                new CleanupTarget(Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue"), "*", true, TimeSpan.Zero),
            ],
        },
        new("oldlogs", "Anciens journaux de Windows",
            "Archives de journaux de maintenance (CBS) et traces de Windows Update de plus de 7 jours. Les journaux en cours ne sont jamais touchés.",
            "\uE81C", Admin: true, CleanupKind.Files)
        {
            Targets = () =>
            [
                new CleanupTarget(Path.Combine(Windows, @"Logs\CBS"), "CbsPersist_*", false, Week),
                new CleanupTarget(Path.Combine(Windows, @"Logs\WindowsUpdate"), "*.etl", false, Week),
            ],
        },
    ];

    /// <summary>
    /// Dossier %TEMP% de l'utilisateur, ou null s'il n'a pas l'allure d'un dossier temporaire. La variable TEMP/TMP est
    /// modifiable par l'utilisateur ou un installateur : mal réglée (« C:\ », « D:\ », le profil, Documents…), elle ne doit
    /// jamais conduire à supprimer les fichiers de plus de 24 h d'un dossier de données. Seuls sont acceptés le dossier
    /// standard (%LOCALAPPDATA%\Temp) et un dossier dont le nom est « Temp » ou « Tmp » (hors racine de lecteur).
    /// </summary>
    public static string? UserTempDir()
    {
        string full;
        try { full = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\'); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException) { return null; }
        if (string.Equals(full, Path.Combine(LocalAppData, "Temp"), StringComparison.OrdinalIgnoreCase)) return full;
        var leaf = Path.GetFileName(full);
        return leaf.Equals("Temp", StringComparison.OrdinalIgnoreCase) || leaf.Equals("Tmp", StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public static CleanupCategory? Get(string key) => All.FirstOrDefault(c => c.Key == key);

    /// <summary>Clés autorisées pour l'action administrateur (liste blanche).</summary>
    public static readonly string[] AdminKeys = [.. All.Where(c => c.Admin).Select(c => c.Key)];

    /// <summary>Clés exécutées dans le processus de l'interface (droits de l'utilisateur).</summary>
    public static readonly string[] UserKeys = [.. All.Where(c => !c.Admin).Select(c => c.Key)];

    public static bool RebootPending() =>
        RegistryAccess.KeyExists(Core.Model.RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
}

/// <summary>
/// Analyse et suppression. Règles : jamais de suivi de jonction/lien symbolique (ni en énumération, ni en suppression),
/// fichiers verrouillés ignorés, rien hors de la racine de la catégorie, énumération bornée dans le temps.
/// </summary>
internal static class CleanupEngine
{
    private const int MaxEntries = 300_000;

    public static CleanupStats Analyze(CleanupCategory category, TimeSpan budget, CancellationToken ct)
    {
        var stats = new CleanupStats();
        switch (category.Kind)
        {
            case CleanupKind.RecycleBin:
                if (RecycleBin.Query() is { } rb) { stats.Bytes = rb.Bytes; stats.Files = rb.Items; }
                else stats.AccessDenied = true;
                break;
            case CleanupKind.DeliveryOptimization:
                var doStats = MeasureFolder(CleanupCatalog.DeliveryOptimizationCacheDir, budget, ct);
                stats.Bytes = doStats.Bytes; stats.Files = doStats.Files; stats.AccessDenied = doStats.AccessDenied; stats.Partial = doStats.Partial;
                break;
            default:
                var deadline = Stopwatch.StartNew();
                foreach (var target in category.Targets())
                    Walk(target, stats, budget, deadline, ct, onFile: null, onDirectory: null);
                break;
        }
        return stats;
    }

    /// <summary>Nettoie une catégorie de fichiers (ou la corbeille). La catégorie DeliveryOptimization est gérée par l'action admin.</summary>
    public static CleanupStats Clean(CleanupCategory category, TimeSpan budget, CancellationToken ct)
    {
        var stats = new CleanupStats();
        if (category.Kind == CleanupKind.RecycleBin)
        {
            var before = RecycleBin.Query();
            if (before is { Items: 0 }) return stats; // déjà vide : SHEmptyRecycleBin signalerait une erreur
            if (!RecycleBin.Empty()) { stats.Note = "La corbeille n'a pas pu être vidée."; return stats; }
            stats.Bytes = before?.Bytes ?? 0;
            stats.Files = before?.Items ?? 0;
            return stats;
        }
        if (category.Kind != CleanupKind.Files) throw new InvalidOperationException("Catégorie non prise en charge ici.");

        var clock = Stopwatch.StartNew();
        foreach (var target in category.Targets())
        {
            var rootFinal = SafeDelete.RootFinalPath(target.Root);
            if (rootFinal is null) continue; // dossier absent, illisible ou lien : rien à faire
            var directories = new List<string>();
            var scan = new CleanupStats();
            Walk(target, scan, budget, clock, ct,
                onFile: (path, length) =>
                {
                    switch (SafeDelete.Delete(path, rootFinal, directory: false))
                    {
                        case SafeDelete.Outcome.Deleted: stats.Bytes += length; stats.Files++; break;
                        case SafeDelete.Outcome.Missing: break;
                        default: stats.Skipped++; break;
                    }
                },
                onDirectory: directories.Add);
            stats.Partial |= scan.Partial;
            stats.AccessDenied |= scan.AccessDenied;
            // Dossiers devenus vides, du plus profond au moins profond (jamais la racine elle-même).
            var cutoff = DateTime.UtcNow - target.MinAge;
            for (var i = directories.Count - 1; i >= 0; i--)
            {
                var dir = directories[i];
                try
                {
                    if (Directory.GetCreationTimeUtc(dir) > cutoff) continue;
                    if (Directory.EnumerateFileSystemEntries(dir).Any()) continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                SafeDelete.Delete(dir, rootFinal, directory: true);
            }
        }
        return stats;
    }

    /// <summary>Mesure simple d'un dossier (taille totale), sans filtre d'âge.</summary>
    public static CleanupStats MeasureFolder(string root, TimeSpan budget, CancellationToken ct)
    {
        var stats = new CleanupStats();
        Walk(new CleanupTarget(root, "*", true, TimeSpan.Zero), stats, budget, Stopwatch.StartNew(), ct, null, null);
        return stats;
    }

    /// <summary>
    /// Parcours itératif. Les entrées marquées « point d'analyse » (jonctions, liens symboliques, fichiers cloud
    /// non locaux) ne sont ni suivies ni supprimées. Tout chemin est vérifié sous la racine.
    /// </summary>
    private static void Walk(CleanupTarget target, CleanupStats stats, TimeSpan budget, Stopwatch clock, CancellationToken ct,
        Action<string, long>? onFile, Action<string>? onDirectory)
    {
        string root;
        try
        {
            root = Path.GetFullPath(target.Root).TrimEnd('\\');
            var rootInfo = new DirectoryInfo(root);
            if (!rootInfo.Exists) return;
            if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0) { stats.Note = "Dossier ignoré (lien)."; return; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { stats.AccessDenied = true; return; }

        var prefix = root + "\\";
        var cutoff = DateTime.UtcNow - target.MinAge;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };
        var pending = new Stack<string>();
        pending.Push(root);
        var seen = 0;
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (clock.Elapsed > budget || seen > MaxEntries) { stats.Partial = true; return; }
            var dir = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options).ToList(); }
            catch (UnauthorizedAccessException) { stats.AccessDenied = true; continue; }
            catch (IOException) { continue; }

            foreach (var entry in entries)
            {
                seen++;
                FileAttributes attributes;
                string full;
                try
                {
                    attributes = entry.Attributes;
                    full = entry.FullName;
                }
                catch (IOException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                if (entry is DirectoryInfo)
                {
                    if (!target.Recursive) continue;
                    pending.Push(full);
                    onDirectory?.Invoke(full);
                    continue;
                }
                if (entry is not FileInfo file) continue;
                // Filtre de nom appliqué au premier niveau uniquement (les sous-dossiers d'une catégorie « * » sont complets).
                if (target.Pattern != "*" && dir.Length == root.Length &&
                    !FileSystemName.MatchesSimpleExpression(target.Pattern, file.Name, ignoreCase: true)) continue;
                if (target.Pattern != "*" && dir.Length != root.Length) continue;

                DateTime newest;
                long length;
                try
                {
                    newest = file.LastWriteTimeUtc > file.CreationTimeUtc ? file.LastWriteTimeUtc : file.CreationTimeUtc;
                    length = file.Length;
                }
                catch (IOException) { continue; }
                if (target.MinAge > TimeSpan.Zero && newest > cutoff) continue;

                if (onFile is null)
                {
                    stats.Bytes += length;
                    stats.Files++;
                }
                else
                {
                    onFile(full, length);
                }
            }
        }
    }
}
