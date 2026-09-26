using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using Timonier.UI.Services;
using static Timonier.Modules.Startup.StartupUi;

namespace Timonier.Modules.Startup;

/// <summary>Onglet « Applications au démarrage » : Run, Run32, dossiers Démarrage, applications du Store.</summary>
internal sealed class AppsPanel : StackPanel, IStartupPanel
{
    private readonly StackPanel _summary = new();
    private readonly SegmentedBar _filter = new(compact: true);
    private readonly TextBox _search;
    private readonly Button _refresh;
    private readonly StackPanel _list = new();
    private readonly StackPanel _readOnly = new();
    private TweakListView? _tweaks;
    private List<StartupItem> _items = [];
    private string _query = "";
    private bool _loading;

    public bool IsDataLoaded { get; private set; }

    public AppsPanel()
    {
        Children.Add(_summary);

        _search = SearchBox(L("Search for an app or publisher"), q => { _query = q; Render(); });
        _filter.Add(LC("feminine plural", "All"));
        _filter.Add(L("Enabled"));
        _filter.Add(LC("feminine plural", "Disabled"));
        _filter.Select(0, notify: false);
        _filter.SelectionChanged += (_, _) => Render();
        _refresh = Button(L("Refresh"), "", "Pp.Button", async (_, _) => await ReloadAsync());
        var settings = Button(L("Windows Settings"), "", "Pp.SubtleButton", (_, _) => OpenSettings());
        Children.Add(Toolbar(_search, _filter, _refresh, settings));

        Children.Add(_list);
        Children.Add(_readOnly);
    }

    public event EventHandler<string>? CountChanged;

    public async Task EnsureLoadedAsync()
    {
        if (IsDataLoaded) return;
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (_loading) return;
        _loading = true;
        _refresh.IsEnabled = false;
        if (!IsDataLoaded)
        {
            _list.Children.Clear();
            _list.Children.Add(StateCard("", L("Looking for startup apps…"), busy: true));
        }
        try
        {
            _items = await Task.Run(() => StartupInventory.Load(details: true));
            IsDataLoaded = true;
            Render();
            if (_tweaks is null)
            {
                _tweaks = new TweakListView(StartupModule.Category, showRecommendations: false) { Margin = new Thickness(0, 16, 0, 0) };
                Children.Add(_tweaks);
                Children.Add(Footnote());
            }
        }
        catch (Exception ex)
        {
            Log.Error("Startup", "énumération des applications au démarrage", ex);
            _list.Children.Clear();
            _list.Children.Add(StateCard("", L("Couldn't read startup apps"), ex.Message));
        }
        finally
        {
            _loading = false;
            _refresh.IsEnabled = true;
        }
    }

    public void Highlight(string tweakId) => _tweaks?.Highlight(tweakId);

    private static Border Footnote() => PageScaffold.InfoBar(
        L("Disabling an app here is the same as doing it in Task Manager: the entry stays in place and can be enabled again at any time. The change takes effect at the next sign-in. An app can also start through a service or a scheduled task: see the corresponding tabs."), "");

    private void Render()
    {
        var toggleable = _items.Where(i => !i.IsReadOnly).ToList();
        RenderSummary(toggleable);

        IEnumerable<StartupItem> view = toggleable;
        view = _filter.SelectedIndex switch
        {
            1 => view.Where(i => i.Enabled),
            2 => view.Where(i => !i.Enabled),
            _ => view,
        };
        if (_query.Length > 0)
            view = view.Where(i => Matches(i, _query));
        var list = view.OrderByDescending(i => i.Enabled).ThenBy(i => i.DisplayName, StringComparer.Create(Culture, true)).ToList();

        _list.Children.Clear();
        if (list.Count == 0)
        {
            _list.Children.Add(toggleable.Count == 0
                ? StateCard("", L("No apps run at startup"), L("Your sign-in is as light as it can be."))
                : StateCard("", L("No results"), L("No apps match the search or the selected filter.")));
        }
        else
        {
            foreach (var item in list) _list.Children.Add(Row(item));
        }

        _readOnly.Children.Clear();
        var ro = _items.Where(i => i.IsReadOnly && (_query.Length == 0 || Matches(i, _query))).ToList();
        if (ro.Count > 0)
        {
            var title = PageScaffold.Section(L("Read-only entries"));
            title.Margin = new Thickness(0, 22, 0, 4);
            _readOnly.Children.Add(title);
            var cap = Caption(L("RunOnce: commands that run only once at the next sign-in (often the end of an installation). Policy: programs enforced by a Group Policy. They can't be disabled from Task Manager."));
            cap.Margin = new Thickness(0, 0, 0, 8);
            _readOnly.Children.Add(cap);
            foreach (var item in ro) _readOnly.Children.Add(Row(item));
        }
    }

