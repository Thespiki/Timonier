using System.ComponentModel;
using System.Diagnostics;
using PcPilot.Core.Platform;
using PcPilot.Core.Security;

namespace PcPilot.Modules.GuidedAccess;

/// <summary>
/// Lance un exécutable choisi par l'utilisateur (chemin local validé, sans shell ni arguments) et attend sa fenêtre.
/// Gère les lanceurs qui se terminent aussitôt en ouvrant une autre application (ex. Bloc-notes, Calculatrice de Windows 11).
/// </summary>
internal static class AppLauncher
{
    public static async Task<AppWindow> LaunchAsync(string path, CancellationToken ct = default)
    {
        var exe = Validate.ExistingLocalFile(path, ".exe");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
        };
        var started = DateTime.Now;
        Process? process;
        try { process = Process.Start(psi); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
        {
            throw new InvalidOperationException("Cette application demande les droits d'administrateur : l'accès guidé ne peut pas la lancer ni la contrôler.");
        }
        if (process is null) throw new InvalidOperationException("L'application n'a pas pu être lancée.");
        Log.Info("GuidedAccess", "application lancée : " + Path.GetFileName(exe));

        using (process)
        {
            var pid = (uint)process.Id;
            var initialForeground = GuidedNative.GetForegroundWindow();
            var until = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < until)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(250, ct);
                var found = await Task.Run(() =>
                {
                    var h = WindowCatalog.FindMainWindow(pid);
                    if (h != 0) return h;
                    // Lanceur intermédiaire : on adopte la nouvelle fenêtre de premier plan d'un processus démarré après le lancement.
                    var fg = GuidedNative.GetForegroundWindow();
                    if (fg != 0 && fg != initialForeground && WindowCatalog.IsCandidate(fg, (uint)Environment.ProcessId)
                        && StartedAfter(GuidedNative.GetProcessId(fg), started.AddSeconds(-2)))
                        return fg;
                    return 0;
                }, ct);
                if (found != 0)
                {
                    var described = await Task.Run(() => WindowCatalog.Describe(found, exe), ct);
                    if (described is not null) return described;
                }
            }
        }
        throw new InvalidOperationException("L'application a été lancée mais aucune fenêtre n'est apparue dans les 20 secondes.");
    }

    private static bool StartedAfter(uint pid, DateTime threshold)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.StartTime >= threshold;
        }
        catch { return false; }
    }
}
