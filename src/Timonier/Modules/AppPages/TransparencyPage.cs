using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using Timonier.UI.Services;

namespace Timonier.Modules.AppPages;

/// <summary>
/// Transparence : ce que Timonier peut faire (catalogue complet, opérations exactes), ce qui est indisponible
/// sur ce PC et pourquoi, ce qu'il ne peut pas ou ne veut pas faire, son architecture de sécurité et ses données.
/// </summary>
public sealed class TransparencyPage : UserControl, INavigationAware
{
    private static CultureInfo Culture => Core.Localization.Loc.Culture;
    private static readonly string[] TabKeys = ["overview", "tweaks", "actions", "unavailable", "limits", "security"];

    private readonly SegmentedBar _tabs = new();
    private readonly ContentControl _host = new();
    private readonly Dictionary<int, FrameworkElement> _built = [];
    private List<TweakRow>? _rows;

    public TransparencyPage()
    {
        var stack = PageScaffold.Create(this, L("Transparency"),
            L("Everything Timonier can change, how it does it, what it refuses to do, and what it keeps on this PC."),
            AppPagesModule.TransparencyGlyph);

        _tabs.Add(L("Overview"), "");
        _tabs.Add(L("Settings"), "");
        _tabs.Add(L("Actions"), "");
        _tabs.Add(L("Unavailable here"), "");
        _tabs.Add(L("Limits"), "");
        _tabs.Add(L("Security & data"), "");
        _tabs.SelectionChanged += (_, i) => ShowTab(i);
        _tabs.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_tabs);
        stack.Children.Add(_host);

        _tabs.Select(0, notify: false);
        ShowTab(0);

