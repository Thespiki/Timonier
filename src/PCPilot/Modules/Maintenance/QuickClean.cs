using PcPilot.Core.Catalog;
using PcPilot.Core.Platform;
using PcPilot.UI.Services;

namespace PcPilot.Modules.Maintenance;

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
        var approx = temp.Partial ? "au moins " : "";
        var summary = $"{approx}{Format.Bytes(total)} récupérables";
        var detail = $"Fichiers temporaires : {Format.Bytes(temp.Bytes)} · Corbeille : {Format.Bytes(bin.Bytes)}";
        return new HealthResult(total >= Threshold ? HealthStatus.Info : HealthStatus.Good, summary, detail);
    }

    public static async Task RunAsync()
    {
        var (temp, bin) = await Task.Run(() => (
            CleanupEngine.Analyze(CleanupCatalog.Get("usertemp")!, TimeSpan.FromSeconds(5), CancellationToken.None),
            CleanupEngine.Analyze(CleanupCatalog.Get("recyclebin")!, TimeSpan.FromSeconds(2), CancellationToken.None)));
        if (temp.Bytes + bin.Bytes == 0)
        {
            AppHost.Toasts.Show("Rien à nettoyer : pas de fichiers temporaires anciens et la corbeille est vide.", ToastKind.Success);
            return;
        }
        var withBin = bin.Files > 0 || bin.Bytes > 0;
        var question = withBin
            ? $"Supprimer {Format.Bytes(temp.Bytes)} de fichiers temporaires (plus de 24 h) et vider la corbeille ({Format.Bytes(bin.Bytes)}, {bin.Files} élément(s)) ?\n\n" +
              "Le contenu de la corbeille sera supprimé définitivement. Les fichiers en cours d'utilisation sont ignorés."
            : $"Supprimer {Format.Bytes(temp.Bytes)} de fichiers temporaires de plus de 24 h ?\n\nLa corbeille est déjà vide. Les fichiers en cours d'utilisation sont ignorés.";
        var ok = await AppHost.Dialogs.ConfirmAsync("Nettoyage rapide", question, "Nettoyer", "Annuler", danger: withBin);
        if (!ok) return;
        var (t, b) = await Task.Run(() => (
            CleanupEngine.Clean(CleanupCatalog.Get("usertemp")!, TimeSpan.FromMinutes(2), CancellationToken.None),
            withBin ? CleanupEngine.Clean(CleanupCatalog.Get("recyclebin")!, TimeSpan.FromMinutes(2), CancellationToken.None) : new CleanupStats()));
        var freed = t.Bytes + b.Bytes;
        var message = $"Nettoyage rapide terminé : {Format.Bytes(freed)} libérés."
                      + (t.Skipped > 0 ? $" {t.Skipped} fichier(s) en cours d'utilisation ignoré(s)." : "")
                      + (b.Note is { } note ? " " + note : "");
        AppHost.Toasts.Show(message, b.Note is null ? ToastKind.Success : ToastKind.Warning);
    }

    public static async Task CreateRestorePointAsync()
    {
        AppHost.Toasts.Show("Création du point de restauration…", ToastKind.Info);
        var outcome = await AppHost.Engine.RunActionAsync(RestorePointCreateAction.ActionId, new Dictionary<string, string> { ["description"] = "PC Pilot" });
        AppHost.Toasts.ShowOutcome(outcome);
    }
}
