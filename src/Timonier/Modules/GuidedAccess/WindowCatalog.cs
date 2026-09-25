using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Timonier.Modules.GuidedAccess;

/// <summary>Fenêtre d'application pouvant servir de cible à l'accès guidé.</summary>
internal sealed record AppWindow(nint Handle, uint ProcessId, string Title, string ProcessName, string? ExePath, ImageSource? Icon)
{
    /// <summary>Chemin de l'exécutable si l'application a été lancée depuis Timonier (permet « Relancer »).</summary>
    public string? LaunchedPath { get; init; }

    public bool IsStoreHost => ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);

    public string Subtitle => IsStoreHost ? "Application du Microsoft Store" : ProcessName + ".exe";
}

/// <summary>Énumération (lecture seule) des fenêtres d'application ouvertes, hors Timonier et hors fenêtres système.</summary>
internal static class WindowCatalog
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow",
    };

    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost", "LockApp",
        "SystemSettingsBroker", "Widgets", "WidgetBoard", "ShellHost",
    };

    /// <summary>À appeler hors du thread UI : les icônes sont gelées et utilisables depuis l'interface.</summary>
    public static List<AppWindow> Enumerate()
    {
        var own = (uint)Environment.ProcessId;
        var result = new List<AppWindow>();
        var names = new Dictionary<uint, (string Name, string? Path)>();
        foreach (var h in GuidedNative.TopLevelWindows())
        {
            if (!IsCandidate(h, own)) continue;
            var pid = GuidedNative.GetProcessId(h);
            if (!names.TryGetValue(pid, out var info))
            {
                info = (ProcessName(pid), GuidedNative.GetProcessPath(pid));
                names[pid] = info;
            }
            if (info.Name.Length == 0 || ExcludedProcesses.Contains(info.Name)) continue;
            result.Add(new AppWindow(h, pid, GuidedNative.GetText(h).Trim(), info.Name, info.Path, LoadIcon(h, info.Path)));
        }
        return result;
    }

    public static bool IsCandidate(nint h, uint ownPid)
    {
        if (!GuidedNative.IsWindowVisible(h) || GuidedNative.GetWindowTextLength(h) == 0) return false;
        if (GuidedNative.GetWindow(h, GuidedNative.GW_OWNER) != 0) return false;
        var ex = (long)GuidedNative.GetWindowLongPtr(h, GuidedNative.GWL_EXSTYLE);
        if ((ex & GuidedNative.WS_EX_TOOLWINDOW) != 0 || (ex & GuidedNative.WS_EX_NOACTIVATE) != 0) return false;
        if (GuidedNative.IsCloaked(h)) return false;
        if (GuidedNative.GetProcessId(h) == ownPid) return false;
        return !ExcludedClasses.Contains(GuidedNative.GetClass(h));
    }

    /// <summary>Première fenêtre candidate d'un processus (après un lancement ou si la fenêtre cible a été recréée).</summary>
    public static nint FindMainWindow(uint pid)
    {
        var own = (uint)Environment.ProcessId;
        foreach (var h in GuidedNative.TopLevelWindows())
            if (GuidedNative.GetProcessId(h) == pid && IsCandidate(h, own)) return h;
        return 0;
    }

    public static AppWindow? Describe(nint h, string? launchedPath = null)
    {
        if (h == 0 || !GuidedNative.IsWindow(h)) return null;
        var pid = GuidedNative.GetProcessId(h);
        var path = GuidedNative.GetProcessPath(pid);
        return new AppWindow(h, pid, GuidedNative.GetText(h).Trim(), ProcessName(pid), path, LoadIcon(h, path)) { LaunchedPath = launchedPath };
    }

    private static string ProcessName(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    private static ImageSource? LoadIcon(nint hwnd, string? exePath)
    {
        try
        {
            nint icon = 0;
            if (GuidedNative.SendMessageTimeout(hwnd, GuidedNative.WM_GETICON, GuidedNative.ICON_BIG, 0, GuidedNative.SMTO_ABORTIFHUNG, 150, out var big) != 0)
                icon = big;
            if (icon == 0) icon = GuidedNative.GetClassLongPtr(hwnd, GuidedNative.GCLP_HICON);
            if (icon == 0 && GuidedNative.SendMessageTimeout(hwnd, GuidedNative.WM_GETICON, GuidedNative.ICON_SMALL2, 0, GuidedNative.SMTO_ABORTIFHUNG, 150, out var small) != 0)
                icon = small;
            if (icon != 0)
            {
                // Icône appartenant à la fenêtre : on la copie sans la détruire.
                var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            if (exePath is not null && File.Exists(exePath))
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (ico is null) return null;
                var source = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
        }
        catch { /* pas d'icône : une vignette générique est affichée */ }
        return null;
    }
}
