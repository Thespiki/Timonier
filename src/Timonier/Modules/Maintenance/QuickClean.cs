using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Maintenance;

/// <summary>Actions rapides du tableau de bord (thread UI) et estimation du contrôle de santé (hors thread UI).</summary>
internal static class QuickClean
{
    private static readonly long Threshold = 500L * 1024 * 1024;

    /// <summary>Estimation rapide (≤ 2 s) : %TEMP% (fichiers de plus de 24 h) + corbeille.</summary>
    public static HealthResult EstimateHealth()
    {
        var temp = CleanupEngine.Analyze(CleanupCatalog.Get("usertemp")!, TimeSpan.FromSeconds(1.5), CancellationToken.None);
        var bin = CleanupEngine.Analyze(CleanupCatalog.Get("recyclebin")!, TimeSpan.FromSeconds(1), CancellationToken.None);
        var total = temp.Bytes + bin.Bytes;
        var summary = temp.Partial ? L("at least {0} can be freed", Format.Bytes(total)) : L("{0} can be freed", Format.Bytes(total));
        var detail = L("Temporary files: {0} · Recycle Bin: {1}", Format.Bytes(temp.Bytes), Format.Bytes(bin.Bytes));
        return new HealthResult(total >= Threshold ? HealthStatus.Info : HealthStatus.Good, summary, detail);
    }

    public static async Task RunAsync()
    {
        var (temp, bin) = await Task.Run(() => (
            CleanupEngine.Analyze(CleanupCatalog.Get("usertemp")!, TimeSpan.FromSeconds(5), CancellationToken.None),
            CleanupEngine.Analyze(CleanupCatalog.Get("recyclebin")!, TimeSpan.FromSeconds(2), CancellationToken.None)));
        if (temp.Bytes + bin.Bytes == 0)
        {
            AppHost.Toasts.Show(L("Nothing to clean up: no old temporary files and the Recycle Bin is empty."), ToastKind.Success);
            return;
        }
        var withBin = bin.Files > 0 || bin.Bytes > 0;
        var question = withBin
            ? LP(bin.Files, "Delete {1} of temporary files (older than 24 h) and empty the Recycle Bin ({2}, {0} item)?\n\nThe contents of the Recycle Bin will be permanently deleted. Files in use are skipped.",
                "Delete {1} of temporary files (older than 24 h) and empty the Recycle Bin ({2}, {0} items)?\n\nThe contents of the Recycle Bin will be permanently deleted. Files in use are skipped.",
                Format.Bytes(temp.Bytes), Format.Bytes(bin.Bytes))
            : L("Delete {0} of temporary files older than 24 h?\n\nThe Recycle Bin is already empty. Files in use are skipped.", Format.Bytes(temp.Bytes));
        var ok = await AppHost.Dialogs.ConfirmAsync(L("Quick cleanup"), question, L("Clean up"), L("Undo"), danger: withBin);
        if (!ok) return;
        var (t, b) = await Task.Run(() => (
            CleanupEngine.Clean(CleanupCatalog.Get("usertemp")!, TimeSpan.FromMinutes(2), CancellationToken.None),
            withBin ? CleanupEngine.Clean(CleanupCatalog.Get("recyclebin")!, TimeSpan.FromMinutes(2), CancellationToken.None) : new CleanupStats()));
        var freed = t.Bytes + b.Bytes;
        var message = t.Skipped > 0
            ? LP(t.Skipped, "Quick cleanup complete: {1} freed. {0} file in use skipped.",
                "Quick cleanup complete: {1} freed. {0} files in use skipped.", Format.Bytes(freed))
            : L("Quick cleanup complete: {0} freed.", Format.Bytes(freed));
        if (b.Note is { } note) message += " " + note;
        AppHost.Toasts.Show(message, b.Note is null ? ToastKind.Success : ToastKind.Warning);
    }

    public static async Task CreateRestorePointAsync()
    {
        AppHost.Toasts.Show(L("Creating the restore point…"), ToastKind.Info);
        var outcome = await AppHost.Engine.RunActionAsync(RestorePointCreateAction.ActionId, new Dictionary<string, string> { ["description"] = "Timonier" });
        AppHost.Toasts.ShowOutcome(outcome);
    }
}
