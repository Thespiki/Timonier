using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Search;
using Timonier.UI.Controls;
using Timonier.UI.Services;
using static Timonier.Modules.WindowsTools.WindowsToolsUi;

namespace Timonier.Modules.WindowsTools;

/// <summary>
/// Page « Outils Windows » : filtre + pastilles de catégories, grille d'outils, puis pages des Paramètres Windows
/// regroupées par section (repliables, construites à la première ouverture).
/// </summary>
public sealed class WindowsToolsPage : UserControl, INavigationAware
{
    private const int FilterAll = -1;
    private const int FilterSettings = -2;

    private readonly TextBox _search;
    private readonly WrapPanel _chips = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly List<(int Filter, Button Button, TextBlock Count)> _chipButtons = [];
    private readonly StackPanel _toolsHost = new();
    private readonly StackPanel _settingsHost = new();
    private readonly StackPanel _settingsHeader = new() { Margin = new Thickness(0, 18, 0, 8) };
    private readonly Border _empty;
    private readonly TextBlock _emptyText;
    private readonly List<ToolGroupView> _toolGroups = [];
    private readonly List<SettingsSectionView> _sections = [];
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private int _filter = FilterAll;
    private string[] _tokens = [];

    public WindowsToolsPage()
    {
        var stack = PageScaffold.Create(this, L("Windows Tools"),
            L("Admin consoles, classic panels, special folders and direct access to every Settings page. Timonier only opens the tool you choose: nothing is changed."), WindowsToolsModule.Glyph);

        // ------------------------------------------------------------ barre de filtre
        _search = new TextBox { Tag = L("Filter: drivers, partition, firewall, environment variables…") }.Styled("Pp.SearchBox");
        System.Windows.Automation.AutomationProperties.SetName(_search, L("Filter tools and settings"));
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && _search.Text.Length > 0) { _search.Clear(); e.Handled = true; }
        };
        _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFilter(); };

        var tools = ToolsCatalog.Available;
        var sections = SettingsCatalog.Sections;
        var linkCount = sections.Sum(s => s.Links.Count);

        AddChip(L("All"), null, FilterAll, tools.Count + linkCount);
        foreach (var (group, title, glyph) in ToolsCatalog.Groups)
        {
            var n = tools.Count(t => t.Group == group);
            if (n > 0) AddChip(title, glyph, (int)group, n);
        }
        AddChip(L("Windows Settings"), "", FilterSettings, linkCount);

        var legend = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        legend.Children.Add(Badge(L("Admin"), "", "Pp.WarningBackground", "Pp.Warning"));
        legend.Children.Add(new TextBlock
        {
            Text = L("Windows asks for administrator permission (UAC) when it opens."),
            Margin = new Thickness(0, 0, 16, 4), VerticalAlignment = VerticalAlignment.Center,
        }.Styled("Pp.Caption"));
        if (tools.Any(t => t.ProOnly))
        {
            legend.Children.Add(Badge("Pro+", "", "Pp.InfoBackground", "Pp.Info"));
            legend.Children.Add(new TextBlock
            {
                Text = L("Pro, Education and Enterprise editions."),
                Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center,
            }.Styled("Pp.Caption"));
        }

        var bar = new StackPanel();
        bar.Children.Add(_search);
        bar.Children.Add(_chips);
        bar.Children.Add(legend);
        var barCard = PageScaffold.Card(bar);
        barCard.Padding = new Thickness(16, 14, 16, 10);
        barCard.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(barCard);

        // ------------------------------------------------------------ état vide
        _emptyText = new TextBlock { TextWrapping = TextWrapping.Wrap }.Styled("Pp.Body");
        var emptyPanel = new DockPanel();
        var emptyIcon = Icon("", 16);
        emptyIcon.Margin = new Thickness(0, 1, 10, 0);
        emptyIcon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(emptyIcon, Dock.Left);
        emptyPanel.Children.Add(emptyIcon);
        emptyPanel.Children.Add(_emptyText);
        _empty = new Border { Child = emptyPanel, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 8) }.Styled("Pp.InfoBar");
        stack.Children.Add(_empty);

        // ------------------------------------------------------------ outils
        stack.Children.Add(_toolsHost);
        if (tools.Count == 0)
            _toolsHost.Children.Add(PageScaffold.InfoBar(L("No Windows tools were found in the System32 folder."), "", "Pp.InfoBar.Warning"));
        foreach (var (group, title, glyph) in ToolsCatalog.Groups)
        {
            var items = tools.Where(t => t.Group == group).ToList();
            if (items.Count == 0) continue;
            var view = new ToolGroupView(group, title, glyph, items);
            _toolGroups.Add(view);
            _toolsHost.Children.Add(view.Root);
        }

        // ------------------------------------------------------------ Paramètres Windows
        var settingsHeader = _settingsHeader;
        settingsHeader.Children.Add(PageScaffold.Section(L("Windows Settings")));
        settingsHeader.Children.Add(new TextBlock
        {
            Text = LP(linkCount,
                "{0} page of the Settings app, organized as in Windows. Expand a section, then click to open the page.",
                "{0} pages of the Settings app, organized as in Windows. Expand a section, then click to open the page."),
            TextWrapping = TextWrapping.Wrap,
        }.Styled("Pp.Caption"));
        _settingsHost.Children.Add(settingsHeader);
        foreach (var s in sections)
        {
            var view = new SettingsSectionView(s);
            _sections.Add(view);
            _settingsHost.Children.Add(view.Root);
        }
        stack.Children.Add(_settingsHost);

        SelectChip(FilterAll);
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is string s && s.StartsWith("search:", StringComparison.Ordinal))
        {
            SelectChip(FilterAll);
            _search.Text = s["search:".Length..];
            _debounce.Stop();
            ApplyFilter();
        }
        else if (parameter is "section:settings")
        {
            _search.Text = "";
            _debounce.Stop();
            SelectChip(FilterSettings);
        }
    }

    public void OnNavigatedFrom() => _debounce.Stop();

    private void AddChip(string title, string? glyph, int filter, int count)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
        {
            var i = Icon(glyph, 12);
            i.Margin = new Thickness(0, 0, 6, 0);
            i.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Control.Foreground))
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1),
            });
            panel.Children.Add(i);
        }
        panel.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var countText = new TextBlock { Text = count.ToString(Culture), Margin = new Thickness(6, 0, 0, 0), Opacity = 0.7, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(countText);
        var b = new Button { Content = panel, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 4, 12, 4), MinHeight = 30, FontSize = 13 };
        System.Windows.Automation.AutomationProperties.SetName(b, title);
        b.Click += (_, _) => { SelectChip(filter); ApplyFilter(); };
        _chipButtons.Add((filter, b, countText));
        _chips.Children.Add(b);
    }

    private void SelectChip(int filter)
    {
        _filter = filter;
        foreach (var (f, b, _) in _chipButtons)
        {
            b.SetResourceReference(StyleProperty, f == filter ? "Pp.AccentButton" : "Pp.Button");
            b.FontWeight = f == filter ? FontWeights.SemiBold : FontWeights.Normal;
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _tokens = TextNormalizer.Tokens(_search.Text);
        var showTools = _filter != FilterSettings;
        var showSettings = _filter is FilterAll or FilterSettings;
        var visible = 0;

        // Compteurs des pastilles : résultats du filtre texte dans chaque catégorie.
        var perGroup = _toolGroups.ToDictionary(g => (int)g.Group, g => g.CountMatches(_tokens));
        var settingsCount = _sections.Sum(s => s.CountMatches(_tokens));
        foreach (var (f, _, count) in _chipButtons)
        {
            count.Text = (f switch
            {
                FilterAll => perGroup.Values.Sum() + settingsCount,
                FilterSettings => settingsCount,
                _ => perGroup.GetValueOrDefault(f),
            }).ToString(Culture);
        }

        foreach (var g in _toolGroups)
        {
            var groupAllowed = showTools && (_filter == FilterAll || _filter == (int)g.Group);
            visible += g.Apply(groupAllowed ? _tokens : null);
        }
        _toolsHost.Visibility = showTools && visible > 0 ? Visibility.Visible : Visibility.Collapsed;
        _settingsHeader.Margin = new Thickness(0, visible > 0 ? 18 : 4, 0, 8);

        var settingsVisible = 0;
        foreach (var s in _sections) settingsVisible += s.Apply(showSettings ? _tokens : null);
        _settingsHost.Visibility = showSettings && settingsVisible > 0 ? Visibility.Visible : Visibility.Collapsed;
        visible += settingsVisible;

        if (visible == 0)
        {
            _emptyText.Text = _tokens.Length > 0
                ? L("No results for “{0}” in this category. Try another word (“drivers”, “partition”, “wifi”…) or the “All” category.", _search.Text.Trim())
                : L("No items in this category on this PC.");
            _empty.Visibility = Visibility.Visible;
        }
        else _empty.Visibility = Visibility.Collapsed;
    }

    /// <summary>Texte de recherche normalisé : « mot1 mot2 … » entouré d'espaces (recherche par préfixe de mot).</summary>
    internal static string Haystack(params IEnumerable<string>[] parts) =>
        " " + string.Join(' ', parts.SelectMany(p => p).SelectMany(s => TextNormalizer.Tokens(s, keepStopWords: true))) + " ";

    internal static bool Matches(string haystack, string[] tokens) =>
        tokens.All(t => haystack.Contains(" " + t, StringComparison.Ordinal));

    // ================================================================ groupe d'outils
    private sealed class ToolGroupView
    {
        public ToolGroup Group { get; }
        public StackPanel Root { get; } = new() { Margin = new Thickness(0, 10, 0, 6) };
        private readonly TextBlock _count;
        private readonly List<(WinTool Tool, FrameworkElement Card, string Hay)> _cards = [];

        public ToolGroupView(ToolGroup group, string title, string glyph, List<WinTool> tools)
        {
            Group = group;
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var icon = Icon(glyph, 16, "Pp.AccentText");
            icon.Margin = new Thickness(0, 2, 10, 0);
            header.Children.Add(icon);
            var t = PageScaffold.Section(title);
            t.Margin = new Thickness(0);
            header.Children.Add(t);
            _count = new TextBlock { Margin = new Thickness(10, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Caption");
            header.Children.Add(_count);
            Root.Children.Add(header);

            var grid = new CardGrid { MinItemWidth = 255, Spacing = 8 };
            foreach (var tool in tools)
            {
                var card = BuildCard(tool);
                grid.Children.Add(card);
                _cards.Add((tool, card, Haystack([tool.Title, tool.Description, tool.Command, title], tool.Keywords)));
            }
            Root.Children.Add(grid);
        }

        public int CountMatches(string[] tokens) => _cards.Count(c => Matches(c.Hay, tokens));

        /// <summary>Applique le filtre (null = groupe masqué) ; renvoie le nombre d'outils visibles.</summary>
        public int Apply(string[]? tokens)
        {
            if (tokens is null) { Root.Visibility = Visibility.Collapsed; return 0; }
            var n = 0;
            foreach (var (_, card, hay) in _cards)
            {
                var ok = Matches(hay, tokens);
                card.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
                if (ok) n++;
            }
            _count.Text = n == _cards.Count ? n.ToString(Culture) : L("{0} of {1}", n, _cards.Count);
            Root.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            return n;
        }

        private static FrameworkElement BuildCard(WinTool tool)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconBox = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 10, 0), Child = Icon(tool.Glyph, 16, "Pp.AccentText"),
            }.Brush(Border.BackgroundProperty, "Pp.AccentSubtle");
            ((TextBlock)iconBox.Child).HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(iconBox);

            var body = new StackPanel();
            Grid.SetColumn(body, 1);
            body.Children.Add(new TextBlock
            {
                Text = tool.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            }.Styled("Pp.Body"));
            body.Children.Add(new TextBlock
            {
                Text = tool.Description, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36, Margin = new Thickness(0, 2, 0, 6),
            }.Styled("Pp.Caption"));
            var badges = new WrapPanel();
            var command = tool.Command.StartsWith("control /name ", StringComparison.Ordinal) ? tool.Command["control /name ".Length..] : tool.Command;
            var commandBadge = Badge(command, null, mono: true);
            commandBadge.MaxWidth = 190;
            commandBadge.ToolTip = tool.Command;
            badges.Children.Add(commandBadge);
            if (tool.Admin) badges.Children.Add(Badge(L("Admin"), "", "Pp.WarningBackground", "Pp.Warning"));
            if (tool.ProOnly) badges.Children.Add(Badge("Pro+", "", "Pp.InfoBackground", "Pp.Info"));
            body.Children.Add(badges);
            grid.Children.Add(body);

            var card = new Border { Child = grid, Padding = new Thickness(12, 10, 12, 6), Margin = new Thickness(0) }.Styled("Pp.CardInteractive");
            card.ToolTip = tool.Admin
                ? L("Open “{0}” ({1}) — Windows will ask for administrator permission.", tool.Title, tool.Command)
                : L("Open “{0}” ({1})", tool.Title, tool.Command);
            MakeClickable(card, tool.Title, () => OpenWithFeedback(card, () => Open(tool)));
            return card;
        }
    }

    // ================================================================ section des Paramètres
    private sealed class SettingsSectionView
    {
        public Border Root { get; }
        private readonly SettingsSection _section;
        private readonly TextBlock _chevron;
        private readonly TextBlock _count;
        private readonly Border _bodyHost = new() { Padding = new Thickness(12, 0, 12, 12), Visibility = Visibility.Collapsed };
        private readonly List<(SettingLink Link, string Hay)> _links;
        private CardGrid? _grid;
        private readonly Dictionary<SettingLink, FrameworkElement> _items = [];
        private bool _expanded;
        private string[] _tokens = [];

        public SettingsSectionView(SettingsSection section)
        {
            _section = section;
            _links = [.. section.Links.Select(l => (l, Haystack([l.Title, l.Parent ?? "", section.Title, l.Key], l.Keywords)))];

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Icon(section.Glyph, 18, "Pp.AccentText");
            icon.Margin = new Thickness(0, 0, 14, 0);
            header.Children.Add(icon);
            var title = new TextBlock { Text = section.Title, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.CardTitle");
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            _count = new TextBlock { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.Caption");
            Grid.SetColumn(_count, 2);
            header.Children.Add(_count);
            _chevron = Icon("", 12, "Pp.TextSecondary");
            Grid.SetColumn(_chevron, 3);
            header.Children.Add(_chevron);

            var headerHost = new Border { Child = header, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(8) };
            headerHost.SetResourceReference(Border.BackgroundProperty, "Pp.CardBackground");
            headerHost.MouseEnter += (_, _) => headerHost.SetResourceReference(Border.BackgroundProperty, "Pp.CardHover");
            headerHost.MouseLeave += (_, _) => headerHost.SetResourceReference(Border.BackgroundProperty, "Pp.CardBackground");
            MakeClickable(headerHost, section.Title, () => { _expanded = !_expanded; Refresh(); });

            var stack = new StackPanel();
            stack.Children.Add(headerHost);
            stack.Children.Add(_bodyHost);
            Root = new Border { Child = stack, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 6) }.Styled("Pp.Card");
        }

        public int CountMatches(string[] tokens) => _links.Count(l => Matches(l.Hay, tokens));

        /// <summary>Applique le filtre (null = section masquée) ; renvoie le nombre de liens visibles.</summary>
        public int Apply(string[]? tokens)
        {
            if (tokens is null) { Root.Visibility = Visibility.Collapsed; return 0; }
            _tokens = tokens;
            var n = _links.Count(l => Matches(l.Hay, tokens));
            Root.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            _count.Text = n == _links.Count ? LP(n, "{0} page", "{0} pages") : L("{0} of {1}", n, _links.Count);
            Refresh();
            return n;
        }

        private void Refresh()
        {
            // Un filtre actif déplie automatiquement les sections qui contiennent des résultats.
            var open = _expanded || _tokens.Length > 0;
            _chevron.Text = open ? "" : "";
            System.Windows.Automation.AutomationProperties.SetItemStatus(Root, open ? L("expanded") : L("collapsed"));
            _bodyHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (!open) return;
            EnsureBuilt();
            foreach (var (link, hay) in _links)
                _items[link].Visibility = Matches(hay, _tokens) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void EnsureBuilt()
        {
            if (_grid is not null) return;
            _grid = new CardGrid { MinItemWidth = 230, Spacing = 6 };
            foreach (var (link, _) in _links)
            {
                var item = BuildLink(link);
                _items[link] = item;
                _grid.Children.Add(item);
            }
            _bodyHost.Child = _grid;
        }

        private static FrameworkElement BuildLink(SettingLink link)
        {
            var dock = new DockPanel();
            var icon = Icon(link.Glyph, 16, "Pp.AccentText");
            icon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
            var open = Icon("", 11, "Pp.TextTertiary");
            open.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(open, Dock.Right);
            dock.Children.Add(open);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = link.Title, TextTrimming = TextTrimming.CharacterEllipsis }.Styled("Pp.Body"));
            if (link.Parent is not null)
            {
                var parent = new TextBlock { Text = link.Parent, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11 }.Styled("Pp.Caption");
                parent.SetResourceReference(TextBlock.ForegroundProperty, "Pp.TextTertiary");
                text.Children.Add(parent);
            }
            dock.Children.Add(text);

            var item = new Border
            {
                Child = dock, Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
                MinHeight = 44,
            };
            item.SetResourceReference(Border.BackgroundProperty, "Pp.CardSecondary");
            item.SetResourceReference(Border.BorderBrushProperty, "Pp.CardBorder");
            item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "Pp.SubtleHover");
            item.MouseLeave += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "Pp.CardSecondary");
            item.ToolTip = $"{link.Path} › {link.Title}\n{link.Uri}";
            MakeClickable(item, link.Title, () => OpenWithFeedback(item, () => Open(link)));
            return item;
        }
    }

    /// <summary>Désactive brièvement l'élément cliqué pour éviter les ouvertures multiples.</summary>
    private static void OpenWithFeedback(FrameworkElement element, Func<bool> open)
    {
        if (!element.IsEnabled) return;
        element.IsEnabled = false;
        element.Opacity = 0.6;
        open();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            element.IsEnabled = true;
            element.Opacity = 1;
        };
        timer.Start();
    }
}
