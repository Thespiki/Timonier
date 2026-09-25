using System.Globalization;
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
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

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

        _search = SearchBox("Rechercher une application ou un éditeur", q => { _query = q; Render(); });
        _filter.Add("Toutes");
        _filter.Add("Activées");
        _filter.Add("Désactivées");
        _filter.Select(0, notify: false);
        _filter.SelectionChanged += (_, _) => Render();
        _refresh = Button("Actualiser", "", "Pp.Button", async (_, _) => await ReloadAsync());
        var settings = Button("Paramètres Windows", "", "Pp.SubtleButton", (_, _) => OpenSettings());
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
            _list.Children.Add(StateCard("", "Recherche des applications au démarrage…", busy: true));
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
            _list.Children.Add(StateCard("", "Impossible de lire les applications au démarrage", ex.Message));
        }
        finally
        {
            _loading = false;
            _refresh.IsEnabled = true;
        }
    }

    public void Highlight(string tweakId) => _tweaks?.Highlight(tweakId);

    private static Border Footnote() => PageScaffold.InfoBar(
        "Désactiver une application ici revient à le faire dans le Gestionnaire des tâches : l'entrée reste en place et peut être "
        + "réactivée à tout moment. Le changement prend effet à la prochaine ouverture de session. Une application peut aussi "
        + "démarrer par un service ou une tâche planifiée : voyez les onglets correspondants.", "");

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
        var list = view.OrderByDescending(i => i.Enabled).ThenBy(i => i.DisplayName, StringComparer.Create(Fr, true)).ToList();

        _list.Children.Clear();
        if (list.Count == 0)
        {
            _list.Children.Add(toggleable.Count == 0
                ? StateCard("", "Aucune application ne se lance au démarrage", "Votre ouverture de session est aussi légère que possible.")
                : StateCard("", "Aucun résultat", "Aucune application ne correspond à la recherche ou au filtre choisi."));
        }
        else
        {
            foreach (var item in list) _list.Children.Add(Row(item));
        }

        _readOnly.Children.Clear();
        var ro = _items.Where(i => i.IsReadOnly && (_query.Length == 0 || Matches(i, _query))).ToList();
        if (ro.Count > 0)
        {
            var title = PageScaffold.Section("Entrées en lecture seule");
            title.Margin = new Thickness(0, 22, 0, 4);
            _readOnly.Children.Add(title);
            var cap = Caption("RunOnce : commandes exécutées une seule fois à la prochaine ouverture de session (souvent la fin d'une installation). "
                              + "Stratégie : programmes imposés par une stratégie de groupe. Elles ne se désactivent pas depuis le Gestionnaire des tâches.");
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
        CountChanged?.Invoke(this, enabled.ToString(Fr));

        var profile = AppHost.Profile;
        var low = profile?.Tier == PerformanceTier.Low;
        var threshold = low ? 8 : 12;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddMetric(grid, 0, enabled.ToString(Fr), enabled > 1 ? "activées" : "activée", enabled > threshold ? "Pp.Warning" : "Pp.AccentText");
        AddMetric(grid, 1, disabled.ToString(Fr), disabled > 1 ? "désactivées" : "désactivée", "Pp.TextSecondary");
        AddMetric(grid, 2, missing.ToString(Fr), missing > 1 ? "introuvables" : "introuvable", missing > 0 ? "Pp.Warning" : "Pp.TextSecondary");

        string advice;
        if (enabled > threshold)
            advice = $"C'est beaucoup{(low ? " pour ce PC d'entrée de gamme" : "")} : chaque application lancée au démarrage retarde l'ouverture de session "
                     + "et occupe de la mémoire. Gardez l'essentiel (antivirus, pilotes audio ou tactiles, synchronisation dont vous avez besoin).";
        else if (enabled == 0)
            advice = "Aucune application ne se lance à l'ouverture de session.";
        else
            advice = low
                ? "Sur un PC d'entrée de gamme, chaque application en moins au démarrage se ressent : désactivez ce qui ne vous sert pas dès l'ouverture de session."
                : "Nombre raisonnable. Désactivez ce qui ne vous sert pas dès l'ouverture de session pour gagner quelques secondes.";
        if (missing > 0)
            advice += $" {missing} entrée{(missing > 1 ? "s pointent" : " pointe")} vers un fichier supprimé : vous pouvez les retirer.";
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
        if (item.Scope == StartupScope.Machine) titleLine.Children.Add(Badge("Tous les utilisateurs", "Neutral", ""));
        if (!item.Enabled && !item.IsReadOnly) titleLine.Children.Add(Badge("Désactivée"));
        if (item.FileMissing) titleLine.Children.Add(Badge("Fichier introuvable", "Warning", ""));
        if (item.IsPolicyLocked) titleLine.Children.Add(Badge(item.Enabled ? "Imposée par l'organisation" : "Bloquée par l'organisation", "Info", ""));
        body.Children.Add(titleLine);

        var meta = new List<string> { item.Publisher is { Length: > 0 } pub ? pub : "Éditeur inconnu", item.SourceLabel };
        if (!item.Enabled && item.DisabledSince is { } since) meta.Add("désactivée le " + since.ToString("d MMMM yyyy", Fr));
        var metaText = Caption(string.Join(" · ", meta));
        metaText.ToolTip = item.Kind is StartupKind.Run or StartupKind.Run32 ? "Nom de l'entrée : " + item.Name : null;
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
            actions.Children.Add(IconButton("", "Ouvrir l'emplacement", (_, _) => RevealInExplorer(location)));
        else actions.Children.Add(new Border { Width = 34 });
        if (item.CanDelete)
        {
            var del = IconButton("", item.NeedsAdmin ? "Supprimer l'entrée (administrateur)" : "Supprimer l'entrée", async (s, _) => await DeleteAsync(item, (Button)s!));
            actions.Children.Add(del);
        }
        else actions.Children.Add(new Border { Width = 34 });
        if (item.CanToggle)
        {
            var state = new TextBlock { Text = item.Enabled ? "Activée" : "Désactivée", Width = 74, TextAlignment = TextAlignment.Right, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }
                .Styled("Pp.Body");
            var toggle = new CheckBox { IsChecked = item.Enabled, VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.ToggleSwitch");
            System.Windows.Automation.AutomationProperties.SetName(toggle, $"Lancer {item.DisplayName} au démarrage");
            if (item.NeedsAdmin) toggle.ToolTip = "Entrée commune à tous les utilisateurs : droits administrateur requis.";
            toggle.Click += async (_, _) => await ToggleAsync(item, toggle, state, busy);
            actions.Children.Add(state);
            actions.Children.Add(toggle);
        }
        else if (item.IsReadOnly)
        {
            actions.Children.Add(Badge(item.Kind == StartupKind.RunOnce ? "Une seule fois" : "Stratégie", "Neutral"));
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
            state.Text = item.Enabled ? "Activée" : "Désactivée";
        }
        finally
        {
            toggle.IsEnabled = true;
            busy.Visibility = Visibility.Collapsed;
        }
    }

    private async Task DeleteAsync(StartupItem item, Button button)
    {
        var ok = await AppHost.Dialogs.ConfirmAsync("Supprimer l'entrée de démarrage ?",
            $"« {item.DisplayName} » sera retiré de la liste de démarrage ({item.SourceLabel}).\n\nCommande : {item.Command}\n\n"
            + "Le programme lui-même n'est pas désinstallé, et l'entrée peut être restaurée depuis le journal de Timonier. "
            + "Préférez la désactivation si vous n'êtes pas sûr : elle a le même effet au démarrage.",
            "Supprimer", "Annuler", danger: true);
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
        catch (Exception ex) { AppHost.Toasts.Show("Impossible d'ouvrir les Paramètres : " + ex.Message, ToastKind.Error); }
    }
}
