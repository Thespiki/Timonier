using PcPilot.Core.Platform;
using PcPilot.Core.Settings;

namespace PcPilot.Modules.GuidedAccess;

/// <summary>
/// Masquage temporaire de la barre des tâches (principale et secondaires) avec restauration garantie :
/// fin de session, exception, fermeture du processus (ProcessExit, exception non gérée) et, si PC Pilot a été arrêté
/// de force, au lancement suivant (marqueur dans les préférences, voir <see cref="RecoverIfNeeded"/>).
/// Aucun réglage Windows n'est modifié : seules les fenêtres de l'Explorateur sont masquées puis réaffichées.
/// </summary>
internal static class TaskbarGuard
{
    private const string MarkerKey = "guided.recovery.taskbar";
    private static readonly Lock Gate = new();
    private static readonly List<nint> Hidden = [];
    private static bool _hooked;

    public static bool IsHidden
    {
        get { lock (Gate) return Hidden.Count > 0; }
    }

    /// <summary>Masque les barres des tâches visibles (thread UI).</summary>
    public static void Hide()
    {
        EnsureProcessHooks();
        var bars = FindTaskbars();
        lock (Gate)
        {
            foreach (var h in bars)
            {
                if (!GuidedNative.IsWindowVisible(h) || Hidden.Contains(h)) continue;
                if (GuidedNative.ShowWindow(h, GuidedNative.SW_HIDE) || !GuidedNative.IsWindowVisible(h)) Hidden.Add(h);
            }
            if (Hidden.Count == 0) return;
        }
        try
        {
            SettingsStore.Current.ModuleData[MarkerKey] = "1";
            SettingsStore.Save();
        }
        catch (Exception ex) { Log.Warn("GuidedAccess", "marqueur de restauration : " + ex.Message); }
    }

    /// <summary>Réaffiche les barres masquées. Idempotent, sûr depuis n'importe quel thread.</summary>
    public static void Restore()
    {
        nint[] bars;
        lock (Gate)
        {
            bars = [.. Hidden];
            Hidden.Clear();
        }
        foreach (var h in bars)
        {
            try
            {
                if (GuidedNative.IsWindow(h)) GuidedNative.ShowWindow(h, GuidedNative.SW_SHOWNA);
            }
            catch { /* on tente les autres */ }
        }
        if (bars.Length > 0) ClearMarker();
    }

    /// <summary>
    /// Si une session précédente a été interrompue brutalement (processus tué), la barre des tâches peut être restée masquée :
    /// on la réaffiche. Renvoie true si une restauration a eu lieu.
    /// </summary>
    public static bool RecoverIfNeeded()
    {
        if (GuidedSession.Current is not null) return false;
        if (!SettingsStore.Current.ModuleData.ContainsKey(MarkerKey)) return false;
        var restored = false;
        foreach (var h in FindTaskbars())
        {
            if (GuidedNative.IsWindowVisible(h)) continue;
            GuidedNative.ShowWindow(h, GuidedNative.SW_SHOWNA);
            restored = true;
        }
        ClearMarker();
        Log.Info("GuidedAccess", restored ? "barre des tâches réaffichée après une session interrompue" : "marqueur de session interrompue effacé");
        return restored;
    }

    private static void ClearMarker()
    {
        try
        {
            if (SettingsStore.Current.ModuleData.Remove(MarkerKey)) SettingsStore.Save();
        }
        catch (Exception ex) { Log.Warn("GuidedAccess", "marqueur de restauration : " + ex.Message); }
    }

    private static List<nint> FindTaskbars()
    {
        var list = new List<nint>();
        var main = GuidedNative.FindWindow("Shell_TrayWnd", null);
        if (main != 0) list.Add(main);
        nint h = 0;
        for (var i = 0; i < 16; i++)
        {
            h = GuidedNative.FindWindowEx(0, h, "Shell_SecondaryTrayWnd", null);
            if (h == 0) break;
            list.Add(h);
        }
        return list;
    }

    private static void EnsureProcessHooks()
    {
        if (_hooked) return;
        _hooked = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Restore();
    }
}
