using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.GuidedAccess;

/// <summary>
/// Session d'accès guidé (thread UI) : verrouille le PC sur une application jusqu'à la saisie du code.
/// Filtre clavier (<see cref="KeyboardGuard"/>), garde du premier plan (SetWinEventHook), masquage de la barre des tâches
/// (<see cref="TaskbarGuard"/>) et écrans de code (<see cref="GuidedOverlay"/>). Tout est restauré à la fin, en cas
/// d'exception, à la fermeture du processus et si l'objet est libéré. Aucun réglage Windows n'est modifié.
/// </summary>
internal sealed class GuidedSession : IDisposable
{
    public static GuidedSession? Current { get; private set; }

    /// <summary>Déclenché (thread UI) quand une session démarre ou se termine.</summary>
    public static event EventHandler? StateChanged;

    private readonly GuidedOptions _options;
    private readonly Dispatcher _dispatcher;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _refocus;

    private nint _target;
    private uint _targetPid;
    private string _targetTitle;
    private readonly string? _launchedPath;
    private DateTime? _deadline;
    private IDisposable? _background;
    private nint _foregroundHook, _minimizeHook;
    private GuidedOverlay? _overlay;
    private bool _ended;
    private bool _relaunching;
    private long _lastRefocus;
    private int _fights;
    private long _fightWindowStart;

    private GuidedSession(AppWindow target, GuidedOptions options)
    {
        _options = options.Clone();
        _dispatcher = Dispatcher.CurrentDispatcher;
        _target = target.Handle;
        _targetPid = target.ProcessId;
        _targetTitle = string.IsNullOrWhiteSpace(target.Title) ? target.ProcessName : target.Title;
        _launchedPath = target.LaunchedPath;
        if (options.TimeLimitMinutes > 0) _deadline = _startedAt.AddMinutes(options.TimeLimitMinutes);
        _tick = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Guard(OnTick);
        _refocus = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
        _refocus.Tick += (_, _) => { _refocus.Stop(); Guard(Refocus); };
    }

    public string TargetTitle => _targetTitle;
    public DateTime StartedAt => _startedAt;

    /// <summary>Démarre une session (thread UI). En cas d'échec, tout est restauré et l'exception est relancée.</summary>
    public static GuidedSession Start(AppWindow target, GuidedOptions options)
    {
        if (Current is not null) throw new InvalidOperationException(L("Un accès guidé est déjà actif."));
        if (!GuidedNative.IsWindow(target.Handle)) throw new InvalidOperationException(L("La fenêtre choisie n'existe plus. Actualisez la liste."));
        var session = new GuidedSession(target, options);
        Current = session;
        try
        {
            session.Begin();
        }
        catch
        {
            session.End(showSummary: false);
            throw;
        }
        StateChanged?.Invoke(null, EventArgs.Empty);
        return session;
    }

    private unsafe void Begin()
    {
        Log.Info("GuidedAccess", "début de session");
        _background = AppHost.Background.Acquire(L("Accès guidé actif"));
        KeyboardGuard.Install(_options, () => _dispatcher.InvokeAsync(() => Guard(() => ShowOverlay(OverlayMode.Exit))));
        if (_options.HideTaskbar) TaskbarGuard.Hide();
        if (_options.KeepForeground)
        {
            _foregroundHook = GuidedNative.SetWinEventHook(GuidedNative.EVENT_SYSTEM_FOREGROUND, GuidedNative.EVENT_SYSTEM_FOREGROUND, 0,
                &OnWinEvent, 0, 0, GuidedNative.WINEVENT_OUTOFCONTEXT);
            _minimizeHook = GuidedNative.SetWinEventHook(GuidedNative.EVENT_SYSTEM_MINIMIZESTART, GuidedNative.EVENT_SYSTEM_MINIMIZESTART, 0,
                &OnWinEvent, 0, 0, GuidedNative.WINEVENT_OUTOFCONTEXT);
            if (_foregroundHook == 0) Log.Warn("GuidedAccess", "garde du premier plan indisponible");
        }
        // Masquage de Timonier après le traitement de l'événement « arrière-plan » (priorité inférieure) : la fenêtre est
        // encore visible à ce moment-là, donc l'icône de notification (et son menu « Quitter ») n'apparaît pas.
        _dispatcher.InvokeAsync(() =>
        {
            if (!_ended) Application.Current?.MainWindow?.Hide();
        }, DispatcherPriority.Background);
        if (_options.Maximize && !GuidedNative.IsZoomed(_target)) GuidedNative.ShowWindow(_target, GuidedNative.SW_MAXIMIZE);
        BringToFront(_target);
        _tick.Start();
    }

