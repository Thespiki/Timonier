using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Apps;

/// <summary>
/// Vue « Installer » : catalogue vérifié, recherche, filtre, suggestions adaptées au PC (constructeur, carte graphique,
/// gamme), sélection multiple et installation groupée par winget.
/// </summary>
internal sealed class InstallView : StackPanel
{
    // Index des filtres dans la liste déroulante (les catégories du catalogue suivent, dans leur ordre).
    private const int AllFilter = 0;
    private const int EssentialsFilter = 1;
    private const int ForPcFilter = 2;
    private const int InstalledFilter = 3;
    private const int FirstCategoryFilter = 4;

    private readonly AppsContext _ctx;
    private readonly TextBox _search = AppsUi.SearchBox(L("Rechercher une application (nom, usage, éditeur…)"));
    private readonly ComboBox _filter = new() { Width = 230, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _suggestHost = new() { Focusable = false };
    private readonly StackPanel _sections = new();
    private readonly TextBlock _noResult = AppsUi.Caption("");
    private readonly Dictionary<string, AppTile> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(FrameworkElement Header, TwoColumnPanel Grid, TextBlock Counter)> _groups = [];
    private readonly DispatcherTimer _debounce;
    private HashSet<string> _installedIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _built;

    // Barre d'action fixe (affichée par la page sous la zone de défilement).
    private readonly TextBlock _selectionText = AppsUi.Text("");
    private readonly Button _installButton;
    private readonly Button _clearButton;
    public Border ActionBar { get; }

    public InstallView(AppsContext ctx)
    {
        _ctx = ctx;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFilter(); };

        // Barre d'outils : recherche + filtre.
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(_filter, Dock.Right);
        bar.Children.Add(_filter);
        bar.Children.Add(_search);
        Children.Add(bar);
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _filter.Items.Add(L("Toutes les catégories"));
        _filter.Items.Add(L("Essentiels"));
        _filter.Items.Add(L("Suggestions pour ce PC"));
        _filter.Items.Add(L("Déjà installées"));
        foreach (var (name, _) in AppsCatalog.Categories) _filter.Items.Add(name);
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => ApplyFilter();
        System.Windows.Automation.AutomationProperties.SetName(_filter, L("Filtrer par catégorie"));

        var note = AppsUi.Caption(L("Installation silencieuse par winget depuis sa source communautaire ; les programmes sont téléchargés chez chaque éditeur. Installer vaut acceptation de la licence de chaque éditeur."));
        note.Margin = new Thickness(2, 6, 0, 0);
        Children.Add(note);

        Children.Add(_suggestHost);
        Children.Add(_sections);
        _noResult.Margin = new Thickness(2, 18, 0, 0);
        _noResult.Visibility = Visibility.Collapsed;
        Children.Add(_noResult);

        // Barre d'action.
        _clearButton = AppsUi.Button(L("Tout désélectionner"), null, "Pp.SubtleButton", (_, _) => ClearSelection());
        _installButton = AppsUi.Button(L("Installer"), "", "Pp.AccentButton", async (_, _) => await InstallSelectedAsync());
        _installButton.Margin = new Thickness(8, 0, 0, 0);
        var actions = AppsUi.Row(_clearButton, _installButton);
        _selectionText.VerticalAlignment = VerticalAlignment.Center;
        _selectionText.FontSize = 13;
        var dock = new DockPanel { MaxWidth = 1060, Margin = new Thickness(36, 10, 36, 10) };
        DockPanel.SetDock(actions, Dock.Right);
        dock.Children.Add(actions);
        dock.Children.Add(_selectionText);
        ActionBar = new Border { Child = dock, BorderThickness = new Thickness(0, 1, 0, 0), Visibility = Visibility.Collapsed }
            .Themed(Border.BackgroundProperty, "Pp.CardBackground")
            .Themed(Border.BorderBrushProperty, "Pp.Divider");

        ctx.Activity.BusyChanged += UpdateActionBar;
        ctx.InstalledChanged += () => _ = RefreshInstalledAsync();
    }

    public event Action? ActionBarChanged;

    public int SelectedCount => _tiles.Values.Count(t => t.IsSelected);

    // ================================================================== Chargement

    public async Task ActivateAsync()
    {
        if (_built) return;
        _built = true;
        BuildTiles();
        BuildSuggestions();
        if (!AppHost.Profile.HardwareLoaded) AppHost.HardwareLoaded += OnHardwareLoaded;
        ApplyFilter();
        await RefreshInstalledAsync();
    }

    private void OnHardwareLoaded(object? sender, EventArgs e)
    {
        AppHost.HardwareLoaded -= OnHardwareLoaded;
        foreach (var t in _tiles.Values) t.UpdateBadges(_installedIds.Contains(t.App.WingetId));
        BuildSuggestions();
    }

