using Microsoft.Win32;
using Timonier.Core.Engine;
using Timonier.Core.Platform;

namespace Timonier.Modules.AppPages;

/// <summary>
/// Démarrage de Timonier avec Windows : valeur « Timonier » de HKCU\...\Run (réglage propre à l'application,
/// sans élévation). La valeur StartupApproved (désactivation depuis le Gestionnaire des tâches) est seulement lue,
/// et retirée quand l'utilisateur réactive explicitement le démarrage depuis Timonier.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal const string ValueName = "Timonier";

    public static string Command => $"\"{AppPaths.ExecutablePath}\" --background";

    public static string? CurrentValue()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return k?.GetValue(ValueName) as string;
        }
        catch (Exception ex)
        {
            Log.Warn("AppPages", "lecture Run : " + ex.Message);
            return null;
        }
    }

    public static bool IsEnabled() => CurrentValue() is { Length: > 0 };

    /// <summary>La valeur existe mais lance un autre exécutable (Timonier déplacé ou autre copie).</summary>
    public static bool PointsElsewhere() =>
        CurrentValue() is { Length: > 0 } v && !string.Equals(v.Trim(), Command, StringComparison.OrdinalIgnoreCase);

    /// <summary>Désactivé depuis le Gestionnaire des tâches (premier octet impair dans StartupApproved\Run).</summary>
    public static bool DisabledByTaskManager()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(ApprovedKey, false);
            return k?.GetValue(ValueName) is byte[] { Length: > 0 } b && (b[0] & 1) == 1;
        }
        catch { return false; }
    }

    public static void Enable()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey, true))
            k.SetValue(ValueName, Command, RegistryValueKind.String);
        // Réactivation explicite par l'utilisateur : on retire notre éventuelle marque « désactivé » du Gestionnaire des tâches.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true);
        approved?.DeleteValue(ValueName, false);
        Log.Info("AppPages", "démarrage avec Windows activé");
    }

    public static void Disable()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        k?.DeleteValue(ValueName, false);
        Log.Info("AppPages", "démarrage avec Windows désactivé");
    }
}

/// <summary>Inventaire (lecture seule) des données conservées par Timonier.</summary>
internal static class DataInventory
{
    public sealed record Item(string Title, string Description, string Location, string Size);

    public static string CacheDir => Path.Combine(AppPaths.LocalData, "cache");
    public static string SettingsFile => Path.Combine(AppPaths.LocalData, "settings.json");
    public static string ProfileCacheFile => Path.Combine(AppPaths.LocalData, "system-profile.json");
    public static string UserJournalFile => Path.Combine(AppPaths.LocalData, "journal.json");
    /// <summary>Journaux du broker élevé (voir Log.UseProtectedFile).</summary>
    public static string BrokerLogs => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppPaths.AppName, "logs");

    public static List<Item> Read()
    {
        var list = new List<Item>
        {
            new(L("Preferences"), L("Theme, options, hashed PIN, module preferences."), SettingsFile, FileSize(SettingsFile)),
            new(L("User history"), L("{0} — changes made without administrator rights (up to 1,000).", Count(JournalWriter.UserStore.All().Count)),
                UserJournalFile, FileSize(UserJournalFile)),
            new(L("Admin history"), L("{0} — changes made by the admin session (up to 500). Readable by everyone, can only be changed by administrators.", Count(MachineJournalCount())), @"HKEY_LOCAL_MACHINE\SOFTWARE\Timonier\Journal", L("Registry")),
            new(L("Cache"), L("PC hardware profile (speeds up startup) and temporary module data. Re-created automatically."),
                $"{ProfileCacheFile}\n{CacheDir}", Format.Bytes(Size(ProfileCacheFile) + DirSize(CacheDir))),
            new(L("Diagnostic logs"), L("Technical messages when errors occur (1 MB per file, up to 2 files). No secrets, no data sent. Those from the admin session are in a folder reserved for administrators (readable by everyone)."),
                $"{AppPaths.Logs}\n{BrokerLogs}", Format.Bytes(DirSize(AppPaths.Logs) + DirSize(BrokerLogs))),
        };
        if (StartupRegistration.IsEnabled())
            list.Add(new(LC("feature name", "Start with Windows"), L("Starts Timonier in the notification area when you sign in."),
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run → Timonier", L("Registry")));
        return list;
    }

    private static string Count(int n) => LP(n, "{0:N0} entry", "{0:N0} entries");

    private static int MachineJournalCount()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(AppPaths.MachineRegistryKey + @"\Journal", false);
            return k?.ValueCount ?? 0;
        }
        catch { return 0; }
    }

    private static long Size(string file)
    {
        try { return File.Exists(file) ? new FileInfo(file).Length : 0; }
        catch { return 0; }
    }

    private static string FileSize(string file) => File.Exists(file) ? Format.Bytes(Size(file)) : LC("file", "Missing");

    public static long DirSize(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return 0;
            return new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Sum(f => f.Length);
        }
        catch { return 0; }
    }
}
