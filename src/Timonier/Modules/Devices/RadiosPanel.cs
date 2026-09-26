using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;
using Timonier.UI.Services;
using Windows.Devices.Radios;

namespace Timonier.Modules.Devices;

/// <summary>
/// Interrupteurs Wi-Fi / Bluetooth / réseau mobile en direct (WinRT). Abonnement aux changements d'état uniquement
/// pendant que la page est affichée ; repli sur les Paramètres Windows si l'API n'est pas utilisable.
/// </summary>
internal sealed class RadiosPanel : UserControl
{
    private readonly StackPanel _rows = new();
    private readonly List<(Radio Radio, CheckBox Switch, TextBlock State)> _items = [];
    private bool _attached, _updating;

    /// <summary>Résumé pour la tuile d'aperçu (ex. « Wi-Fi activé · Bluetooth coupé »).</summary>
    public event Action<string, string?>? SummaryChanged;

    public RadiosPanel()
    {
        Focusable = false;
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var airplane = DevUi.Button(L("Airplane mode…"), "", "Pp.SubtleButton", (_, _) => Open("ms-settings:network-airplanemode"));
        DockPanel.SetDock(airplane, Dock.Right);
        footer.Children.Add(airplane);
        footer.Children.Add(new TextBlock
        {
            Text = L("Same effect as the buttons in the notification center: immediate change, for your session."),
            VerticalAlignment = VerticalAlignment.Center,
        }.Styled("Pp.Caption"));

        var stack = new StackPanel();
        stack.Children.Add(_rows);
        stack.Children.Add(DevUi.Divider(new Thickness(0, 6, 0, 0)));
        stack.Children.Add(footer);
        Content = DevUi.Card(stack);

        _rows.Children.Add(Loading());
    }

    private static UIElement Loading()
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        p.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 80, Height = 3, VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(new TextBlock { Text = L("Reading radios…"), Margin = new Thickness(12, 0, 0, 0) }.Styled("Pp.Caption"));
        return p;
    }

    public async Task LoadAsync()
    {
        var snap = await RadioService.LoadAsync();
        Detach();
        _items.Clear();
        _rows.Children.Clear();

        if (snap.Error is not null || snap.Radios.Count == 0)
        {
            var message = snap.Error is not null
                ? L("Timonier can't read the state of the radios on this PC. Use Windows Settings.")
                : L("No Wi-Fi, Bluetooth or cellular radio was detected (or they're disabled in Device Manager).");
            var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var open = DevUi.Button(L("Open Settings"), "", "Pp.Button", (_, _) => Open("ms-settings:network-airplanemode"));
            DockPanel.SetDock(open, Dock.Right);
            dock.Children.Add(open);
            dock.Children.Add(new TextBlock { Text = message, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }.Styled("Pp.Body"));
            _rows.Children.Add(dock);
            SummaryChanged?.Invoke(snap.Error is not null ? L("State unavailable") : L("No radios"), null);
            return;
        }

        if (snap.Access != RadioAccessStatus.Allowed)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = L("Windows doesn't allow Timonier to change the state of the radios (“Radios” setting on the Privacy page): the switches show the state, but changes will go through Settings."),
                Margin = new Thickness(0, 2, 0, 8),
            }.Styled("Pp.Caption"));
        }

        var first = true;
        foreach (var radio in snap.Radios)
        {
            if (!first) _rows.Children.Add(DevUi.Divider(new Thickness(0, 4, 0, 4)));
            first = false;
            _rows.Children.Add(BuildRow(radio, snap.Access == RadioAccessStatus.Allowed));
        }
        Attach();
        UpdateSummary();
    }

    private FrameworkElement BuildRow(Radio radio, bool canControl)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = DevUi.IconTile(RadioService.KindGlyph(radio.Kind), 34);
        tile.Margin = new Thickness(0, 0, 14, 0);
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = RadioService.KindLabel(radio.Kind);
        text.Children.Add(DevUi.Text(title));
        var state = DevUi.Caption("");
        text.Children.Add(state);
        if (!string.IsNullOrWhiteSpace(radio.Name) && !radio.Name.Equals(title, StringComparison.OrdinalIgnoreCase))
            state.ToolTip = radio.Name;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var sw = new CheckBox { Content = "", IsChecked = radio.State == RadioState.On }.Styled("Pp.ToggleSwitch");
        System.Windows.Automation.AutomationProperties.SetName(sw, title);
        sw.IsEnabled = radio.State != RadioState.Disabled;
        sw.Click += async (_, _) => await OnToggledAsync(radio, sw, canControl);
        Grid.SetColumn(sw, 2);
        grid.Children.Add(sw);

        _items.Add((radio, sw, state));
        UpdateRow(radio, sw, state);
        return grid;
    }

    private async Task OnToggledAsync(Radio radio, CheckBox sw, bool canControl)
    {
        if (_updating) return;
        var want = sw.IsChecked == true;
        if (!canControl)
        {
            sw.IsChecked = !want;
            Open(RadioService.SettingsUri(radio.Kind));
            return;
        }
        sw.IsEnabled = false;
        var (ok, message) = await RadioService.SetAsync(radio, want);
        sw.IsEnabled = true;
        if (!ok)
        {
            AppHost.Toasts.Show(message, ToastKind.Warning, LC("Windows Settings app", "Settings"), () => Open(RadioService.SettingsUri(radio.Kind)));
        }
        Refresh();
    }

    private void UpdateRow(Radio radio, CheckBox sw, TextBlock state)
    {
        _updating = true;
        sw.IsChecked = radio.State == RadioState.On;
        sw.IsEnabled = radio.State != RadioState.Disabled;
        _updating = false;
        state.Text = radio.State switch
        {
            RadioState.On => L("On"),
            RadioState.Off => L("Off"),
            RadioState.Disabled => L("Adapter disabled (hardware airplane mode or Device Manager)"),
            _ => L("Unknown state"),
        };
    }

    private void Refresh()
    {
        foreach (var (radio, sw, state) in _items) UpdateRow(radio, sw, state);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var parts = _items.Select(i => i.Radio.State == RadioState.On ? L("{0} on", RadioService.KindLabel(i.Radio.Kind)) : L("{0} off", RadioService.KindLabel(i.Radio.Kind))).ToList();
        SummaryChanged?.Invoke(parts.FirstOrDefault() ?? L("No radios"), parts.Count > 1 ? string.Join(" · ", parts.Skip(1)) : null);
    }

    // ------------------------------------------------------------------ Abonnements (page visible uniquement)

    public void Attach()
    {
        if (_attached) return;
        foreach (var (radio, _, _) in _items) radio.StateChanged += OnStateChanged;
        _attached = true;
    }

    public void Detach()
    {
        if (!_attached) return;
        foreach (var (radio, _, _) in _items) radio.StateChanged -= OnStateChanged;
        _attached = false;
    }

    private void OnStateChanged(Radio sender, object args) => Dispatcher.InvokeAsync(Refresh);

    private static void Open(string uri)
    {
        try { ProcessRunner.OpenSettingsUri(uri); }
        catch (Exception ex) { Log.Warn("Devices", "ouverture des Paramètres : " + ex.Message); }
    }
}
