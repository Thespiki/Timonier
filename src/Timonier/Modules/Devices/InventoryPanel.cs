using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Platform;
using Timonier.UI.Services;

namespace Timonier.Modules.Devices;

/// <summary>
/// Inventaire des périphériques : énumération WMI hors du thread UI, regroupement par classe (groupes repliés, lignes
/// créées à l'ouverture), recherche, problèmes en tête avec explications, détails et pilote à la demande,
/// désactivation/réactivation encadrée par les actions du broker.
/// </summary>
internal sealed class InventoryPanel : UserControl
{
    /// <summary>Classes techniques masquées par défaut (sauf si elles ont un problème).</summary>
    private static readonly HashSet<string> TechnicalClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "SoftwareDevice", "SoftwareComponent", "Volume", "VolumeSnapshot", "Computer", "Processor", "Firmware",
        "Extension", "LegacyDriver", "SCSIAdapter", "HDC",
    };

    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    private readonly TextBox _search = new TextBox { Tag = L("Rechercher un nom, un fabricant, un identifiant…"), MinWidth = 220 }.Styled("Pp.SearchBox");
    private readonly ComboBox _filter = new() { MinWidth = 190, Height = 34, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _showTechnical = new CheckBox { Content = L("Composants techniques"), Margin = new Thickness(16, 0, 0, 0) }.Styled("Pp.ToggleSwitch");
    private readonly Button _refresh;
    private readonly ProgressBar _busy = DevUi.BusyBar(80);
    private readonly TextBlock _status = DevUi.Caption(L("Analyse du matériel…"));
    private readonly StackPanel _messages = new();
    private readonly StackPanel _disabledHost = new();
    private readonly StackPanel _problemsHost = new();
    private readonly StackPanel _groupsHost = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private List<DeviceEntry> _all = [];
    private bool _loading, _actionRunning;

    /// <summary>(total, problèmes, désactivés) pour la tuile d'aperçu ; total = -1 en cas d'erreur.</summary>
    public event Action<int, int, int>? SummaryChanged;

    public InventoryPanel()
    {
        Focusable = false;
        foreach (var label in new[] { L("Tous les périphériques"), L("Avec un problème"), L("Désactivés") }) _filter.Items.Add(label);
        _filter.SelectedIndex = 0;
        System.Windows.Automation.AutomationProperties.SetName(_filter, L("Filtre"));
        System.Windows.Automation.AutomationProperties.SetName(_search, L("Rechercher un périphérique"));
        _showTechnical.ToolTip = L("Afficher aussi les composants système, logiciels et de stockage interne (masqués par défaut, sauf en cas de problème).");

        _refresh = DevUi.Button(L("Actualiser"), "", "Pp.SubtleButton", async (_, _) => await LoadAsync());
        var devmgmt = DevUi.Button(L("Gestionnaire de périphériques"), "", "Pp.SubtleButton", (_, _) => OpenDeviceManager());

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_filter);
        right.Children.Add(_showTechnical);
        DockPanel.SetDock(right, Dock.Right);
        toolbar.Children.Add(right);
        toolbar.Children.Add(_search);

        var statusRow = new DockPanel { Margin = new Thickness(2, 0, 0, 8) };
        var statusButtons = new StackPanel { Orientation = Orientation.Horizontal };
        statusButtons.Children.Add(_refresh);
        statusButtons.Children.Add(devmgmt);
        DockPanel.SetDock(statusButtons, Dock.Right);
        statusRow.Children.Add(statusButtons);
        var statusLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        statusLeft.Children.Add(_status);
        statusLeft.Children.Add(_busy);
        statusRow.Children.Add(statusLeft);

        var root = new StackPanel();
        root.Children.Add(toolbar);
        root.Children.Add(statusRow);
        root.Children.Add(_messages);
        root.Children.Add(_disabledHost);
        root.Children.Add(_problemsHost);
        root.Children.Add(_groupsHost);
        Content = root;

        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Render(); };
        _filter.SelectionChanged += (_, _) => Render();
        _showTechnical.Click += (_, _) => Render();
        Unloaded += (_, _) => _debounce.Stop();
    }

    /// <summary>Préremplit la recherche (les groupes correspondants s'ouvrent).</summary>
    public void SetSearch(string text)
    {
        _search.Text = text;
        _debounce.Stop();
        Render();
    }

    // ================================================================== Chargement

    public async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        _refresh.IsEnabled = false;
        _busy.Visibility = Visibility.Visible;
        if (_all.Count == 0) _status.Text = L("Analyse du matériel…");
        try
        {
            _all = await Task.Run(DeviceInventory.Load);
            _messages.Children.Clear();
            CleanDisabledStore();
            Render();
        }
        catch (Exception ex)
        {
            Log.Error("Devices", "énumération des périphériques", ex);
            _messages.Children.Clear();
            var bar = Timonier.UI.Controls.PageScaffold.InfoBar(
                L("Impossible de lire la liste des périphériques (service WMI indisponible ou trop lent). Réessayez dans un instant, ou ouvrez le Gestionnaire de périphériques."), "", "Pp.InfoBar.Danger");
            _messages.Children.Add(bar);
            _status.Text = L("Liste indisponible");
            SummaryChanged?.Invoke(-1, 0, 0);
        }
        finally
        {
            _loading = false;
            _refresh.IsEnabled = true;
            _busy.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Oublie les périphériques de la liste « Désactivés par Timonier » qui ont été réactivés entre-temps (ailleurs).</summary>
    private void CleanDisabledStore()
    {
        foreach (var (id, _) in DisabledDevicesStore.Load())
        {
            var e = _all.FirstOrDefault(d => string.Equals(d.InstanceId, id, StringComparison.OrdinalIgnoreCase));
            if (e is not null && !e.IsDisabled) DisabledDevicesStore.Set(id, null);
        }
    }

    // ================================================================== Rendu

    private void Render()
    {
        var mode = _filter.SelectedIndex;
        var tokens = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var searching = tokens.Length > 0;
        var showTechnical = _showTechnical.IsChecked == true || searching || mode != 0;

        var visible = _all.Where(d =>
                (mode != 1 || d.HasProblem) && (mode != 2 || d.IsDisabled) &&
                (showTechnical || d.HasProblem || d.IsDisabled || !TechnicalClasses.Contains(d.PnpClass ?? "")) &&
                (!searching || tokens.All(t => Matches(d, t))))
            .ToList();

        var problems = _all.Count(d => d.HasProblem);
        var disabled = _all.Count(d => d.IsDisabled);
        _status.Text = string.Join(" · ", new[]
        {
            LP(_all.Count, "{0} périphérique", "{0} périphériques"),
            problems > 0 ? LP(problems, "{0} en erreur", "{0} en erreur") : L("aucun en erreur"),
            disabled > 0 ? LP(disabled, "{0} désactivé", "{0} désactivés") : null,
            visible.Count != _all.Count ? LP(visible.Count, "{0} affiché", "{0} affichés") : null,
        }.Where(x => x is not null));
        SummaryChanged?.Invoke(_all.Count, problems, disabled);

        RenderDisabledStore();

        // Problèmes d'abord, avec leur explication.
        _problemsHost.Children.Clear();
        var problemDevices = visible.Where(d => d.HasProblem).OrderBy(d => d.Class.Order).ThenBy(d => d.Name).ToList();
        if (problemDevices.Count > 0)
        {
            var body = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var icon = DevUi.Icon("", 16, "Pp.Warning");
            icon.Margin = new Thickness(0, 0, 10, 0);
            head.Children.Add(icon);
            head.Children.Add(DevUi.Text(LP(problemDevices.Count, "{0} périphérique à vérifier", "{0} périphériques à vérifier"), "Pp.CardTitle"));
            body.Children.Add(head);
            var first = true;
            foreach (var d in problemDevices)
            {
                if (!first) body.Children.Add(DevUi.Divider(new Thickness(0, 2, 0, 2)));
                first = false;
                body.Children.Add(BuildRow(d, showClass: true));
            }
            var card = new Border { Child = body, Margin = new Thickness(0, 0, 0, 12) }.Styled("Pp.InfoBar.Warning");
            _problemsHost.Children.Add(card);
        }

        // Groupes par classe.
        _groupsHost.Children.Clear();
        var groups = visible.Where(d => !d.HasProblem)
            .GroupBy(d => d.Class.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.First().Class.Order).ThenBy(g => g.First().Class.Title, StringComparer.CurrentCulture)
            .ToList();
        foreach (var g in groups)
            _groupsHost.Children.Add(BuildGroup(g.First().Class, [.. g.OrderBy(d => d.Name, StringComparer.CurrentCulture)], autoExpand: searching || mode != 0));

        if (visible.Count == 0 && _all.Count > 0)
        {
            _groupsHost.Children.Add(DevUi.Card(DevUi.Text(searching
                ? L("Aucun périphérique ne correspond à cette recherche.")
                : mode == 1 ? L("Aucun périphérique en erreur : tout fonctionne correctement.")
                : mode == 2 ? L("Aucun périphérique désactivé.")
                : L("Aucun périphérique à afficher."))));
        }
    }

    private static bool Matches(DeviceEntry d, string token)
    {
        const CompareOptions o = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return Compare.IndexOf(d.Name, token, o) >= 0
               || (d.Manufacturer is { } m && Compare.IndexOf(m, token, o) >= 0)
               || Compare.IndexOf(d.Class.Title, token, o) >= 0
               || d.InstanceId.Contains(token, StringComparison.OrdinalIgnoreCase)
               || (d.PnpClass is { } c && c.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private FrameworkElement BuildGroup(DeviceClassInfo cls, List<DeviceEntry> devices, bool autoExpand)
    {
        var body = new StackPanel { Margin = new Thickness(16, 0, 16, 8) };
        var chevron = DevUi.Icon("", 12, "Pp.TextSecondary");

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = DevUi.IconTile(cls.Glyph, 30);
        tile.Margin = new Thickness(0, 0, 12, 0);
        header.Children.Add(tile);
        var title = DevUi.Text(cls.Title);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var disabled = devices.Count(d => d.IsDisabled);
        if (disabled > 0) badges.Children.Add(DevUi.Badge(LP(disabled, "{0} désactivé", "{0} désactivés")));
        badges.Children.Add(DevUi.Badge(devices.Count.ToString(Culture)));
        Grid.SetColumn(badges, 2);
        header.Children.Add(badges);
        chevron.Margin = new Thickness(8, 0, 4, 0);
        Grid.SetColumn(chevron, 3);
        header.Children.Add(chevron);

        var toggle = new Button { Content = header, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 10, 12, 10) }
            .Styled("Pp.SubtleButton");
        System.Windows.Automation.AutomationProperties.SetName(toggle, cls.Title);

        var expanded = autoExpand || _expanded.Contains(cls.Key);
        void Apply()
        {
            chevron.Text = expanded ? "" : "";
            body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (expanded && body.Children.Count == 0)
            {
                var first = true;
                foreach (var d in devices)
                {
                    if (!first) body.Children.Add(DevUi.Divider(new Thickness(0, 2, 0, 2)));
                    first = false;
                    body.Children.Add(BuildRow(d, showClass: false));
                }
            }
        }
        toggle.Click += (_, _) =>
        {
            expanded = !expanded;
            if (expanded) _expanded.Add(cls.Key); else _expanded.Remove(cls.Key);
            Apply();
        };
        Apply();

        var stack = new StackPanel();
        stack.Children.Add(toggle);
        stack.Children.Add(body);
        return new Border { Child = stack, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 6) }.Styled("Pp.Card");
    }

    // ================================================================== Ligne de périphérique

    private FrameworkElement BuildRow(DeviceEntry d, bool showClass)
    {
        var root = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var name = DevUi.Text(d.Name);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        name.ToolTip = d.Name;
        text.Children.Add(name);
        var problem = DeviceCatalog.Problem(d.ErrorCode);
        var parts = new List<string>();
        if (showClass) parts.Add(d.Class.Title);
        if (d.Manufacturer is { } m) parts.Add(m);
        parts.Add(d.HasProblem && problem is { } p ? p.Summary : d.IsDisabled ? L("Désactivé") : !d.Present ? L("Non connecté") : L("Fonctionne correctement"));
        var caption = DevUi.Caption(string.Join(" · ", parts));
        if (d.HasProblem) caption.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Warning");
        text.Children.Add(caption);
        grid.Children.Add(text);

        var badgeHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (d.IsDisabled) badgeHost.Children.Add(DevUi.Badge(L("Désactivé")));
        else if (d.HasProblem) badgeHost.Children.Add(DevUi.Badge(L("Code {0}", d.ErrorCode), "Pp.Warning"));
        var (level, reason) = d.Protection;
        if (level == DeviceProtection.Protected)
        {
            var lockIcon = DevUi.Icon("", 14, "Pp.TextTertiary");
            lockIcon.Margin = new Thickness(4, 0, 8, 0);
            lockIcon.ToolTip = L("Composant protégé : {0}", reason);
            badgeHost.Children.Add(lockIcon);
        }
        Grid.SetColumn(badgeHost, 1);
        grid.Children.Add(badgeHost);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var details = new Border { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 8, 0, 2) }
            .Themed(Border.BackgroundProperty, "Pp.CardSecondary");
        var detailsButton = DevUi.Button(L("Détails"), "", "Pp.SubtleButton");
        detailsButton.Click += (_, _) =>
        {
            if (details.Child is null) details.Child = BuildDetails(d, problem, reason);
            details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        };
        buttons.Children.Add(detailsButton);

        if (d.IdIsActionable && (d.IsDisabled || level != DeviceProtection.Protected))
        {
            var enable = d.IsDisabled;
            var action = DevUi.Button(enable ? L("Activer") : L("Désactiver"), enable ? "" : "", enable ? "Pp.AccentButton" : "Pp.Button");
            action.Margin = new Thickness(6, 0, 0, 0);
            action.MinWidth = 118;
            if (level == DeviceProtection.Sensitive && !enable) action.ToolTip = reason;
            action.Click += async (_, _) => await RunActionAsync(d, enable, action);
            buttons.Children.Add(action);
        }
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        root.Children.Add(grid);
        root.Children.Add(details);
        return root;
    }

    private static FrameworkElement BuildDetails(DeviceEntry d, (string Summary, string Advice)? problem, string? protectionReason)
    {
        var s = new StackPanel();
        if (problem is { } p && d.HasProblem)
        {
            s.Children.Add(DevUi.KeyValue(L("Problème"), p.Summary));
            s.Children.Add(DevUi.KeyValue(L("Que faire ?"), p.Advice));
        }
        s.Children.Add(DevUi.KeyValue(L("Catégorie"), d.Class.Title + (d.PnpClass is { Length: > 0 } c && c != d.Class.Title ? $" ({c})" : "")));
        s.Children.Add(DevUi.KeyValue(L("Identifiant"), d.InstanceId, selectable: true));
        if (d.Service is { Length: > 0 } svc) s.Children.Add(DevUi.KeyValue(L("Service du pilote"), svc));
        var driver = DevUi.KeyValue(L("Pilote"), L("Lecture…"));
        s.Children.Add(driver);
        if (protectionReason is not null)
            s.Children.Add(DevUi.KeyValue(d.Protection.Level == DeviceProtection.Protected ? L("Protégé") : L("Prudence"), protectionReason));

        var copy = DevUi.Button(L("Copier l'identifiant"), "", "Pp.LinkButton", (_, _) =>
        {
            try
            {
                Clipboard.SetText(d.InstanceId);
                AppHost.Toasts.Show(L("Identifiant copié."), ToastKind.Success);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                AppHost.Toasts.Show(L("Le presse-papiers est occupé, réessayez."), ToastKind.Warning);
            }
        });
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        copy.Margin = new Thickness(148, 4, 0, 0);
        s.Children.Add(copy);

        _ = LoadDriverAsync(d, driver);
        return s;
    }

    private static async Task LoadDriverAsync(DeviceEntry d, Grid row)
    {
        string text;
        try
        {
            var info = await Task.Run(() => d.IdIsActionable ? DeviceInventory.LoadDriver(d.InstanceId) : null);
            text = info is null
                ? L("Aucune information de pilote (périphérique sans pilote ou pilote intégré au système).")
                : string.Join(" · ", new[]
                {
                    info.Version is { } v ? L("version {0}", v) : null,
                    info.Date is { } dt ? L("du {0}", Format.Day(dt)) : null,
                    info.Provider is { } pr ? L("fournisseur : {0}", pr) : null,
                    info.Inf,
                    info.Signed == true ? (info.Signer is { Length: > 0 } sg ? L("signé par {0}", sg) : L("signé")) : info.Signed == false ? L("non signé") : null,
                }.Where(x => !string.IsNullOrEmpty(x)));
        }
        catch (Exception ex)
        {
            Log.Warn("Devices", "lecture du pilote : " + ex.Message);
            text = L("Informations du pilote indisponibles.");
        }
        if (row.Children.Count > 1 && row.Children[1] is TextBlock t) t.Text = text;
    }

    // ================================================================== Actions

    private async Task RunActionAsync(DeviceEntry d, bool enable, Button button)
    {
        if (_actionRunning) return;
        var (level, reason) = d.Protection;
        string actionId;
        if (enable)
        {
            actionId = EnableDeviceAction.ActionId;
        }
        else
        {
            var sensitive = level == DeviceProtection.Sensitive;
            var message = sensitive
                ? L("« {0} » ne fonctionnera plus jusqu'à sa réactivation.\n\n{1}\nWindows affichera une seconde confirmation depuis le processus administrateur.\n\nVous pourrez le réactiver ici, même s'il n'apparaît plus dans la liste (section « Désactivés par Timonier »).", d.Name, reason)
                : L("« {0} » ne fonctionnera plus jusqu'à sa réactivation.\n\nVous pourrez le réactiver ici, même s'il n'apparaît plus dans la liste (section « Désactivés par Timonier »).", d.Name);
            if (!await AppHost.Dialogs.ConfirmAsync(L("Désactiver ce périphérique ?"), message, L("Désactiver"), L("Annuler"), danger: sensitive))
                return;
            actionId = sensitive ? DisableSensitiveDeviceAction.ActionId : DisableDeviceAction.ActionId;
        }

        _actionRunning = true;
        button.IsEnabled = false;
        _busy.Visibility = Visibility.Visible;
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(actionId, new Dictionary<string, string> { ["id"] = d.InstanceId });
            AppHost.Toasts.ShowOutcome(outcome);
            if (outcome.Success) DisabledDevicesStore.Set(d.InstanceId, enable ? null : d.Name);
        }
        finally
        {
            _actionRunning = false;
            button.IsEnabled = true;
        }
        await LoadAsync();
    }

    private void RenderDisabledStore()
    {
        _disabledHost.Children.Clear();
        var store = DisabledDevicesStore.Load();
        if (store.Count == 0) return;
        var body = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(DevUi.Icon("", 16, "Pp.AccentText"));
        head.Children.Add(new TextBlock { Text = L("Désactivés par Timonier"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Styled("Pp.CardTitle"));
        body.Children.Add(head);
        foreach (var (id, name) in store.OrderBy(k => k.Value, StringComparer.CurrentCulture))
        {
            var present = _all.FirstOrDefault(x => string.Equals(x.InstanceId, id, StringComparison.OrdinalIgnoreCase));
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 2) };
            var button = DevUi.Button(L("Réactiver"), "", "Pp.AccentButton");
            button.MinWidth = 118;
            var entry = present ?? new DeviceEntry { InstanceId = id, Name = name, ErrorCode = 22, Present = false };
            button.Click += async (_, _) => await RunActionAsync(entry, enable: true, button);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(DevUi.Text(name));
            t.Children.Add(DevUi.Caption(present is null ? L("Non détecté actuellement (débranché ?) : rebranchez-le si la réactivation échoue.") : L("Désactivé")));
            row.Children.Add(t);
            body.Children.Add(row);
        }
        _disabledHost.Children.Add(new Border { Child = body, Margin = new Thickness(0, 0, 0, 12) }.Styled("Pp.InfoBar"));
    }

    private static void OpenDeviceManager()
    {
        // Chemin absolu : un « devmgmt.msc » relatif pourrait être cherché dans le dossier courant.
        try { ProcessRunner.Launch(SystemTool.Mmc, Path.Combine(Environment.SystemDirectory, "devmgmt.msc")); }
        catch (Exception ex)
        {
            Log.Warn("Devices", "Gestionnaire de périphériques : " + ex.Message);
            AppHost.Toasts.Show(L("Impossible d'ouvrir le Gestionnaire de périphériques."), ToastKind.Warning);
        }
    }
}
