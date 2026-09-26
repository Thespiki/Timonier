using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Modules.Startup;

public enum StartupScope { User, Machine }

/// <summary>Origine d'une application lancée au démarrage.</summary>
public enum StartupKind
{
    /// <summary>Clé Run (HKCU ou HKLM 64 bits).</summary>
    Run,
    /// <summary>Clé Run 32 bits (HKLM\SOFTWARE\WOW6432Node).</summary>
    Run32,
    /// <summary>Dossier Démarrage (utilisateur ou commun).</summary>
    Folder,
    /// <summary>Tâche de démarrage d'une application empaquetée (Microsoft Store, MSIX).</summary>
    Packaged,
    /// <summary>RunOnce : exécution unique à la prochaine ouverture de session (lecture seule).</summary>
    RunOnce,
    /// <summary>Stratégie « Exécuter ces programmes à l'ouverture de session » (lecture seule).</summary>
    Policy,
}

/// <summary>Application lancée automatiquement à l'ouverture de session.</summary>
public sealed class StartupItem
{
    public required string Name { get; init; }
    public required string DisplayName { get; set; }
    public required StartupScope Scope { get; init; }
    public required StartupKind Kind { get; init; }
    public string Command { get; init; } = "";
    public string? TargetPath { get; set; }
    public string? Publisher { get; set; }
    public bool Enabled { get; set; }
    public DateTime? DisabledSince { get; set; }
    public bool FileMissing { get; set; }
    /// <summary>État brut d'une tâche empaquetée (0 désactivée, 1 désactivée par l'utilisateur, 2 activée, 3/4 imposé par stratégie).</summary>
    public int PackagedState { get; init; }
    /// <summary>Fichier à sélectionner dans l'Explorateur (« Ouvrir l'emplacement »).</summary>
    public string? LocationPath { get; set; }
    public ImageSource? Icon { get; set; }

    public string Key => $"{ScopeParam}|{KindParam}|{Name}";
    public string ScopeParam => Scope == StartupScope.User ? "user" : "machine";
    public string KindParam => StartupInventory.KindParam(Kind);

    public bool IsReadOnly => Kind is StartupKind.RunOnce or StartupKind.Policy;
    public bool IsPolicyLocked => Kind == StartupKind.Packaged && PackagedState is 3 or 4;
    public bool CanToggle => Kind is StartupKind.Run or StartupKind.Run32 or StartupKind.Folder
                             || (Kind == StartupKind.Packaged && PackagedState is 0 or 1 or 2);
    public bool CanDelete => Kind is StartupKind.Run or StartupKind.Run32;
    public bool NeedsAdmin => Scope == StartupScope.Machine;

    public string ScopeLabel => Scope == StartupScope.User ? L("User") : L("All users");

    public string SourceLabel => Kind switch
    {
        StartupKind.Run => L("Registry {0}", Scope == StartupScope.User ? @"HKCU\…\Run" : @"HKLM\…\Run"),
        StartupKind.Run32 => L("Registry {0} (32-bit)", @"HKLM\…\Run"),
        StartupKind.Folder => Scope == StartupScope.User ? L("Startup folder") : L("Common Startup folder"),
        StartupKind.Packaged => L("Store app"),
        StartupKind.RunOnce => L("RunOnce (once)"),
        StartupKind.Policy => L("Group Policy"),
        _ => "",
    };
}