    private static bool Matches(StartupItem i, string q) =>
        i.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
        || i.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
        || (i.Publisher?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || i.Command.Contains(q, StringComparison.CurrentCultureIgnoreCase);

    private void RenderSummary(List<StartupItem> toggleable)
    {
        var enabled = toggleable.Count(i => i.Enabled);
        var disabled = toggleable.Count - enabled;
        var missing = toggleable.Count(i => i.FileMissing);
        CountChanged?.Invoke(this, enabled.ToString(Culture));

        var profile = AppHost.Profile;
        var low = profile?.Tier == PerformanceTier.Low;
        var threshold = low ? 8 : 12;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddMetric(grid, 0, enabled.ToString(Culture), LP(enabled, "enabled", "enabled"), enabled > threshold ? "Pp.Warning" : "Pp.AccentText");
        AddMetric(grid, 1, disabled.ToString(Culture), LP(disabled, "disabled", "disabled"), "Pp.TextSecondary");
        AddMetric(grid, 2, missing.ToString(Culture), LP(missing, "missing", "missing"), missing > 0 ? "Pp.Warning" : "Pp.TextSecondary");

        string advice;
        if (enabled > threshold)
            advice = low
                ? L("That's a lot for this entry-level PC: every app that runs at startup delays sign-in and takes up memory. Keep the essentials (antivirus, audio or touch drivers, sync you need).")
                : L("That's a lot: every app that runs at startup delays sign-in and takes up memory. Keep the essentials (antivirus, audio or touch drivers, sync you need).");
        else if (enabled == 0)
            advice = L("No apps run at sign-in.");
        else
            advice = low
                ? L("On an entry-level PC, every app removed from startup makes a difference: disable what you don't need right at sign-in.")
                : L("Reasonable number. Disable what you don't need right at sign-in to save a few seconds.");
        if (missing > 0)
            advice += " " + LP(missing, "{0} entry points to a deleted file: you can remove it.", "{0} entries point to a deleted file: you can remove them.");
        var text = Caption(advice);
        text.FontSize = 13;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(text, 3);
        grid.Children.Add(text);

        _summary.Children.Clear();
        _summary.Children.Add(new Border { Child = grid, Padding = new Thickness(20, 14, 20, 14) }.Styled("Pp.Card"));
    }

    private static void AddMetric(Grid grid, int column, string value, string label, string brush)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 28, 0), MinWidth = 70 };
        var v = new TextBlock { Text = value }.Styled("Pp.Metric");
        v.SetResourceReference(TextBlock.ForegroundProperty, brush);
        stack.Children.Add(v);
        stack.Children.Add(Caption(label));
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }

    private FrameworkElement Row(StartupItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement tile = item.Icon is { } icon
            ? ImageTile(icon)
            : GlyphTile(item.Kind == StartupKind.Packaged ? "" : item.Kind == StartupKind.Folder ? "" : StartupModule.AppsGlyph);
        tile.Margin = new Thickness(0, 0, 14, 0);
        if (!item.Enabled) tile.Opacity = 0.55;
        grid.Children.Add(tile);

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleLine = new WrapPanel();
        var title = new TextBlock { Text = item.DisplayName, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 2), TextTrimming = TextTrimming.CharacterEllipsis }
            .Styled("Pp.Body");
        titleLine.Children.Add(title);
        if (item.Scope == StartupScope.Machine) titleLine.Children.Add(Badge(L("All users"), "Neutral", ""));
        if (!item.Enabled && !item.IsReadOnly) titleLine.Children.Add(Badge(L("Disabled")));
        if (item.FileMissing) titleLine.Children.Add(Badge(L("File not found"), "Warning", ""));
        if (item.IsPolicyLocked) titleLine.Children.Add(Badge(item.Enabled ? L("Enforced by your organization") : L("Blocked by your organization"), "Info", ""));
        body.Children.Add(titleLine);

        var meta = new List<string> { item.Publisher is { Length: > 0 } pub ? pub : L("Unknown publisher"), item.SourceLabel };
        if (!item.Enabled && item.DisabledSince is { } since) meta.Add(L("disabled on {0}", Format.Day(since)));
        var metaText = Caption(string.Join(" · ", meta));
        metaText.ToolTip = item.Kind is StartupKind.Run or StartupKind.Run32 ? L("Entry name: {0}", item.Name) : null;
        body.Children.Add(metaText);
        var command = item.Kind == StartupKind.Packaged ? item.Command : item.TargetPath is not null && item.Kind == StartupKind.Folder
            ? $"{item.Command}  →  {item.TargetPath}" : item.Command;
        var cmd = Caption(command, tertiary: true, trim: true);
        cmd.Margin = new Thickness(0, 2, 0, 0);
        body.Children.Add(cmd);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var busy = new ProgressBar { IsIndeterminate = true, Width = 40, Height = 3, Margin = new Thickness(0, 0, 10, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(busy);
        if (item.LocationPath is { Length: > 0 } location)
            actions.Children.Add(IconButton("", L("Open file location"), (_, _) => RevealInExplorer(location)));
        else actions.Children.Add(new Border { Width = 34 });
        if (item.CanDelete)
        {
            var del = IconButton("", item.NeedsAdmin ? L("Delete entry (administrator)") : L("Delete entry"), async (s, _) => await DeleteAsync(item, (Button)s!));
            actions.Children.Add(del);
        }
        else actions.Children.Add(new Border { Width = 34 });
        if (item.CanToggle)
        {
            var state = new TextBlock { Text = item.Enabled ? LC("feminine", "On") : L("Disabled"), Width = 74, TextAlignment = TextAlignment.Right, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }
                .Styled("Pp.Body");
            var toggle = new CheckBox { IsChecked = item.Enabled, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.ToggleSwitch");
            System.Windows.Automation.AutomationProperties.SetName(toggle, L("Run {0} at startup", item.DisplayName));
            if (item.NeedsAdmin) toggle.ToolTip = L("Entry shared by all users: administrator rights required.");
            toggle.Click += async (_, _) => await ToggleAsync(item, toggle, state, busy);
            actions.Children.Add(state);
            actions.Children.Add(toggle);
        }
        else if (item.IsReadOnly)
        {
            actions.Children.Add(Badge(item.Kind == StartupKind.RunOnce ? L("Once") : L("Policy"), "Neutral"));
        }
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        return new Border { Child = grid }.Styled("Pp.Card");
    }

    private async Task ToggleAsync(StartupItem item, CheckBox toggle, TextBlock state, ProgressBar busy)
    {
        var desired = toggle.IsChecked == true;
        toggle.IsEnabled = false;
        busy.Visibility = Visibility.Visible;
        try
        {
            var p = new Dictionary<string, string>
            {
                ["scope"] = item.ScopeParam, ["kind"] = item.KindParam, ["name"] = item.Name, ["enabled"] = desired ? "true" : "false",
            };
            var outcome = await StartupPage.RunAsync(item.NeedsAdmin ? "startup.item.setenabled" : "startup.item.setenabled.user", p);
            AppHost.Toasts.ShowOutcome(outcome);
            if (outcome.Success)
            {
                item.Enabled = desired;
                item.DisabledSince = desired ? null : DateTime.Now;
                Render();
                return;
            }
            toggle.IsChecked = item.Enabled;
            state.Text = item.Enabled ? LC("feminine", "On") : L("Disabled");
        }
        finally
        {
            toggle.IsEnabled = true;
            busy.Visibility = Visibility.Collapsed;
        }
    }

    private async Task DeleteAsync(StartupItem item, Button button)
    {
        var ok = await AppHost.Dialogs.ConfirmAsync(L("Delete startup entry?"),
            L("“{0}” will be removed from the startup list ({1}).\n\nCommand: {2}\n\nThe program itself isn't uninstalled, and the entry can be restored from Timonier's History. If you're not sure, disable it instead: it has the same effect at startup.", item.DisplayName, item.SourceLabel, item.Command),
            L("Remove"), L("Undo"), danger: true);
        if (!ok) return;
        button.IsEnabled = false;
        var p = new Dictionary<string, string> { ["scope"] = item.ScopeParam, ["kind"] = item.KindParam, ["name"] = item.Name };
        var outcome = await StartupPage.RunAsync(item.NeedsAdmin ? "startup.item.delete" : "startup.item.delete.user", p);
        AppHost.Toasts.ShowOutcome(outcome);
        if (outcome.Success)
        {
            _items.Remove(item);
            Render();
        }
        else button.IsEnabled = true;
    }

    private static void OpenSettings()
    {
        try { ProcessRunner.OpenSettingsUri("ms-settings:startupapps"); }
        catch (Exception ex) { AppHost.Toasts.Show(L("Couldn't open Settings: {0}", ex.Message), ToastKind.Error); }
    }
}
