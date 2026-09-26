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
        var stack = PageScaffold.Create(this, L("Transparence"),
            L("Tout ce que Timonier peut modifier, comment il le fait, ce qu'il refuse de faire et ce qu'il conserve sur ce PC."),
            AppPagesModule.TransparencyGlyph);

        _tabs.Add(L("Vue d'ensemble"), "");
        _tabs.Add(L("Réglages"), "");
        _tabs.Add(L("Actions"), "");
        _tabs.Add(L("Indisponible ici"), "");
        _tabs.Add(L("Limites"), "");
        _tabs.Add(L("Sécurité et données"), "");
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
                content = AppUi.StateCard("", L("Cette section n'a pas pu être affichée"), ex.Message);
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
                return new TweakRow(t, t.Title, CategoryTitle(t.Category), t.RequiresAdmin ? L("Oui") : L("Non"), RiskLabel(t.Risk),
                    EffectLabel(t.Effect), reason ?? L("Disponible"), reason is null);
            })
            .OrderBy(r => r.Category, StringComparer.Create(Culture, false))
            .ThenBy(r => r.Title, StringComparer.Create(Culture, false))];
        return _rows;
    }

    private static string CategoryTitle(string id) => AppHost.Registry.GetCategory(id)?.Title ?? id;

    internal static string RiskLabel(RiskLevel r) => r switch
    {
        RiskLevel.Moderate => L("Modéré"),
        RiskLevel.Advanced => L("Avancé"),
        _ => L("Sans risque"),
    };

    internal static string EffectLabel(ApplyEffect e)
    {
        if (e == ApplyEffect.None) return L("Immédiat");
        var parts = new List<string>();
        if (e.HasFlag(ApplyEffect.RestartExplorer)) parts.Add(L("Redémarrage de l'Explorateur"));
        if (e.HasFlag(ApplyEffect.SignOut)) parts.Add(L("Reconnexion"));
        if (e.HasFlag(ApplyEffect.Reboot)) parts.Add(L("Redémarrage du PC"));
        return string.Join(", ", parts);
    }

    private static string KindLabel(TweakKind k) => k switch
    {
        TweakKind.Toggle => LC("tweak kind", "Interrupteur"),
        TweakKind.Choice => LC("tweak kind", "Choix"),
        _ => LC("tweak kind", "Action ponctuelle"),
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
            AppUi.MetricTile("", rows.Count.ToString(Culture), L("réglages au catalogue")),
            AppUi.MetricTile("", reg.Actions.Count.ToString(Culture), L("actions paramétrées")),
            AppUi.MetricTile("", rows.Count(r => r.Available).ToString(Culture), L("réglages disponibles sur ce PC"), "Success"),
            AppUi.MetricTile("", rows.Count(r => !r.Tweak.RequiresAdmin).ToString(Culture), L("réglages sans droits administrateur"), "Success"),
            AppUi.MetricTile("", rows.Count(r => r.Tweak.RequiresAdmin).ToString(Culture), L("réglages avec droits administrateur"), "Info"),
            AppUi.MetricTile("", rows.Count(r => r.Tweak.IsReversible).ToString(Culture), L("réglages annulables depuis le journal"))));

        if (!AppHost.Profile.HardwareLoaded)
        {
            var info = PageScaffold.InfoBar(L("Détection du matériel en cours : les disponibilités liées au matériel (batterie, Wi-Fi, carte graphique…) seront précisées dans quelques secondes."), "");
            info.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(info);
        }

        // Tableau par catégorie
        root.Children.Add(AppUi.Section(L("Par catégorie")));
        var table = new Grid();
        string[] headers = [L("Catégorie"), L("Réglages"), L("Admin"), L("Modérés"), L("Avancés"), L("Non annulables"), L("Indisponibles")];
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

        var legend = AppUi.Caption(L("« Modérés » : peuvent dégrader une fonctionnalité, une confirmation est demandée. « Avancés » : masqués hors mode avancé. « Non annulables » : actions ponctuelles qui lancent un outil système (ex. nettoyage) ; tout le reste est enregistré dans le journal avec l'état précédent."));
        legend.Margin = new Thickness(2, 8, 0, 0);
        root.Children.Add(legend);

        // Actions par module
        root.Children.Add(AppUi.Section(L("Actions par module")));
        var actionsWrap = new WrapPanel();
        foreach (var g in reg.Actions.GroupBy(a => ModuleOf(a.Id)).OrderByDescending(g => g.Count()))
        {
            var s = new StackPanel();
            s.Children.Add(AppUi.Text(g.Key, "Pp.Body", wrap: false));
            var detail = new List<string> { LP(g.Count(), "{0} action", "{0} actions"), LP(g.Count(a => a.RequiresAdmin), "{0} admin", "{0} admin") };
            var confirm = g.Count(a => a.RequiresElevatedConfirmation);
            if (confirm > 0) detail.Add(LP(confirm, "{0} avec confirmation élevée", "{0} avec confirmation élevée"));
            s.Children.Add(AppUi.Caption(string.Join(" · ", detail)));
            var card = AppUi.Card(s, new Thickness(0, 0, 10, 10));
            card.MinWidth = 210;
            actionsWrap.Children.Add(card);
        }
        root.Children.Add(actionsWrap);

        var extra = AppUi.Caption(L("S'y ajoutent {0} contrôles de santé (lecture seule, sans droits d'administrateur), {1} actions rapides et {2} pages.", reg.HealthChecks.Count, reg.QuickActions.Count, reg.Pages.Count));
        extra.Margin = new Thickness(2, 2, 0, 0);
        root.Children.Add(extra);

        // Cohérence du catalogue
        root.Children.Add(AppUi.Section(L("Cohérence du catalogue")));
        if (reg.Errors.Count == 0)
            root.Children.Add(PageScaffold.InfoBar(L("Aucune incohérence détectée au chargement : identifiants uniques, catégories connues, options valides."), "", "Pp.InfoBar.Success"));
        else
        {
            root.Children.Add(PageScaffold.InfoBar(LP(reg.Errors.Count,
                "{0} incohérence détectée au chargement. Les éléments concernés sont ignorés ou partiellement disponibles.",
                "{0} incohérences détectées au chargement. Les éléments concernés sont ignorés ou partiellement disponibles."), "", "Pp.InfoBar.Warning"));
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
        var intro = AppUi.Caption(L("Liste exhaustive de ce que Timonier sait modifier. Le processus administrateur refuse tout réglage absent de ce catalogue : il n'existe aucun moyen de lui faire exécuter autre chose. Sélectionnez une ligne pour voir les opérations exactes."));
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);

        var categories = new ComboBox { MinWidth = 220, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(categories, L("Catégorie"));
        categories.Items.Add(L("Toutes les catégories"));
        foreach (var c in rows.Select(r => r.Category).Distinct()) categories.Items.Add(c);
        categories.SelectedIndex = 0;

        var onlyAvailable = new CheckBox { Content = L("Disponibles sur ce PC"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        var count = AppUi.Caption("");
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(14, 0, 0, 0);

        var grid = MakeGrid(L("Catalogue des réglages"), 380);
        grid.Columns.Add(Col(L("Réglage"), nameof(TweakRow.Title), new DataGridLength(2.2, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("Catégorie"), nameof(TweakRow.Category), new DataGridLength(1.1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("Admin"), nameof(TweakRow.Admin), new DataGridLength(70)));
        grid.Columns.Add(Col(L("Risque"), nameof(TweakRow.Risk), new DataGridLength(96)));
        grid.Columns.Add(Col(L("Effet"), nameof(TweakRow.Effect), new DataGridLength(1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("Sur ce PC"), nameof(TweakRow.Availability), new DataGridLength(1.3, DataGridLengthUnitType.Star)));
        _grid = grid;

        var details = new ContentControl { Margin = new Thickness(0, 14, 0, 0) };
        details.Content = AppUi.StateCard("", L("Sélectionnez un réglage"), L("Ses options, les opérations exactes (registre, services, tâches), ses conditions et sa réversibilité s'afficheront ici."));

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
            count.Text = LP(list.Count, "{0} réglage", "{0} réglages");
        }
        var search = AppUi.SearchBox(L("Rechercher un réglage, un identifiant…"), q => { query = q; Apply(); });
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
        var open = AppUi.Button(L("Ouvrir le réglage"), "", "Pp.Button", (_, _) =>
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
        B(t.RequiresAdmin ? AppUi.Badge(L("Droits administrateur"), "Info", "") : AppUi.Badge(L("Sans droits administrateur"), "Success", ""));
        B(AppUi.Badge(L("Risque : {0}", RiskLabel(t.Risk).ToLower(Culture)), t.Risk == RiskLevel.Safe ? "Success" : "Warning"));
        B(t.IsReversible ? AppUi.Badge(LC("badge", "Annulable"), "Success", "") : AppUi.Badge(LC("badge", "Non annulable"), "Warning", ""));
        if (t.Effect != ApplyEffect.None) B(AppUi.Badge(EffectLabel(t.Effect), "Warning", ""));
        B(row.Available ? AppUi.Badge(L("Disponible sur ce PC"), "Success", "") : AppUi.Badge(L("Indisponible sur ce PC"), "Danger", ""));
        s.Children.Add(badges);

        if (!row.Available) s.Children.Add(PageScaffold.KeyValue(L("Pourquoi indisponible"), row.Availability));
        if (t.Warning is not null) s.Children.Add(PageScaffold.KeyValue(L("À savoir"), t.Warning));
        if (t.WindowsDefault is { } def && t.GetOption(def) is { } defOpt) s.Children.Add(PageScaffold.KeyValue(L("Par défaut dans Windows"), defOpt.Label));
        var rec = t.RecommendationFor(AppHost.Profile);
        if (rec is not null && t.GetOption(rec) is { } recOpt) s.Children.Add(PageScaffold.KeyValue(L("Recommandé pour ce PC"), recOpt.Label));
        if (t.CustomDetect is not null) s.Children.Add(PageScaffold.KeyValue(L("Détection"), L("Personnalisée (lecture seule), en plus des opérations ci-dessous.")));

        foreach (var opt in t.Options)
        {
            var title = AppUi.Text(t.Kind == TweakKind.Action ? L("Opérations exécutées") : L("Option « {0} »", opt.Label), "Pp.Body");
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 14, 0, 2);
            s.Children.Add(title);
            if (opt.Description is not null) s.Children.Add(AppUi.Caption(opt.Description));
            if (opt.Operations.Count == 0)
                s.Children.Add(AppUi.Caption(L("Aucune opération (état constaté uniquement)."), tertiary: true));
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
                var confirmation = a.RequiresElevatedConfirmation ? L("Toujours") : overrides ? L("Selon les paramètres") : L("Non");
                return new ActionRow(a.Id, a.Title, ModuleOf(a.Id), a.RequiresAdmin ? L("Oui") : L("Non"), confirmation,
                    a.RequiresAdmin, a.RequiresElevatedConfirmation || overrides);
            })
            .OrderBy(a => a.Module, StringComparer.Create(Culture, false)).ThenBy(a => a.Title, StringComparer.Create(Culture, false))
            .ToList();

        var root = new StackPanel();
        var intro = AppUi.Caption(L("Les actions paramétrées (changer le DNS, installer une application, désactiver un périphérique…) valident chaque paramètre (formats en liste blanche, longueurs bornées) dans l'interface, puis à nouveau dans le processus administrateur. Les actions sensibles affichent une confirmation depuis le processus élevé lui-même, qu'aucun programme non élevé ne peut cliquer à votre place."));
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);

        root.Children.Add(AppUi.Tiles(
            AppUi.MetricTile("", rows.Count.ToString(Culture), L("actions au total")),
            AppUi.MetricTile("", rows.Count(r => r.IsAdmin).ToString(Culture), L("exigent les droits admin"), "Info"),
            AppUi.MetricTile("", rows.Count(r => !r.IsAdmin).ToString(Culture), L("sans droits admin"), "Success"),
            AppUi.MetricTile("", rows.Count(r => r.HasConfirmation).ToString(Culture), L("avec confirmation élevée"), "Warning")));

        var grid = MakeGrid(L("Liste des actions"), 420);
        grid.Columns.Add(Col(L("Action"), nameof(ActionRow.Title), new DataGridLength(2, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("Identifiant"), nameof(ActionRow.Id), new DataGridLength(1.6, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("Module"), nameof(ActionRow.Module), new DataGridLength(1.1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Col(L("Admin"), nameof(ActionRow.Admin), new DataGridLength(72)));
        grid.Columns.Add(Col(L("Confirmation élevée"), nameof(ActionRow.Confirmation), new DataGridLength(170)));
        grid.ItemsSource = rows;

        var query = "";
        var search = AppUi.SearchBox(L("Rechercher une action ou un identifiant…"), q =>
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
            ? L("Ce PC : {0} — édition {1}.", p.WindowsLabel, p.EditionLabel)
            : p.HasBattery
                ? L("Ce PC : {0} — édition {1}, {2} avec batterie.", p.WindowsLabel, p.EditionLabel, p.FormFactorLabel.ToLower(Culture))
                : L("Ce PC : {0} — édition {1}, {2}.", p.WindowsLabel, p.EditionLabel, p.FormFactorLabel.ToLower(Culture));
        var sentences = new List<string> { machine };
        if (p.IsManaged) sentences.Add(L("Il est géré par une organisation."));
        sentences.Add(L("Timonier n'affiche jamais un réglage sans effet sur votre configuration comme s'il fonctionnait : il le marque indisponible et en donne la raison."));
        var intro = AppUi.Caption(string.Join(" ", sentences));
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);
        if (!p.HardwareLoaded)
        {
            var info = PageScaffold.InfoBar(L("Détection du matériel en cours : les raisons liées au matériel apparaîtront dans quelques secondes."), "");
            info.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(info);
        }

        var groups = Rows().Where(r => !r.Available).GroupBy(r => r.Availability).OrderByDescending(g => g.Count()).ToList();
        if (groups.Count == 0)
        {
            root.Children.Add(AppUi.StateCard("", L("Tout est disponible sur ce PC"),
                L("Chaque réglage du catalogue est compatible avec votre édition de Windows, votre version et votre matériel.")));
            return root;
        }
        foreach (var g in groups)
        {
            var s = new StackPanel();
            var head = new DockPanel();
            var badge = AppUi.Badge(LP(g.Count(), "{0} réglage", "{0} réglages"), "Neutral");
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
                link.ToolTip = L("{0} — voir les détails", r.Category);
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
        var intro = AppUi.Caption(L("Timonier préfère vous dire ce qu'il ne fait pas plutôt que de vous laisser croire le contraire."));
        intro.Margin = new Thickness(2, 0, 0, 12);
        root.Children.Add(intro);

        var managed = p.IsManaged
            ? L("Ce PC est géré par une organisation ({0}) : ses stratégies s'appliquent périodiquement et remplacent les valeurs modifiées localement.", string.Join(", ", new[] {
                p.IsDomainJoined ? L("domaine Active Directory") : null, p.IsEntraJoined ? "Microsoft Entra ID" : null, p.IsMdmManaged ? "MDM/Intune" : null }
                .Where(x => x is not null)))
            : L("Ce PC n'est géré par aucune organisation (ni domaine, ni Entra ID, ni MDM) : vos réglages locaux ne sont pas écrasés par des stratégies distantes.");
        var telemetry = p.SupportsTelemetryOff
            ? L("Votre édition ({0}) accepte le niveau « Sécurité » (0).", p.EditionLabel)
            : L("Sur votre édition ({0}), Windows applique au minimum le niveau « Données de diagnostic requises » (1), même si la valeur 0 est écrite.", p.EditionLabel);

        (string Glyph, string Title, string Text)[] items =
        [
            ("", L("Bloquer Ctrl+Alt+Suppr ou Windows+L"),
                L("Ces combinaisons sont traitées par Windows avant toute application (« séquence d'attention sécurisée ») : aucun logiciel ne peut les intercepter. L'accès guidé et le mode kiosque peuvent masquer les options de l'écran Ctrl+Alt+Suppr, pas empêcher la combinaison.")),
            ("", L("Passer outre les stratégies d'une organisation"), managed),
            ("", L("Garantir qu'un réglage survive aux mises à jour majeures"),
                L("Les mises à jour de fonctionnalités (ex. 24H2 → 25H2) réinstallent une partie de Windows et peuvent rétablir des services, tâches, applications ou valeurs par défaut. Le journal et le tableau de bord permettent de vérifier et de réappliquer.")),
            ("", L("Couper totalement la télémétrie sur toutes les éditions"),
                L("Le niveau 0 des données de diagnostic n'est respecté que par les éditions Entreprise, Éducation, IoT et Server. {0}", telemetry)),
            ("", L("Affaiblir la sécurité de Windows"),
                L("Timonier ne propose pas de désactiver durablement Microsoft Defender, le pare-feu, le contrôle de compte d'utilisateur (UAC), SmartScreen, le démarrage sécurisé ou les mises à jour de sécurité. Il peut afficher leur état et vous aider à les réactiver. Les mises à jour peuvent être suspendues dans les limites prévues par Windows, jamais bloquées définitivement.")),
            ("", L("Changer les applications par défaut à votre place"),
                L("Les associations de fichiers et de protocoles (navigateur, PDF…) sont protégées par Windows (hachage de l'utilisateur, pilote UCPD). Timonier ouvre la bonne page des Paramètres : c'est vous qui validez le choix.")),
            ("", L("Fonctions à distance ou dans le cloud"),
                L("Pas de compte, pas de synchronisation, pas de contrôle à distance, pas de mise à jour automatique de Timonier : tout se fait sur ce PC, par vous.")),
            ("", L("Installer des applications sans connexion"),
                L("Timonier n'initie lui-même aucune connexion réseau. Quand vous installez ou mettez à jour une application, c'est winget (Microsoft) qui télécharge le programme depuis le site de l'éditeur ou le Microsoft Store.")),
            ("", L("Agir sans votre accord"),
                L("Aucun réglage n'est appliqué automatiquement. Les droits administrateur ne sont demandés qu'au moment d'une modification qui les exige, via l'invite UAC de Windows.")),
            ("", L("Retirer définitivement des composants protégés"),
                L("Les applications que Windows déclare non supprimables (Paramètres, composants de l'interface…) ne sont pas retirées de force : cela casse les mises à jour et d'autres fonctionnalités.")),
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
        root.Children.Add(AppUi.Section(L("Comment Timonier protège votre PC")));
        var broker = AppHost.Broker;
        var state = broker.IsRunning
            ? broker.StartedAt is { } at
                ? L("Session administrateur active depuis {0} ; fermeture automatique après {1} min d'inactivité.", at.ToString("t", Culture), AppHost.Settings.BrokerIdleMinutes)
                : L("Session administrateur active ; fermeture automatique après {0} min d'inactivité.", AppHost.Settings.BrokerIdleMinutes)
            : L("Aucune session administrateur n'est ouverte en ce moment : Timonier tourne avec vos droits d'utilisateur standard.");
        var stateBar = PageScaffold.InfoBar(state, broker.IsRunning ? "" : "", broker.IsRunning ? "Pp.InfoBar.Warning" : "Pp.InfoBar.Success");
        stateBar.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(stateBar);

        (string Glyph, string Title, string Text)[] points =
        [
            ("", L("Interface sans élévation"), L("La fenêtre de Timonier tourne avec vos droits normaux. Elle ne peut rien modifier qui exige les droits administrateur.")),
            ("", L("Processus administrateur à la demande"), L("Quand une modification l'exige, un second processus Timonier est lancé via l'invite UAC de Windows. Une seule invite par session.")),
            ("", L("Canal privé et vérifié"), L("Les deux processus communiquent par un canal nommé au nom aléatoire, accessible uniquement à votre compte et fermé au réseau. Chacun vérifie l'identité de l'autre (numéro de processus et emplacement de Timonier.exe) avant tout échange.")),
            ("", L("Catalogue fermé"), L("Le processus administrateur n'accepte que des identifiants de réglages et d'actions compilés dans l'application. Il ne reçoit jamais de commande, de chemin de registre ni de script.")),
            ("", L("Paramètres validés deux fois"), L("Chaque paramètre (adresse IP, identifiant d'application, nom de compte…) est vérifié par liste blanche dans l'interface, puis à nouveau par le processus administrateur.")),
            ("", L("Confirmations affichées par le processus élevé"), L("Les actions sensibles (compte administrateur, ouverture de session automatique, installation hors catalogue…) sont confirmées dans une fenêtre du processus administrateur, qu'un programme non élevé ne peut pas cliquer à votre place.")),
            ("", L("Journal machine protégé"), L("Les données d'annulation des modifications administrateur sont stockées dans HKLM\\SOFTWARE\\Timonier\\Journal, modifiable uniquement par les administrateurs : un programme malveillant sans droits ne peut pas y glisser de fausses instructions.")),
            ("", L("Fermeture automatique"), L("La session administrateur se ferme après {0} min d'inactivité (réglable), et immédiatement quand Timonier se ferme.", AppHost.Settings.BrokerIdleMinutes)),
            ("", L("Aucune ligne de commande"), L("Les outils système sont lancés par chemin absolu avec des arguments séparés, sans interpréteur de commandes ; les rares scripts PowerShell sont des constantes, les données passant par des variables d'environnement. L'injection de commande est impossible par construction.")),
            ("", L("Aucune télémétrie, aucun réseau"), L("Timonier n'envoie rien, à personne. Il n'ouvre aucune connexion lui-même (seuls winget ou le Microsoft Store le font quand vous installez une application).")),
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
        root.Children.Add(AppUi.Section(L("Données stockées sur ce PC")));
        var dataPanel = new StackPanel();
        dataPanel.Children.Add(AppUi.StateCard("", L("Calcul des tailles…"), busy: true));
        var dataCard = AppUi.Card(dataPanel);
        root.Children.Add(dataCard);
        _ = FillDataAsync(dataPanel);

        // Arrière-plan
        root.Children.Add(AppUi.Section(L("Fonctionnement en arrière-plan")));
        var reasons = AppHost.Background.Reasons;
        var bg = new StackPanel();
        if (reasons.Count == 0)
            bg.Children.Add(AppUi.SettingRow("", L("Aucune activité en arrière-plan"),
                L("Timonier se ferme complètement quand vous fermez la fenêtre : aucun service, aucune tâche planifiée, aucun processus résident."), null));
        else
        {
            bg.Children.Add(AppUi.SettingRow("", L("Timonier doit rester actif"),
                AppHost.Settings.AllowBackground
                    ? L("Si vous fermez la fenêtre, Timonier restera dans la zone de notification pour :")
                    : L("Ces fonctions en auraient besoin, mais vous avez interdit le fonctionnement en arrière-plan (Paramètres) : Timonier se fermera avec la fenêtre."), null));
            bg.Children.Add(AppUi.Bullets([.. reasons]));
        }
        if (AppHost.Settings.StartWithWindows || StartupRegistration.IsEnabled())
            bg.Children.Add(AppUi.Caption(L("Timonier est configuré pour démarrer avec Windows (dans la zone de notification). Désactivable dans ses Paramètres.")));
        root.Children.Add(AppUi.Card(bg));
        return root;
    }

    private static async Task FillDataAsync(StackPanel panel)
    {
        var info = await Task.Run(DataInventory.Read);
        panel.Children.Clear();
        var intro = AppUi.Caption(L("Tout reste sur ce PC. Rien n'est envoyé ni synchronisé. Aucun secret n'est stocké en clair : le code PIN est conservé sous forme de hachage PBKDF2."));
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
        var open = AppUi.Button(L("Ouvrir le dossier de Timonier"), "", "Pp.Button", (_, _) => OpenFolder(AppPaths.LocalData));
        open.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(open);
        buttons.Children.Add(AppUi.Button(L("Ouvrir les journaux de diagnostic"), "", "Pp.Button", (_, _) => OpenFolder(AppPaths.Logs)));
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
            AppHost.Toasts.Show(L("Impossible d'ouvrir le dossier : {0}", ex.Message), ToastKind.Error);
        }
    }
}
