using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Maintenance;

/// <summary>Section « Points de restauration » : état de la protection, création, liste (admin), activation.</summary>
internal sealed class RestorePanel
{
    public FrameworkElement Root { get; }

    private readonly TextBlock _statusTitle = MaintUi.Text("", "Pp.CardTitle");
    private readonly TextBlock _statusDetail = MaintUi.Text("", "Pp.Caption");
    private readonly Border _statusTile;
    private readonly TextBlock _statusIcon;
    private readonly Button _create, _list, _enable;
    private readonly ProgressBar _busyBar = MaintUi.BusyBar(100);
    private readonly TextBlock _busyText = MaintUi.Text("", "Pp.Caption");
    private readonly StackPanel _listHost = new() { Visibility = Visibility.Collapsed };
    private bool _busy;

    public RestorePanel()
    {
        _statusIcon = new TextBlock { FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Icon");
        _statusTile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Child = _statusIcon, VerticalAlignment = VerticalAlignment.Top };

        var statusTexts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        statusTexts.Children.Add(_statusTitle);
        _statusDetail.Margin = new Thickness(0, 3, 0, 0);
        statusTexts.Children.Add(_statusDetail);
        var status = new DockPanel();
        DockPanel.SetDock(_statusTile, Dock.Left);
        status.Children.Add(_statusTile);
        status.Children.Add(statusTexts);

        _create = MaintUi.Button(L("Create a restore point"), "", "Pp.AccentButton", async (_, _) => await CreateAsync());
        _list = MaintUi.Button(L("Show existing restore points"), "", "Pp.Button", async (_, _) => await LoadListAsync());
        _enable = MaintUi.Button(L("Turn on protection"), "", "Pp.Button", async (_, _) => await EnableAsync());
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in new[] { _create, _list, _enable })
        {
            b.Margin = new Thickness(0, 0, 8, 8);
            buttons.Children.Add(b);
        }
        var busyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        // La ligne de progression n'occupe de place que pendant une opération.
        busyRow.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(UIElement.Visibility)) { Source = _busyBar });
        busyRow.Children.Add(_busyBar);
        _busyText.Margin = new Thickness(10, 0, 0, 0);
        busyRow.Children.Add(_busyText);

        var tools = new WrapPanel();
        tools.Children.Add(MaintUi.Button(L("Open System Restore"), "", "Pp.LinkButton", (_, _) => MaintUi.OpenTool(SystemTool.Rstrui)));
        tools.Children.Add(MaintUi.Button(L("System protection settings"), "", "Pp.LinkButton", (_, _) => MaintUi.OpenTool(SystemTool.SystemPropertiesProtection)));
        foreach (FrameworkElement link in tools.Children) link.Margin = new Thickness(0, 0, 20, 0);

        var body = new StackPanel();
        body.Children.Add(status);
        body.Children.Add(buttons);
        body.Children.Add(busyRow);
        body.Children.Add(_listHost);
        body.Children.Add(MaintUi.Divider(new Thickness(0, 8, 0, 8)));
        var limit = MaintUi.Text(L("Windows creates at most one restore point every 24 hours: if a recent one already exists, the request is ignored (the existing restore point remains usable). Listing and creating restore points require administrator permission."), "Pp.Caption");
        body.Children.Add(limit);
        body.Children.Add(tools);

        Root = MaintUi.Card(body);
    }

    /// <summary>Lecture instantanée du registre (sans droits).</summary>
    public void RefreshStatus()
    {
        var policy = RestoreStatus.DisabledByPolicy;
        var enabled = RestoreStatus.ProtectionEnabled;
        (string glyph, string fg, string bg, string title, string detail) = (policy, enabled) switch
        {
            (true, _) => ("", "Pp.Danger", "Pp.DangerBackground", L("System Restore turned off by your organization"),
                L("A policy prevents restore points from being created on this PC.")),
            (_, true) => ("", "Pp.Success", "Pp.SuccessBackground", L("System protection on"),
                L("Windows also creates restore points automatically before installing updates or drivers.")),
            (_, false) => ("", "Pp.Warning", "Pp.WarningBackground", L("System protection off"),
                L("No restore points can be created: turn on protection for the system drive.")),
            _ => ("", "Pp.Info", "Pp.InfoBackground", L("System protection status unknown"),
                L("Show existing restore points or open the protection settings to check.")),
        };
        _statusIcon.Text = glyph;
        _statusIcon.SetResourceReference(TextBlock.ForegroundProperty, fg);
        _statusTile.SetResourceReference(Border.BackgroundProperty, bg);
        _statusTitle.Text = title;
        _statusDetail.Text = detail;
        _enable.Visibility = !policy && enabled != true ? Visibility.Visible : Visibility.Collapsed;
        _create.IsEnabled = !_busy && !policy;
    }

    private void SetBusy(bool busy, string text = "")
    {
        _busy = busy;
        _create.IsEnabled = _list.IsEnabled = _enable.IsEnabled = !busy;
        _busyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _busyText.Text = text;
        if (!busy) RefreshStatus();
    }

    private async Task CreateAsync()
    {
        if (_busy) return;
        var description = await AppHost.Dialogs.PromptAsync(L("Create a restore point"),
            L("Give this restore point a name so you can recognize it later (64 characters max.)."), "Timonier",
            validate: RestorePointCreateAction.CheckDescription);
        if (description is null) return;
        SetBusy(true, L("Creating the restore point…"));
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(RestorePointCreateAction.ActionId,
                new Dictionary<string, string> { ["description"] = description.Trim() }, new Progress<string>(s => _busyText.Text = s));
            AppHost.Toasts.ShowOutcome(outcome);
            if (outcome.Success && _listHost.Visibility == Visibility.Visible)
            {
                SetBusy(false);
                await LoadListAsync();
            }
        }
        finally
        {
            if (_busy) SetBusy(false);
        }
    }

    private async Task EnableAsync()
    {
        if (_busy) return;
        SetBusy(true, L("Turning on system protection…"));
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(RestoreEnableAction.ActionId, null, new Progress<string>(s => _busyText.Text = s));
            AppHost.Toasts.ShowOutcome(outcome);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadListAsync()
    {
        if (_busy) return;
        SetBusy(true, L("Reading restore points…"));
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(RestorePointListAction.ActionId);
            _listHost.Children.Clear();
            _listHost.Visibility = Visibility.Visible;
            if (!outcome.Success || outcome.Data is not { } d)
            {
                _listHost.Children.Add(MaintUi.Text(outcome.Cancelled ? L("List not shown: administrator permission denied.") : L("Couldn't read the restore points: {0}", outcome.Message), "Pp.Caption"));
                return;
            }
            var count = int.TryParse(d.GetValueOrDefault("count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0;
            var title = MaintUi.Text(count == 0 ? L("No restore points") : LP(count, "{0} restore point", "{0} restore points"), "Pp.CardTitle");
            title.Margin = new Thickness(0, 6, 0, 6);
            _listHost.Children.Add(title);
            if (count == 0)
            {
                _listHost.Children.Add(MaintUi.Text(L("Create one now so you can go back if there's a problem."), "Pp.Caption"));
                return;
            }
            for (var i = 0; i < count; i++)
            {
                var date = DateTime.TryParse(d.GetValueOrDefault($"{i}.date"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : (DateTime?)null;
                var type = int.TryParse(d.GetValueOrDefault($"{i}.type"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 0;
                _listHost.Children.Add(BuildPointRow(d.GetValueOrDefault($"{i}.desc") ?? "", date, type, i == 0));
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static FrameworkElement BuildPointRow(string description, DateTime? date, int type, bool newest)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var when = MaintUi.Text(date is { } d ? Format.Date(d.ToLocalTime()) : L("Unknown date"), "Pp.Body", wrap: false);
        var desc = MaintUi.Text(description.Length == 0 ? L("(no description)") : description, "Pp.Body");
        desc.TextTrimming = TextTrimming.CharacterEllipsis;
        desc.TextWrapping = TextWrapping.NoWrap;
        desc.ToolTip = description;
        Grid.SetColumn(desc, 1);
        var badge = newest
            ? MaintUi.Badge(L("Most recent"), "Pp.AccentText", "Pp.AccentSubtle")
            : MaintUi.Badge(RestorePoints.TypeLabel(type));
        badge.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(badge, 2);
        grid.Children.Add(when);
        grid.Children.Add(desc);
        grid.Children.Add(badge);
        var row = new Border { Child = grid, Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 4) };
        row.SetResourceReference(Border.BackgroundProperty, "Pp.CardSecondary");
        return row;
    }
}