    /// <summary>Termine la session et restaure tout. Idempotent.</summary>
    public void End(bool showSummary = true)
    {
        if (_ended) return;
        _ended = true;
        _tick.Stop();
        _refocus.Stop();
        try { KeyboardGuard.Uninstall(); } catch (Exception ex) { Log.Error("GuidedAccess", "retrait du filtre clavier", ex); }
        if (_foregroundHook != 0) { GuidedNative.UnhookWinEvent(_foregroundHook); _foregroundHook = 0; }
        if (_minimizeHook != 0) { GuidedNative.UnhookWinEvent(_minimizeHook); _minimizeHook = 0; }
        try { TaskbarGuard.Restore(); } catch (Exception ex) { Log.Error("GuidedAccess", "restauration de la barre des tâches", ex); }
        try { _overlay?.ForceClose(); } catch { /* déjà fermée */ }
        _overlay = null;
        if (ReferenceEquals(Current, this)) Current = null;

        // La fenêtre principale doit réapparaître AVANT de libérer le jeton d'arrière-plan (sinon l'application se fermerait).
        try { (Application.Current as App)?.ShowMainWindow(); } catch (Exception ex) { Log.Error("GuidedAccess", "réaffichage de Timonier", ex); }
        _background?.Dispose();
        _background = null;

        var duration = DateTime.UtcNow - _startedAt;
        Log.Info("GuidedAccess", "fin de session (" + FormatDuration(duration) + ")");
        if (showSummary)
            AppHost.Toasts?.Show(L("Accès guidé terminé après {0} sur « {1} ».", FormatDuration(duration), _targetTitle), ToastKind.Success);
        StateChanged?.Invoke(null, EventArgs.Empty);
    }

    public void Dispose() => End(showSummary: false);

