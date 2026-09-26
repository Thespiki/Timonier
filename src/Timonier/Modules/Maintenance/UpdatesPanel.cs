using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Maintenance;

/// <summary>Section « Windows Update » : état (COM, sans droits), pause 1 à 5 semaines, heures d'activité.</summary>
internal sealed class UpdatesPanel
{
    public FrameworkElement Root { get; }
    public event Action<UpdateStatus>? StatusChanged;

    private readonly TextBlock _lastInstall = MaintUi.Text("…", "Pp.Body");
    private readonly TextBlock _lastSearch = MaintUi.Text("…", "Pp.Body");
    private readonly TextBlock _state = MaintUi.Text("…", "Pp.Body");
    private readonly TextBlock _hours = MaintUi.Text("…", "Pp.Body");
    private readonly Border _stateBadgeHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };

    private readonly ComboBox _weeks = new() { Width = 150 };
    private readonly Button _pause, _resume, _applyHours;
    private readonly ComboBox _start = new() { Width = 96 }, _end = new() { Width = 96 };
    private readonly TextBlock _hoursHint = MaintUi.Text("", "Pp.Caption");
    private readonly TextBlock _pauseHint = MaintUi.Text("", "Pp.Caption");
    private bool _busy;
    private UpdateStatus? _status;

    public UpdatesPanel()
    {
        var root = new StackPanel();

        // ---- État
        var status = new StackPanel();
        status.Children.Add(_stateBadgeHost);
        status.Children.Add(KeyValue(L("Last successful install"), _lastInstall));
        status.Children.Add(KeyValue(L("Last successful check"), _lastSearch));
        status.Children.Add(KeyValue(L("Status"), _state));
        status.Children.Add(KeyValue(L("Active hours"), _hours));
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var check = MaintUi.Button(L("Check for updates"), "", "Pp.AccentButton", (_, _) => MaintUi.OpenSettings("ms-settings:windowsupdate-action"));
        check.Margin = new Thickness(0, 0, 8, 8);
        var history = MaintUi.Button(L("Update history"), "", "Pp.Button", (_, _) => MaintUi.OpenSettings("ms-settings:windowsupdate-history"));
        history.Margin = new Thickness(0, 0, 8, 8);
        var refresh = MaintUi.Button(L("Refresh"), "", "Pp.SubtleButton", async (_, _) => await RefreshAsync());
        refresh.Margin = new Thickness(0, 0, 8, 8);
        actions.Children.Add(check);
        actions.Children.Add(history);
        actions.Children.Add(refresh);
        status.Children.Add(actions);
        root.Children.Add(MaintUi.Card(status, new Thickness(0, 0, 0, 12)));

        // ---- Pause + heures d'activité, côte à côte
        for (var w = 1; w <= 5; w++) _weeks.Items.Add(new ComboBoxItem { Content = LP(w, "{0} week", "{0} weeks"), Tag = w });
        _weeks.SelectedIndex = 0;
        _pause = MaintUi.Button(L("Pause"), "", "Pp.Button", async (_, _) => await PauseAsync());
        _resume = MaintUi.Button(L("Resume now"), "", "Pp.AccentButton", async (_, _) => await ResumeAsync());
        var pauseRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        _weeks.Margin = new Thickness(0, 0, 8, 8);
        _pause.Margin = new Thickness(0, 0, 8, 8);
        _resume.Margin = new Thickness(0, 0, 8, 8);
        pauseRow.Children.Add(_weeks);
        pauseRow.Children.Add(_pause);
        pauseRow.Children.Add(_resume);
        var pauseCard = new StackPanel();
        pauseCard.Children.Add(CardHeader("", L("Pause updates")));
        pauseCard.Children.Add(MaintUi.Text(L("Postpones the installation of all updates, just like in Settings. They resume automatically when the pause ends, and Windows installs them before allowing another pause."), "Pp.Caption"));
        pauseCard.Children.Add(pauseRow);
        pauseCard.Children.Add(_pauseHint);

        for (var h = 0; h < 24; h++)
        {
            _start.Items.Add(new ComboBoxItem { Content = L("{0} h", h), Tag = h });
            _end.Items.Add(new ComboBoxItem { Content = L("{0} h", h), Tag = h });
        }
        _start.SelectedIndex = 8;
        _end.SelectedIndex = 17;
        _start.SelectionChanged += (_, _) => ValidateHours();
        _end.SelectionChanged += (_, _) => ValidateHours();
        _applyHours = MaintUi.Button(L("Apply"), "", "Pp.Button", async (_, _) => await ApplyHoursAsync());
        var hoursRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var from = MaintUi.Text(LC("hours range", "From"), "Pp.Body", wrap: false);
        from.VerticalAlignment = VerticalAlignment.Center;
        from.Margin = new Thickness(0, 0, 8, 8);
        var to = MaintUi.Text(LC("hours range", "to"), "Pp.Body", wrap: false);
        to.VerticalAlignment = VerticalAlignment.Center;
        to.Margin = new Thickness(0, 0, 8, 8);
        _start.Margin = new Thickness(0, 0, 8, 8);
        _end.Margin = new Thickness(0, 0, 8, 8);
        _applyHours.Margin = new Thickness(0, 0, 8, 8);
        hoursRow.Children.Add(from);
        hoursRow.Children.Add(_start);
        hoursRow.Children.Add(to);
        hoursRow.Children.Add(_end);
        hoursRow.Children.Add(_applyHours);
        var hoursCard = new StackPanel();
        hoursCard.Children.Add(CardHeader("", L("Active hours")));
        hoursCard.Children.Add(MaintUi.Text(L("Windows won't restart automatically to install updates during this range (18 hours at most). Overrides the automatic setting."), "Pp.Caption"));
        hoursCard.Children.Add(hoursRow);
        hoursCard.Children.Add(_hoursHint);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = MaintUi.Card(pauseCard, new Thickness(0, 0, 6, 0));
        var right = MaintUi.Card(hoursCard, new Thickness(6, 0, 0, 0));
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        root.Children.Add(grid);

        Root = root;
        ValidateHours();
    }

    private static FrameworkElement CardHeader(string glyph, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var icon = MaintUi.Icon(glyph, 16, "Pp.AccentText");
        icon.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(icon);
        row.Children.Add(MaintUi.Text(title, "Pp.CardTitle", wrap: false));
        return row;
    }

    private static Grid KeyValue(string key, TextBlock value)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var k = MaintUi.Text(key, "Pp.Caption", wrap: false);
        k.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 1);
        g.Children.Add(k);
        g.Children.Add(value);
        return g;
    }

    private static string When(DateTime? d) => d is { } v ? $"{Format.Date(v)} ({Format.Ago(v)})" : LC("feminine", "Unknown");

    public async Task RefreshAsync()
    {
        UpdateStatus s;
        try { s = await Task.Run(UpdateStatus.Read); }
        catch (Exception ex)
        {
            Log.Error("Maintenance", "état Windows Update", ex);
            _state.Text = L("Couldn't read the Windows Update status.");
            return;
        }
        _status = s;
        _lastInstall.Text = s.ComFailed ? L("Unavailable (Windows Update agent unreachable)") : When(s.LastInstall);
        _lastSearch.Text = s.ComFailed ? L("Unavailable") : When(s.LastSearch);
        var parts = new List<string>();
        if (s.IsPaused) parts.Add(L("Updates paused until {0}", Format.Day(s.PausedUntil!.Value)));
        if (s.RebootPending) parts.Add(L("Restart required to finish installing"));
        if (s.ManagedByPolicy) parts.Add(L("Automatic updates turned off by a policy"));
        _state.Text = parts.Count == 0 ? L("No action needed") : string.Join(" · ", parts);
        _hours.Text = s.SmartActiveHours || s.ActiveStart is null || s.ActiveEnd is null
            ? L("Set automatically by Windows based on your activity")
            : L("From {0}:00 to {1}:00", s.ActiveStart, s.ActiveEnd);
        if (!s.SmartActiveHours && s.ActiveStart is { } a && s.ActiveEnd is { } b && a is >= 0 and < 24 && b is >= 0 and < 24)
        {
            _start.SelectedIndex = a;
            _end.SelectedIndex = b;
        }

        var h = s.ToHealth();
        var (text, fg, bg, glyph) = s.IsPaused
            ? (L("Paused"), "Pp.Warning", "Pp.WarningBackground", "")
            : h.Status switch
            {
                Core.Catalog.HealthStatus.Critical => (L("Updates overdue"), "Pp.Danger", "Pp.DangerBackground", ""),
                Core.Catalog.HealthStatus.Warning => (L("Needs review"), "Pp.Warning", "Pp.WarningBackground", ""),
                Core.Catalog.HealthStatus.Unknown => (L("Unknown state"), "Pp.TextSecondary", "Pp.CardSecondary", ""),
                _ => s.RebootPending ? (L("Restart required"), "Pp.Warning", "Pp.WarningBackground", "") : (L("Windows is up to date"), "Pp.Success", "Pp.SuccessBackground", ""),
            };
        _stateBadgeHost.Child = MaintUi.Badge(text, fg, bg, glyph);

        _resume.Visibility = s.IsPaused ? Visibility.Visible : Visibility.Collapsed;
        if (_pause.Content is StackPanel { Children.Count: 2 } sp && sp.Children[1] is TextBlock label)
            label.Text = s.IsPaused ? L("Extend pause") : L("Pause");
        _pauseHint.Text = s.PauseBlocked ? L("Pausing updates is blocked by an organization policy.") : "";
        _pauseHint.Margin = new Thickness(0, s.PauseBlocked ? 2 : 0, 0, 0);
        SetBusy(_busy);
        StatusChanged?.Invoke(s);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _pause.IsEnabled = !busy && _status?.PauseBlocked != true;
        _resume.IsEnabled = !busy;
        _weeks.IsEnabled = !busy;
        _applyHours.IsEnabled = !busy && HoursValid(out _);
    }

    private bool HoursValid(out string message)
    {
        if (_start.SelectedItem is not ComboBoxItem { Tag: int s } || _end.SelectedItem is not ComboBoxItem { Tag: int e })
        {
            message = "";
            return false;
        }
        var span = ActiveHoursAction.Span(s, e);
        if (span == 0) { message = L("Start and end must be different."); return false; }
        if (span > 18) { message = LP(span, "Range of {0} hour: 18 hours maximum.", "Range of {0} hours: 18 hours maximum."); return false; }
        message = LP(span, "Range of {0} hour.", "Range of {0} hours.");
        return true;
    }

    private void ValidateHours()
    {
        var ok = HoursValid(out var message);
        _hoursHint.Text = message;
        _hoursHint.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Pp.TextSecondary" : "Pp.Danger");
        _hoursHint.Margin = new Thickness(0, 2, 0, 0);
        _applyHours.IsEnabled = !_busy && ok;
    }

    private async Task PauseAsync()
    {
        if (_busy || _weeks.SelectedItem is not ComboBoxItem { Tag: int weeks }) return;
        SetBusy(true);
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(UpdatesPauseAction.ActionId,
                new Dictionary<string, string> { ["weeks"] = weeks.ToString(CultureInfo.InvariantCulture) });
            AppHost.Toasts.ShowOutcome(outcome);
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private async Task ResumeAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            AppHost.Toasts.ShowOutcome(await AppHost.Engine.RunActionAsync(UpdatesResumeAction.ActionId));
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private async Task ApplyHoursAsync()
    {
        if (_busy || _start.SelectedItem is not ComboBoxItem { Tag: int s } || _end.SelectedItem is not ComboBoxItem { Tag: int e }) return;
        SetBusy(true);
        try
        {
            AppHost.Toasts.ShowOutcome(await AppHost.Engine.RunActionAsync(ActiveHoursAction.ActionId, new Dictionary<string, string>
            {
                ["start"] = s.ToString(CultureInfo.InvariantCulture),
                ["end"] = e.ToString(CultureInfo.InvariantCulture),
            }));
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }
}