        Loaded += (_, _) => AppHost.HardwareLoaded += OnHardwareLoaded;
        Unloaded += (_, _) => AppHost.HardwareLoaded -= OnHardwareLoaded;
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is string s && s.StartsWith("tab:", StringComparison.Ordinal))
        {
            var i = Array.IndexOf(TabKeys, s[4..]);
            if (i >= 0) _tabs.Select(i);
        }
        else if (parameter is string t && t.StartsWith("tweak:", StringComparison.Ordinal))
        {
            _tabs.Select(1);
            _pendingTweak = t[6..];
            SelectPendingTweak();
        }
    }

    private void OnHardwareLoaded(object? sender, EventArgs e)
    {
        // Les conditions matérielles (batterie, Wi-Fi…) sont désormais connues : on recalcule les vues qui en dépendent.
        _rows = null;
        foreach (var key in new[] { 0, 1, 3 }) _built.Remove(key);
        ShowTab(_tabs.SelectedIndex);
    }

    private void ShowTab(int index)
    {
        if (!_built.TryGetValue(index, out var content))
        {
            try
            {
                content = index switch
                {
                    0 => BuildOverview(),
                    1 => BuildTweaks(),
                    2 => BuildActions(),
                    3 => BuildUnavailable(),
                    4 => BuildLimits(),
                    _ => BuildSecurity(),
                };
            }
            catch (Exception ex)
            {
                Log.Error("AppPages", "transparence, onglet " + index, ex);
                content = AppUi.StateCard("", L("This section couldn't be displayed"), ex.Message);
            }
            if (index != 5) _built[index] = content; // « Sécurité et données » est recalculée à chaque visite (état en direct)
        }
        _host.Content = content;
    }

    // ================================================================== Données

    private sealed record TweakRow(TweakDefinition Tweak, string Title, string Category, string Admin, string Risk, string Effect,
        string Availability, bool Available);

    private List<TweakRow> Rows()
    {
        if (_rows is not null) return _rows;
        var reg = AppHost.Registry;
        _rows = [.. reg.Tweaks
            .Select(t =>
            {
                var reason = t.Requirement.Check(AppHost.Profile);
                return new TweakRow(t, t.Title, CategoryTitle(t.Category), t.RequiresAdmin ? L("Yes") : L("No"), RiskLabel(t.Risk),
                    EffectLabel(t.Effect), reason ?? L("Available"), reason is null);
            })
            .OrderBy(r => r.Category, StringComparer.Create(Culture, false))
            .ThenBy(r => r.Title, StringComparer.Create(Culture, false))];
        return _rows;
    }

    private static string CategoryTitle(string id) => AppHost.Registry.GetCategory(id)?.Title ?? id;

    internal static string RiskLabel(RiskLevel r) => r switch
    {
        RiskLevel.Moderate => L("Moderate"),
        RiskLevel.Advanced => L("Advanced"),
        _ => L("Safe"),
    };

    internal static string EffectLabel(ApplyEffect e)
    {
        if (e == ApplyEffect.None) return L("Immediate");
        var parts = new List<string>();
        if (e.HasFlag(ApplyEffect.RestartExplorer)) parts.Add(L("File Explorer restart"));
        if (e.HasFlag(ApplyEffect.SignOut)) parts.Add(L("Sign out and back in"));
        if (e.HasFlag(ApplyEffect.Reboot)) parts.Add(L("PC restart"));
        return string.Join(", ", parts);
    }

    private static string KindLabel(TweakKind k) => k switch
    {
        TweakKind.Toggle => LC("tweak kind", "Toggle"),
        TweakKind.Choice => LC("tweak kind", "Choice"),
        _ => LC("tweak kind", "One-time action"),
    };

    /// <summary>Module propriétaire d'une action (préfixe de l'identifiant), en clair.</summary>
    private static string ModuleOf(string id)
    {
        var prefix = id.Split('.')[0];
        var key = prefix switch { "custom" => "customization", "perf" => "performance", "app" => "journal", _ => prefix };
        return AppHost.Registry.GetCategory(key)?.Title ?? AppHost.Registry.GetPage(key)?.Title ?? prefix;
    }

    // ================================================================== Vue d'ensemble

    private FrameworkElement BuildOverview()
    {
        var reg = AppHost.Registry;
        var rows = Rows();
        var root = new StackPanel();

        root.Children.Add(AppUi.Tiles(
            AppUi.MetricTile("", rows.Count.ToString(Culture), L("settings in the catalog")),
            AppUi.MetricTile("", reg.Actions.Count.ToString(Culture), L("parameterized actions")),
            AppUi.MetricTile("", rows.Count(r => r.Available).ToString(Culture), L("settings available on this PC"), "Success"),
            AppUi.MetricTile("", rows.Count(r => !r.Tweak.RequiresAdmin).ToString(Culture), L("settings without administrator rights"), "Success"),
            AppUi.MetricTile("", rows.Count(r => r.Tweak.RequiresAdmin).ToString(Culture), L("settings with administrator rights"), "Info"),
            AppUi.MetricTile("", rows.Count(r => r.Tweak.IsReversible).ToString(Culture), L("settings you can undo from History"))));

        if (!AppHost.Profile.HardwareLoaded)
        {
            var info = PageScaffold.InfoBar(L("Detecting hardware: hardware-related availability (battery, Wi-Fi, graphics card…) will be shown in a few seconds."), "");
            info.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(info);
        }

        // Tableau par catégorie
        root.Children.Add(AppUi.Section(L("By category")));
        var table = new Grid();
        string[] headers = [L("Category"), L("Settings"), L("Admin"), LC("plural (settings)", "Moderate"), LC("plural (settings)", "Advanced"), LC("plural (settings)", "Can't be undone"), LC("plural (settings)", "Unavailable")];
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        for (var i = 1; i < headers.Length; i++) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        var r0 = 0;
        void AddRow(string[] cells, bool header)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < cells.Length; c++)
            {
                var t = header ? AppUi.Caption(cells[c]) : AppUi.Text(cells[c], "Pp.Body", wrap: false);
                if (header) t.FontWeight = FontWeights.SemiBold;
                if (c > 0) t.HorizontalAlignment = HorizontalAlignment.Right;
                if (!header && c > 0 && cells[c] == "0") t.Themed(TextBlock.ForegroundProperty, "Pp.TextTertiary");
                t.Margin = new Thickness(c == 0 ? 0 : 8, 7, 0, 7);
                Grid.SetRow(t, r0);
                Grid.SetColumn(t, c);
                table.Children.Add(t);
            }
            if (!header)
            {
                var line = AppUi.Divider(new Thickness(0));
                line.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(line, r0);
                Grid.SetColumnSpan(line, cells.Length);
                table.Children.Add(line);
            }
            r0++;
        }
        AddRow(headers, true);
        foreach (var group in rows.GroupBy(r => r.Category).OrderByDescending(g => g.Count()))
        {
            AddRow([group.Key, group.Count().ToString(Culture), group.Count(r => r.Tweak.RequiresAdmin).ToString(Culture),
                group.Count(r => r.Tweak.Risk == RiskLevel.Moderate).ToString(Culture), group.Count(r => r.Tweak.Risk == RiskLevel.Advanced).ToString(Culture),
                group.Count(r => !r.Tweak.IsReversible).ToString(Culture), group.Count(r => !r.Available).ToString(Culture)], false);
        }
        AddRow([L("Total"), rows.Count.ToString(Culture), rows.Count(r => r.Tweak.RequiresAdmin).ToString(Culture),
            rows.Count(r => r.Tweak.Risk == RiskLevel.Moderate).ToString(Culture), rows.Count(r => r.Tweak.Risk == RiskLevel.Advanced).ToString(Culture),
            rows.Count(r => !r.Tweak.IsReversible).ToString(Culture), rows.Count(r => !r.Available).ToString(Culture)], false);
        foreach (var t in table.Children.OfType<TextBlock>().Where(t => Grid.GetRow(t) == r0 - 1)) t.FontWeight = FontWeights.SemiBold;
        root.Children.Add(AppUi.Card(table));

        var legend = AppUi.Caption(L("“Moderate”: may degrade a feature, so confirmation is required. “Advanced”: hidden unless advanced mode is on. “Can't be undone”: one-time actions that launch a system tool (e.g., cleanup); everything else is recorded in History with the previous state."));
        legend.Margin = new Thickness(2, 8, 0, 0);
        root.Children.Add(legend);

        // Actions par module
        root.Children.Add(AppUi.Section(L("Actions by module")));
        var actionsWrap = new WrapPanel();
        foreach (var g in reg.Actions.GroupBy(a => ModuleOf(a.Id)).OrderByDescending(g => g.Count()))
        {
            var s = new StackPanel();
            s.Children.Add(AppUi.Text(g.Key, "Pp.Body", wrap: false));
            var detail = new List<string> { LP(g.Count(), "{0} action", "{0} actions"), LP(g.Count(a => a.RequiresAdmin), "{0} admin", "{0} admin") };
            var confirm = g.Count(a => a.RequiresElevatedConfirmation);
            if (confirm > 0) detail.Add(LP(confirm, "{0} with elevated confirmation", "{0} with elevated confirmation"));
            s.Children.Add(AppUi.Caption(string.Join(" · ", detail)));
            var card = AppUi.Card(s, new Thickness(0, 0, 10, 10));
            card.MinWidth = 210;
            actionsWrap.Children.Add(card);
        }
        root.Children.Add(actionsWrap);

        var extra = AppUi.Caption(L("Plus {0} health checks (read-only, no administrator rights), {1} quick actions, and {2} pages.", reg.HealthChecks.Count, reg.QuickActions.Count, reg.Pages.Count));
        extra.Margin = new Thickness(2, 2, 0, 0);
        root.Children.Add(extra);

        // Cohérence du catalogue
        root.Children.Add(AppUi.Section(L("Catalog consistency")));
        if (reg.Errors.Count == 0)
            root.Children.Add(PageScaffold.InfoBar(L("No inconsistencies detected at load: unique IDs, known categories, valid options."), "", "Pp.InfoBar.Success"));
        else
        {
            root.Children.Add(PageScaffold.InfoBar(LP(reg.Errors.Count,
                "{0} inconsistency detected at load. Affected items are ignored or partially available.",
                "{0} inconsistencies detected at load. Affected items are ignored or partially available."), "", "Pp.InfoBar.Warning"));
            var list = AppUi.Bullets([.. reg.Errors.Take(30)]);
            root.Children.Add(AppUi.Card(list, new Thickness(0, 8, 0, 0)));
        }
        return root;
    }

    // ================================================================== Réglages (catalogue complet)

    private DataGrid? _grid;
    private string? _pendingTweak;

    private FrameworkElement BuildTweaks()
    {
        var rows = Rows();
        var root = new StackPanel();
        var intro = AppUi.Caption(L("Complete list of what Timonier can change. The administrator process rejects any setting that isn't in this catalog: there's no way to make it run anything else. Select a row to see the exact operations."));
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);

        var categories = new ComboBox { MinWidth = 220, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(categories, L("Category"));
        categories.Items.Add(L("All categories"));
        foreach (var c in rows.Select(r => r.Category).Distinct()) categories.Items.Add(c);
        categories.SelectedIndex = 0;

        var onlyAvailable = new CheckBox { Content = LC("plural (settings)", "Available on this PC"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        var count = AppUi.Caption("");
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(14, 0, 0, 0);

        var grid = MakeGrid(L("Settings catalog"), 380);
        grid.Columns.Add(Col(L("Setting"), nameof(TweakRow.Title), new DataGridLength(2.2, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("Category"), nameof(TweakRow.Category), new DataGridLength(1.1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("Admin"), nameof(TweakRow.Admin), new DataGridLength(70)));
        grid.Columns.Add(Col(L("Risk"), nameof(TweakRow.Risk), new DataGridLength(96)));
        grid.Columns.Add(Col(L("Effect"), nameof(TweakRow.Effect), new DataGridLength(1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("On this PC"), nameof(TweakRow.Availability), new DataGridLength(1.3, DataGridLengthUnitType.Star)));
        _grid = grid;

        var details = new ContentControl { Margin = new Thickness(0, 14, 0, 0) };
        details.Content = AppUi.StateCard("", L("Select a setting"), L("Its options, exact operations (registry, services, tasks), requirements, and reversibility will appear here."));

        var query = "";
        void Apply()
        {
            IEnumerable<TweakRow> items = rows;
            if (categories.SelectedIndex > 0 && categories.SelectedItem is string cat) items = items.Where(r => r.Category == cat);
            if (onlyAvailable.IsChecked == true) items = items.Where(r => r.Available);
            if (query.Length > 0)
                items = items.Where(r => Contains(r.Title, query) || Contains(r.Tweak.Id, query) || Contains(r.Tweak.Description, query)
                    || r.Tweak.Keywords.Any(k => Contains(k, query)));
            var list = items.ToList();
            grid.ItemsSource = list;
            count.Text = LP(list.Count, "{0} setting", "{0} settings");
        }
        var search = AppUi.SearchBox(L("Search for a setting or ID…"), q => { query = q; Apply(); });
        categories.SelectionChanged += (_, _) => Apply();
        onlyAvailable.Checked += (_, _) => Apply();
        onlyAvailable.Unchecked += (_, _) => Apply();
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is TweakRow r) details.Content = BuildTweakDetails(r);
        };

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(categories);
        right.Children.Add(onlyAvailable);
        right.Children.Add(count);
        DockPanel.SetDock(right, Dock.Right);
        toolbar.Children.Add(right);
        toolbar.Children.Add(search);
        root.Children.Add(toolbar);
        root.Children.Add(grid);
        root.Children.Add(details);
        Apply();
        SelectPendingTweak();
        return root;
    }

    /// <summary>Tableau en lecture seule, virtualisé, aux couleurs du thème.</summary>
    private static DataGrid MakeGrid(string name, double height)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.None,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            Height = height,
            RowHeight = 34,
            EnableRowVirtualization = true,
            BorderThickness = new Thickness(1),
        };
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        grid.Themed(Control.BackgroundProperty, "Pp.CardBackground");
        grid.Themed(Control.BorderBrushProperty, "Pp.CardBorder");
        grid.Themed(Control.ForegroundProperty, "Pp.TextPrimary");
        grid.RowBackground = null;
        if (Application.Current?.TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) is Style baseHeader)
        {
            var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), baseHeader);
            header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            grid.ColumnHeaderStyle = header;
        }
        System.Windows.Automation.AutomationProperties.SetName(grid, name);
        return grid;
    }

    private static readonly Style CellText = CreateCellText();

    private static Style CreateCellText()
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0)));
        s.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        s.Seal();
        return s;
    }

    private static DataGridTextColumn Col(string header, string path, DataGridLength width) =>
        new() { Header = header, Binding = new Binding(path), Width = width, ElementStyle = CellText };

    private void SelectPendingTweak()
    {
        if (_pendingTweak is null || _grid?.ItemsSource is not IEnumerable<TweakRow> items) return;
        var row = items.FirstOrDefault(r => r.Tweak.Id == _pendingTweak);
        _pendingTweak = null;
        if (row is null) return;
        _grid.SelectedItem = row;
        _grid.ScrollIntoView(row);
    }

    private static bool Contains(string? text, string query) =>
        text is not null && Culture.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private static FrameworkElement BuildTweakDetails(TweakRow row)
    {
        var t = row.Tweak;
        var s = new StackPanel();
        var head = new DockPanel();
        var open = AppUi.Button(L("Open setting"), "", "Pp.Button", (_, _) =>
            AppHost.Navigator.Navigate(AppHost.Registry.PageIdForCategory(t.Category), "tweak:" + t.Id));
        DockPanel.SetDock(open, Dock.Right);
        open.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(open);
        var titles = new StackPanel();
        titles.Children.Add(AppUi.Text(t.Title, "Pp.CardTitle"));
        titles.Children.Add(AppUi.Caption($"{row.Category}{(t.Group is null ? "" : " › " + t.Group)} · {t.Id}", tertiary: true));
        head.Children.Add(titles);
        s.Children.Add(head);

        var desc = AppUi.Text(t.Description);
        desc.Margin = new Thickness(0, 10, 0, 10);
        s.Children.Add(desc);

        var badges = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        void B(Border b) { b.Margin = new Thickness(0, 0, 6, 6); badges.Children.Add(b); }
        B(AppUi.Badge(KindLabel(t.Kind), "Neutral"));
        B(t.RequiresAdmin ? AppUi.Badge(L("Administrator rights"), "Info", "") : AppUi.Badge(L("No administrator rights"), "Success", ""));
        B(AppUi.Badge(L("Risk: {0}", RiskLabel(t.Risk).ToLower(Culture)), t.Risk == RiskLevel.Safe ? "Success" : "Warning"));
        B(t.IsReversible ? AppUi.Badge(LC("badge", "Undoable"), "Success", "") : AppUi.Badge(LC("badge", "Can't be undone"), "Warning", ""));
        if (t.Effect != ApplyEffect.None) B(AppUi.Badge(EffectLabel(t.Effect), "Warning", ""));
        B(row.Available ? AppUi.Badge(L("Available on this PC"), "Success", "") : AppUi.Badge(L("Unavailable on this PC"), "Danger", ""));
        s.Children.Add(badges);

        if (!row.Available) s.Children.Add(PageScaffold.KeyValue(L("Why it's unavailable"), row.Availability));
        if (t.Warning is not null) s.Children.Add(PageScaffold.KeyValue(L("Good to know"), t.Warning));
        if (t.WindowsDefault is { } def && t.GetOption(def) is { } defOpt) s.Children.Add(PageScaffold.KeyValue(LC("field label", "Windows default"), defOpt.Label));
        var rec = t.RecommendationFor(AppHost.Profile);
        if (rec is not null && t.GetOption(rec) is { } recOpt) s.Children.Add(PageScaffold.KeyValue(L("Recommended for this PC"), recOpt.Label));
        if (t.CustomDetect is not null) s.Children.Add(PageScaffold.KeyValue(L("Detection"), L("Custom (read-only), in addition to the operations below.")));

        foreach (var opt in t.Options)
        {
            var title = AppUi.Text(t.Kind == TweakKind.Action ? L("Operations performed") : L("Option “{0}”", opt.Label), "Pp.Body");
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 14, 0, 2);
            s.Children.Add(title);
            if (opt.Description is not null) s.Children.Add(AppUi.Caption(opt.Description));
            if (opt.Operations.Count == 0)
                s.Children.Add(AppUi.Caption(L("No operations (state check only)."), tertiary: true));
            foreach (var op in opt.Operations)
            {
                var line = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var icon = AppUi.Icon(op switch
                {
                    RegSet or RegDeleteValue or RegDeleteKey => "",
                    ServiceStartOp => "",
                    ScheduledTaskOp => "",
                    RunToolOp => "",
                    _ => "",
                }, 13, "Pp.TextTertiary");
                icon.Margin = new Thickness(0, 2, 10, 0);
                icon.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(icon, Dock.Left);
                line.Children.Add(icon);
                var text = new TextBlock { Text = op.Describe(), TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12 };
                text.Themed(TextBlock.ForegroundProperty, "Pp.TextSecondary");
                line.Children.Add(text);
                s.Children.Add(line);
            }
        }
        return AppUi.Card(s);
    }

    // ================================================================== Actions

    /// <summary>Ligne du tableau des actions : libellés traduits pour l'affichage, booléens pour les comptes.</summary>
    private sealed record ActionRow(string Id, string Title, string Module, string Admin, string Confirmation, bool IsAdmin, bool HasConfirmation);

    private static FrameworkElement BuildActions()
    {
        var reg = AppHost.Registry;
        var rows = reg.Actions
            .Select(a =>
            {
                // « Selon les paramètres » n'est vrai que si l'action surcharge RequiresElevatedConfirmationFor ; sinon « Non ».
                var overrides = !a.RequiresElevatedConfirmation && a.GetType().GetMethod(nameof(IActionHandler.RequiresElevatedConfirmationFor),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, [typeof(IReadOnlyDictionary<string, string>)]) is not null;
                var confirmation = a.RequiresElevatedConfirmation ? L("Always") : overrides ? L("Depending on parameters") : L("No");
                return new ActionRow(a.Id, a.Title, ModuleOf(a.Id), a.RequiresAdmin ? L("Yes") : L("No"), confirmation,
                    a.RequiresAdmin, a.RequiresElevatedConfirmation || overrides);
            })
            .OrderBy(a => a.Module, StringComparer.Create(Culture, false)).ThenBy(a => a.Title, StringComparer.Create(Culture, false))
            .ToList();

        var root = new StackPanel();
        var intro = AppUi.Caption(L("Parameterized actions (change DNS, install an app, disable a device…) validate each parameter (allowlisted formats, bounded lengths) in the interface, then again in the administrator process. Sensitive actions show a confirmation from the elevated process itself, which no non-elevated program can click on your behalf."));
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);

        root.Children.Add(AppUi.Tiles(
            AppUi.MetricTile("", rows.Count.ToString(Culture), L("actions in total")),
            AppUi.MetricTile("", rows.Count(r => r.IsAdmin).ToString(Culture), L("require admin rights"), "Info"),
            AppUi.MetricTile("", rows.Count(r => !r.IsAdmin).ToString(Culture), L("no admin rights"), "Success"),
            AppUi.MetricTile("", rows.Count(r => r.HasConfirmation).ToString(Culture), L("with elevated confirmation"), "Warning")));

        var grid = MakeGrid(L("Action list"), 420);
        grid.Columns.Add(Col(L("Action"), nameof(ActionRow.Title), new DataGridLength(2, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("ID"), nameof(ActionRow.Id), new DataGridLength(1.6, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("Module"), nameof(ActionRow.Module), new DataGridLength(1.1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("Admin"), nameof(ActionRow.Admin), new DataGridLength(72)));
        grid.Columns.Add(Col(L("Elevated confirmation"), nameof(ActionRow.Confirmation), new DataGridLength(170)));
        grid.ItemsSource = rows;

        var query = "";
        var search = AppUi.SearchBox(L("Search for an action or ID…"), q =>
        {
            query = q;
            grid.ItemsSource = query.Length == 0 ? rows : rows.Where(r => Contains(r.Title, query) || Contains(r.Id, query) || Contains(r.Module, query)).ToList();
        });
        search.MaxWidth = 440;
        search.HorizontalAlignment = HorizontalAlignment.Left;
        search.Margin = new Thickness(0, 4, 0, 10);
        search.Width = 440;
        root.Children.Add(search);
        root.Children.Add(grid);
        return root;
    }

    // ================================================================== Indisponible sur ce PC

    private FrameworkElement BuildUnavailable()
    {
        var p = AppHost.Profile;
        var root = new StackPanel();
        // Phrases complètes juxtaposées : description du PC, gestion éventuelle, principe.
        var machine = !p.HardwareLoaded
            ? L("This PC: {0} — {1} edition.", p.WindowsLabel, p.EditionLabel)
            : p.HasBattery
                ? L("This PC: {0} — {1} edition, {2} with battery.", p.WindowsLabel, p.EditionLabel, p.FormFactorLabel.ToLower(Culture))
                : L("This PC: {0} — {1} edition, {2}.", p.WindowsLabel, p.EditionLabel, p.FormFactorLabel.ToLower(Culture));
        var sentences = new List<string> { machine };
        if (p.IsManaged) sentences.Add(L("It's managed by an organization."));
        sentences.Add(L("Timonier never shows a setting that has no effect on your configuration as if it worked: it marks it unavailable and gives the reason."));
        var intro = AppUi.Caption(string.Join(" ", sentences));
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);
        if (!p.HardwareLoaded)
        {
            var info = PageScaffold.InfoBar(L("Detecting hardware: hardware-related reasons will appear in a few seconds."), "");
            info.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(info);
        }

        var groups = Rows().Where(r => !r.Available).GroupBy(r => r.Availability).OrderByDescending(g => g.Count()).ToList();
        if (groups.Count == 0)
        {
            root.Children.Add(AppUi.StateCard("", L("Everything is available on this PC"),
                L("Every setting in the catalog is compatible with your Windows edition, version, and hardware.")));
            return root;
        }
        foreach (var g in groups)
        {
            var s = new StackPanel();
            var head = new DockPanel();
            var badge = AppUi.Badge(LP(g.Count(), "{0} setting", "{0} settings"), "Neutral");
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);
            var reason = AppUi.Text(g.Key, "Pp.Body");
            reason.FontWeight = FontWeights.SemiBold;
            reason.Margin = new Thickness(0, 0, 12, 0);
            head.Children.Add(reason);
            s.Children.Add(head);
            var items = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var r in g.OrderBy(r => r.Category).ThenBy(r => r.Title))
            {
                var link = AppUi.Button($"{r.Title}", null, "Pp.LinkButton", (_, _) =>
                {
                    _tabs.Select(1);
                    _pendingTweak = r.Tweak.Id;
                    SelectPendingTweak();
                });
                link.ToolTip = L("{0} — see details", r.Category);
                link.Margin = new Thickness(0, 0, 16, 2);
                items.Children.Add(link);
            }
            s.Children.Add(items);
            root.Children.Add(AppUi.Card(s, new Thickness(0, 0, 0, 10)));
        }
        return root;
    }

    // ================================================================== Limites

    private static FrameworkElement BuildLimits()
    {
        var p = AppHost.Profile;
        var root = new StackPanel();
        var intro = AppUi.Caption(L("Timonier would rather tell you what it doesn't do than let you believe otherwise."));
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);

        var managed = p.IsManaged
            ? L("This PC is managed by an organization ({0}): its policies are applied periodically and replace locally changed values.", string.Join(", ", new[] {
                p.IsDomainJoined ? L("Active Directory domain") : null, p.IsEntraJoined ? "Microsoft Entra ID" : null, p.IsMdmManaged ? "MDM/Intune" : null }
                .Where(x => x is not null)))
            : L("This PC isn't managed by any organization (no domain, Entra ID, or MDM): your local settings aren't overwritten by remote policies.");
        var telemetry = p.SupportsTelemetryOff
            ? L("Your edition ({0}) supports the “Security” level (0).", p.EditionLabel)
            : L("On your edition ({0}), Windows enforces at least the “Required diagnostic data” level (1), even if the value 0 is written.", p.EditionLabel);

        (string Glyph, string Title, string Text)[] items =
        [
            ("", L("Block Ctrl+Alt+Del or Windows+L"),
                L("These key combinations are handled by Windows before any app (“secure attention sequence”): no software can intercept them. Guided access and kiosk mode can hide the options on the Ctrl+Alt+Del screen, but can't block the key combination.")),
            ("", L("Override an organization's policies"), managed),
            ("", L("Guarantee that a setting survives major updates"),
                L("Feature updates (e.g., 24H2 → 25H2) reinstall part of Windows and may restore services, tasks, apps, or default values. History and the dashboard let you check and reapply.")),
            ("", L("Completely turn off telemetry on all editions"),
                L("Diagnostic data level 0 is only honored by the Enterprise, Education, IoT, and Server editions. {0}", telemetry)),
            ("", L("Weaken Windows security"),
                L("Timonier doesn't offer to permanently disable Microsoft Defender, the firewall, User Account Control (UAC), SmartScreen, Secure Boot, or security updates. It can show their status and help you turn them back on. Updates can be paused within the limits Windows allows, never blocked permanently.")),
            ("", L("Change default apps for you"),
                L("File and protocol associations (browser, PDF…) are protected by Windows (user hash, UCPD driver). Timonier opens the right Settings page: you confirm the choice yourself.")),
            ("", L("Remote or cloud features"),
                L("No account, no sync, no remote control, no automatic Timonier updates: everything happens on this PC, done by you.")),
            ("", L("Install apps without a connection"),
                L("Timonier doesn't initiate any network connection itself. When you install or update an app, winget (Microsoft) downloads the program from the publisher's website or the Microsoft Store.")),
            ("", L("Act without your consent"),
                L("No setting is applied automatically. Administrator rights are only requested when a change requires them, through the Windows UAC prompt.")),
            ("", L("Permanently remove protected components"),
                L("Apps that Windows declares non-removable (Settings, interface components…) aren't forcibly removed: doing so breaks updates and other features.")),
        ];
        foreach (var (glyph, title, text) in items)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tile = AppUi.GlyphTile(glyph, "Pp.NeutralBackground", "Pp.TextSecondary");
            tile.Margin = new Thickness(0, 0, 14, 0);
            g.Children.Add(tile);
            var s = new StackPanel();
            var t = AppUi.Text(title);
            t.FontWeight = FontWeights.SemiBold;
            s.Children.Add(t);
            var d = AppUi.Caption(text);
            d.Margin = new Thickness(0, 3, 0, 0);
            s.Children.Add(d);
            Grid.SetColumn(s, 1);
            g.Children.Add(s);
            root.Children.Add(AppUi.Card(g, new Thickness(0, 0, 0, 8)));
        }
        return root;
    }

    // ================================================================== Sécurité et données

    private FrameworkElement BuildSecurity()
    {
        var root = new StackPanel();

        // Architecture
        root.Children.Add(AppUi.Section(L("How Timonier protects your PC")));
        var broker = AppHost.Broker;
        var state = broker.IsRunning
            ? broker.StartedAt is { } at
                ? L("Admin session active since {0}; closes automatically after {1} min of inactivity.", at.ToString("t", Culture), AppHost.Settings.BrokerIdleMinutes)
                : L("Admin session active; closes automatically after {0} min of inactivity.", AppHost.Settings.BrokerIdleMinutes)
            : L("No admin session is open right now: Timonier is running with your standard user rights.");
        var stateBar = PageScaffold.InfoBar(state, broker.IsRunning ? "" : "", broker.IsRunning ? "Pp.InfoBar.Warning" : "Pp.InfoBar.Success");
        stateBar.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(stateBar);

        (string Glyph, string Title, string Text)[] points =
        [
            ("", L("Non-elevated interface"), L("The Timonier window runs with your normal rights. It can't change anything that requires administrator rights.")),
            ("", L("On-demand administrator process"), L("When a change requires it, a second Timonier process is started through the Windows UAC prompt. Only one prompt per session.")),
            ("", L("Private, verified channel"), L("The two processes communicate through a named pipe with a random name, accessible only to your account and closed to the network. Each one verifies the other's identity (process ID and Timonier.exe location) before any exchange.")),
            ("", L("Closed catalog"), L("The administrator process only accepts setting and action IDs compiled into the app. It never receives commands, registry paths, or scripts.")),
            ("", L("Parameters validated twice"), L("Each parameter (IP address, app ID, account name…) is checked against an allowlist in the interface, then again by the administrator process.")),
            ("", L("Confirmations shown by the elevated process"), L("Sensitive actions (administrator account, automatic sign-in, installing outside the catalog…) are confirmed in a window of the administrator process, which a non-elevated program can't click on your behalf.")),
            ("", L("Protected machine history"), L("Undo data for admin changes is stored in HKLM\\SOFTWARE\\Timonier\\Journal, which only administrators can change: a malicious program without rights can't slip in fake instructions.")),
            ("", L("Automatic closing"), L("The admin session closes after {0} min of inactivity (adjustable), and immediately when Timonier closes.", AppHost.Settings.BrokerIdleMinutes)),
            ("", L("No command line"), L("System tools are launched by absolute path with separate arguments, without a command interpreter; the few PowerShell scripts are constants, with data passed through environment variables. Command injection is impossible by design.")),
            ("", L("No telemetry, no network"), L("Timonier sends nothing to anyone. It doesn't open any connection itself (only winget or the Microsoft Store do, when you install an app).")),
        ];
        var pointsPanel = new StackPanel();
        var first = true;
        foreach (var (glyph, title, text) in points)
        {
            if (!first) pointsPanel.Children.Add(AppUi.Divider(new Thickness(0, 8, 0, 8)));
            first = false;
            pointsPanel.Children.Add(AppUi.SettingRow(glyph, title, text, null));
        }
        root.Children.Add(AppUi.Card(pointsPanel));

        // Données stockées
        root.Children.Add(AppUi.Section(L("Data stored on this PC")));
        var dataPanel = new StackPanel();
        dataPanel.Children.Add(AppUi.StateCard("", L("Calculating sizes…"), busy: true));
        var dataCard = AppUi.Card(dataPanel);
        root.Children.Add(dataCard);
        _ = FillDataAsync(dataPanel);

        // Arrière-plan
        root.Children.Add(AppUi.Section(L("Background operation")));
        var reasons = AppHost.Background.Reasons;
        var bg = new StackPanel();
        if (reasons.Count == 0)
            bg.Children.Add(AppUi.SettingRow("", L("No background activity"),
                L("Timonier closes completely when you close the window: no service, no scheduled task, no resident process."), null));
        else
        {
            bg.Children.Add(AppUi.SettingRow("", L("Timonier needs to stay running"),
                AppHost.Settings.AllowBackground
                    ? L("If you close the window, Timonier will stay in the notification area to:")
                    : L("These features would need it, but you've disallowed background operation (Settings): Timonier will close with the window."), null));
            bg.Children.Add(AppUi.Bullets([.. reasons]));
        }
        if (AppHost.Settings.StartWithWindows || StartupRegistration.IsEnabled())
            bg.Children.Add(AppUi.Caption(L("Timonier is set to start with Windows (in the notification area). You can turn this off in its Settings.")));
        root.Children.Add(AppUi.Card(bg));
        return root;
    }

    private static async Task FillDataAsync(StackPanel panel)
    {
        var info = await Task.Run(DataInventory.Read);
        panel.Children.Clear();
        var intro = AppUi.Caption(L("Everything stays on this PC. Nothing is sent or synced. No secrets are stored in plain text: the PIN is stored as a PBKDF2 hash."));
        intro.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(intro);
        foreach (var item in info)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var s = new StackPanel();
            var t = AppUi.Text(item.Title);
            t.FontWeight = FontWeights.SemiBold;
            s.Children.Add(t);
            s.Children.Add(AppUi.Caption(item.Description));
            var path = AppUi.Caption(item.Location, tertiary: true);
            path.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(path);
            g.Children.Add(s);
            var size = AppUi.Text(item.Size, "Pp.Caption", wrap: false);
            size.VerticalAlignment = VerticalAlignment.Top;
            size.Margin = new Thickness(16, 2, 0, 0);
            Grid.SetColumn(size, 1);
            g.Children.Add(size);
            panel.Children.Add(g);
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var open = AppUi.Button(L("Open Timonier folder"), "", "Pp.Button", (_, _) => OpenFolder(AppPaths.LocalData));
        open.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(open);
        buttons.Children.Add(AppUi.Button(L("Open diagnostic logs"), "", "Pp.Button", (_, _) => OpenFolder(AppPaths.Logs)));
        panel.Children.Add(buttons);
    }

    internal static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            ProcessRunner.OpenFolder(path);
        }
        catch (Exception ex)
        {
            AppHost.Toasts.Show(L("Couldn't open the folder: {0}", ex.Message), ToastKind.Error);
        }
    }
}
