using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Apps;

/// <summary>
/// Vue « Installées » : programmes de bureau inscrits dans le registre (lecture seule), recherche, tri, désinstallation
/// par winget (jamais par la chaîne de désinstallation brute) et lien vers les Paramètres Windows.
/// </summary>
internal sealed class InstalledView : StackPanel
{
    private readonly AppsContext _ctx;
    private readonly TextBox _search = AppsUi.SearchBox(L("Search for a program or publisher…"));
    private readonly ComboBox _sort = new() { Width = 200, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = AppsUi.Caption("");
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly StackPanel _list = new();
    private readonly Button _refresh;
    private readonly DispatcherTimer _debounce;
    private List<ProgramRow> _rows = [];
    private bool _loaded;
    private bool _loading;

    public InstalledView(AppsContext ctx)
    {
        _ctx = ctx;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFilter(); };

        var bar = new DockPanel();
        DockPanel.SetDock(_sort, Dock.Right);
        bar.Children.Add(_sort);
        bar.Children.Add(_search);
        Children.Add(bar);
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        foreach (var s in new[] { L("Sort by name"), L("Sort by size"), L("Sort by install date"), L("Sort by publisher") }) _sort.Items.Add(s);
        _sort.SelectedIndex = 0;
        _sort.SelectionChanged += (_, _) => ApplyFilter();
        System.Windows.Automation.AutomationProperties.SetName(_sort, L("Sort order"));

        _refresh = AppsUi.Button(L("Refresh"), "", "Pp.SubtleButton", async (_, _) => await LoadAsync(true));
        var settings = AppsUi.Button(L("Installed apps (Settings)"), "", "Pp.SubtleButton",
            (_, _) => ProcessRunner.OpenSettingsUri("ms-settings:appsfeatures"));
        settings.Margin = new Thickness(6, 0, 0, 0);
        var tools = AppsUi.Row(_refresh, settings);
        var line = new DockPanel { Margin = new Thickness(2, 10, 0, 6) };
        DockPanel.SetDock(tools, Dock.Right);
        line.Children.Add(tools);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(_summary);
        Children.Add(line);

        Children.Add(_host);
        ctx.InstalledChanged += () => { if (_loaded) _ = LoadAsync(false); };
        ctx.Activity.BusyChanged += UpdateButtons;
    }

    public async Task ActivateAsync()
    {
        if (!_loaded) await LoadAsync(false);
    }

    private async Task LoadAsync(bool refresh)
    {
        if (_loading) return;
        _loading = true;
        _refresh.IsEnabled = false;
        if (!_loaded) _host.Content = AppsUi.Loading(L("Reading the program list…"));
        try
        {
            var programs = await _ctx.GetInstalledAsync(refresh);
            _rows = [.. programs.Select(p => new ProgramRow(p, this))];
            _list.Children.Clear();
            foreach (var r in _rows) _list.Children.Add(r);
            var totalSize = programs.Sum(p => p.SizeBytes);
            _summary.Text = LP(programs.Count, "{0} desktop program · {1}", "{0} desktop programs · {1}", Format.Bytes(totalSize));
            _summary.ToolTip = L("Sizes reported by publishers (sometimes missing). Store apps are listed in the “Preinstalled” tab.");
            _host.Content = AppsUi.Card(_list, new Thickness(12, 4, 12, 4));
            _loaded = true;
            ApplyFilter();
            UpdateButtons();
        }
        catch (Exception ex)
        {
            Log.Error("Apps", "liste des programmes", ex);
            _host.Content = AppsUi.EmptyState("", L("List unavailable"), L("Couldn't read the program list: {0}", ex.Message), "Pp.Warning");
        }
        finally
        {
            _loading = false;
            _refresh.IsEnabled = true;
        }
    }