    private void BuildTiles()
    {
        foreach (var (category, glyph) in AppsCatalog.Categories)
        {
            var apps = AppsCatalog.All.Where(a => a.Category == category).ToList();
            if (apps.Count == 0) continue;
            var header = AppsUi.SectionHeader(category, glyph, out var counter);
            var grid = new TwoColumnPanel { Margin = new Thickness(-3, 0, -3, 0) };
            foreach (var app in apps)
            {
                var tile = new AppTile(app);
                tile.SelectionChanged += UpdateActionBar;
                _tiles[app.WingetId] = tile;
                grid.Children.Add(tile);
            }
            _sections.Children.Add(header);
            _sections.Children.Add(grid);
            _groups.Add((header, grid, counter));
        }
    }

    private async Task RefreshInstalledAsync()
    {
        try
        {
            var installed = await _ctx.GetInstalledAsync();
            var names = installed.Select(p => p.DisplayName).ToList();
            _installedIds = new HashSet<string>(
                AppsCatalog.All.Where(a => names.Any(a.MatchesInstalledName)).Select(a => a.WingetId), StringComparer.OrdinalIgnoreCase);
            foreach (var t in _tiles.Values) t.UpdateBadges(_installedIds.Contains(t.App.WingetId));
            if (_filter.SelectedIndex == InstalledFilter) ApplyFilter();
        }
        catch (Exception ex) { Log.Warn("Apps", "détection des applications installées : " + ex.Message); }
    }

    // ================================================================== Suggestions pour ce PC

    private void BuildSuggestions()
    {
        var p = AppHost.Profile;
        var relevant = AppsCatalog.All.Where(a => a.IsRelevantFor(p)).ToList();
        var links = GpuLinks(p).ToList();
        if (relevant.Count == 0 && links.Count == 0) { _suggestHost.Content = null; return; }

        var body = new StackPanel();
        var head = new DockPanel();
        var tile = AppsUi.IconTile("", 34);
        tile.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(tile, Dock.Left);
        head.Children.Add(tile);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(AppsUi.Strong(L("Suggestions pour ce PC")));
        var hw = new List<string>();
        if (p.ManufacturerKnown) hw.Add(p.Manufacturer + (p.Model.Length > 0 ? " " + p.Model : ""));
        hw.AddRange(p.Gpus.Select(g => g.Name).Where(n => n.Length > 0));
        titles.Children.Add(AppsUi.Caption(hw.Count > 0 ? string.Join(" · ", hw) : L("Outils du constructeur et pilotes graphiques adaptés")));
        head.Children.Add(titles);
        body.Children.Add(head);

        var wrap = new WrapPanel { Margin = new Thickness(46, 10, 0, 0) };
        foreach (var app in relevant)
        {
            var target = _tiles.GetValueOrDefault(app.WingetId);
            var b = AppsUi.Button(app.Name, "", "Pp.Button", (_, _) =>
            {
                if (target is null) return;
                target.IsSelected = true;
                target.BringIntoView();
            });
            b.Margin = new Thickness(0, 0, 8, 6);
            b.ToolTip = L("{0}\n\nAjouter à la sélection.", app.Description);
            wrap.Children.Add(b);
        }
        foreach (var (label, url) in links)
        {
            var b = AppsUi.Button(label, "", "Pp.SubtleButton", (_, _) => OpenOfficialSite(url));
            b.Margin = new Thickness(0, 0, 8, 6);
            b.ToolTip = L("Ouvre le site officiel dans votre navigateur : {0}", url);
            wrap.Children.Add(b);
        }
        body.Children.Add(wrap);
        var card = AppsUi.Card(body, new Thickness(16, 14, 16, 10));
        card.Margin = new Thickness(0, 14, 0, 0);
        _suggestHost.Content = card;
    }

    private static IEnumerable<(string Label, string Url)> GpuLinks(SystemProfile p)
    {
        if (p.Gpus.Any(g => g.Vendor == HardwareVendor.Nvidia))
            yield return (L("Application NVIDIA (site officiel)"), "https://www.nvidia.com/fr-fr/software/nvidia-app/");
        if (p.Gpus.Any(g => g.Vendor == HardwareVendor.Amd))
            yield return (L("Pilotes AMD Radeon (site officiel)"), "https://www.amd.com/fr/support/download/drivers.html");
    }

    /// <summary>Ouvre une adresse CONSTANTE du code (sites officiels des pilotes) dans le navigateur par défaut.</summary>
    private static void OpenOfficialSite(string url)
    {
        try { ProcessRunner.OpenUrl(url); }
        catch (Exception ex) { AppHost.Toasts.Show(L("Impossible d'ouvrir le navigateur : {0}", ex.Message), ToastKind.Error); }
    }

