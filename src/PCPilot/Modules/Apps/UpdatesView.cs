using System.Windows;
using System.Windows.Controls;
using PcPilot.Core.Platform;

namespace PcPilot.Modules.Apps;

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
        _upgradeAll = AppsUi.Button("Tout mettre à jour", "", "Pp.AccentButton", async (_, _) => await UpgradeAllAsync());
        _scan = AppsUi.Button("Rechercher", "", "Pp.Button", async (_, _) => await ScanAsync());
        _scan.Margin = new Thickness(8, 0, 0, 0);
        var buttons = AppsUi.Row(_upgradeAll, _scan);
        var bar = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        _summary.Margin = new Thickness(2, 0, 12, 0);
        bar.Children.Add(_summary);
        Children.Add(bar);

        var note = AppsUi.Caption("winget compare vos logiciels aux versions publiées dans ses sources (connexion Internet). Les mises à jour " +
                                  "s'installent en silencieux avec l'accord des licences des éditeurs ; fermez les programmes concernés avant.");
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
        _host.Content = AppsUi.EmptyState("", "winget est indisponible",
            "Installez le « Programme d'installation d'application » depuis le Microsoft Store pour rechercher et installer les mises à jour.",
            "Pp.Warning");

    private async Task ScanAsync()
    {
        if (_scanning || !_ctx.WingetAvailable) return;
        _scanning = true;
        UpdateButtons();
        _summary.Text = "Recherche en cours…";
        _host.Content = AppsUi.Loading("Interrogation des sources winget… (quelques secondes à une minute)");
        try
        {
            var r = await Winget.RunAsync(["upgrade", "--accept-source-agreements", "--disable-interactivity"], TimeSpan.FromMinutes(3), null, CancellationToken.None);
            var items = Winget.ParseUpgradeTable(r.Output);
            _scanned = true;
            _lastScan = DateTime.Now;
            if (items.Count == 0 && !r.Success && unchecked((uint)r.ExitCode) != 0x8A150014 && r.Output.Trim().Length == 0)
            {
                _summary.Text = "";
                _host.Content = AppsUi.EmptyState("", "Recherche impossible",
                    "winget n'a pas pu consulter ses sources (" + Winget.Describe(r.ExitCode, r.TimedOut) + "). Vérifiez la connexion Internet puis réessayez.",
                    "Pp.Warning");
                return;
            }
            Show(items);
        }
        catch (Exception ex)
        {
            Log.Error("Apps", "recherche des mises à jour", ex);
            _summary.Text = "";
            _host.Content = AppsUi.EmptyState("", "Recherche impossible", ex.Message, "Pp.Warning");
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
        var when = _lastScan is { } t ? $" · vérifié à {t:HH:mm}" : "";
        if (items.Count == 0)
        {
            _summary.Text = "Aucune mise à jour" + when;
            _host.Content = AppsUi.EmptyState("", "Tout est à jour",
                "winget ne connaît pas de version plus récente pour vos logiciels. Les applications du Store se mettent à jour par le Store.",
                "Pp.Success");
            return;
        }
        _summary.Text = AppsContext.Plural(items.Count, "mise à jour disponible", "mises à jour disponibles") + when;
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
        text.Children.Add(AppsUi.Caption(item.Id + (item.Source.Length > 0 ? " · source " + item.Source : "")));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var versions = AppsUi.Row(AppsUi.Badge(item.Version, "Pp.Neutral"), AppsUi.Icon("", 12, "Pp.TextTertiary"), AppsUi.Badge(item.Available, "Pp.Success"));
        ((FrameworkElement)versions.Children[1]).Margin = new Thickness(0, 0, 6, 0);
        versions.VerticalAlignment = VerticalAlignment.Center;
        versions.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(versions, 2);
        grid.Children.Add(versions);

        var button = AppsUi.Button("Mettre à jour", null, "Pp.Button", async (_, _) => await UpgradeAsync(item, name));
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
        var outcome = await _ctx.Activity.RunAsync($"Mise à jour de {name}", WingetUpgradeAction.ActionId,
            new Dictionary<string, string> { ["ids"] = item.Id });
        if (outcome is null) return;
        _ctx.NotifyInstalledChanged();
        await ScanAsync();
    }

    /// <summary>Met à jour tout ce que winget sait mettre à jour (après confirmation).</summary>
    public async Task UpgradeAllAsync()
    {
        if (_ctx.Activity.IsBusy || !_ctx.WingetAvailable) return;
        if (!await AppHost.Dialogs.ConfirmAsync("Tout mettre à jour ?",
                "winget va télécharger et installer en silencieux toutes les mises à jour disponibles, y compris celles non listées ici " +
                "(applications du Store gérées par winget).\n\nFermez les programmes concernés : un programme ouvert peut empêcher sa mise à jour. " +
                "L'opération peut durer plusieurs minutes ; vous pouvez l'annuler entre deux applications. Installer vaut acceptation des licences des éditeurs.",
                "Tout mettre à jour")) return;
        var outcome = await _ctx.Activity.RunAsync("Mise à jour de toutes les applications", WingetUpgradeAllAction.ActionId, []);
        if (outcome is null) return;
        _ctx.NotifyInstalledChanged();
        await ScanAsync();
    }
}
