using System.Windows;
using Timonier.Core.Engine;
using Timonier.Core.Platform;

namespace Timonier.UI.Services;

public interface INavigator
{
    string? CurrentPageId { get; }
    /// <summary>Ouvre une page. Paramètres usuels : "tweak:&lt;id&gt;" pour mettre un réglage en évidence, ou un objet propre à la page.</summary>
    void Navigate(string pageId, object? parameter = null);
}

/// <summary>Implémenté (optionnellement) par une page pour recevoir le paramètre de navigation.</summary>
public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);
    void OnNavigatedFrom() { }
}

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string primary = "Continuer", string secondary = "Annuler", bool danger = false);
    Task AlertAsync(string title, string message);
    /// <summary>Saisie de texte. <paramref name="validate"/> renvoie un message d'erreur ou null si valide.</summary>
    Task<string?> PromptAsync(string title, string message, string? initial = null, bool password = false, Func<string, string?>? validate = null);
    /// <summary>Boîte de dialogue avec contenu personnalisé ; renvoie true si le bouton principal est choisi.</summary>
    Task<bool> ShowAsync(string title, FrameworkElement content, string primary = "OK", string? secondary = "Annuler", bool danger = false);
}

public enum ToastKind { Info, Success, Warning, Error }

public interface IToastService
{
    void Show(string message, ToastKind kind = ToastKind.Info, string? actionLabel = null, Action? action = null, TimeSpan? duration = null);

    /// <summary>Affiche le résultat d'une opération : succès avec « Annuler », effets (redémarrages) et erreurs.</summary>
    void ShowOutcome(ApplyOutcome outcome);
}

/// <summary>
/// Raisons pour lesquelles Timonier doit rester actif quand on ferme la fenêtre (accès guidé actif, tâche en cours…).
/// Sans raison enregistrée, fermer la fenêtre quitte l'application : rien ne tourne en arrière-plan inutilement.
/// </summary>
public sealed class BackgroundRegistry
{
    private readonly List<(Guid Id, string Reason)> _reasons = [];
    private readonly Lock _gate = new();

    public event EventHandler? Changed;

    public IReadOnlyList<string> Reasons
    {
        get { lock (_gate) return [.. _reasons.Select(r => r.Reason)]; }
    }

    public bool IsNeeded
    {
        get { lock (_gate) return _reasons.Count > 0; }
    }

    public IDisposable Acquire(string reason)
    {
        var id = Guid.NewGuid();
        lock (_gate) _reasons.Add((id, reason));
        Log.Info("Background", "+ " + reason);
        Changed?.Invoke(this, EventArgs.Empty);
        return new Releaser(this, id, reason);
    }

    private void Release(Guid id, string reason)
    {
        lock (_gate) _reasons.RemoveAll(r => r.Id == id);
        Log.Info("Background", "- " + reason);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Releaser(BackgroundRegistry owner, Guid id, string reason) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release(id, reason);
        }
    }
}

/// <summary>Effets à appliquer après certains réglages (redémarrage de l'Explorateur, etc.).</summary>
public static class SystemEffects
{
    public static event EventHandler? RebootPendingChanged;
    public static bool RebootPending { get; private set; }
    public static bool SignOutPending { get; private set; }

    public static void MarkReboot()
    {
        RebootPending = true;
        RebootPendingChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void MarkSignOut()
    {
        SignOutPending = true;
        RebootPendingChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Redémarre l'Explorateur Windows de l'utilisateur (barre des tâches, bureau). Sans élévation.</summary>
    public static async Task RestartExplorerAsync()
    {
        await ProcessRunner.RunAsync(SystemTool.TaskKill, ["/f", "/im", "explorer.exe"], new RunOptions { Timeout = TimeSpan.FromSeconds(15) });
        await Task.Delay(800);
        ProcessRunner.Launch(SystemTool.Explorer);
    }

    public static Task RebootNowAsync() =>
        ProcessRunner.RunAsync(SystemTool.Shutdown, ["/r", "/t", "5", "/c", "Redémarrage demandé depuis Timonier"], new RunOptions());

    public static Task SignOutNowAsync() =>
        ProcessRunner.RunAsync(SystemTool.Shutdown, ["/l"], new RunOptions());
}

/// <summary>Icône de la zone de notification (créée uniquement si Timonier reste actif en arrière-plan).</summary>
public sealed class TrayIcon : IDisposable
{
    private System.Windows.Forms.NotifyIcon? _icon;
    private readonly Action _open;
    private readonly Action _exit;

    public TrayIcon(Action open, Action exit)
    {
        _open = open;
        _exit = exit;
    }

    public void Show(IReadOnlyList<string> reasons)
    {
        if (_icon is null)
        {
            _icon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(AppPaths.ExecutablePath),
                ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(),
            };
            _icon.ContextMenuStrip.Items.Add("Ouvrir Timonier", null, (_, _) => _open());
            _icon.ContextMenuStrip.Items.Add("Quitter", null, (_, _) => _exit());
            _icon.DoubleClick += (_, _) => _open();
        }
        var text = reasons.Count == 0 ? "Timonier" : "Timonier — " + string.Join(", ", reasons);
        _icon.Text = text.Length > 63 ? text[..60] + "…" : text;
        _icon.Visible = true;
    }

    public void Notify(string title, string message)
    {
        if (_icon is { Visible: true }) _icon.ShowBalloonTip(4000, title, message, System.Windows.Forms.ToolTipIcon.Info);
    }

    public void Hide()
    {
        if (_icon is not null) _icon.Visible = false;
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
