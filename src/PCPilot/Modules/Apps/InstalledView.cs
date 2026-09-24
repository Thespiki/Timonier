using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PcPilot.Core.Platform;
using PcPilot.UI.Services;

namespace PcPilot.Modules.Apps;

/// <summary>
/// Vue « Installées » : programmes de bureau inscrits dans le registre (lecture seule), recherche, tri, désinstallation
/// par winget (jamais par la chaîne de désinstallation brute) et lien vers les Paramètres Windows.
/// </summary>
internal sealed class InstalledView : StackPanel
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private readonly AppsContext _ctx;
    private readonly TextBox _search = AppsUi.SearchBox("Rechercher un programme ou un éditeur…");
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
        foreach (var s in new[] { "Trier par nom", "Trier par taille", "Trier par date d'installation", "Trier par éditeur" }) _sort.Items.Add(s);
        _sort.SelectedIndex = 0;
        _sort.SelectionChanged += (_, _) => ApplyFilter();
        System.Windows.Automation.AutomationProperties.SetName(_sort, "Ordre de tri");

        _refresh = AppsUi.Button("Actualiser", "", "Pp.SubtleButton", async (_, _) => await LoadAsync(true));
        var settings = AppsUi.Button("Applications installées (Paramètres)", "", "Pp.SubtleButton",
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
        if (!_loaded) _host.Content = AppsUi.Loading("Lecture de la liste des programmes…");
        try
        {
            var programs = await _ctx.GetInstalledAsync(refresh);
            _rows = [.. programs.Select(p => new ProgramRow(p, this))];
            _list.Children.Clear();
            foreach (var r in _rows) _list.Children.Add(r);
            var totalSize = programs.Sum(p => p.SizeBytes);
            _summary.Text = $"{AppsContext.Plural(programs.Count, "programme", "programmes")} de bureau · {Format.Bytes(totalSize)}";
            _summary.ToolTip = "Tailles déclarées par les éditeurs (parfois absentes). Les applications du Store figurent dans l'onglet « Préinstallées ».";
            _host.Content = AppsUi.Card(_list, new Thickness(12, 4, 12, 4));
            _loaded = true;
            ApplyFilter();
            UpdateButtons();
        }
        catch (Exception ex)
        {
            Log.Error("Apps", "liste des programmes", ex);
            _host.Content = AppsUi.EmptyState("", "Liste indisponible", "La liste des programmes n'a pas pu être lue : " + ex.Message, "Pp.Warning");
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
            var empty = AppsUi.Caption($"Aucun programme ne correspond à « {_search.Text.Trim()} ».");
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
        var message = $"« {p.DisplayName} »{(p.Publisher is { Length: > 0 } pub ? $" ({pub})" : "")} sera désinstallé par winget, en mode silencieux quand " +
                      "l'éditeur le permet ; sinon son assistant de désinstallation s'affiche.\n\nVos documents ne sont pas touchés, mais les réglages " +
                      "du programme peuvent être perdus. Une confirmation administrateur sera demandée.";
        if (!await AppHost.Dialogs.ConfirmAsync("Désinstaller ce programme ?", message, "Désinstaller", "Annuler", danger: true)) return;
        var parameters = p.ProductCode is { } code
            ? new Dictionary<string, string> { ["productCode"] = code, ["name"] = p.DisplayName }
            : new Dictionary<string, string> { ["name"] = p.DisplayName };
        var outcome = await _ctx.Activity.RunAsync($"Désinstallation de {p.DisplayName}", WingetUninstallAction.ActionId, parameters);
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
            if (p.Version is { Length: > 0 } v) sub.Add("version " + v);
            sub.Add(p.ScopeLabel + (p.Is32Bit ? " · 32 bits" : ""));
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
            var date = AppsUi.Caption(p.InstallDate is { } d ? "installé le " + d.ToString("d MMM yyyy", Fr) : "date inconnue");
            date.HorizontalAlignment = HorizontalAlignment.Right;
            meta.Children.Add(date);
            Grid.SetColumn(meta, 2);
            grid.Children.Add(meta);

            _uninstall = AppsUi.Button("Désinstaller", "", "Pp.Button", async (_, _) => await owner.UninstallAsync(p));
            _uninstall.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_uninstall, 3);
            grid.Children.Add(_uninstall);
            Child = grid;
        }

        public void UpdateButton(bool winget, bool busy)
        {
            _uninstall.IsEnabled = winget && !busy && !Program.NoRemove;
            ToolTipService.SetShowOnDisabled(_uninstall, true);
            _uninstall.ToolTip = Program.NoRemove ? "L'éditeur ne permet pas de désinstaller ce programme."
                : !winget ? "winget est nécessaire : utilisez sinon les Paramètres Windows."
                : null;
        }
    }
}