    private void ApplyFilter()
    {
        if (!_loaded) return;
        var terms = AppsContext.Fold(_search.Text.Trim()).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<ProgramRow> ordered = _sort.SelectedIndex switch
        {
            1 => _rows.OrderByDescending(r => r.Program.SizeBytes),
            2 => _rows.OrderByDescending(r => r.Program.InstallDate ?? DateTime.MinValue),
            3 => _rows.OrderBy(r => r.Program.Publisher ?? "￿", StringComparer.CurrentCultureIgnoreCase),
            _ => _rows.OrderBy(r => r.Program.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        };
        _list.Children.Clear();
        var visible = 0;
        foreach (var r in ordered)
        {
            var ok = terms.All(t => r.SearchText.Contains(t, StringComparison.Ordinal));
            r.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            _list.Children.Add(r);
            if (ok) visible++;
        }
        if (visible == 0)
        {
            var empty = AppsUi.Caption(L("No program matches “{0}”.", _search.Text.Trim()));
            empty.Margin = new Thickness(6, 14, 0, 14);
            _list.Children.Add(empty);
        }
    }

    private void UpdateButtons()
    {
        foreach (var r in _rows) r.UpdateButton(_ctx.WingetAvailable, _ctx.Activity.IsBusy);
    }

    private async Task UninstallAsync(InstalledProgram p)
    {
        if (_ctx.Activity.IsBusy) return;
        var who = p.Publisher is { Length: > 0 } pub ? L("“{0}” ({1})", p.DisplayName, pub) : L("“{0}”", p.DisplayName);
        // Programme installé pour ce compte seulement : désinstallé sans élévation (son désinstalleur ne doit jamais
        // s'exécuter avec les droits administrateur, voir WingetUninstallAction).
        var message = p.PerUser
            ? L("{0} will be uninstalled by winget, silently when the publisher allows it; otherwise its uninstall wizard appears.\n\nYour documents aren't affected, but the program's settings may be lost.", who)
            : L("{0} will be uninstalled by winget, silently when the publisher allows it; otherwise its uninstall wizard appears.\n\nYour documents aren't affected, but the program's settings may be lost. You'll be asked for administrator confirmation.", who);
        if (!await AppHost.Dialogs.ConfirmAsync(L("Uninstall this program?"), message, L("Uninstall"), L("Undo"), danger: true)) return;
        var parameters = p.ProductCode is { } code
            ? new Dictionary<string, string> { ["productCode"] = code, ["name"] = p.DisplayName }
            : new Dictionary<string, string> { ["name"] = p.DisplayName };
        var actionId = p.PerUser ? WingetUninstallUserAction.ActionId : WingetUninstallAction.ActionId;
        var outcome = await _ctx.Activity.RunAsync(L("Uninstalling {0}", p.DisplayName), actionId, parameters);
        if (outcome is { Success: true }) _ctx.NotifyInstalledChanged();
    }

    // ================================================================== Ligne de programme

    private sealed class ProgramRow : Border
    {
        private readonly Button _uninstall;
        public InstalledProgram Program { get; }
        public string SearchText { get; }

        public ProgramRow(InstalledProgram p, InstalledView owner)
        {
            Program = p;
            SearchText = AppsContext.Fold($"{p.DisplayName} {p.Publisher} {p.Version}");
            Padding = new Thickness(4, 10, 4, 10);
            BorderThickness = new Thickness(0, 0, 0, 1);
            this.Themed(BorderBrushProperty, "Pp.Divider");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var letter = AppsUi.LetterTile(p.DisplayName, 32);
            letter.Margin = new Thickness(0, 0, 12, 0);
            grid.Children.Add(letter);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var name = AppsUi.Strong(p.DisplayName, 13.5);
            name.ToolTip = p.DisplayName;
            text.Children.Add(name);
            var sub = new List<string>();
            if (p.Publisher is { Length: > 0 } pub) sub.Add(pub);
            if (p.Version is { Length: > 0 } v) sub.Add(L("version {0}", v));
            sub.Add(p.ScopeLabel + (p.Is32Bit ? " · " + L("32-bit") : ""));
            var caption = AppsUi.Caption(string.Join(" · ", sub));
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            caption.TextWrapping = TextWrapping.NoWrap;
            text.Children.Add(caption);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var size = AppsUi.Text(p.SizeBytes > 0 ? Format.Bytes(p.SizeBytes) : "—");
            size.FontSize = 13;
            size.HorizontalAlignment = HorizontalAlignment.Right;
            meta.Children.Add(size);
            var date = AppsUi.Caption(p.InstallDate is { } d ? L("installed on {0}", Format.Day(d)) : L("unknown date"));
            date.HorizontalAlignment = HorizontalAlignment.Right;
            meta.Children.Add(date);
            Grid.SetColumn(meta, 2);
            grid.Children.Add(meta);

            _uninstall = AppsUi.Button(L("Uninstall"), "", "Pp.Button", async (_, _) => await owner.UninstallAsync(p));
            _uninstall.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_uninstall, 3);
            grid.Children.Add(_uninstall);
            Child = grid;
        }

        public void UpdateButton(bool winget, bool busy)
        {
            _uninstall.IsEnabled = winget && !busy && !Program.NoRemove;
            ToolTipService.SetShowOnDisabled(_uninstall, true);
            _uninstall.ToolTip = Program.NoRemove ? L("The publisher doesn't allow this program to be uninstalled.")
                : !winget ? L("winget is required: otherwise, use Windows Settings.")
                : null;
        }
    }
}