    // ================================================================== Garde du premier plan

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0) return;   // OBJID_WINDOW uniquement
        var session = Current;
        if (session is null || session._ended) return;
        try
        {
            if (evt == GuidedNative.EVENT_SYSTEM_MINIMIZESTART) session.OnMinimized(hwnd);
            else session.OnForeground(hwnd);
        }
        catch (Exception ex) { Log.Error("GuidedAccess", "garde du premier plan", ex); }
    }

    private void OnMinimized(nint hwnd)
    {
        if (hwnd != _target) return;
        ScheduleRefocus();
    }

    private void OnForeground(nint hwnd)
    {
        if (IsAllowedForeground(hwnd)) return;
        if (hwnd != 0 && hwnd == MainWindowHandle()) Application.Current?.MainWindow?.Hide();
        ScheduleRefocus();
    }

    private bool IsAllowedForeground(nint hwnd)
    {
        if (hwnd == 0) return false;
        if (_overlay is not null) return hwnd == _overlay.Handle;
        var pid = GuidedNative.GetProcessId(hwnd);
        return pid == _targetPid && hwnd != 0;
    }

    /// <summary>Ramène la cible avec un délai minimal pour éviter une « bataille » de focus avec une autre application.</summary>
    private void ScheduleRefocus()
    {
        if (_refocus.IsEnabled) return;
        var now = Environment.TickCount64;
        if (now - _fightWindowStart > 10_000) { _fightWindowStart = now; _fights = 0; }
        _fights++;
        var delay = _fights > 15 ? 1500 : now - _lastRefocus < 300 ? 300 : 60;
        _refocus.Interval = TimeSpan.FromMilliseconds(delay);
        _refocus.Start();
    }

    private void Refocus()
    {
        if (_ended) return;
        _lastRefocus = Environment.TickCount64;
        if (_overlay is not null)
        {
            _overlay.Activate();
            BringToFront(_overlay.Handle);
            return;
        }
        if (!GuidedNative.IsWindow(_target)) return;   // géré par le minuteur (application fermée)
        if (GuidedNative.IsIconic(_target))
            GuidedNative.ShowWindow(_target, _options.Maximize ? GuidedNative.SW_MAXIMIZE : GuidedNative.SW_RESTORE);
        if (!IsAllowedForeground(GuidedNative.GetForegroundWindow())) BringToFront(_target);
    }

    private static void BringToFront(nint hwnd)
    {
        if (hwnd == 0) return;
        var fg = GuidedNative.GetForegroundWindow();
        if (fg == hwnd) return;
        var me = GuidedNative.GetCurrentThreadId();
        var fgThread = fg != 0 ? GuidedNative.GetWindowThreadProcessId(fg, out _) : 0;
        var attached = fgThread != 0 && fgThread != me && GuidedNative.AttachThreadInput(me, fgThread, true);
        try
        {
            GuidedNative.AllowSetForegroundWindow(GuidedNative.ASFW_ANY);
            GuidedNative.BringWindowToTop(hwnd);
            GuidedNative.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) GuidedNative.AttachThreadInput(me, fgThread, false);
        }
    }

    private static nint MainWindowHandle() =>
        Application.Current?.MainWindow is { } w ? new WindowInteropHelper(w).Handle : 0;

    // ================================================================== Minuteur (session active uniquement)

    private void OnTick()
    {
        if (_ended || _relaunching) return;
        if (_deadline is { } d && DateTime.UtcNow >= d && _overlay is null)
        {
            ShowOverlay(OverlayMode.TimeUp);
            return;
        }
        if (!GuidedNative.IsWindow(_target) || !GuidedNative.IsWindowVisible(_target))
        {
            var replacement = WindowCatalog.FindMainWindow(_targetPid);
            if (replacement != 0) _target = replacement;
            else if (_overlay is null) { ShowOverlay(OverlayMode.TargetClosed); return; }
        }
        // Explorateur redémarré pendant la session : sa nouvelle barre des tâches est visible, on la masque à nouveau.
        if (_options.HideTaskbar) TaskbarGuard.Hide();
        // Filet de sécurité : un changement de premier plan a pu échapper aux événements.
        if (_options.KeepForeground && !_refocus.IsEnabled && !IsAllowedForeground(GuidedNative.GetForegroundWindow())) ScheduleRefocus();
    }

    // ================================================================== Écrans de code

    private void ShowOverlay(OverlayMode mode)
    {
        if (_ended || _overlay is not null) return;
        var overlay = new GuidedOverlay(mode, _targetTitle, mode == OverlayMode.TargetClosed && _launchedPath is not null);
        overlay.Chosen += (_, choice) => Guard(() => OnOverlayChoice(choice));
        _overlay = overlay;
        overlay.Show();
        overlay.Activate();
        BringToFront(overlay.Handle);
    }

    private void CloseOverlay()
    {
        var o = _overlay;
        _overlay = null;
        o?.ForceClose();
    }

    private void OnOverlayChoice(OverlayChoice choice)
    {
        switch (choice)
        {
            case OverlayChoice.End:
                End();
                break;
            case OverlayChoice.Extend:
                _deadline = DateTime.UtcNow.AddMinutes(GuidedOverlay.ExtendMinutes);
                CloseOverlay();
                BringToFront(_target);
                AppHost.Toasts?.Show(LP(GuidedOverlay.ExtendMinutes, "Accès guidé prolongé de {0} minute.", "Accès guidé prolongé de {0} minutes."), ToastKind.Info);
                break;
            case OverlayChoice.Relaunch:
                _ = RelaunchAsync();
                break;
            default:
                CloseOverlay();
                BringToFront(_target);
                break;
        }
    }

    private async Task RelaunchAsync()
    {
        if (_launchedPath is null || _relaunching) return;
        _relaunching = true;
        try
        {
            CloseOverlay();
            var w = await AppLauncher.LaunchAsync(_launchedPath);
            if (_ended) return;
            _target = w.Handle;
            _targetPid = w.ProcessId;
            if (!string.IsNullOrWhiteSpace(w.Title)) _targetTitle = w.Title;
            if (_options.Maximize) GuidedNative.ShowWindow(_target, GuidedNative.SW_MAXIMIZE);
            BringToFront(_target);
        }
        catch (Exception ex)
        {
            Log.Error("GuidedAccess", "relance de l'application", ex);
        }
        finally
        {
            _relaunching = false;
        }
        if (!_ended && !GuidedNative.IsWindow(_target)) ShowOverlay(OverlayMode.TargetClosed);
    }

    /// <summary>Toute exception inattendue termine la session proprement (rien ne doit rester verrouillé).</summary>
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Log.Error("GuidedAccess", "erreur pendant la session : fin de l'accès guidé", ex);
            End(showSummary: false);
            AppHost.Toasts?.Show(L("L'accès guidé a été interrompu à cause d'une erreur : {0}", ex.Message), ToastKind.Error);
        }
    }

    public static string FormatDuration(TimeSpan d)
    {
        if (d.TotalMinutes < 1) return L("{0} s", Math.Max(1, (int)d.TotalSeconds));
        if (d.TotalHours < 1) return L("{0} min", (int)d.TotalMinutes);
        return L("{0} h {1:00}", (int)d.TotalHours, d.Minutes);
    }
}
