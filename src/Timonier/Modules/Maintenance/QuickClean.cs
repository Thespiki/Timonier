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
        var summary = temp.Partial ? L("au moins {0} récupérables", Format.Bytes(total)) : L("{0} récupérables", Format.Bytes(total));
        var detail = L("Fichiers temporaires : {0} · Corbeille : {1}", Format.Bytes(temp.Bytes), Format.Bytes(bin.Bytes));
        return new HealthResult(total >= Threshold ? HealthStatus.Info : HealthStatus.Good, summary, detail);
    }

    public static async Task RunAsync()
    {
        var (temp, bin) = await Task.Run(() => (
            CleanupEngine.Analyze(CleanupCatalog.Get("usertemp")!, TimeSpan.FromSeconds(5), CancellationToken.None),
            CleanupEngine.Analyze(CleanupCatalog.Get("recyclebin")!, TimeSpan.FromSeconds(2), CancellationToken.None)));
        if (temp.Bytes + bin.Bytes == 0)
        {
            AppHost.Toasts.Show(L("Rien à nettoyer : pas de fichiers temporaires anciens et la corbeille est vide."), ToastKind.Success);
            return;
        }
        var withBin = bin.Files > 0 || bin.Bytes > 0;
        var question = withBin
            ? LP(bin.Files, "Supprimer {1} de fichiers temporaires (plus de 24 h) et vider la corbeille ({2}, {0} élément) ?\n\nLe contenu de la corbeille sera supprimé définitivement. Les fichiers en cours d'utilisation sont ignorés.",
                "Supprimer {1} de fichiers temporaires (plus de 24 h) et vider la corbeille ({2}, {0} éléments) ?\n\nLe contenu de la corbeille sera supprimé définitivement. Les fichiers en cours d'utilisation sont ignorés.",
                Format.Bytes(temp.Bytes), Format.Bytes(bin.Bytes))
            : L("Supprimer {0} de fichiers temporaires de plus de 24 h ?\n\nLa corbeille est déjà vide. Les fichiers en cours d'utilisation sont ignorés.", Format.Bytes(temp.Bytes));
        var ok = await AppHost.Dialogs.ConfirmAsync(L("Nettoyage rapide"), question, L("Nettoyer"), L("Annuler"), danger: withBin);
        if (!ok) return;
        var (t, b) = await Task.Run(() => (
            CleanupEngine.Clean(CleanupCatalog.Get("usertemp")!, TimeSpan.FromMinutes(2), CancellationToken.None),
            withBin ? CleanupEngine.Clean(CleanupCatalog.Get("recyclebin")!, TimeSpan.FromMinutes(2), CancellationToken.None) : new CleanupStats()));
        var freed = t.Bytes + b.Bytes;
        var message = t.Skipped > 0
            ? LP(t.Skipped, "Nettoyage rapide terminé : {1} libérés. {0} fichier en cours d'utilisation ignoré.",
                "Nettoyage rapide terminé : {1} libérés. {0} fichiers en cours d'utilisation ignorés.", Format.Bytes(freed))
            : L("Nettoyage rapide terminé : {0} libérés.", Format.Bytes(freed));
        if (b.Note is { } note) message += " " + note;
        AppHost.Toasts.Show(message, b.Note is null ? ToastKind.Success : ToastKind.Warning);
    }

    public static async Task CreateRestorePointAsync()
    {
        AppHost.Toasts.Show(L("Création du point de restauration…"), ToastKind.Info);
        var outcome = await AppHost.Engine.RunActionAsync(RestorePointCreateAction.ActionId, new Dictionary<string, string> { ["description"] = "Timonier" });
        AppHost.Toasts.ShowOutcome(outcome);
    }
}
