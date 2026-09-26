using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Devices;

/// <summary>
/// Batterie : charge en direct (rafraîchie toutes les 30 s, uniquement pendant l'affichage de la page), usure calculée
/// à partir de la capacité d'origine et de la capacité actuelle, cycles de charge (rapport powercfg, sans élévation).
/// </summary>
internal sealed class BatteryPanel : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly TextBlock _charge = new TextBlock().Styled("Pp.Metric");
    private readonly TextBlock _chargeState = DevUi.Caption("");
    private readonly TextBlock _chargeIcon = DevUi.Icon("", 18, "Pp.AccentText");
    private readonly TextBlock _wear = new TextBlock { Text = "…" }.Styled("Pp.Metric");
    private readonly TextBlock _wearText = DevUi.Caption(L("Calculating…"));
    private readonly TextBlock _cycles = new TextBlock { Text = "…" }.Styled("Pp.Metric");
    private readonly TextBlock _cyclesText = DevUi.Caption(L("Reading the Windows report…"));
    private readonly Border _healthTrack = new() { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 14, 0, 4) };
    private readonly Border _healthFill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly TextBlock _healthLegend = DevUi.Caption("");
    private readonly TextBlock _details = DevUi.Caption("");
    private readonly Button _reportButton;
    private double _healthRatio = -1;
    private bool _reportLoaded;

    /// <summary>Résumé pour la tuile d'aperçu.</summary>
    public event Action<string, string?>? SummaryChanged;

    public BatteryPanel()
    {
        Focusable = false;
        var metrics = new Grid();
        for (var i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.Children.Add(Metric(0, LC("battery", "Charge"), _charge, _chargeState, _chargeIcon));
        metrics.Children.Add(Metric(1, L("Wear"), _wear, _wearText, DevUi.Icon("", 18, "Pp.AccentText")));
        metrics.Children.Add(Metric(2, L("Charge cycles"), _cycles, _cyclesText, DevUi.Icon("", 18, "Pp.AccentText")));

        _healthTrack.SetResourceReference(Border.BackgroundProperty, "Pp.ControlFill");
        _healthTrack.Child = _healthFill;
        _healthTrack.SizeChanged += (_, _) => LayoutHealthBar();

        _reportButton = DevUi.Button(L("Detailed report"), "", "Pp.Button", async (_, _) => await OpenReportAsync());
        var saver = DevUi.Button(L("Battery saver"), "", "Pp.SubtleButton", (_, _) => Open("ms-settings:batterysaver"));
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _reportButton.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(_reportButton);
        buttons.Children.Add(saver);

        var stack = new StackPanel();
        stack.Children.Add(metrics);
        stack.Children.Add(_healthTrack);
        stack.Children.Add(_healthLegend);
        stack.Children.Add(_details);
        stack.Children.Add(buttons);
        _details.Margin = new Thickness(0, 6, 0, 0);
        Content = DevUi.Card(stack, new Thickness(18, 16, 18, 14));

        _timer.Tick += (_, _) => UpdateLive();
    }

    private static FrameworkElement Metric(int column, string label, TextBlock value, TextBlock caption, TextBlock icon)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        icon.Margin = new Thickness(0, 0, 8, 0);
        head.Children.Add(icon);
        head.Children.Add(DevUi.Caption(label));
        var s = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        s.Children.Add(head);
        s.Children.Add(value);
        s.Children.Add(caption);
        Grid.SetColumn(s, column);
        return s;
    }

    public void Start()
    {
        UpdateLive();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void UpdateLive()
    {
        var now = BatteryService.Now();
        _charge.Text = now.Percent is { } p ? Format.Percent(p / 100.0) : "—";
        _chargeIcon.Text = now.Charging ? "" : "";
        _chargeState.Text = (now.OnAc, now.Charging) switch
        {
            (true, true) => L("Plugged in, charging"),
            (true, false) => now.Percent >= 95 ? L("Plugged in, fully charged") : L("Plugged in, charging paused"),
            _ => now.Remaining is { } r ? L("On battery · about {0} left", Format.Duration(r)) : L("On battery"),
        } + (now.Saver ? " · " + LC("short form", "battery saver on") : "");
    }

    /// <summary>Capacités (WinRT, rapide) puis rapport powercfg (cycles, fabricant) en arrière-plan.</summary>
    public async Task LoadAsync()
    {
        UpdateLive();
        var caps = await Task.Run(BatteryService.Capacities);
        ApplyCapacities(caps?.DesignMwh ?? 0, caps?.FullMwh ?? 0);
        if (_reportLoaded) return;
        _reportLoaded = true;
        var packs = await BatteryService.LoadReportAsync();
        if (packs is null || packs.Count == 0)
        {
            _cycles.Text = "—";
            _cyclesText.Text = L("Windows report unavailable");
            return;
        }
        var cycles = packs.Select(b => b.Cycles ?? 0).Where(c => c > 0).Sum();
        _cycles.Text = cycles > 0 ? cycles.ToString(Culture) : "—";
        _cyclesText.Text = cycles > 0 ? L("Full cycles since manufacture") : L("Not reported by the battery");
        // Le rapport donne les valeurs par batterie : plus précis que l'agrégat WinRT quand celui-ci est vide.
        if (caps is null || caps.Value.DesignMwh <= 0)
            ApplyCapacities(packs.Sum(b => b.DesignMwh), packs.Sum(b => b.FullMwh));
        var info = packs.Select(b => string.Join(" · ", new[] { packs.Count > 1 ? b.Name : null, b.Manufacturer is { } m ? L("Manufacturer: {0}", m) : null, b.Chemistry }
            .Where(x => !string.IsNullOrEmpty(x)))).Where(x => x.Length > 0);
        _details.Text = string.Join("\n", info);
        _details.Visibility = _details.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyCapacities(long design, long full)
    {
        if (design <= 0 || full <= 0)
        {
            _wear.Text = "—";
            _wearText.Text = L("Capacity not reported by the firmware");
            _healthRatio = -1;
            _healthTrack.Visibility = Visibility.Collapsed;
            _healthLegend.Text = "";
            SummaryChanged?.Invoke(L("Health unknown"), null);
            return;
        }
        var wear = Math.Clamp(1 - (double)full / design, 0, 1);
        var tone = BatteryService.WearBrush(wear);
        _wear.Text = Format.Percent(wear);
        _wear.SetResourceReference(TextBlock.ForegroundProperty, tone);
        _wearText.Text = wear > 0.40 ? L("Heavy wear: battery life significantly reduced")
                       : wear > 0.20 ? L("Noticeable wear, normal after a few years")
                       : full >= design ? L("No wear measured (values reported by the firmware)")
                       : L("Battery in good condition");
        _healthRatio = Math.Clamp((double)full / design, 0, 1);
        _healthFill.SetResourceReference(Border.BackgroundProperty, tone);
        _healthTrack.Visibility = Visibility.Visible;
        _healthLegend.Text = L("Current capacity: {0:0.0} Wh out of {1:0.0} Wh originally ({2}).", full / 1000.0, design / 1000.0, Format.Percent(_healthRatio));
        LayoutHealthBar();
        SummaryChanged?.Invoke(L("{0} wear", Format.Percent(wear)), tone);
    }

    private void LayoutHealthBar()
    {
        if (_healthRatio >= 0) _healthFill.Width = Math.Max(0, _healthTrack.ActualWidth * _healthRatio);
    }

    private async Task OpenReportAsync()
    {
        _reportButton.IsEnabled = false;
        var (ok, message) = await BatteryService.OpenHtmlReportAsync();
        _reportButton.IsEnabled = true;
        AppHost.Toasts.Show(message, ok ? ToastKind.Info : ToastKind.Warning);
    }

    private static void Open(string uri)
    {
        try { ProcessRunner.OpenSettingsUri(uri); }
        catch (Exception ex) { Log.Warn("Devices", "ouverture des Paramètres : " + ex.Message); }
    }
}
