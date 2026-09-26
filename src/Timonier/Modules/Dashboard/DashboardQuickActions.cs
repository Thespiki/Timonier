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
        var ok = await AppHost.Dialogs.ConfirmAsync(L("Redémarrer l'Explorateur Windows ?"),
            L("La barre des tâches et le bureau disparaissent une ou deux secondes puis reviennent. Les fenêtres de l'Explorateur de fichiers ouvertes seront fermées ; vos applications et documents ne sont pas touchés."),
            L("Redémarrer l'Explorateur"));
        if (!ok) return;
        try
        {
            await SystemEffects.RestartExplorerAsync();
            AppHost.Toasts.Show(L("Explorateur Windows redémarré."), ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error("Dashboard", "redémarrage de l'Explorateur", ex);
            AppHost.Toasts.Show(L("Impossible de redémarrer l'Explorateur : {0}", ex.Message), ToastKind.Error);
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
            catch (Exception inner) { Fail("taskmgr", L("Impossible d'ouvrir le Gestionnaire des tâches : {0}", inner.Message), inner); }
        }
        catch (Exception ex) { Fail("taskmgr", L("Impossible d'ouvrir le Gestionnaire des tâches : {0}", ex.Message), ex); }
        return Task.CompletedTask;
    }

    public static Task OpenSettingsAsync()
    {
        try { ProcessRunner.OpenSettingsUri("ms-settings:"); }
        catch (Exception ex) { Fail("ms-settings", L("Impossible d'ouvrir les Paramètres Windows : {0}", ex.Message), ex); }
        return Task.CompletedTask;
    }

    public static Task LockAsync()
    {
        if (!DashNative.LockWorkStation())
            AppHost.Toasts.Show(L("Windows a refusé le verrouillage de la session."), ToastKind.Warning);
        return Task.CompletedTask;
    }

    private static void Fail(string what, string message, Exception ex)
    {
        Log.Error("Dashboard", "ouverture de " + what, ex);
        AppHost.Toasts.Show(message, ToastKind.Error);
    }
}
