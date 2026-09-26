using System.Globalization;
using System.Text;
using System.Text.Json;
using Timonier.Core.Platform;
using Timonier.Core.Settings;

namespace Timonier.Modules.Apps;

/// <summary>Application supprimée avec Timonier (mémorisée pour proposer sa réinstallation depuis le Store).</summary>
internal sealed record RemovedApp(string Family, string Name, DateTime Date);

/// <summary>État partagé entre les vues de la page (opération en cours, cache des programmes installés, historique).</summary>
internal sealed class AppsContext(ActivityPanel activity)
{
    private const string RemovedKey = "apps.removed";
    private Task<List<InstalledProgram>>? _installed;

    public ActivityPanel Activity { get; } = activity;
    public bool WingetAvailable { get; set; }

    /// <summary>Déclenché (thread UI) après une installation, une désinstallation ou une mise à jour.</summary>
    public event Action? InstalledChanged;

    public Task<List<InstalledProgram>> GetInstalledAsync(bool refresh = false)
    {
        if (refresh || _installed is null || _installed.IsFaulted) _installed = Task.Run(() => InstalledPrograms.Read());
        return _installed;
    }

    public void NotifyInstalledChanged()
    {
        _installed = null;
        InstalledChanged?.Invoke();
    }

    // ------------------------------------------------------------------ Historique des suppressions

    public static List<RemovedApp> LoadRemoved()
    {
        try
        {
            if (AppHost.Settings.ModuleData.TryGetValue(RemovedKey, out var json) && json.Length > 0)
                return JsonSerializer.Deserialize<List<RemovedApp>>(json) ?? [];
        }
        catch (Exception ex) { Log.Warn("Apps", "historique illisible : " + ex.Message); }
        return [];
    }

    public static void RecordRemoved(string family, string name)
    {
        var list = LoadRemoved();
        list.RemoveAll(r => r.Family == family);
        list.Insert(0, new RemovedApp(family, name, DateTime.Now));
        SaveRemoved(list.Take(60).ToList());
    }

    public static void ForgetRemoved(string family)
    {
        var list = LoadRemoved();
        if (list.RemoveAll(r => r.Family == family) > 0) SaveRemoved(list);
    }

    private static void SaveRemoved(List<RemovedApp> list)
    {
        AppHost.Settings.ModuleData[RemovedKey] = JsonSerializer.Serialize(list);
        SettingsStore.Save();
    }

    // ------------------------------------------------------------------ Recherche

    /// <summary>Minuscules sans accents, pour une recherche tolérante.</summary>
    public static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
