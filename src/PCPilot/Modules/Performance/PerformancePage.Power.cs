using System.Windows;
using System.Windows.Controls;
using PcPilot.Core.Platform;
using PcPilot.UI.Controls;
using PcPilot.UI.Services;

namespace PcPilot.Modules.Performance;

/// <summary>Instantané de l'alimentation, lu hors du thread UI.</summary>
internal sealed record PowerSnapshot(
    List<PowerScheme> Schemes, PowerSource Source, bool ModeApi, Guid? UserAc, Guid? UserDc, Guid? Effective,
    PowerTimeouts Timeouts, string UltimateName)
{
    public PowerScheme? Active => Schemes.FirstOrDefault(s => s.IsActive);

    public static PowerSnapshot Load()
    {
        var modeApi = PowerApi.UserModeApiAvailable;
        return new PowerSnapshot(
            PowerApi.EnumerateSchemes(), PowerApi.GetSource(), modeApi,
            modeApi ? PowerApi.GetUserMode(ac: true) : null, modeApi ? PowerApi.GetUserMode(ac: false) : null,
            PowerApi.GetEffectiveMode(), PowerApi.ReadTimeouts(),
            PowerApi.ReadFriendlyName(PowerApi.UltimateTemplate) ?? "Performances optimales");
    }
}

public sealed partial class PerformancePage
{
    private static readonly int[] TimeoutPresets = [0, 1, 2, 3, 5, 10, 15, 20, 25, 30, 45, 60, 120, 180, 240, 300];

    // Carte « Alimentation »
    private readonly TextBlock _powerTitle = PerfUi.Text("Pp.CardTitle");
    private readonly TextBlock _powerStatus = PerfUi.Text("Pp.Caption", "Lecture de l'état de l'alimentation…");
    private readonly StackPanel _modePanel = new();
    private readonly StackPanel _plansPanel = new();
    private readonly WrapPanel _addPanel = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _addWarning = PerfUi.Text("Pp.Caption", "", new Thickness(0, 6, 0, 0));
    private readonly Border _powerError = PageScaffold.InfoBar("", "", "Pp.InfoBar.Danger");
    private readonly ProgressBar _powerProgress = new() { IsIndeterminate = true, Height = 3, Margin = new Thickness(0, 8, 0, 0) };
    private bool _powerBusy;

    // Carte « Veille et écran »
    private readonly ComboBox _monitorAc = TimeoutCombo("Éteindre l'écran après, sur secteur");
    private readonly ComboBox _monitorDc = TimeoutCombo("Éteindre l'écran après, sur batterie");
    private readonly ComboBox _sleepAc = TimeoutCombo("Mettre en veille après, sur secteur");
    private readonly ComboBox _sleepDc = TimeoutCombo("Mettre en veille après, sur batterie");
    private readonly List<UIElement> _batteryColumn = [];
    private readonly TextBlock _sleepCaption = PerfUi.Text("Pp.Caption", "", new Thickness(0, 10, 0, 0));
    private bool _updatingTimeouts;

    private PowerSnapshot? _power;

    // ================================================================== Construction

    private Border BuildPowerCard()
    {
        var content = new StackPanel();

        var header = new DockPanel();
        var circle = PerfUi.IconCircle("", 40, 18);
        circle.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(circle, Dock.Left);
        header.Children.Add(circle);

        var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var settings = PerfUi.Button("Paramètres Windows", "Pp.SubtleButton", "");
        settings.ToolTip = "Paramètres > Système > Alimentation et batterie";
        settings.Click += (_, _) => OpenUri("ms-settings:powersleep");
        var advanced = PerfUi.Button("Options avancées", "Pp.SubtleButton", "");
        advanced.ToolTip = "Options d'alimentation du Panneau de configuration (paramètres détaillés des plans)";
        advanced.Click += (_, _) => Launch(SystemTool.Control, "powercfg.cpl");
        links.Children.Add(settings);
        links.Children.Add(advanced);
        DockPanel.SetDock(links, Dock.Right);
        header.Children.Add(links);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _powerTitle.FontWeight = FontWeights.SemiBold;
        _powerTitle.Text = "Plan d'alimentation";
        titles.Children.Add(_powerTitle);
        titles.Children.Add(_powerStatus);
        header.Children.Add(titles);
        content.Children.Add(header);
        content.Children.Add(_powerProgress);

        _powerError.Visibility = Visibility.Collapsed;
        _powerError.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(_powerError);

        content.Children.Add(PerfUi.Divider(14, 12));
        content.Children.Add(SubTitle("Mode d'alimentation"));
        content.Children.Add(PerfUi.Text("Pp.Caption",
            "Ajuste le plan « Utilisation normale » vers l'autonomie ou la réactivité, séparément sur secteur et sur batterie.",
            new Thickness(0, 2, 0, 8)));
        content.Children.Add(_modePanel);

        content.Children.Add(PerfUi.Divider(14, 12));
        content.Children.Add(SubTitle("Plans d'alimentation"));
        content.Children.Add(PerfUi.Text("Pp.Caption",
            "Windows 10 et 11 recommandent « Utilisation normale » (équilibré), qui s'adapte à la charge. Les autres plans sont des " +
            "réglages fixes, utiles dans des cas précis.", new Thickness(0, 2, 0, 6)));
        content.Children.Add(_plansPanel);
        content.Children.Add(_addPanel);
        content.Children.Add(_addWarning);

        return PerfUi.Card(content).WithPadding(new Thickness(20, 16, 20, 16));
    }