/// <summary>
/// Inventaire des applications lancées au démarrage, au format du Gestionnaire des tâches :
/// l'état activé/désactivé est lu et écrit dans Explorer\StartupApproved (Run, Run32, StartupFolder), ce qui
/// garantit que Windows, le Gestionnaire des tâches et les Paramètres affichent la même chose.
/// Lecture seule et sans élévation ; la construction des opérations d'écriture est aussi centralisée ici.
/// </summary>
public static partial class StartupInventory
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    public const string Run32Key = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    public const string RunOnce32Key = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce";
    public const string PolicyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run";
    public const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    public const string AppModelKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

    [GeneratedRegex(@"^[A-Za-z0-9._\-]{1,200}\\[A-Za-z0-9._\-]{1,200}$")]
    private static partial Regex PackagedNameRx();

    public static string KindParam(StartupKind kind) => kind switch
    {
        StartupKind.Run => "run",
        StartupKind.Run32 => "run32",
        StartupKind.Folder => "folder",
        StartupKind.Packaged => "packaged",
        StartupKind.RunOnce => "runonce",
        _ => "policy",
    };

    public static StartupKind ParseKind(string kind) => kind.ToLowerInvariant() switch
    {
        "run" => StartupKind.Run,
        "run32" => StartupKind.Run32,
        "folder" => StartupKind.Folder,
        "packaged" => StartupKind.Packaged,
        _ => throw new ArgumentException(L("Unknown entry type: {0}", kind)),
    };

    public static RegHive Hive(StartupScope scope) => scope == StartupScope.User ? RegHive.CurrentUser : RegHive.LocalMachine;

    /// <summary>Clé Run correspondant à une entrée (null pour les dossiers et les applications empaquetées).</summary>
    public static string? RunKeyFor(StartupScope scope, StartupKind kind) => (scope, kind) switch
    {
        (_, StartupKind.Run) => RunKey,
        (StartupScope.Machine, StartupKind.Run32) => Run32Key,
        _ => null,
    };

    /// <summary>Sous-clé StartupApproved utilisée par le Gestionnaire des tâches pour ce type d'entrée.</summary>
    public static string? ApprovedKeyFor(StartupKind kind) => kind switch
    {
        StartupKind.Run => ApprovedBase + "Run",
        StartupKind.Run32 => ApprovedBase + "Run32",
        StartupKind.Folder => ApprovedBase + "StartupFolder",
        _ => null,
    };

    public static string StartupFolder(StartupScope scope) =>
        Environment.GetFolderPath(scope == StartupScope.User ? Environment.SpecialFolder.Startup : Environment.SpecialFolder.CommonStartup);

    /// <summary>
    /// Valeur StartupApproved écrite par le Gestionnaire des tâches : 12 octets, premier octet 02 = activé (reste à zéro),
    /// 03 = désactivé suivi (à l'octet 4) de l'horodatage FILETIME UTC de la désactivation.
    /// </summary>
    public static byte[] ApprovedBytes(bool enabled, DateTime utcNow)
    {
        var bytes = new byte[12];
        bytes[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled) BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(bytes, 4);
        return bytes;
    }

    /// <summary>Lecture d'une valeur StartupApproved : bit 0 du premier octet = désactivé (02/06 activé, 03/07 désactivé).</summary>
    public static (bool Enabled, DateTime? Since) ParseApproved(object? value)
    {
        if (value is not byte[] { Length: > 0 } b) return (true, null);
        var disabled = (b[0] & 1) == 1;
        DateTime? since = null;
        if (disabled && b.Length >= 12)
        {
            var ft = BitConverter.ToInt64(b, 4);
            if (ft > 0)
            {
                try { since = DateTime.FromFileTimeUtc(ft).ToLocalTime(); } catch (ArgumentOutOfRangeException) { }
            }
        }
        return (!disabled, since);
    }

    // ------------------------------------------------------------------ Validation (actions, broker compris)

    /// <summary>
    /// Vérifie qu'une entrée existe réellement dans l'énumération actuelle ; lève une exception de validation sinon.
    /// Aucun nom arbitraire n'atteint ainsi une écriture de registre.
    /// </summary>
    public static void EnsureExists(StartupScope scope, StartupKind kind, string name, string? userSid)
    {
        switch (kind)
        {
            case StartupKind.Run:
            case StartupKind.Run32:
            {
                var runKey = RunKeyFor(scope, kind) ?? throw new Core.Security.ValidationException(L("Entry type not allowed for this scope."));
                using var root = RegistryAccess.OpenRoot(Hive(scope), userSid, writable: false);
                using var k = root.OpenSubKey(runKey, false);
                if (k is null || !k.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new Core.Security.ValidationException(L("This startup entry no longer exists. Refresh the list."));
                return;
            }
            case StartupKind.Folder:
            {
                if (name.IndexOfAny(['\\', '/', ':']) >= 0 || name.Contains(".."))
                    throw new Core.Security.ValidationException(L("Invalid file name."));
                var folder = StartupFolder(scope);
                var exists = Directory.Exists(folder) && Directory.EnumerateFiles(folder)
                    .Any(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
                if (!exists) throw new Core.Security.ValidationException(L("This shortcut is no longer in the Startup folder. Refresh the list."));
                return;
            }
            case StartupKind.Packaged:
            {
                if (scope != StartupScope.User || !PackagedNameRx().IsMatch(name))
                    throw new Core.Security.ValidationException(L("Invalid app task ID."));
                using var root = RegistryAccess.OpenRoot(RegHive.CurrentUser, userSid, writable: false);
                using var k = root.OpenSubKey(AppModelKey + "\\" + name, false);
                if (k?.GetValue("State") is not int state)
                    throw new Core.Security.ValidationException(L("This startup task no longer exists. Refresh the list."));
                if (state is 3 or 4)
                    throw new Core.Security.ValidationException(L("This app's state is enforced by an organization policy."));
                return;
            }
            default:
                throw new Core.Security.ValidationException(L("This entry type is read-only."));
        }
    }

    /// <summary>Opérations d'activation/désactivation, identiques à celles du Gestionnaire des tâches.</summary>
    public static List<Operation> SetEnabledOps(StartupScope scope, StartupKind kind, string name, bool enabled, DateTime utcNow)
    {
        if (kind == StartupKind.Packaged)
            return [new RegSet(RegHive.CurrentUser, AppModelKey + "\\" + name, "State", RegistryValueKind.DWord, enabled ? 2 : 1)];
        var approved = ApprovedKeyFor(kind) ?? throw new ArgumentException("Type non modifiable : " + kind);
        return [new RegSet(Hive(scope), approved, name, RegistryValueKind.Binary, ApprovedBytes(enabled, utcNow))];
    }

    /// <summary>Suppression d'une entrée Run (et de son état StartupApproved) : entièrement restaurable depuis le journal.</summary>
    public static List<Operation> DeleteOps(StartupScope scope, StartupKind kind, string name)
    {
        var runKey = RunKeyFor(scope, kind) ?? throw new ArgumentException(L("Only registry entries can be deleted."));
        return
        [
            new RegDeleteValue(Hive(scope), runKey, name),
            new RegDeleteValue(Hive(scope), ApprovedKeyFor(kind)!, name),
        ];
    }

    // ------------------------------------------------------------------ Énumération

    /// <summary>Nombre d'applications activées au démarrage (contrôle de santé : rapide, registre et dossiers seulement).</summary>
    public static (int Enabled, int Total) Count()
    {
        var items = Load(details: false).Where(i => !i.IsReadOnly).ToList();
        return (items.Count(i => i.Enabled), items.Count);
    }

    /// <summary>
    /// Énumère toutes les sources de démarrage. <paramref name="details"/> : résout les exécutables, éditeurs,
    /// noms affichés et icônes (plus lent : à appeler hors du thread UI).
    /// </summary>
    public static List<StartupItem> Load(bool details)
    {
        var items = new List<StartupItem>();
        AddRun(items, StartupScope.User, StartupKind.Run, RunKey);
        AddRun(items, StartupScope.Machine, StartupKind.Run, RunKey);
        if (Environment.Is64BitOperatingSystem) AddRun(items, StartupScope.Machine, StartupKind.Run32, Run32Key);
        AddFolder(items, StartupScope.User);
        AddFolder(items, StartupScope.Machine);
        AddPackaged(items, details);
        AddReadOnly(items, StartupScope.User, StartupKind.RunOnce, RunOnceKey);
        AddReadOnly(items, StartupScope.Machine, StartupKind.RunOnce, RunOnceKey);
        if (Environment.Is64BitOperatingSystem) AddReadOnly(items, StartupScope.Machine, StartupKind.RunOnce, RunOnce32Key);
        AddReadOnly(items, StartupScope.User, StartupKind.Policy, PolicyRunKey);
        AddReadOnly(items, StartupScope.Machine, StartupKind.Policy, PolicyRunKey);

        if (details)
        {
            var infoCache = new Dictionary<string, FileVersionInfo?>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.Where(i => i.Kind != StartupKind.Packaged))
            {
                try { Enrich(item, infoCache); }
                catch (Exception ex) { Log.Warn("Startup", $"détails de {item.Name} : {ex.Message}"); }
            }
        }
        return items;
    }

    private static RegistryKey? Open(StartupScope scope, string key)
    {
        try
        {
            using var root = RegistryAccess.OpenRoot(Hive(scope), null, writable: false);
            return root.OpenSubKey(key, false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    private static Dictionary<string, object?> ReadApproved(StartupScope scope, StartupKind kind)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (ApprovedKeyFor(kind) is not { } key) return result;
        using var k = Open(scope, key);
        if (k is null) return result;
        foreach (var n in k.GetValueNames()) result[n] = k.GetValue(n);
        return result;
    }

    private static void AddRun(List<StartupItem> items, StartupScope scope, StartupKind kind, string key)
    {
        using var k = Open(scope, key);
        if (k is null) return;
        var approved = ReadApproved(scope, kind);
        foreach (var name in k.GetValueNames())
        {
            if (name.Length == 0) continue;
            var command = k.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
            var (enabled, since) = ParseApproved(approved.GetValueOrDefault(name));
            items.Add(new StartupItem
            {
                Name = name, DisplayName = name, Scope = scope, Kind = kind, Command = command,
                Enabled = enabled, DisabledSince = since,
            });
        }
    }

    private static void AddReadOnly(List<StartupItem> items, StartupScope scope, StartupKind kind, string key)
    {
        using var k = Open(scope, key);
        if (k is null) return;
        foreach (var name in k.GetValueNames())
        {
            if (name.Length == 0) continue;
            var command = k.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
            items.Add(new StartupItem { Name = name, DisplayName = name.TrimStart('!', '*'), Scope = scope, Kind = kind, Command = command, Enabled = true });
        }
    }

    private static void AddFolder(List<StartupItem> items, StartupScope scope)
    {
        string folder;
        try { folder = StartupFolder(scope); } catch { return; }
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        var approved = ReadApproved(scope, StartupKind.Folder);
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            var (enabled, since) = ParseApproved(approved.GetValueOrDefault(name));
            items.Add(new StartupItem
            {
                Name = name, DisplayName = Path.GetFileNameWithoutExtension(name), Scope = scope, Kind = StartupKind.Folder,
                Command = file, Enabled = enabled, DisabledSince = since, LocationPath = file,
            });
        }
    }

    private static void AddPackaged(List<StartupItem> items, bool details)
    {
        using var k = Open(StartupScope.User, AppModelKey);
        if (k is null) return;
        foreach (var pfn in k.GetSubKeyNames())
        {
            try
            {
                using var app = k.OpenSubKey(pfn, false);
                if (app is null) continue;
                foreach (var taskId in app.GetSubKeyNames())
                {
                    using var task = app.OpenSubKey(taskId, false);
                    if (task?.GetValue("State") is not int state) continue;
                    var name = pfn + "\\" + taskId;
                    if (!PackagedNameRx().IsMatch(name)) continue;
                    var item = new StartupItem
                    {
                        Name = name, DisplayName = FriendlyPackageName(pfn), Scope = StartupScope.User, Kind = StartupKind.Packaged,
                        Command = pfn, Enabled = state is 2 or 4, PackagedState = state,
                    };
                    if (details) EnrichPackaged(item, pfn);
                    items.Add(item);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
    }

    /// <summary>« Microsoft.WindowsTerminal_8wekyb3d8bbwe » → « WindowsTerminal » (repli si le paquet est introuvable).</summary>
    private static string FriendlyPackageName(string pfn)
    {
        var name = pfn.Split('_')[0];
        var dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }

    private static void EnrichPackaged(StartupItem item, string pfn)
    {
        try
        {
            var pm = new Windows.Management.Deployment.PackageManager();
            var pkg = pm.FindPackagesForUser(string.Empty, pfn).FirstOrDefault();
            if (pkg is null) return;
            try { if (!string.IsNullOrWhiteSpace(pkg.DisplayName)) item.DisplayName = pkg.DisplayName; } catch { }
            try { item.Publisher = pkg.PublisherDisplayName; } catch { }
            try
            {
                var logo = pkg.Logo;
                if (logo is not null && logo.IsFile && File.Exists(logo.LocalPath))
                {
                    item.Icon = LoadImage(logo.LocalPath);
                }
            }
            catch { /* logo absent */ }
            try { item.LocationPath = pkg.InstalledLocation?.Path; } catch { }
        }
        catch (Exception ex)
        {
            Log.Warn("Startup", $"paquet {pfn} : {ex.Message}");
        }
    }

    private static void Enrich(StartupItem item, Dictionary<string, FileVersionInfo?> cache)
    {
        string? target = null;
        if (item.Kind == StartupKind.Folder)
        {
            var file = item.Command;
            target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file) : file;
            if (target is not null && !File.Exists(target)) { item.FileMissing = Path.IsPathFullyQualified(target); target = null; }
            item.LocationPath = file;
        }
        else
        {
            target = ResolveExecutable(item.Command);
            if (target is null) item.FileMissing = FirstTokenIsAbsolutePath(item.Command);
            item.LocationPath = target;
        }
        item.TargetPath = target;
        if (target is null) return;

        if (!cache.TryGetValue(target, out var info))
        {
            try { info = FileVersionInfo.GetVersionInfo(target); } catch { info = null; }
            cache[target] = info;
        }
        if (info is not null)
        {
            if (!string.IsNullOrWhiteSpace(info.CompanyName)) item.Publisher = info.CompanyName.Trim();
            if (item.Kind is StartupKind.Run or StartupKind.Run32 && !string.IsNullOrWhiteSpace(info.FileDescription)
                && !IsGenericHost(target))
                item.DisplayName = info.FileDescription.Trim();
        }
        item.Icon = ExtractIcon(target);
    }

    /// <summary>Hôtes génériques (rundll32, cmd…) : leur description ne dit rien de l'application lancée.</summary>
    private static bool IsGenericHost(string path)
    {
        var f = Path.GetFileName(path).ToLowerInvariant();
        return f is "rundll32.exe" or "cmd.exe" or "powershell.exe" or "pwsh.exe" or "wscript.exe" or "cscript.exe" or "mshta.exe"
                 or "conhost.exe" or "explorer.exe" or "regsvr32.exe";
    }

    private static bool FirstTokenIsAbsolutePath(string command)
    {
        var c = Environment.ExpandEnvironmentVariables(command.Trim());
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            c = end > 1 ? c[1..end] : c.Trim('"');
        }
        return c.Length > 3 && char.IsLetter(c[0]) && c[1] == ':' && c[2] == '\\';
    }

    /// <summary>Extrait l'exécutable d'une ligne de commande (guillemets, espaces non protégés, variables d'environnement).</summary>
    public static string? ResolveExecutable(string command)
    {
        var c = Environment.ExpandEnvironmentVariables(command.Trim());
        if (c.Length == 0) return null;
        if (c[0] == '"')
        {
            var end = c.IndexOf('"', 1);
            return Existing(end > 1 ? c[1..end] : c.Trim('"'));
        }
        var idx = -1;
        for (var guard = 0; guard < 32; guard++)
        {
            idx = c.IndexOf(' ', idx + 1);
            var part = idx < 0 ? c : c[..idx];
            if (Existing(part.TrimEnd(',')) is { } found) return found;
            if (idx < 0) break;
        }
        return null;
    }

    private static string? Existing(string p)
    {
        p = p.Trim();
        if (p.Length is 0 or > 400 || p.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
        try
        {
            if (Path.IsPathFullyQualified(p))
            {
                if (p.StartsWith(@"\\", StringComparison.Ordinal)) return null; // pas d'accès réseau
                if (File.Exists(p)) return Path.GetFullPath(p);
                if (!p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p + ".exe")) return Path.GetFullPath(p + ".exe");
                return null;
            }
            if (p.IndexOfAny(['\\', '/', ':']) >= 0) return null;
            foreach (var dir in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
            {
                var candidate = Path.Combine(dir, p);
                if (File.Exists(candidate)) return candidate;
                if (!p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate + ".exe")) return candidate + ".exe";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { }
        return null;
    }

    /// <summary>Cible d'un raccourci .lnk (COM WScript.Shell, lecture seule).</summary>
    private static string? ShortcutTarget(string lnk)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            shell = Activator.CreateInstance(type);
            shortcut = ((dynamic)shell!).CreateShortcut(lnk);
            var target = (string?)((dynamic)shortcut!).TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch { return null; }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>Icône associée à un exécutable, figée pour être utilisable depuis le thread UI.</summary>
    private static ImageSource? ExtractIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 32;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}
