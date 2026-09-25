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
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] TabKeys = ["overview", "tweaks", "actions", "unavailable", "limits", "security"];

    private readonly SegmentedBar _tabs = new();
    private readonly ContentControl _host = new();
    private readonly Dictionary<int, FrameworkElement> _built = [];
    private List<TweakRow>? _rows;

    public TransparencyPage()
    {
        var stack = PageScaffold.Create(this, "Transparence",
            "Tout ce que Timonier peut modifier, comment il le fait, ce qu'il refuse de faire et ce qu'il conserve sur ce PC.",
            AppPagesModule.TransparencyGlyph);

        _tabs.Add("Vue d'ensemble", "");
        _tabs.Add("Réglages", "");
        _tabs.Add("Actions", "");
        _tabs.Add("Indisponible ici", "");
        _tabs.Add("Limites", "");
        _tabs.Add("Sécurité et données", "");
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
                content = AppUi.StateCard("", "Cette section n'a pas pu être affichée", ex.Message);
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
                return new TweakRow(t, t.Title, CategoryTitle(t.Category), t.RequiresAdmin ? "Oui" : "Non", RiskLabel(t.Risk),
                    EffectLabel(t.Effect), reason ?? "Disponible", reason is null);
            })
            .OrderBy(r => r.Category, StringComparer.CurrentCulture)
            .ThenBy(r => r.Title, StringComparer.CurrentCulture)];
        return _rows;
    }

    private static string CategoryTitle(string id) => AppHost.Registry.GetCategory(id)?.Title ?? id;

    internal static string RiskLabel(RiskLevel r) => r switch
    {
        RiskLevel.Moderate => "Modéré",
        RiskLevel.Advanced => "Avancé",
        _ => "Sans risque",
    };

    internal static string EffectLabel(ApplyEffect e)
    {
        if (e == ApplyEffect.None) return "Immédiat";
        var parts = new List<string>();
        if (e.HasFlag(ApplyEffect.RestartExplorer)) parts.Add("Redémarrage de l'Explorateur");
        if (e.HasFlag(ApplyEffect.SignOut)) parts.Add("Reconnexion");
        if (e.HasFlag(ApplyEffect.Reboot)) parts.Add("Redémarrage du PC");
        return string.Join(", ", parts);
    }

    private static string KindLabel(TweakKind k) => k switch
    {
        TweakKind.Toggle => "Interrupteur",
        TweakKind.Choice => "Choix",
        _ => "Action ponctuelle",
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
            AppUi.MetricTile("", rows.Count.ToString(Fr), "réglages au catalogue"),
            AppUi.MetricTile("", reg.Actions.Count.ToString(Fr), "actions paramétrées"),
            AppUi.MetricTile("", rows.Count(r => r.Available).ToString(Fr), "réglages disponibles sur ce PC", "Success"),
            AppUi.MetricTile("", rows.Count(r => !r.Tweak.RequiresAdmin).ToString(Fr), "réglages sans droits administrateur", "Success"),
            AppUi.MetricTile("", rows.Count(r => r.Tweak.RequiresAdmin).ToString(Fr), "réglages avec droits administrateur", "Info"),
            AppUi.MetricTile("", rows.Count(r => r.Tweak.IsReversible).ToString(Fr), "réglages annulables depuis le journal")));

        if (!AppHost.Profile.HardwareLoaded)
        {
            var info = PageScaffold.InfoBar("Détection du matériel en cours : les disponibilités liées au matériel (batterie, Wi-Fi, carte graphique…) seront précisées dans quelques secondes.", "");
            info.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(info);
        }

        // Tableau par catégorie
        root.Children.Add(AppUi.Section("Par catégorie"));
        var table = new Grid();
        string[] headers = ["Catégorie", "Réglages", "Admin", "Modérés", "Avancés", "Non annulables", "Indisponibles"];
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
            AddRow([group.Key, group.Count().ToString(Fr), group.Count(r => r.Tweak.RequiresAdmin).ToString(Fr),
                group.Count(r => r.Tweak.Risk == RiskLevel.Moderate).ToString(Fr), group.Count(r => r.Tweak.Risk == RiskLevel.Advanced).ToString(Fr),
                group.Count(r => !r.Tweak.IsReversible).ToString(Fr), group.Count(r => !r.Available).ToString(Fr)], false);
        }
        AddRow(["Total", rows.Count.ToString(Fr), rows.Count(r => r.Tweak.RequiresAdmin).ToString(Fr),
            rows.Count(r => r.Tweak.Risk == RiskLevel.Moderate).ToString(Fr), rows.Count(r => r.Tweak.Risk == RiskLevel.Advanced).ToString(Fr),
            rows.Count(r => !r.Tweak.IsReversible).ToString(Fr), rows.Count(r => !r.Available).ToString(Fr)], false);
        foreach (var t in table.Children.OfType<TextBlock>().Where(t => Grid.GetRow(t) == r0 - 1)) t.FontWeight = FontWeights.SemiBold;
        root.Children.Add(AppUi.Card(table));

        var legend = AppUi.Caption("« Modérés » : peuvent dégrader une fonctionnalité, une confirmation est demandée. « Avancés » : masqués hors mode avancé. "
            + "« Non annulables » : actions ponctuelles qui lancent un outil système (ex. nettoyage) ; tout le reste est enregistré dans le journal avec l'état précédent.");
        legend.Margin = new Thickness(2, 8, 0, 0);
        root.Children.Add(legend);

        // Actions par module
        root.Children.Add(AppUi.Section("Actions par module"));
        var actionsWrap = new WrapPanel();
        foreach (var g in reg.Actions.GroupBy(a => ModuleOf(a.Id)).OrderByDescending(g => g.Count()))
        {
            var s = new StackPanel();
            s.Children.Add(AppUi.Text(g.Key, "Pp.Body", wrap: false));
            var detail = $"{g.Count()} action{(g.Count() > 1 ? "s" : "")} · {g.Count(a => a.RequiresAdmin)} admin";
            var confirm = g.Count(a => a.RequiresElevatedConfirmation);
            if (confirm > 0) detail += $" · {confirm} avec confirmation élevée";
            s.Children.Add(AppUi.Caption(detail));
            var card = AppUi.Card(s, new Thickness(0, 0, 10, 10));
            card.MinWidth = 210;
            actionsWrap.Children.Add(card);
        }
        root.Children.Add(actionsWrap);

        var extra = AppUi.Caption($"S'y ajoutent {reg.HealthChecks.Count} contrôles de santé (lecture seule, sans droits d'administrateur), "
            + $"{reg.QuickActions.Count} actions rapides et {reg.Pages.Count} pages.");
        extra.Margin = new Thickness(2, 2, 0, 0);
        root.Children.Add(extra);

        // Cohérence du catalogue
        root.Children.Add(AppUi.Section("Cohérence du catalogue"));
        if (reg.Errors.Count == 0)
            root.Children.Add(PageScaffold.InfoBar("Aucune incohérence détectée au chargement : identifiants uniques, catégories connues, options valides.", "", "Pp.InfoBar.Success"));
        else
        {
            root.Children.Add(PageScaffold.InfoBar($"{reg.Errors.Count} incohérence(s) détectée(s) au chargement. Les éléments concernés sont ignorés ou partiellement disponibles.", "", "Pp.InfoBar.Warning"));
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
        var intro = AppUi.Caption("Liste exhaustive de ce que Timonier sait modifier. Le processus administrateur refuse tout réglage absent de ce catalogue : "
            + "il n'existe aucun moyen de lui faire exécuter autre chose. Sélectionnez une ligne pour voir les opérations exactes.");
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);

        var categories = new ComboBox { MinWidth = 220, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(categories, "Catégorie");
        categories.Items.Add("Toutes les catégories");
        foreach (var c in rows.Select(r => r.Category).Distinct()) categories.Items.Add(c);
        categories.SelectedIndex = 0;

        var onlyAvailable = new CheckBox { Content = "Disponibles sur ce PC", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        var count = AppUi.Caption("");
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(14, 0, 0, 0);

        var grid = MakeGrid("Catalogue des réglages", 380);
        grid.Columns.Add(Col("Réglage", nameof(TweakRow.Title), new DataGridLength(2.2, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col("Catégorie", nameof(TweakRow.Category), new DataGridLength(1.1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col("Admin", nameof(TweakRow.Admin), new DataGridLength(70)));
        grid.Columns.Add(Col("Risque", nameof(TweakRow.Risk), new DataGridLength(96)));
        grid.Columns.Add(Col("Effet", nameof(TweakRow.Effect), new DataGridLength(1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col("Sur ce PC", nameof(TweakRow.Availability), new DataGridLength(1.3, DataGridLengthUnitType.Star)));
        _grid = grid;

        var details = new ContentControl { Margin = new Thickness(0, 14, 0, 0) };
        details.Content = AppUi.StateCard("", "Sélectionnez un réglage", "Ses options, les opérations exactes (registre, services, tâches), ses conditions et sa réversibilité s'afficheront ici.");

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
            count.Text = list.Count <= 1 ? $"{list.Count} réglage" : $"{list.Count} réglages";
        }
        var search = AppUi.SearchBox("Rechercher un réglage, un identifiant…", q => { query = q; Apply(); });
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
        text is not null && Fr.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private static FrameworkElement BuildTweakDetails(TweakRow row)
    {
        var t = row.Tweak;
        var s = new StackPanel();
        var head = new DockPanel();
        var open = AppUi.Button("Ouvrir le réglage", "", "Pp.Button", (_, _) =>
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
        B(t.RequiresAdmin ? AppUi.Badge("Droits administrateur", "Info", "") : AppUi.Badge("Sans droits administrateur", "Success", ""));
        B(AppUi.Badge("Risque : " + RiskLabel(t.Risk).ToLower(Fr), t.Risk == RiskLevel.Safe ? "Success" : "Warning"));
        B(t.IsReversible ? AppUi.Badge("Annulable", "Success", "") : AppUi.Badge("Non annulable", "Warning", ""));
        if (t.Effect != ApplyEffect.None) B(AppUi.Badge(EffectLabel(t.Effect), "Warning", ""));
        B(row.Available ? AppUi.Badge("Disponible sur ce PC", "Success", "") : AppUi.Badge("Indisponible sur ce PC", "Danger", ""));
        s.Children.Add(badges);

        if (!row.Available) s.Children.Add(PageScaffold.KeyValue("Pourquoi indisponible", row.Availability));
        if (t.Warning is not null) s.Children.Add(PageScaffold.KeyValue("À savoir", t.Warning));
        if (t.WindowsDefault is { } def && t.GetOption(def) is { } defOpt) s.Children.Add(PageScaffold.KeyValue("Par défaut dans Windows", defOpt.Label));
        var rec = t.RecommendationFor(AppHost.Profile);
        if (rec is not null && t.GetOption(rec) is { } recOpt) s.Children.Add(PageScaffold.KeyValue("Recommandé pour ce PC", recOpt.Label));
        if (t.CustomDetect is not null) s.Children.Add(PageScaffold.KeyValue("Détection", "Personnalisée (lecture seule), en plus des opérations ci-dessous."));

        foreach (var opt in t.Options)
        {
            var title = AppUi.Text(t.Kind == TweakKind.Action ? "Opérations exécutées" : $"Option « {opt.Label} »", "Pp.Body");
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 14, 0, 2);
            s.Children.Add(title);
            if (opt.Description is not null) s.Children.Add(AppUi.Caption(opt.Description));
            if (opt.Operations.Count == 0)
                s.Children.Add(AppUi.Caption("Aucune opération (état constaté uniquement).", tertiary: true));
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

    private sealed record ActionRow(string Id, string Title, string Module, string Admin, string Confirmation);

    private static FrameworkElement BuildActions()
    {
        var reg = AppHost.Registry;
        var rows = reg.Actions
            .Select(a => new ActionRow(a.Id, a.Title, ModuleOf(a.Id), a.RequiresAdmin ? "Oui" : "Non",
                a.RequiresElevatedConfirmation ? "Toujours" : "Selon les paramètres ou non"))
            .OrderBy(a => a.Module, StringComparer.CurrentCulture).ThenBy(a => a.Title, StringComparer.CurrentCulture)
            .ToList();
        // « Selon les paramètres » n'est vrai que si l'action surcharge RequiresElevatedConfirmationFor ; sinon « Non ».
        rows = [.. rows.Select(r =>
        {
            var h = reg.GetAction(r.Id)!;
            if (h.RequiresElevatedConfirmation) return r;
            var overrides = h.GetType().GetMethod(nameof(IActionHandler.RequiresElevatedConfirmationFor),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, [typeof(IReadOnlyDictionary<string, string>)]) is not null;
            return r with { Confirmation = overrides ? "Selon les paramètres" : "Non" };
        })];

        var root = new StackPanel();
        var intro = AppUi.Caption("Les actions paramétrées (changer le DNS, installer une application, désactiver un périphérique…) valident chaque paramètre "
            + "(formats en liste blanche, longueurs bornées) dans l'interface, puis à nouveau dans le processus administrateur. Les actions sensibles "
            + "affichent une confirmation depuis le processus élevé lui-même, qu'aucun programme non élevé ne peut cliquer à votre place.");
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);

        root.Children.Add(AppUi.Tiles(
            AppUi.MetricTile("", rows.Count.ToString(Fr), "actions au total"),
            AppUi.MetricTile("", rows.Count(r => r.Admin == "Oui").ToString(Fr), "exigent les droits admin", "Info"),
            AppUi.MetricTile("", rows.Count(r => r.Admin == "Non").ToString(Fr), "sans droits admin", "Success"),
            AppUi.MetricTile("", rows.Count(r => r.Confirmation != "Non").ToString(Fr), "avec confirmation élevée", "Warning")));

        var grid = MakeGrid("Liste des actions", 420);
        grid.Columns.Add(Col("Action", nameof(ActionRow.Title), new DataGridLength(2, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col("Identifiant", nameof(ActionRow.Id), new DataGridLength(1.6, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col("Module", nameof(ActionRow.Module), new DataGridLength(1.1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col("Admin", nameof(ActionRow.Admin), new DataGridLength(72)));
        grid.Columns.Add(Col("Confirmation élevée", nameof(ActionRow.Confirmation), new DataGridLength(170)));
        grid.ItemsSource = rows;

        var query = "";
        var search = AppUi.SearchBox("Rechercher une action ou un identifiant…", q =>
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
        var summary = $"Ce PC : {p.WindowsLabel} — édition {p.EditionLabel}"
            + (p.HardwareLoaded ? $", {p.FormFactorLabel.ToLower(Fr)}{(p.HasBattery ? " avec batterie" : "")}" : "")
            + (p.IsManaged ? ", géré par une organisation." : ".");
        var intro = AppUi.Caption(summary + " Timonier n'affiche jamais un réglage sans effet sur votre configuration comme s'il fonctionnait : "
            + "il le marque indisponible et en donne la raison.");
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);
        if (!p.HardwareLoaded)
        {
            var info = PageScaffold.InfoBar("Détection du matériel en cours : les raisons liées au matériel apparaîtront dans quelques secondes.", "");
            info.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(info);
        }

        var groups = Rows().Where(r => !r.Available).GroupBy(r => r.Availability).OrderByDescending(g => g.Count()).ToList();
        if (groups.Count == 0)
        {
            root.Children.Add(AppUi.StateCard("", "Tout est disponible sur ce PC",
                "Chaque réglage du catalogue est compatible avec votre édition de Windows, votre version et votre matériel."));
            return root;
        }
        foreach (var g in groups)
        {
            var s = new StackPanel();
            var head = new DockPanel();
            var badge = AppUi.Badge(g.Count() <= 1 ? "1 réglage" : $"{g.Count()} réglages", "Neutral");
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
                link.ToolTip = $"{r.Category} — voir les détails";
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
        var intro = AppUi.Caption("Timonier préfère vous dire ce qu'il ne fait pas plutôt que de vous laisser croire le contraire.");
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);

        var managed = p.IsManaged
            ? "Ce PC est géré par une organisation (" + string.Join(", ", new[] {
                p.IsDomainJoined ? "domaine Active Directory" : null, p.IsEntraJoined ? "Microsoft Entra ID" : null, p.IsMdmManaged ? "MDM/Intune" : null }
                .Where(x => x is not null)) + ") : ses stratégies s'appliquent périodiquement et remplacent les valeurs modifiées localement."
            : "Ce PC n'est géré par aucune organisation (ni domaine, ni Entra ID, ni MDM) : vos réglages locaux ne sont pas écrasés par des stratégies distantes.";
        var telemetry = p.SupportsTelemetryOff
            ? $"Votre édition ({p.EditionLabel}) accepte le niveau « Sécurité » (0)."
            : $"Sur votre édition ({p.EditionLabel}), Windows applique au minimum le niveau « Données de diagnostic requises » (1), même si la valeur 0 est écrite.";

        (string Glyph, string Title, string Text)[] items =
        [
            ("", "Bloquer Ctrl+Alt+Suppr ou Windows+L",
                "Ces combinaisons sont traitées par Windows avant toute application (« séquence d'attention sécurisée ») : aucun logiciel ne peut les intercepter. "
                + "L'accès guidé et le mode kiosque peuvent masquer les options de l'écran Ctrl+Alt+Suppr, pas empêcher la combinaison."),
            ("", "Passer outre les stratégies d'une organisation", managed),
            ("", "Garantir qu'un réglage survive aux mises à jour majeures",
                "Les mises à jour de fonctionnalités (ex. 24H2 → 25H2) réinstallent une partie de Windows et peuvent rétablir des services, tâches, applications "
                + "ou valeurs par défaut. Le journal et le tableau de bord permettent de vérifier et de réappliquer."),
            ("", "Couper totalement la télémétrie sur toutes les éditions",
                "Le niveau 0 des données de diagnostic n'est respecté que par les éditions Entreprise, Éducation, IoT et Server. " + telemetry),
            ("", "Affaiblir la sécurité de Windows",
                "Timonier ne propose pas de désactiver durablement Microsoft Defender, le pare-feu, le contrôle de compte d'utilisateur (UAC), SmartScreen, "
                + "le démarrage sécurisé ou les mises à jour de sécurité. Il peut afficher leur état et vous aider à les réactiver. Les mises à jour peuvent être "
                + "suspendues dans les limites prévues par Windows, jamais bloquées définitivement."),
            ("", "Changer les applications par défaut à votre place",
                "Les associations de fichiers et de protocoles (navigateur, PDF…) sont protégées par Windows (hachage de l'utilisateur, pilote UCPD). "
                + "Timonier ouvre la bonne page des Paramètres : c'est vous qui validez le choix."),
            ("", "Fonctions à distance ou dans le cloud",
                "Pas de compte, pas de synchronisation, pas de contrôle à distance, pas de mise à jour automatique de Timonier : tout se fait sur ce PC, par vous."),
            ("", "Installer des applications sans connexion",
                "Timonier n'initie lui-même aucune connexion réseau. Quand vous installez ou mettez à jour une application, c'est winget (Microsoft) qui télécharge "
                + "le programme depuis le site de l'éditeur ou le Microsoft Store."),
            ("", "Agir sans votre accord",
                "Aucun réglage n'est appliqué automatiquement. Les droits administrateur ne sont demandés qu'au moment d'une modification qui les exige, via l'invite UAC de Windows."),
            ("", "Retirer définitivement des composants protégés",
                "Les applications que Windows déclare non supprimables (Paramètres, composants de l'interface…) ne sont pas retirées de force : "
                + "cela casse les mises à jour et d'autres fonctionnalités."),
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
        root.Children.Add(AppUi.Section("Comment Timonier protège votre PC"));
        var broker = AppHost.Broker;
        var state = broker.IsRunning
            ? $"Session administrateur active{(broker.StartedAt is { } at ? " depuis " + at.ToString("HH:mm", Fr) : "")} ; fermeture automatique après {AppHost.Settings.BrokerIdleMinutes} min d'inactivité."
            : "Aucune session administrateur n'est ouverte en ce moment : Timonier tourne avec vos droits d'utilisateur standard.";
        var stateBar = PageScaffold.InfoBar(state, broker.IsRunning ? "" : "", broker.IsRunning ? "Pp.InfoBar.Warning" : "Pp.InfoBar.Success");
        stateBar.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(stateBar);

        (string Glyph, string Title, string Text)[] points =
        [
            ("", "Interface sans élévation", "La fenêtre de Timonier tourne avec vos droits normaux. Elle ne peut rien modifier qui exige les droits administrateur."),
            ("", "Processus administrateur à la demande", "Quand une modification l'exige, un second processus Timonier est lancé via l'invite UAC de Windows. Une seule invite par session."),
            ("", "Canal privé et vérifié", "Les deux processus communiquent par un canal nommé au nom aléatoire, accessible uniquement à votre compte et fermé au réseau. "
                + "Chacun vérifie l'identité de l'autre (numéro de processus et emplacement de Timonier.exe) avant tout échange."),
            ("", "Catalogue fermé", "Le processus administrateur n'accepte que des identifiants de réglages et d'actions compilés dans l'application. "
                + "Il ne reçoit jamais de commande, de chemin de registre ni de script."),
            ("", "Paramètres validés deux fois", "Chaque paramètre (adresse IP, identifiant d'application, nom de compte…) est vérifié par liste blanche dans l'interface, puis à nouveau par le processus administrateur."),
            ("", "Confirmations affichées par le processus élevé", "Les actions sensibles (compte administrateur, ouverture de session automatique, installation hors catalogue…) sont confirmées dans une fenêtre "
                + "du processus administrateur, qu'un programme non élevé ne peut pas cliquer à votre place."),
            ("", "Journal machine protégé", "Les données d'annulation des modifications administrateur sont stockées dans HKLM\\SOFTWARE\\Timonier\\Journal, modifiable uniquement par les administrateurs : "
                + "un programme malveillant sans droits ne peut pas y glisser de fausses instructions."),
            ("", "Fermeture automatique", $"La session administrateur se ferme après {AppHost.Settings.BrokerIdleMinutes} min d'inactivité (réglable), et immédiatement quand Timonier se ferme."),
            ("", "Aucune ligne de commande", "Les outils système sont lancés par chemin absolu avec des arguments séparés, sans interpréteur de commandes ; les rares scripts PowerShell "
                + "sont des constantes, les données passant par des variables d'environnement. L'injection de commande est impossible par construction."),
            ("", "Aucune télémétrie, aucun réseau", "Timonier n'envoie rien, à personne. Il n'ouvre aucune connexion lui-même (seuls winget ou le Microsoft Store le font quand vous installez une application)."),
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
        root.Children.Add(AppUi.Section("Données stockées sur ce PC"));
        var dataPanel = new StackPanel();
        dataPanel.Children.Add(AppUi.StateCard("", "Calcul des tailles…", busy: true));
        var dataCard = AppUi.Card(dataPanel);
        root.Children.Add(dataCard);
        _ = FillDataAsync(dataPanel);

        // Arrière-plan
        root.Children.Add(AppUi.Section("Fonctionnement en arrière-plan"));
        var reasons = AppHost.Background.Reasons;
        var bg = new StackPanel();
        if (reasons.Count == 0)
            bg.Children.Add(AppUi.SettingRow("", "Aucune activité en arrière-plan",
                "Timonier se ferme complètement quand vous fermez la fenêtre : aucun service, aucune tâche planifiée, aucun processus résident.", null));
        else
        {
            bg.Children.Add(AppUi.SettingRow("", "Timonier doit rester actif",
                AppHost.Settings.AllowBackground
                    ? "Si vous fermez la fenêtre, Timonier restera dans la zone de notification pour :"
                    : "Ces fonctions en auraient besoin, mais vous avez interdit le fonctionnement en arrière-plan (Paramètres) : Timonier se fermera avec la fenêtre.", null));
            bg.Children.Add(AppUi.Bullets([.. reasons]));
        }
        if (AppHost.Settings.StartWithWindows || StartupRegistration.IsEnabled())
            bg.Children.Add(AppUi.Caption("Timonier est configuré pour démarrer avec Windows (dans la zone de notification). Désactivable dans ses Paramètres."));
        root.Children.Add(AppUi.Card(bg));
        return root;
    }

    private static async Task FillDataAsync(StackPanel panel)
    {
        var info = await Task.Run(DataInventory.Read);
        panel.Children.Clear();
        var intro = AppUi.Caption("Tout reste sur ce PC. Rien n'est envoyé ni synchronisé. Aucun secret n'est stocké en clair : le code PIN est conservé sous forme de hachage PBKDF2.");
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
        var open = AppUi.Button("Ouvrir le dossier de Timonier", "", "Pp.Button", (_, _) => OpenFolder(AppPaths.LocalData));
        open.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(open);
        buttons.Children.Add(AppUi.Button("Ouvrir les journaux de diagnostic", "", "Pp.Button", (_, _) => OpenFolder(AppPaths.Logs)));
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
            AppHost.Toasts.Show("Impossible d'ouvrir le dossier : " + ex.Message, ToastKind.Error);
        }
    }
}
