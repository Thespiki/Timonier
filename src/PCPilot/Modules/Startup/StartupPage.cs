using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PcPilot.Core.Engine;
using PcPilot.UI.Controls;
using PcPilot.UI.Services;

namespace PcPilot.Modules.Startup;

internal interface IStartupPanel
{
    bool IsDataLoaded { get; }
    event EventHandler<string>? CountChanged;
    Task EnsureLoadedAsync();
    Task ReloadAsync();
}

/// <summary>
/// Page « Démarrage et services » : sélecteur segmenté entre applications au démarrage, services et tâches planifiées.
/// Chaque onglet est chargé à sa première ouverture, hors du thread UI ; aucun rafraîchissement en arrière-plan.
/// </summary>
public sealed class StartupPage : UserControl, INavigationAware
{
    private static int _localActions;

    private readonly SegmentedBar _tabs = new();
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly IStartupPanel?[] _panels = new IStartupPanel?[3];
    private string? _pendingTweak;

    public StartupPage()
    {
        var stack = PageScaffold.Create(this, "Démarrage et services",
            "Choisissez ce qui se lance avec Windows : applications à l'ouverture de session, services et tâches planifiées.",
            StartupModule.Glyph);
        _tabs.Add("Applications au démarrage", StartupModule.AppsGlyph);
        _tabs.Add("Services", StartupModule.ServicesGlyph);
        _tabs.Add("Tâches planifiées", StartupModule.TasksGlyph);
        _tabs.Margin = new Thickness(0, 0, 0, 16);
        _tabs.SelectionChanged += (_, i) => _ = ShowAsync(i);
        stack.Children.Add(_tabs);
        stack.Children.Add(_host);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Exécute une action du module en signalant qu'elle vient de cette page (pas de rechargement complet en retour).</summary>
    internal static async Task<ApplyOutcome> RunAsync(string actionId, Dictionary<string, string> parameters)
    {
        Interlocked.Increment(ref _localActions);
        try { return await AppHost.Engine.RunActionAsync(actionId, parameters); }
        finally { Interlocked.Decrement(ref _localActions); }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppHost.Engine.Changed += OnEngineChanged;
        if (_tabs.SelectedIndex < 0) _tabs.Select(0);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => AppHost.Engine.Changed -= OnEngineChanged;

    /// <summary>Annulation depuis le journal ou la notification : on relit l'onglet concerné s'il est déjà chargé.</summary>
    private void OnEngineChanged(object? sender, TweakChangedEventArgs e)
    {
        if (Volatile.Read(ref _localActions) > 0 || !e.SourceId.StartsWith("startup.", StringComparison.Ordinal)) return;
        var index = e.SourceId.StartsWith("startup.service.", StringComparison.Ordinal) ? 1
                  : e.SourceId.StartsWith("startup.task.", StringComparison.Ordinal) ? 2
                  : e.SourceId.StartsWith("startup.item.", StringComparison.Ordinal) ? 0 : -1;
        if (index < 0) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (_panels[index] is { IsDataLoaded: true } p) _ = p.ReloadAsync();
        }, DispatcherPriority.Background);
    }

    public void OnNavigatedTo(object? parameter)
    {
        switch (parameter as string)
        {
            case "section:services": _tabs.Select(1); break;
            case "section:tasks": _tabs.Select(2); break;
            case "section:apps": _tabs.Select(0); break;
            case { } s when s.StartsWith("tweak:", StringComparison.Ordinal):
                _pendingTweak = s[6..];
                if (_tabs.SelectedIndex != 0) _tabs.Select(0);
                else _ = ShowAsync(0);
                break;
        }
    }

    private async Task ShowAsync(int index)
    {
        var panel = _panels[index] ??= Create(index);
        _host.Content = panel;
        try
        {
            await panel.EnsureLoadedAsync();
            if (index == 0 && _pendingTweak is { } id && panel is AppsPanel apps)
            {
                _pendingTweak = null;
                await Dispatcher.InvokeAsync(() => apps.Highlight(id), DispatcherPriority.Loaded);
            }
        }
        catch (Exception ex)
        {
            Core.Platform.Log.Error("Startup", "chargement de l'onglet " + index, ex);
        }
    }

    private IStartupPanel Create(int index)
    {
        IStartupPanel panel = index switch
        {
            1 => new ServicesPanel(),
            2 => new TasksPanel(),
            _ => new AppsPanel(),
        };
        panel.CountChanged += (_, count) => _tabs.SetCount(index, count);
        return panel;
    }
}
