using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Apps;

/// <summary>
/// Vue « Préinstallées » : applications du Store installées pour ce compte, rapprochées d'une table d'applications
/// supprimables (explication, risque, recommandation). Suppression pour l'utilisateur sans droits administrateur,
/// option « tous les comptes » (administrateur), historique avec réinstallation depuis le Microsoft Store.
/// </summary>
internal sealed class BloatView : StackPanel
{
    private readonly AppsContext _ctx;
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly List<BloatRow> _rows = [];
    private readonly CheckBox _allUsers;
    private readonly Button _removeSelected;
    private readonly Button _refresh;
    private bool _loaded;
    private bool _loading;

    public BloatView(AppsContext ctx)
    {
        _ctx = ctx;
        Children.Add(AppsUi.InfoBar(
            L("These apps are removed for your account only, without administrator rights, and can be reinstalled from the Microsoft Store. Essential Windows components (Store, App Installer, security, user interface…) are protected and don't appear here. A major Windows update may reinstall some apps."), ""));

        _removeSelected = AppsUi.Button(L("Remove selected"), "", "Pp.AccentButton", async (_, _) => await RemoveSelectedAsync());
        _allUsers = AppsUi.CheckBox(L("Also for all accounts"));
        _allUsers.Content = L("Also for all accounts and future accounts (administrator)");
        _allUsers.Margin = new Thickness(16, 0, 0, 0);
        _allUsers.Padding = new Thickness(8, 0, 0, 0);
        _allUsers.VerticalContentAlignment = VerticalAlignment.Center;
        _allUsers.ToolTip = L("Also removes the app for other accounts and removes it from the Windows image, so it's no longer installed when an account is created.");
        _refresh = AppsUi.Button(L("Refresh"), "", "Pp.SubtleButton", async (_, _) => await LoadAsync());
        var bar = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(_refresh, Dock.Right);
        bar.Children.Add(_refresh);
        bar.Children.Add(AppsUi.Row(_removeSelected, _allUsers));
        Children.Add(bar);
        Children.Add(_host);
        ctx.Activity.BusyChanged += UpdateButtons;
    }

    public async Task ActivateAsync()
    {
        if (!_loaded) await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        _refresh.IsEnabled = false;
        if (!_loaded) _host.Content = AppsUi.Loading(L("Scanning Store apps…"));
        try
        {
            var apps = await Task.Run(AppxService.ListCurrentUser);
            BuildContent(apps);
            _loaded = true;
        }
        catch (Exception ex)
        {
            Log.Error("Apps", "liste des applications du Store", ex);
            _host.Content = AppsUi.EmptyState("", L("List unavailable"), L("Couldn't list Store apps: {0}", ex.Message), "Pp.Warning");
        }
        finally
        {
            _loading = false;
            _refresh.IsEnabled = true;
            UpdateButtons();
        }
    }

    private void BuildContent(List<AppxInfo> apps)
    {
        _rows.Clear();
        var root = new StackPanel();

        // 1. Applications préinstallées connues.
        var known = apps.Where(a => a.Bloat is not null && !a.IsSystemSigned)
            .OrderBy(a => a.Bloat!.Risk).ThenByDescending(a => a.Bloat!.Recommended).ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        root.Children.Add(AppsUi.SectionHeader(L("Preinstalled apps found"), "", out var knownCounter));
        knownCounter.Text = known.Count.ToString();
        if (known.Count == 0)
        {
            root.Children.Add(AppsUi.EmptyState("", L("Nothing to clean up"),
                L("None of the known unnecessary preinstalled apps are present for your account."), "Pp.Success"));
        }
        else
        {
            var list = new StackPanel();
            foreach (var a in known)
            {
                var row = new BloatRow(a, this, selectable: true);
                _rows.Add(row);
                list.Children.Add(row);
            }
            root.Children.Add(AppsUi.Card(list, new Thickness(12, 4, 12, 4)));
        }

        // 2. Autres applications du Store (non système, non protégées).
        var others = apps.Where(a => a.Bloat is null && !a.IsSystemSigned && !a.IsProtected)
            .OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (others.Count > 0)
        {
            var list = new StackPanel();
            foreach (var a in others) list.Children.Add(new BloatRow(a, this, selectable: false));
            var expander = new Expander
            {
                Header = L("Other Store apps installed for your account ({0})", others.Count),
                Content = AppsUi.Card(list, new Thickness(12, 4, 12, 4)),
                Margin = new Thickness(0, 18, 0, 0),
            };
            root.Children.Add(expander);
        }

        // 3. Historique des suppressions.
        var removed = AppsContext.LoadRemoved().Where(r => apps.All(a => a.FamilyName != r.Family)).ToList();
        if (removed.Count > 0)
        {
            root.Children.Add(AppsUi.SectionHeader(L("Removed with Timonier"), "", out var removedCounter));
            removedCounter.Text = removed.Count.ToString();
            var list = new StackPanel();
            foreach (var r in removed) list.Children.Add(RemovedRow(r));
            root.Children.Add(AppsUi.Card(list, new Thickness(12, 4, 12, 4)));
        }

        _host.Content = root;
    }

