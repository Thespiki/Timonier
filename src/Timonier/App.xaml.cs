using System.Windows;
using System.Windows.Threading;
using Timonier.Core.Platform;
using Timonier.UI.Services;
using Timonier.UI.Shell;
using Timonier.UI.Theme;

namespace Timonier;

public partial class App : Application
{
    private TrayIcon? _tray;
    private EventWaitHandle? _activation;
    private bool _exiting;

    /// <summary>Lancé avec --background (démarrage avec Windows) : pas de fenêtre, uniquement l'icône de notification.</summary>
    public bool StartInBackground { get; init; }

    /// <summary>Mode capture d'écran (développement).</summary>
    public CaptureRequest? Capture { get; init; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("App", "tâche non observée", ex.Exception); ex.SetObserved(); };

        if (Capture is not null)
        {
            Core.Settings.SettingsStore.ReadOnly = true;
            Core.Settings.SettingsStore.Current.AppPinHash = null; // la capture ne doit pas afficher l'écran de verrouillage
            if (Capture.Theme is { } theme)
                Core.Settings.SettingsStore.Current.Theme = theme == "dark" ? Core.Settings.ThemePreference.Dark : Core.Settings.ThemePreference.Light;
            if (Capture.Language is { Length: > 0 } language)
                Core.Settings.SettingsStore.Current.Language = language;
        }

        // La langue doit être fixée avant la création du moindre texte (modules, fenêtres).
        Core.Localization.Loc.Initialize(Core.Settings.SettingsStore.Current.Language);
        Core.Search.Synonyms.AddLocalizedIntentWords(Core.Localization.Loc.SearchWords);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(Core.Localization.Loc.Culture.IetfLanguageTag)));

        AppHost.Initialize();
        ThemeManager.Initialize();

        var window = new MainWindow();
        MainWindow = window;
        if (Capture is not null)
        {
            _ = CaptureRenderer.RunAsync(this, window, Capture);
            return;
        }
        _tray = new TrayIcon(ShowMainWindow, () => _ = ExitAsync());
        AppHost.Background.Changed += (_, _) => Dispatcher.InvokeAsync(UpdateTray);
        ListenForActivation();

        if (StartInBackground) _tray.Show(AppHost.Background.Reasons);
        else window.Show();

        // Tâches de démarrage déclarées par les modules (réparations d'un arrêt brutal…), jamais en mode capture.
        foreach (var (id, run) in AppHost.Registry.UiStartupTasks)
        {
            try { run(); }
            catch (Exception ex) { Log.Warn("App", $"tâche de démarrage {id} : {ex.Message}"); }
        }
        Log.Info("App", $"démarrage : {AppHost.Registry.Tweaks.Count} réglages, {AppHost.Registry.Actions.Count} actions, {AppHost.Registry.Pages.Count} pages");
    }

    /// <summary>Appelé par la fenêtre à sa fermeture : reste en arrière-plan seulement si une fonction active l'exige.</summary>
    public bool ShouldStayInBackground() =>
        !_exiting && AppHost.Background.IsNeeded && AppHost.Settings.AllowBackground;

    public void OnMainWindowHidden()
    {
        _tray?.Show(AppHost.Background.Reasons);
        _tray?.Notify(L("Timonier is still running"), string.Join(", ", AppHost.Background.Reasons));
    }

    private void UpdateTray()
    {
        if (MainWindow is { IsVisible: false })
        {
            if (AppHost.Background.IsNeeded) _tray?.Show(AppHost.Background.Reasons);
            else _ = ExitAsync(); // plus aucune raison de tourner : on libère la mémoire
        }
    }

    public void ShowMainWindow()
    {
        if (MainWindow is null) return;
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
        _tray?.Hide();
    }

    /// <summary>Relance Timonier (ex. après un changement de langue) : la nouvelle instance attend la fin de celle-ci.</summary>
    public async Task RestartAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExecutablePath) { UseShellExecute = false };
            psi.ArgumentList.Add("--restart");
            psi.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            System.Diagnostics.Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("App", "redémarrage", ex);
            AppHost.Toasts?.Show(L("Couldn't restart Timonier: {0}", ex.Message), ToastKind.Error);
            return;
        }
        await ExitAsync();
    }

    public async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        await AppHost.ShutdownAsync();
        _tray?.Dispose();
        _activation?.Dispose();
        Shutdown();
    }

    private void ListenForActivation()
    {
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ActivationEventName);
        var thread = new Thread(() =>
        {
            try
            {
                while (_activation.WaitOne())
                {
                    if (_exiting) return;
                    Dispatcher.InvokeAsync(ShowMainWindow);
                }
            }
            catch (ObjectDisposedException) { }
        })
        { IsBackground = true, Name = "Timonier.Activation" };
        thread.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("App", "exception non gérée", e.Exception);
        e.Handled = true;
        AppHost.Toasts?.Show(L("Unexpected error: {0}", e.Exception.Message), ToastKind.Error);
    }
}