    private Border BuildSleepCard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Place(grid, ColumnHeader("", "Sur secteur"), 0, 1);
        var dcHeader = ColumnHeader("", "Sur batterie");
        Place(grid, dcHeader, 0, 2);
        _batteryColumn.Add(dcHeader);

        Place(grid, RowLabel("", "Éteindre l'écran après"), 1, 0);
        Place(grid, _monitorAc, 1, 1);
        Place(grid, _monitorDc, 1, 2);
        Place(grid, RowLabel("", "Mettre en veille après"), 2, 0);
        Place(grid, _sleepAc, 2, 1);
        Place(grid, _sleepDc, 2, 2);
        _batteryColumn.Add(_monitorDc);
        _batteryColumn.Add(_sleepDc);

        Wire(_monitorAc, "monitor", "ac");
        Wire(_monitorDc, "monitor", "dc");
        Wire(_sleepAc, "standby", "ac");
        Wire(_sleepDc, "standby", "dc");

        var content = new StackPanel();
        content.Children.Add(grid);
        content.Children.Add(_sleepCaption);
        return PerfUi.Card(content).WithPadding(new Thickness(20, 14, 20, 14));
    }

    private static void Place(Grid g, UIElement e, int row, int col)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    private static StackPanel ColumnHeader(string glyph, string text)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 6) };
        s.Children.Add(PerfUi.Icon(glyph, 13, "Pp.TextSecondary"));
        s.Children.Add(PerfUi.Text("Pp.Caption", text, new Thickness(8, 0, 0, 0)));
        return s;
    }

    private static StackPanel RowLabel(string glyph, string text)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 6) };
        s.Children.Add(PerfUi.Icon(glyph, 16, "Pp.TextSecondary"));
        s.Children.Add(PerfUi.Text("Pp.Body", text, new Thickness(12, 0, 0, 0)));
        return s;
    }

    private static TextBlock SubTitle(string text)
    {
        var t = PerfUi.Text("Pp.Body", text);
        t.FontWeight = FontWeights.SemiBold;
        return t;
    }

    private static ComboBox TimeoutCombo(string name)
    {
        var c = new ComboBox { Margin = new Thickness(8, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center, IsEnabled = false };
        System.Windows.Automation.AutomationProperties.SetName(c, name);
        return c;
    }

    private void Wire(ComboBox combo, string kind, string source)
    {
        combo.SelectionChanged += async (_, _) =>
        {
            if (_updatingTimeouts || combo.SelectedItem is not ComboBoxItem { Tag: int minutes }) return;
            SetTimeoutsEnabled(false);
            try
            {
                var outcome = await AppHost.Engine.RunActionAsync(SetTimeoutAction.ActionId, new Dictionary<string, string>
                {
                    ["kind"] = kind,
                    ["source"] = source,
                    ["minutes"] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
                AppHost.Toasts.ShowOutcome(outcome);
            }
            finally
            {
                await RefreshPowerAsync();
            }
        };
    }

    private void SetTimeoutsEnabled(bool enabled)
    {
        foreach (var c in new[] { _monitorAc, _monitorDc, _sleepAc, _sleepDc }) c.IsEnabled = enabled;
    }

    // ================================================================== Rafraîchissement

    private async Task RefreshPowerAsync()
    {
        _powerProgress.Visibility = Visibility.Visible;
        try
        {
            _power = await Task.Run(PowerSnapshot.Load);
            _powerError.Visibility = Visibility.Collapsed;
            _lastSource = _power.Source;
            RenderPower(_power);
            RenderTimeouts(_power);
            RefreshHardware(); // l'état secteur/batterie figure dans la fiche matériel
        }
        catch (Exception ex)
        {
            Log.Error("Performance", "lecture de l'alimentation", ex);
            ((TextBlock)((DockPanel)_powerError.Child).Children[1]).Text = "Impossible de lire l'état de l'alimentation : " + ex.Message;
            _powerError.Visibility = Visibility.Visible;
            _powerStatus.Text = "État inconnu";
        }
        finally
        {
            _powerProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void RenderPower(PowerSnapshot s)
    {
        var active = s.Active;
        _powerTitle.Text = active is null ? "Aucun plan actif détecté" : $"Plan actif : {active.Name}";
        var parts = new List<string>();
        if (s.Source.HasBattery)
            parts.Add(s.Source.OnBattery
                ? $"Sur batterie{(s.Source.BatteryPercent is { } b ? $" ({b} %)" : "")}"
                : $"Sur secteur{(s.Source.BatteryPercent is { } c ? $" · batterie {c} %" : "")}");
        else parts.Add("Alimenté sur secteur");
        if (s.Source.BatterySaver) parts.Add("économiseur de batterie actif");
        if (active?.IsBalanced == true && s.Effective is { } eff && PowerApi.ModeFromGuid(eff) is { } mode)
            parts.Add($"mode appliqué : {mode.Label}");
        _powerStatus.Text = string.Join(" · ", parts);

        RenderModes(s);
        RenderPlans(s);
    }

    private void RenderModes(PowerSnapshot s)
    {
        _modePanel.Children.Clear();
        var active = s.Active;
        if (!s.ModeApi)
        {
            var current = s.Effective is { } e && PowerApi.ModeFromGuid(e) is { } m ? $"Mode actuel : {m.Label}. " : "";
            _modePanel.Children.Add(InfoWithLink(current + "Sur cette version de Windows, le mode se règle depuis l'icône de batterie " +
                                                  "ou les Paramètres Windows.", "Ouvrir les paramètres", () => OpenUri("ms-settings:powersleep")));
            return;
        }
        if (active is null || !active.IsBalanced)
        {
            _modePanel.Children.Add(InfoWithLink("Les modes d'alimentation ne s'appliquent qu'au plan « Utilisation normale ». " +
                                                  "Activez-le ci-dessous pour les utiliser.", null, null));
            return;
        }
        _modePanel.Children.Add(ModeRow("", "Sur secteur", ac: true, s.UserAc));
        if (s.Source.HasBattery || AppHost.Profile.HasBattery)
            _modePanel.Children.Add(ModeRow("", "Sur batterie", ac: false, s.UserDc));
    }

    private Grid ModeRow(string glyph, string label, bool ac, Guid? current)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var head = RowLabel(glyph, label);
        head.Margin = new Thickness(0, 0, 12, 0);
        g.Children.Add(head);

        var buttons = new WrapPanel();
        var currentMode = current is { } c ? PowerApi.ModeFromGuid(c) : null;
        foreach (var mode in PowerApi.Modes)
        {
            var selected = currentMode == mode || currentMode == PowerApi.ModeBetterPerformance && mode == PowerApi.ModePerformance;
            var b = PerfUi.Button(mode.ShortLabel, selected ? "Pp.AccentButton" : "Pp.Button", selected ? "" : mode.Glyph);
            b.MinWidth = 150;
            b.Margin = new Thickness(0, 2, 6, 2);
            b.ToolTip = mode.Label;
            b.IsEnabled = !_powerBusy;
            System.Windows.Automation.AutomationProperties.SetName(b, $"{mode.Label} {label.ToLowerInvariant()}");
            b.Click += async (_, _) =>
            {
                if (selected || _powerBusy) return;
                await RunPowerAsync(() => AppHost.Engine.RunActionAsync(SetPowerModeAction.ActionId, new Dictionary<string, string>
                {
                    ["source"] = ac ? "ac" : "dc",
                    ["mode"] = mode.Key,
                }));
            };
            buttons.Children.Add(b);
        }
        Grid.SetColumn(buttons, 1);
        g.Children.Add(buttons);
        return g;
    }

    private void RenderPlans(PowerSnapshot s)
    {
        _plansPanel.Children.Clear();
        if (s.Schemes.Count == 0)
        {
            _plansPanel.Children.Add(PerfUi.Text("Pp.Caption", "Aucun plan d'alimentation n'a pu être énuméré."));
        }
        foreach (var scheme in s.Schemes.OrderByDescending(x => x.IsActive).ThenBy(x => x.Name, StringComparer.CurrentCulture))
            _plansPanel.Children.Add(PlanRow(scheme));

        // Modèles intégrés absents de la liste : proposés à l'ajout.
        _addPanel.Children.Clear();
        var missing = new List<(string Key, string Label)>();
        if (!s.Schemes.Any(x => string.Equals(x.Name, s.UltimateName, StringComparison.CurrentCultureIgnoreCase) || x.Id == PowerApi.UltimateTemplate))
            missing.Add(("ultimate", s.UltimateName));
        if (!s.Schemes.Any(x => x.IsHighPerformance && x.Id != PowerApi.UltimateTemplate && x.Name != s.UltimateName))
            missing.Add(("high", PowerApi.ReadFriendlyName(PowerApi.HighPerformance) ?? "Performances élevées"));
        if (!s.Schemes.Any(x => x.IsPowerSaver))
            missing.Add(("saver", PowerApi.ReadFriendlyName(PowerApi.PowerSaver) ?? "Économie d'énergie"));

        _addWarning.Visibility = Visibility.Collapsed;
        if (missing.Count == 0) return;
        _addPanel.Children.Add(PerfUi.Text("Pp.Caption", "Ajouter un plan :", new Thickness(0, 0, 10, 0)).Centered());
        foreach (var (key, label) in missing)
        {
            var b = PerfUi.Button(label, "Pp.Button", "");
            b.Margin = new Thickness(0, 2, 6, 2);
            b.IsEnabled = !_powerBusy;
            b.Click += async (_, _) => await AddPlanAsync(key, label);
            _addPanel.Children.Add(b);
        }
        if (missing.Any(m => m.Key == "ultimate"))
        {
            var laptop = s.Source.HasBattery || AppHost.Profile.IsLaptopLike;
            _addWarning.Text = laptop
                ? "« Performances optimales » empêche le processeur et les périphériques de s'économiser : sur ce portable, l'autonomie " +
                  "et la chaleur en pâtissent. Conçu pour les stations de travail sur secteur."
                : "« Performances optimales » supprime les micro-latences d'économie d'énergie : utile pour les charges lourdes, au prix " +
                  "d'une consommation plus élevée même au repos.";
            _addWarning.Visibility = Visibility.Visible;
        }
    }

    private Grid PlanRow(PowerScheme scheme)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3), MinHeight = 36 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = scheme.IsActive ? PerfUi.Icon("", 16, "Pp.AccentText") : PerfUi.Icon("", 16, "Pp.TextSecondary");
        g.Children.Add(icon);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(PerfUi.Text("Pp.Body", scheme.Name));
        if (scheme.IsActive) line.Children.Add(PerfUi.AccentBadge("Actif"));
        texts.Children.Add(line);
        var family = scheme.Personality switch
        {
            SchemePersonality.Balanced => "Équilibré : performances à la demande, économie au repos (recommandé).",
            SchemePersonality.HighPerformance => "Performances : le processeur reste à haute fréquence, consommation plus élevée.",
            SchemePersonality.PowerSaver => "Économie : fréquences et luminosité réduites pour maximiser l'autonomie.",
            _ => null,
        };
        if (family is not null) texts.Children.Add(PerfUi.Text("Pp.Caption", family));
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);

        if (!scheme.IsActive)
        {
            var b = PerfUi.Button("Activer", "Pp.Button");
            b.VerticalAlignment = VerticalAlignment.Center;
            b.IsEnabled = !_powerBusy;
            System.Windows.Automation.AutomationProperties.SetName(b, "Activer le plan " + scheme.Name);
            b.Click += async (_, _) => await RunPowerAsync(() => PowerUi.ActivateSchemeAsync(scheme.Id));
            Grid.SetColumn(b, 2);
            g.Children.Add(b);
        }
        return g;
    }

    private void RenderTimeouts(PowerSnapshot s)
    {
        var hasBattery = s.Source.HasBattery || AppHost.Profile.HasBattery;
        foreach (var e in _batteryColumn) e.Visibility = hasBattery ? Visibility.Visible : Visibility.Collapsed;

        _updatingTimeouts = true;
        try
        {
            Fill(_monitorAc, s.Timeouts.MonitorAc);
            Fill(_monitorDc, s.Timeouts.MonitorDc);
            Fill(_sleepAc, s.Timeouts.SleepAc);
            Fill(_sleepDc, s.Timeouts.SleepDc);
        }
        finally { _updatingTimeouts = false; }

        _sleepCaption.Text = s.Active is { } a
            ? $"S'applique au plan actif (« {a.Name} »). « Jamais » garde l'écran allumé ou le PC éveillé tant qu'il est sous tension."
            : "Aucun plan actif : délais indisponibles.";
    }

    private static void Fill(ComboBox combo, int? seconds)
    {
        combo.Items.Clear();
        if (seconds is null)
        {
            combo.Items.Add(new ComboBoxItem { Content = "Indisponible", IsEnabled = false });
            combo.SelectedIndex = 0;
            combo.IsEnabled = false;
            return;
        }
        var minutes = (int)Math.Round(seconds.Value / 60.0);
        var values = TimeoutPresets.ToList();
        if (!values.Contains(minutes) && minutes <= 600) values.Add(minutes);
        values.Sort();
        foreach (var v in values)
        {
            var item = new ComboBoxItem { Content = v == 0 ? "Jamais" : PerfText.Minutes(v), Tag = v };
            combo.Items.Add(item);
            if (v == minutes) combo.SelectedItem = item;
        }
        combo.IsEnabled = true;
    }

    // ================================================================== Actions

    private async Task RunPowerAsync(Func<Task<Core.Engine.ApplyOutcome>> action)
    {
        if (_powerBusy) return;
        _powerBusy = true;
        if (_power is not null) { RenderModes(_power); RenderPlans(_power); }
        try
        {
            AppHost.Toasts.ShowOutcome(await action());
        }
        catch (Exception ex)
        {
            AppHost.Toasts.Show(ex.Message, ToastKind.Error);
        }
        finally
        {
            _powerBusy = false;
            await RefreshPowerAsync();
        }
    }

    private async Task AddPlanAsync(string key, string label)
    {
        if (_powerBusy) return;
        var laptop = _power?.Source.HasBattery == true || AppHost.Profile.IsLaptopLike;
        var message = key switch
        {
            "ultimate" => $"Le plan « {label} » sera ajouté à la liste des plans. Il maintient le processeur et les périphériques à pleine " +
                          "puissance en permanence." + (laptop ? "\n\nSur un portable, l'autonomie sera nettement réduite." : ""),
            "high" => $"Le plan « {label} » sera ajouté à la liste des plans.",
            _ => $"Le plan « {label} » sera ajouté à la liste des plans.",
        };
        if (!await AppHost.Dialogs.ConfirmAsync("Ajouter un plan d'alimentation",
                message + "\n\nUne autorisation administrateur sera demandée. Le plan pourra être supprimé depuis les options d'alimentation de Windows.",
                "Ajouter"))
            return;

        Core.Engine.ApplyOutcome? outcome = null;
        await RunPowerAsync(async () =>
        {
            outcome = await AppHost.Engine.RunActionAsync(AddSchemeAction.ActionId, new Dictionary<string, string> { ["template"] = key });
            return outcome;
        });
        if (outcome is { Success: true } && outcome.Data?.GetValueOrDefault("scheme") is { } id && Guid.TryParse(id, out var scheme)
            && _power?.Active?.Id != scheme
            && await AppHost.Dialogs.ConfirmAsync("Plan ajouté", $"Activer « {label} » maintenant ?", "Activer", "Plus tard"))
        {
            await RunPowerAsync(() => PowerUi.ActivateSchemeAsync(scheme));
        }
    }

    // ================================================================== Divers

    private static Border InfoWithLink(string text, string? linkText, Action? link)
    {
        var dock = new DockPanel();
        var icon = PerfUi.Icon("", 16, "Pp.AccentText");
        icon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        if (linkText is not null && link is not null)
        {
            var b = PerfUi.Button(linkText, "Pp.Button");
            b.Margin = new Thickness(12, 0, 0, 0);
            b.Click += (_, _) => link();
            DockPanel.SetDock(b, Dock.Right);
            dock.Children.Add(b);
        }
        var t = PerfUi.Text("Pp.Body", text);
        t.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(t);
        var border = new Border { Child = dock, Margin = new Thickness(0) };
        border.SetResourceReference(StyleProperty, "Pp.InfoBar");
        return border;
    }

    private static void OpenUri(string uri)
    {
        try { ProcessRunner.OpenSettingsUri(uri); }
        catch (Exception ex) { AppHost.Toasts.Show(ex.Message, ToastKind.Error); }
    }

    private static void Launch(SystemTool tool, params string[] args)
    {
        try { ProcessRunner.Launch(tool, args); }
        catch (Exception ex) { AppHost.Toasts.Show(ex.Message, ToastKind.Error); }
    }
}

internal static class TextBlockExtensions
{
    public static TextBlock Centered(this TextBlock t)
    {
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }
}