    // ================================================================== Filtre

    public void SetSearch(string text)
    {
        _filter.SelectedIndex = 0;
        _search.Text = text;
        _search.CaretIndex = text.Length;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (!_built) return;
        var terms = AppsContext.Fold(_search.Text.Trim()).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filter = Math.Max(_filter.SelectedIndex, AllFilter);
        var category = filter >= FirstCategoryFilter && filter - FirstCategoryFilter < AppsCatalog.Categories.Count
            ? AppsCatalog.Categories[filter - FirstCategoryFilter].Name : null;
        var profile = AppHost.Profile;
        var visible = 0;
        foreach (var tile in _tiles.Values)
        {
            var a = tile.App;
            var ok = filter switch
            {
                AllFilter => true,
                EssentialsFilter => a.HasTag("essentials"),
                ForPcFilter => a.IsRelevantFor(profile) || (profile.Tier == PerformanceTier.Low && a.HasTag("lowend")),
                InstalledFilter => _installedIds.Contains(a.WingetId),
                _ => a.Category == category,
            };
            if (ok && terms.Length > 0) ok = terms.All(t => tile.SearchText.Contains(t, StringComparison.Ordinal));
            tile.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            if (ok) visible++;
        }
        foreach (var (header, grid, counter) in _groups)
        {
            var n = grid.Children.OfType<AppTile>().Count(t => t.Visibility == Visibility.Visible);
            header.Visibility = grid.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            counter.Text = n.ToString();
        }
        _suggestHost.Visibility = terms.Length == 0 && filter == AllFilter ? Visibility.Visible : Visibility.Collapsed;
        _noResult.Text = L("Aucune application du catalogue ne correspond à « {0} ». Le catalogue ne contient que des applications vérifiées ; pour les autres, utilisez winget ou le Microsoft Store.", _search.Text.Trim());
        _noResult.Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================== Sélection et installation

    private void ClearSelection()
    {
        foreach (var t in _tiles.Values) t.IsSelected = false;
    }

    private void UpdateActionBar()
    {
        var n = SelectedCount;
        _selectionText.Text = n == 0 ? "" : LP(n, "{0} application sélectionnée", "{0} applications sélectionnées");
        AppsUi.SetButtonText(_installButton, L("Installer ({0})", n));
        _installButton.IsEnabled = n > 0 && !_ctx.Activity.IsBusy && _ctx.WingetAvailable;
        _clearButton.IsEnabled = !_ctx.Activity.IsBusy;
        var show = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ActionBar.Visibility != show)
        {
            ActionBar.Visibility = show;
            ActionBarChanged?.Invoke();
        }
    }

