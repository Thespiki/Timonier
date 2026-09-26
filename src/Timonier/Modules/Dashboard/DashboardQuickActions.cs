using System.ComponentModel;
using System.Diagnostics;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Dashboard;

/// <summary>Actions rapides du tableau de bord (exécutées sur le thread UI, sans élévation).</summary>
internal static class DashboardQuickActions
{
    public static async Task RestartExplorerAsync()
    {
        var ok = await AppHost.Dialogs.ConfirmAsync(L("Restart Windows Explorer?"),
            L("The taskbar and desktop disappear for a second or two, then come back. Open File Explorer windows will be closed; your apps and documents aren't affected."),
            LC("quick action", "Restart Explorer"));
        if (!ok) return;
        try
        {
            await SystemEffects.RestartExplorerAsync();
            AppHost.Toasts.Show(LC("quick action", "Windows Explorer restarted."), ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error("Dashboard", "redémarrage de l'Explorateur", ex);
            AppHost.Toasts.Show(LC("quick action", "Couldn't restart Explorer: {0}", ex.Message), ToastKind.Error);
        }
    }

    public static Task OpenTaskManagerAsync()
    {
        try
        {
            ProcessRunner.Launch(SystemTool.Taskmgr);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
        {
            // Le Gestionnaire des tâches demande le niveau « le plus élevé disponible » : sur un compte administrateur,
            // Windows exige alors un lancement via le shell (chemin absolu résolu, aucun argument).
            try { Process.Start(new ProcessStartInfo(SystemTools.Resolve(SystemTool.Taskmgr)) { UseShellExecute = true })?.Dispose(); }
            catch (Exception inner) { Fail("taskmgr", L("Couldn't open Task Manager: {0}", inner.Message), inner); }
        }
        catch (Exception ex) { Fail("taskmgr", L("Couldn't open Task Manager: {0}", ex.Message), ex); }
        return Task.CompletedTask;
    }

    public static Task OpenSettingsAsync()
    {
        try { ProcessRunner.OpenSettingsUri("ms-settings:"); }
        catch (Exception ex) { Fail("ms-settings", L("Couldn't open Windows Settings: {0}", ex.Message), ex); }
        return Task.CompletedTask;
    }

    public static Task LockAsync()
    {
        if (!DashNative.LockWorkStation())
            AppHost.Toasts.Show(L("Windows refused to lock the session."), ToastKind.Warning);
        return Task.CompletedTask;
    }

    private static void Fail(string what, string message, Exception ex)
    {
        Log.Error("Dashboard", "ouverture de " + what, ex);
        AppHost.Toasts.Show(message, ToastKind.Error);
    }
}
