using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Localization;
using Timonier.Core.Platform;

namespace Timonier.Modules.Apps;

/// <summary>
/// Vue « Mises à jour » : liste indicative des mises à jour connues de winget (lecture de <c>winget upgrade</c>, sans
/// élévation), mise à jour application par application ou de tout d'un coup (<c>apps.winget.upgradeall</c>).
/// </summary>
internal sealed class UpdatesView : StackPanel
{
    private readonly AppsContext _ctx;
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly TextBlock _summary = AppsUi.Caption("");
    private readonly Button _scan;
    private readonly Button _upgradeAll;
    private readonly List<Button> _rowButtons = [];
    private bool _scanned;
    private bool _scanning;
    private DateTime? _lastScan;

    public UpdatesView(AppsContext ctx)
    {
        _ctx = ctx;
        _upgradeAll = AppsUi.Button(L("Update all"), "", "Pp.AccentButton", async (_, _) => await UpgradeAllAsync());
        _scan = AppsUi.Button(LC("updates", "Check for updates"), "", "Pp.Button", async (_, _) => await ScanAsync());
        _scan.Margin = new Thickness(8, 0, 0, 0);
        var buttons = AppsUi.Row(_upgradeAll, _scan);
        var bar = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        _summary.Margin = new Thickness(2, 0, 12, 0);
        bar.Children.Add(_summary);
        Children.Add(bar);

        var note = AppsUi.Caption(L("winget compares your software with the versions published in its sources (internet connection). Updates install silently and you accept the publishers' licenses; close the programs involved first."));
        note.Margin = new Thickness(2, 8, 0, 4);
        Children.Add(note);
        Children.Add(_host);
        ctx.Activity.BusyChanged += UpdateButtons;
    }

    public async Task ActivateAsync()
    {
        if (!_scanned && _ctx.WingetAvailable) await ScanAsync();
        else if (!_ctx.WingetAvailable) ShowUnavailable();
        UpdateButtons();
    }

    private void ShowUnavailable() =>
        _host.Content = AppsUi.EmptyState("", L("winget is unavailable"),
            L("Install “App Installer” from the Microsoft Store to check for and install updates."),
            "Pp.Warning");

    private async Task ScanAsync()
    {
        if (_scanning || !_ctx.WingetAvailable) return;
        _scanning = true;
        UpdateButtons();
        _summary.Text = L("Checking…");
        _host.Content = AppsUi.Loading(L("Querying winget sources… (a few seconds to a minute)"));
        try
        {
            var r = await Winget.RunAsync(["upgrade", "--accept-source-agreements", "--disable-interactivity"], TimeSpan.FromMinutes(3), null, CancellationToken.None);
            var items = Winget.ParseUpgradeTable(r.Output);
            _scanned = true;
            _lastScan = DateTime.Now;
            if (items.Count == 0 && !r.Success && unchecked((uint)r.ExitCode) != 0x8A150014 && r.Output.Trim().Length == 0)
            {
                _summary.Text = "";
                _host.Content = AppsUi.EmptyState("", L("Check failed"),
                    L("winget couldn't reach its sources ({0}). Check your internet connection and try again.", Winget.Describe(r.ExitCode, r.TimedOut)),
                    "Pp.Warning");
                return;
            }
            Show(items);
        }
        catch (Exception ex)
        {
            Log.Error("Apps", "recherche des mises à jour", ex);
            _summary.Text = "";
            _host.Content = AppsUi.EmptyState("", L("Check failed"), ex.Message, "Pp.Warning");
        }
        finally
        {
            _scanning = false;
            UpdateButtons();
        }
    }

    private void Show(List<UpgradeItem> items)
    {
        _rowButtons.Clear();
        var when = _lastScan is { } t ? " · " + L("checked at {0}", t.ToString("t", Loc.Culture)) : "";
        if (items.Count == 0)
        {
            _summary.Text = L("No updates") + when;
            _host.Content = AppsUi.EmptyState("", L("Everything is up to date"),
                L("winget doesn't know of a newer version of your software. Store apps are updated by the Store."),
                "Pp.Success");
            return;
        }
        _summary.Text = LP(items.Count, "{0} update available", "{0} updates available") + when;
        var list = new StackPanel();
        foreach (var item in items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)) list.Children.Add(Row(item));
        _host.Content = AppsUi.Card(list, new Thickness(12, 4, 12, 4));
    }

    private Border Row(UpgradeItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = AppsCatalog.Find(item.Id)?.Name ?? item.Name;
        var tile = AppsUi.LetterTile(name, 32);
        tile.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var title = AppsUi.Strong(name, 13.5);
        title.ToolTip = item.Name;
        text.Children.Add(title);
        text.Children.Add(AppsUi.Caption(item.Id + (item.Source.Length > 0 ? " · " + L("source {0}", item.Source) : "")));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var versions = AppsUi.Row(AppsUi.Badge(item.Version, "Pp.Neutral"), AppsUi.Icon("", 12, "Pp.TextTertiary"), AppsUi.Badge(item.Available, "Pp.Success"));
        ((FrameworkElement)versions.Children[1]).Margin = new Thickness(0, 0, 6, 0);
        versions.VerticalAlignment = VerticalAlignment.Center;
        versions.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(versions, 2);
        grid.Children.Add(versions);

        var button = AppsUi.Button(L("Update"), null, "Pp.Button", async (_, _) => await UpgradeAsync(item, name));
        button.VerticalAlignment = VerticalAlignment.Center;
        _rowButtons.Add(button);
        Grid.SetColumn(button, 3);
        grid.Children.Add(button);

        return new Border { Child = grid, Padding = new Thickness(4, 10, 4, 10), BorderThickness = new Thickness(0, 0, 0, 1) }
            .Themed(Border.BorderBrushProperty, "Pp.Divider");
    }

    private void UpdateButtons()
    {
        var busy = _ctx.Activity.IsBusy || _scanning;
        _scan.IsEnabled = !busy && _ctx.WingetAvailable;
        _upgradeAll.IsEnabled = !busy && _ctx.WingetAvailable;
        foreach (var b in _rowButtons) b.IsEnabled = !busy;
    }

    private async Task UpgradeAsync(UpgradeItem item, string name)
    {
        var outcome = await _ctx.Activity.RunAsync(L("Updating {0}", name), WingetUpgradeAction.ActionId,
            new Dictionary<string, string> { ["ids"] = item.Id });
        if (outcome is null) return;
        _ctx.NotifyInstalledChanged();
        await ScanAsync();
    }

    /// <summary>Met à jour tout ce que winget sait mettre à jour (après confirmation).</summary>
    public async Task UpgradeAllAsync()
    {
        if (_ctx.Activity.IsBusy || !_ctx.WingetAvailable) return;
        if (!await AppHost.Dialogs.ConfirmAsync(L("Update everything?"),
                L("winget will silently download and install all available updates, including ones not listed here (Store apps managed by winget).\n\nClose the programs involved: an open program can prevent its own update. This can take several minutes; you can cancel between two apps. Installing means you accept the publishers' licenses."),
                L("Update all"))) return;
        var outcome = await _ctx.Activity.RunAsync(L("Updating all apps"), WingetUpgradeAllAction.ActionId, []);
        if (outcome is null) return;
        _ctx.NotifyInstalledChanged();
        await ScanAsync();
    }
}