    private async Task InstallSelectedAsync()
    {
        var apps = _tiles.Values.Where(t => t.IsSelected).Select(t => t.App).ToList();
        if (apps.Count == 0 || _ctx.Activity.IsBusy) return;
        if (apps.Count > WingetIds.Max)
        {
            AppHost.Toasts.Show(L("Sélectionnez au plus {0} applications à la fois.", WingetIds.Max), ToastKind.Warning);
            return;
        }

        var list = new StackPanel();
        list.Children.Add(AppsUi.Caption(apps.Count == 1
            ? L("Cette application sera téléchargée chez son éditeur et installée sans fenêtre :")
            : LP(apps.Count, "{0} application sera téléchargée chez son éditeur et installée sans fenêtre :", "Ces {0} applications seront téléchargées chez leurs éditeurs et installées une par une, sans fenêtre :")));
        var items = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        foreach (var a in apps)
        {
            var icon = AppsUi.Icon(AppsCatalog.GlyphFor(a.Category), 14, "Pp.AccentText");
            icon.Margin = new Thickness(0, 0, 10, 0);
            var name = AppsUi.Text(a.Name);
            name.FontWeight = FontWeights.SemiBold;
            name.FontSize = 13;
            var id = AppsUi.Caption("  " + a.WingetId + (_installedIds.Contains(a.WingetId) ? " · " + L("déjà installée (mise à jour éventuelle)") : ""));
            id.VerticalAlignment = VerticalAlignment.Center;
            var row = AppsUi.Row(icon, name, id);
            row.Margin = new Thickness(0, 2, 0, 2);
            items.Children.Add(row);
        }
        var scroller = new ScrollViewer { Content = items, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        list.Children.Add(scroller);
        list.Children.Add(AppsUi.InfoBar(L("En installant, vous acceptez la licence de chaque éditeur. winget se connecte à Internet ; une autorisation administrateur est demandée une fois pour la session."), ""));
        if (!await AppHost.Dialogs.ShowAsync(L("Installer des applications"), list, L("Installer ({0})", apps.Count))) return;

        var ids = string.Join(",", apps.Select(a => a.WingetId));
        var title = apps.Count == 1 ? L("Installation de {0}", apps[0].Name) : LP(apps.Count, "Installation de {0} application", "Installation de {0} applications");
        var outcome = await _ctx.Activity.RunAsync(title, WingetInstallAction.ActionId, new Dictionary<string, string> { ["ids"] = ids });
        if (outcome is null) return;
        if (outcome.Data is { } data)
            foreach (var (id, value) in data)
                if (value.StartsWith("ok", StringComparison.Ordinal) && _tiles.TryGetValue(id, out var t)) t.IsSelected = false;
        _ctx.NotifyInstalledChanged();
    }

    // ================================================================== Tuile d'application

    private sealed class AppTile : Border
    {
        private readonly CheckBox _check;
        private readonly WrapPanel _badges = new() { VerticalAlignment = VerticalAlignment.Center };
        public CatalogApp App { get; }
        public string SearchText { get; }
        public event Action? SelectionChanged;

        public AppTile(CatalogApp app)
        {
            App = app;
            SearchText = AppsContext.Fold(string.Join(' ', app.Name, app.WingetId, app.Description, app.Manufacturer ?? "",
                string.Join(' ', app.Tags.Select(TagWords))));
            this.Styled("Pp.CardInteractive");
            Margin = new Thickness(3, 3, 3, 3);
            Padding = new Thickness(12, 10, 14, 10);
            Cursor = Cursors.Hand;

            _check = AppsUi.CheckBox(L("Sélectionner {0}", app.Name));
            _check.VerticalAlignment = VerticalAlignment.Top;
            _check.Margin = new Thickness(0, 1, 10, 0);
            _check.Checked += (_, _) => OnSelection();
            _check.Unchecked += (_, _) => OnSelection();

            var name = AppsUi.Strong(app.Name);
            name.Margin = new Thickness(0, 0, 8, 0);
            var top = new DockPanel();
            DockPanel.SetDock(name, Dock.Left);
            top.Children.Add(name);
            top.Children.Add(_badges);

            var desc = AppsUi.Caption(app.Description);
            desc.Margin = new Thickness(0, 3, 0, 0);
            desc.TextTrimming = TextTrimming.WordEllipsis;
            desc.MaxHeight = 50;
            desc.ToolTip = app.Description;

            var text = new StackPanel();
            text.Children.Add(top);
            text.Children.Add(desc);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(text, 1);
            grid.Children.Add(_check);
            grid.Children.Add(text);
            Child = grid;
            UpdateBadges(false);

            MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && IsInside(d, _check)) return;
                _check.IsChecked = _check.IsChecked != true;
            };
        }

        public bool IsSelected
        {
            get => _check.IsChecked == true;
            set => _check.IsChecked = value;
        }

        private void OnSelection()
        {
            SetResourceReference(BorderBrushProperty, IsSelected ? "Pp.Accent" : "Pp.CardBorder");
            SelectionChanged?.Invoke();
        }

        public void UpdateBadges(bool installed)
        {
            _badges.Children.Clear();
            var p = AppHost.Profile;
            if (installed) _badges.Children.Add(AppsUi.Badge(L("Installée"), "Pp.Success"));
            if (App.IsRelevantFor(p)) _badges.Children.Add(AppsUi.Badge(L("Pour ce PC"), "Pp.Accent"));
            if (p.Tier == PerformanceTier.Low && App.HasTag("lowend")) _badges.Children.Add(AppsUi.Badge(L("Léger"), "Pp.Info"));
            if (p.Tier == PerformanceTier.Low && App.HasTag("heavy")) _badges.Children.Add(AppsUi.Badge(L("Exigeant"), "Pp.Warning"));
        }

        private static bool IsInside(DependencyObject d, DependencyObject ancestor)
        {
            for (var cur = d; cur is not null; cur = System.Windows.Media.VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur))
                if (ReferenceEquals(cur, ancestor)) return true;
            return false;
        }

        private static string TagWords(string tag) => tag switch
        {
            "essentials" => L("essentiel, indispensable"),
            "office" => L("bureautique, travail"),
            "gaming" => L("jeu, jeux, gaming"),
            "dev" => L("developpement, programmation, code"),
            "family" => L("famille, enfants, ecole"),
            "media" => L("multimedia, video, musique, photo"),
            "security" => L("securite, confidentialite"),
            "vendor" => L("constructeur, pilote, driver"),
            "lowend" => L("leger, petit pc"),
            _ => tag,
        };
    }
}