    private Border RemovedRow(RemovedApp r)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = AppsUi.IconTile("", 32, "Pp.NeutralBackground", "Pp.Neutral");
        tile.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(tile);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(AppsUi.Strong(r.Name, 13.5));
        text.Children.Add(AppsUi.Caption(L("Removed on {0} · {1}", Format.Day(r.Date), BloatCatalog.NameOf(r.Family))));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var reinstall = AppsUi.Button(L("Reinstall from the Store"), "", "Pp.Button", (_, _) =>
        {
            if (!BloatCatalog.IsValidFamilyName(r.Family)) return;
            try { ProcessRunner.OpenSettingsUri("ms-windows-store://pdp/?PFN=" + r.Family); }
            catch (Exception ex) { AppHost.Toasts.Show(L("Couldn't open the Microsoft Store: {0}", ex.Message), ToastKind.Error); }
        });
        Border? row = null;
        var forget = AppsUi.Button(LC("from list", "Remove"), null, "Pp.SubtleButton", (_, _) =>
        {
            AppsContext.ForgetRemoved(r.Family);
            if (row?.Parent is Panel panel) panel.Children.Remove(row);
        });
        forget.ToolTip = L("Remove from this history");
        forget.Margin = new Thickness(6, 0, 0, 0);
        var buttons = AppsUi.Row(reinstall, forget);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        row = new Border { Child = grid, Padding = new Thickness(4, 10, 4, 10), BorderThickness = new Thickness(0, 0, 0, 1) }
            .Themed(Border.BorderBrushProperty, "Pp.Divider");
        return row;
    }

    private void UpdateButtons()
    {
        var n = _rows.Count(r => r.IsSelected);
        AppsUi.SetButtonText(_removeSelected, n == 0 ? L("Remove selected") : L("Remove selected ({0})", n));
        _removeSelected.IsEnabled = n > 0 && !_ctx.Activity.IsBusy;
        _allUsers.IsEnabled = !_ctx.Activity.IsBusy;
        foreach (var r in _rows) r.SetBusy(_ctx.Activity.IsBusy);
    }

    private Task RemoveSelectedAsync() => RemoveAsync([.. _rows.Where(r => r.IsSelected).Select(r => r.App)]);

    private async Task RemoveAsync(List<AppxInfo> apps)
    {
        if (apps.Count == 0 || _ctx.Activity.IsBusy) return;
        var allUsers = _allUsers.IsChecked == true && apps.All(a => a.Bloat is not null);

        var content = new StackPanel();
        content.Children.Add(AppsUi.Caption(allUsers
            ? L("These apps will be removed for all accounts on this PC and removed from the Windows image:")
            : L("These apps will be removed for your account:")));
        var items = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        foreach (var a in apps)
        {
            var name = AppsUi.Text(a.Title);
            name.FontSize = 13;
            name.FontWeight = FontWeights.SemiBold;
            var row = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
            row.Children.Add(name);
            if (a.Bloat?.Warning is { } w)
            {
                var warn = AppsUi.Caption(w);
                warn.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Warning");
                row.Children.Add(warn);
            }
            items.Children.Add(row);
        }
        content.Children.Add(new ScrollViewer { Content = items, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(AppsUi.InfoBar(L("You can reinstall them from the Microsoft Store (history at the bottom of this page)."), ""));
        var danger = apps.Any(a => a.Bloat?.Risk == BloatRisk.Moderate || a.Bloat is null);
        if (!await AppHost.Dialogs.ShowAsync(apps.Count == 1 ? L("Remove this app?") : LP(apps.Count, "Remove {0} app?", "Remove {0} apps?"),
                content, L("Remove"), L("Undo"), danger)) return;

        var title = allUsers
            ? (apps.Count == 1 ? L("Removing {0} (all accounts)", apps[0].Title)
                : LP(apps.Count, "Removing {0} app (all accounts)", "Removing {0} apps (all accounts)"))
            : (apps.Count == 1 ? L("Removing {0}", apps[0].Title)
                : LP(apps.Count, "Removing {0} app", "Removing {0} apps"));
        var outcome = allUsers
            ? await _ctx.Activity.RunAsync(title, AppxDeprovisionAction.ActionId,
                new Dictionary<string, string> { ["family"] = string.Join(",", apps.Select(a => a.FamilyName).Distinct()) })
            : await _ctx.Activity.RunAsync(title, AppxRemoveAction.ActionId,
                new Dictionary<string, string> { ["fullName"] = string.Join(",", apps.Select(a => a.FullName)) });
        if (outcome?.Data?.TryGetValue("removed", out var removed) == true)
        {
            foreach (var item in removed.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = item.Split('|', 2);
                if (parts.Length == 2 && BloatCatalog.IsValidFamilyName(parts[0])) AppsContext.RecordRemoved(parts[0], parts[1]);
            }
        }
        if (outcome is not null) await LoadAsync();
    }

    // ================================================================== Ligne d'application

    private sealed class BloatRow : Border
    {
        private readonly CheckBox? _check;
        private readonly Button _remove;
        public AppxInfo App { get; }
        public bool IsSelected => _check?.IsChecked == true;

        public BloatRow(AppxInfo app, BloatView owner, bool selectable)
        {
            App = app;
            Padding = new Thickness(4, 10, 4, 10);
            BorderThickness = new Thickness(0, 0, 0, 1);
            this.Themed(BorderBrushProperty, "Pp.Divider");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement lead;
            if (selectable)
            {
                _check = AppsUi.CheckBox(L("Select {0}", app.Title));
                _check.IsChecked = app.Bloat?.Recommended == true;
                _check.VerticalAlignment = VerticalAlignment.Top;
                _check.Margin = new Thickness(0, 2, 12, 0);
                _check.Checked += (_, _) => owner.UpdateButtons();
                _check.Unchecked += (_, _) => owner.UpdateButtons();
                lead = _check;
            }
            else
            {
                lead = AppsUi.LetterTile(app.Title, 28);
                lead.Margin = new Thickness(0, 0, 12, 0);
            }
            grid.Children.Add(lead);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var top = new WrapPanel();
            var name = AppsUi.Strong(app.Title, 13.5);
            name.Margin = new Thickness(0, 0, 8, 0);
            top.Children.Add(name);
            if (app.Bloat is { } b)
            {
                top.Children.Add(b.Risk == BloatRisk.Safe ? AppsUi.Badge(L("Safe"), "Pp.Success") : AppsUi.Badge(L("Consider"), "Pp.Warning"));
                if (b.Recommended) top.Children.Add(AppsUi.Badge(L("Recommended"), "Pp.Accent"));
            }
            text.Children.Add(top);
            var desc = AppsUi.Caption(app.Bloat?.Description ?? (app.Publisher.Length > 0 ? app.Publisher : L("Store app")));
            desc.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(desc);
            if (app.Bloat?.Warning is { } w)
            {
                var warn = AppsUi.Caption("⚠ " + w);
                warn.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Warning");
                warn.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(warn);
            }
            var tech = AppsUi.Caption(L("{0} · version {1}", app.Name, app.Version));
            tech.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextTertiary");
            tech.FontSize = 11;
            tech.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(tech);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            _remove = AppsUi.Button(L("Remove"), "", "Pp.Button", async (_, _) => await owner.RemoveAsync([app]));
            _remove.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_remove, 2);
            grid.Children.Add(_remove);
            Child = grid;
        }

        public void SetBusy(bool busy)
        {
            _remove.IsEnabled = !busy;
            if (_check is not null) _check.IsEnabled = !busy;
        }
    }
}
