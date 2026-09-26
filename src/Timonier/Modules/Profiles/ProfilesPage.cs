using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Timonier.Core.Platform;
using Timonier.Core.Security;
using Timonier.UI.Controls;
using Timonier.UI.Services;
using static Timonier.Modules.Profiles.ProfilesUi;

namespace Timonier.Modules.Profiles;

/// <summary>
/// Assistant « Installation et profils » : 1 Ce PC → 2 Profils → 3 Vérifier le plan → 4 Appliquer → 5 Rapport.
/// Paramètres de navigation : "choose", "choose:&lt;profil&gt;", "plan:&lt;profil&gt;,&lt;profil&gt;" (aperçu du plan, rien n'est appliqué).
/// </summary>
public sealed partial class ProfilesPage : UserControl, INavigationAware
{
    private enum Step { Pc = 0, Choose = 1, Review = 2, Apply = 3, Report = 4 }

    private static readonly string[] StepTitles = [L("This PC"), L("Profiles"), L("Review the plan"), L("Apply"), L("Report")];

    private readonly StackPanel _root;
    private readonly ScrollViewer? _scroll;
    private readonly Grid _stepper = new() { Margin = new Thickness(0, 4, 0, 20) };
    private readonly StackPanel _body = new();
    private readonly DockPanel _nav = new() { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };

    private Step _step = Step.Pc;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private bool _defaultsApplied;
    private bool _userTouched;
    private ImportedPlan? _import;
    private string? _pendingName;

    public ProfilesPage()
    {
        _root = PageScaffold.Create(this, L("Setup & profiles"),
            L("Set up this PC in a few steps: choose usage profiles, review each change, then apply everything at once."),
            ProfilesModule.Glyph);
        _scroll = Content as ScrollViewer;
        _root.Children.Add(_stepper);
        _root.Children.Add(_body);
        _root.Children.Add(_nav);

        Loaded += (_, _) =>
        {
            AppHost.HardwareLoaded -= OnHardwareLoaded;
            AppHost.HardwareLoaded += OnHardwareLoaded;
        };
        Unloaded += (_, _) => AppHost.HardwareLoaded -= OnHardwareLoaded;
        Render();
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (_running || parameter is not string p) return;
        if (p == "choose" || p.StartsWith("choose:", StringComparison.Ordinal))
        {
            EnsureDefaults();
            if (p.Length > 7 && ProfileCatalog.Find(p[7..]) is { } profile && IsOffered(profile))
            {
                _selected.Add(profile.Id);
                _userTouched = true;
            }
            GoTo(Step.Choose);
        }
        else if (p.StartsWith("plan:", StringComparison.Ordinal))
        {
            _selected.Clear();
            foreach (var id in p[5..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                if (ProfileCatalog.Find(id) is { } profile && IsOffered(profile)) _selected.Add(profile.Id);
            _defaultsApplied = _userTouched = true;
            _import = null;
            if (_selected.Count > 0) GoTo(Step.Review);
        }
    }

    private void OnHardwareLoaded(object? sender, EventArgs e)
    {
        if (_running) return;
        _counts = null; // les recommandations dépendent du matériel
        if (!_userTouched) { _defaultsApplied = false; EnsureDefaults(); }
        _selected.RemoveWhere(id => ProfileCatalog.Find(id) is not { } p || !IsOffered(p));
        if (_step is Step.Pc or Step.Choose) Render();
    }

    private static bool IsOffered(ProfileDefinition p) => p.Hidden?.Invoke(AppHost.Profile) != true;

    private void EnsureDefaults()
    {
        if (_defaultsApplied) return;
        _defaultsApplied = true;
        _selected.Clear();
        foreach (var p in ProfileCatalog.All)
            if (IsOffered(p) && p.Suggested?.Invoke(AppHost.Profile) == true) _selected.Add(p.Id);
    }

    private void GoTo(Step step)
    {
        if (step == Step.Choose) EnsureDefaults();
        _step = step;
        if (step == Step.Review) ComputePlan();
        Render();
        _scroll?.ScrollToTop();
    }

    private void Render()
    {
        RenderStepper();
        _body.Children.Clear();
        _nav.Children.Clear();
        switch (_step)
        {
            case Step.Pc: RenderPc(); break;
            case Step.Choose: RenderChoose(); break;
            case Step.Review: RenderReview(); break;
            case Step.Apply: RenderApply(); break;
            case Step.Report: RenderReport(); break;
        }
    }

    // ------------------------------------------------------------------ Stepper

    private void RenderStepper()
    {
        _stepper.Children.Clear();
        _stepper.ColumnDefinitions.Clear();
        for (var i = 0; i < StepTitles.Length; i++)
        {
            if (i > 0) _stepper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 16 });
            _stepper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        var current = (int)_step;
        for (var i = 0; i < StepTitles.Length; i++)
        {
            var done = i < current;
            var active = i == current;
            var circle = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
            UIElement mark = done
                ? Icon("", 12, "Pp.AccentText")
                : new TextBlock { Text = (i + 1).ToString(), FontSize = 13, FontWeight = FontWeights.SemiBold };
            if (mark is TextBlock number && !done)
                number.Themed(TextBlock.ForegroundProperty, active ? "Pp.TextOnAccent" : "Pp.TextSecondary");
            ((FrameworkElement)mark).HorizontalAlignment = HorizontalAlignment.Center;
            ((FrameworkElement)mark).VerticalAlignment = VerticalAlignment.Center;
            circle.Child = mark;
            circle.Themed(Border.BackgroundProperty, active ? "Pp.Accent" : done ? "Pp.AccentSubtle" : "Pp.ControlFill");
            circle.Themed(Border.BorderBrushProperty, active ? "Pp.Accent" : done ? "Pp.AccentSubtle" : "Pp.ControlStroke");

            var label = new TextBlock { Text = StepTitles[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 13 };
            label.Themed(TextBlock.ForegroundProperty, active ? "Pp.TextPrimary" : "Pp.TextSecondary");
            if (active) label.FontWeight = FontWeights.SemiBold;

            var item = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(circle);
            item.Children.Add(label);
            System.Windows.Automation.AutomationProperties.SetName(item, active ? L("Step {0}: {1} (in progress)", i + 1, StepTitles[i])
                : done ? L("Step {0}: {1} (completed)", i + 1, StepTitles[i])
                : L("Step {0}: {1}", i + 1, StepTitles[i]));

            // Retour possible vers une étape déjà franchie, tant que rien n'a été appliqué.
            if (done && _step is Step.Choose or Step.Review)
            {
                var target = (Step)i;
                item.Cursor = Cursors.Hand;
                item.ToolTip = L("Go back to this step");
                item.MouseLeftButtonUp += (_, _) => GoTo(target);
            }
            Grid.SetColumn(item, i * 2);
            _stepper.Children.Add(item);

            if (i > 0)
            {
                var line = new Border { Height = 2, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(1) };
                line.Themed(Border.BackgroundProperty, i <= current ? "Pp.Accent" : "Pp.Divider");
                Grid.SetColumn(line, i * 2 - 1);
                _stepper.Children.Add(line);
            }
        }
    }

    private void NavLeft(params UIElement[] items)
    {
        foreach (var e in items)
        {
            if (e is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(e, Dock.Left);
            _nav.Children.Add(e);
        }
    }

    private void NavRight(params UIElement[] items)
    {
        // Ajoutés de droite à gauche : le premier élément est le plus à droite (bouton principal).
        foreach (var e in items)
        {
            if (e is FrameworkElement fe) fe.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(e, Dock.Right);
            _nav.Children.Add(e);
        }
    }

    // ------------------------------------------------------------------ Étape 1 : Ce PC

    private void RenderPc()
    {
        var pc = AppHost.Profile;
        var hw = pc.HardwareLoaded;
        var pending = L("Detecting…");

        var card = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var tile = GlyphTile("");
        tile.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(tile, Dock.Left);
        head.Children.Add(tile);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Text(string.IsNullOrWhiteSpace(pc.Model) ? L("This PC") : $"{pc.Manufacturer} {pc.Model}".Trim(), "Pp.CardTitle"));
        titles.Children.Add(Caption(L("What Timonier detected. Profiles and recommendations adapt to it.")));
        head.Children.Add(titles);
        card.Children.Add(head);
        card.Children.Add(Divider(4, 6));

        card.Children.Add(KeyValue("Windows", pc.WindowsLabel));
        card.Children.Add(KeyValue(L("Edition"), pc.IsHomeEdition
            ? L("{0} — some policies only apply to Pro and higher editions", pc.EditionLabel)
            : pc.EditionLabel));
        card.Children.Add(KeyValue(L("Device type"), hw ? (pc.IsVirtualMachine ? L("{0} (virtual machine)", pc.FormFactorLabel) : pc.FormFactorLabel) : pending));
        card.Children.Add(KeyValue(L("Battery"), hw ? (pc.HasBattery ? L("Yes: the “Laptop and battery life” profile is offered") : L("No")) : pending));
        card.Children.Add(KeyValue(L("Processor"), hw ? Fallback(pc.CpuName) : pending));
        card.Children.Add(KeyValue(L("Memory"), hw ? (pc.RamGb > 0 ? L("{0:0.#} GB", pc.RamGb) : LC("feminine", "Unknown")) : pending));
        card.Children.Add(KeyValue(L("System disk"), hw ? DiskLabel(pc) : pending));
        card.Children.Add(KeyValue(L("Performance tier"), hw
            ? (pc.Tier == PerformanceTier.Low ? L("{0}: the “Low-end PC” profile is preselected", pc.TierLabel) : pc.TierLabel)
            : pending));

        Button? rename = null;
        // Membre d'un domaine : renommer localement romprait la relation d'approbation avec le domaine.
        if (AppHost.Registry.GetAction(RenameComputerAction.ActionId) is not null && !pc.IsDomainJoined)
        {
            rename = Button(L("Rename…"), "", "Pp.Button");
            rename.Click += async (_, _) => await RenameAsync(rename);
        }
        var name = _pendingName is null ? Fallback(pc.MachineName) : L("{0}, will become “{1}” after the restart", pc.MachineName, _pendingName);
        card.Children.Add(KeyValue(L("PC name"), name, rename));
        _body.Children.Add(Card(card));

        if (!hw)
            _body.Children.Add(InfoBar(L("Hardware detection is finishing in the background; this card will update by itself."), ""));
        if (pc.IsManaged)
            _body.Children.Add(InfoBar(
                L("This PC is managed by an organization (domain, Microsoft Entra ID or device management). Its policies may override or undo the changes made here. If in doubt, ask your IT department."),
                "", "Pp.InfoBar.Warning"));
        if (!pc.IsUserAdmin)
            _body.Children.Add(InfoBar(
                L("Your account isn't an administrator: settings that affect the whole PC will ask for an administrator's password."),
                ""));

        _body.Children.Add(Section(L("What profiles don't do")));
        var honest = new StackPanel();
        foreach (var line in new[]
        {
            L("They don't install any drivers and don't activate Windows."),
            L("They don't change your Microsoft account and don't create any user accounts."),
            L("They don't remove any preinstalled apps; they only install the apps you check."),
            L("They never turn off the antivirus, the firewall, User Account Control (UAC) or security updates."),
            L("Every applied setting is recorded in History and can be undone there, except those marked “Can't be undone” in the plan."),
            L("Nothing is applied without your consent: the “Review the plan” step shows every change before starting."),
        })
        {
            honest.Children.Add(Bullet(line));
        }
        _body.Children.Add(Card(honest));

        NavLeft(Button(L("Import a configuration…"), "", "Pp.Button", (_, _) => _ = ImportAsync()));
        NavRight(Button(L("Choose profiles"), "", "Pp.AccentButton", (_, _) => GoTo(Step.Choose)));
    }

    private static string Fallback(string value) => string.IsNullOrWhiteSpace(value) ? L("Unknown") : value;

    // eMMC : classée « SSD » par Windows, détectée comme sur la page Performance.
    private static string DiskLabel(SystemProfile pc) => Performance.HardwareAdvice.IsEmmc(pc.SystemDisk) ? L("eMMC storage") : pc.SystemDisk?.Media switch
    {
        DiskMedia.Nvme => "SSD NVMe",
        DiskMedia.Ssd => "SSD",
        DiskMedia.Hdd => L("Mechanical hard drive (HDD)"),
        _ => L("Unknown type"),
    };

    private static TextBlock Section(string title)
    {
        var t = Text(title, "Pp.SectionTitle");
        t.Margin = new Thickness(0, 12, 0, 8);
        return t;
    }

    private static UIElement Bullet(string text, string glyph = "", string brush = "Pp.Success")
    {
        var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var icon = Icon(glyph, 12, brush);
        icon.Margin = new Thickness(0, 3, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(Text(text));
        return dock;
    }

    private async Task RenameAsync(Button button)
    {
        var current = AppHost.Profile.MachineName;
        var name = await AppHost.Dialogs.PromptAsync(L("Rename this PC"),
            L("New name (15 characters maximum: letters without accents, numbers and hyphens). It takes effect at the next restart; an administrator will need to confirm."),
            _pendingName ?? current, false, RenameComputerAction.Check);
        if (name is null) return;
        name = name.Trim();
        if (string.Equals(name, _pendingName ?? current, StringComparison.OrdinalIgnoreCase))
        {
            AppHost.Toasts.Show(L("This is already the name of this PC."), ToastKind.Info);
            return;
        }
        button.IsEnabled = false;
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync(RenameComputerAction.ActionId, new Dictionary<string, string> { ["name"] = name });
            AppHost.Toasts.ShowOutcome(outcome);
            if (outcome.Success)
            {
                _pendingName = name;
                if (_step == Step.Pc) Render();
            }
        }
        finally { button.IsEnabled = true; }
    }

    // ------------------------------------------------------------------ Étape 2 : Profils

    private Dictionary<string, (int Tweaks, int Apps)>? _counts;
    private bool _countsLoading;

    /// <summary>Compte les réglages de chaque profil hors du thread d'interface (certaines conditions lisent le registre).</summary>
    private async void LoadCounts()
    {
        if (_countsLoading) return;
        _countsLoading = true;
        var hardware = AppHost.Profile.HardwareLoaded;
        try
        {
            var counts = await Task.Run(() => ProfileCatalog.All.ToDictionary(p => p.Id, p => ProfilePlanner.Count(p, AppHost.Registry, AppHost.Profile)));
            // Matériel détecté pendant le calcul : le résultat serait périmé, on recommence.
            if (hardware == AppHost.Profile.HardwareLoaded) _counts = counts;
        }
        catch (Exception ex)
        {
            Log.Warn("Profiles", "décompte des profils : " + ex.Message);
            _counts = []; // pas de nouvelle tentative en boucle : les cartes s'affichent sans décompte
        }
        finally { _countsLoading = false; }
        if (_counts is null && hardware != AppHost.Profile.HardwareLoaded) LoadCounts();
        else if (_step == Step.Choose && !_running) Render();
    }

    private void RenderChoose()
    {
        var pc = AppHost.Profile;
        if (_counts is null) LoadCounts();
        var intro = Caption(L("Check one or more profiles: they combine. If two profiles request a different option for the same setting, you'll choose in the next step. Nothing is applied before the plan is reviewed."));
        intro.Margin = new Thickness(0, 0, 0, 12);
        _body.Children.Add(intro);

        if (_import is not null)
        {
            var back = Button(L("Review the imported plan"), null, "Pp.Button", (_, _) => GoTo(Step.Review));
            _body.Children.Add(InfoBar(L("A plan imported from “{0}” is in progress. Checking or unchecking a profile will replace it with a plan calculated for this PC.", _import.FileName),
                "", "Pp.InfoBar", back));
        }
        if (!pc.HardwareLoaded)
            _body.Children.Add(InfoBar(L("Hardware detection in progress: the offered and preselected profiles may still change."), ""));

        var grid = new UniformGrid { Columns = 2 };
        var index = 0;
        foreach (var profile in ProfileCatalog.All.Where(IsOffered))
        {
            var cardElement = ProfileCard(profile);
            cardElement.Margin = new Thickness(index % 2 == 0 ? 0 : 6, 0, index % 2 == 0 ? 6 : 0, 12);
            grid.Children.Add(cardElement);
            index++;
        }
        _body.Children.Add(grid);

        NavLeft(Button(L("Back"), "", "Pp.Button", (_, _) => GoTo(Step.Pc)),
                Button(L("Import…"), "", "Pp.SubtleButton", (_, _) => _ = ImportAsync()));
        var count = _selected.Count;
        var next = Button(count == 0 ? L("Review the plan") : LP(count, "Review the plan ({0} profile)", "Review the plan ({0} profiles)"), "", "Pp.AccentButton",
            (_, _) => GoTo(Step.Review));
        next.IsEnabled = count > 0 || _import is not null;
        next.ToolTip = next.IsEnabled ? null : L("Check at least one profile.");
        NavRight(next);
    }

    private Border ProfileCard(ProfileDefinition profile)
    {
        var pc = AppHost.Profile;
        var selected = _selected.Contains(profile.Id);
        (int Tweaks, int Apps)? counts = _counts?.TryGetValue(profile.Id, out var c) == true ? c : null;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = GlyphTile(profile.Glyph, selected ? "Pp.Accent" : "Pp.AccentSubtle", selected ? "Pp.TextOnAccent" : "Pp.AccentText");
        tile.Margin = new Thickness(0, 2, 12, 0);
        grid.Children.Add(tile);

        var stack = new StackPanel();
        Grid.SetColumn(stack, 1);
        stack.Children.Add(Text(profile.Title, "Pp.CardTitle"));
        var badges = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
        if (profile.Suggested?.Invoke(pc) == true) badges.Children.Add(Badge(L("Suggested for this PC"), "Accent", ""));
        if (counts is var (tweaks, apps))
        {
            badges.Children.Add(Badge(tweaks == 0 ? L("No settings available") : LP(tweaks, "{0} setting", "{0} settings"), "Neutral", null,
                L("Settings available on this PC that this profile can change (before comparing with the current state).")));
            if (apps > 0) badges.Children.Add(Badge(LP(apps, "{0} app", "{0} apps"), "Neutral", ""));
        }
        if (profile.Id == ProfileCatalog.LowEnd && pc.HardwareLoaded && pc.Tier != PerformanceTier.Low)
            badges.Children.Add(Badge(L("This PC isn't low-end"), "Info", null, L("Only visual tweaks are offered; deeper settings are reserved for low-end PCs.")));
        stack.Children.Add(badges);
        var desc = Caption(profile.Description);
        desc.Margin = new Thickness(0, 2, 0, 6);
        stack.Children.Add(desc);
        foreach (var change in profile.Changes)
        {
            var line = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var dot = Icon("", 10, "Pp.TextTertiary");
            dot.Margin = new Thickness(0, 3, 8, 0);
            dot.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(dot, Dock.Left);
            line.Children.Add(dot);
            line.Children.Add(Caption(change));
            stack.Children.Add(line);
        }
        if (profile.Notes is not null)
        {
            var note = Caption(profile.Notes, "Pp.TextTertiary");
            note.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(note);
        }
        grid.Children.Add(stack);

        var check = new CheckBox { IsChecked = selected, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0), Focusable = true };
        System.Windows.Automation.AutomationProperties.SetName(check, profile.Title);
        check.Click += (_, _) => Toggle(profile.Id);
        Grid.SetColumn(check, 2);
        grid.Children.Add(check);

        var border = new Border { Child = grid, Cursor = Cursors.Hand }.Styled("Pp.CardInteractive");
        if (selected)
        {
            border.BorderThickness = new Thickness(2);
            border.Padding = new Thickness(15, 11, 15, 11);
            border.Themed(Border.BorderBrushProperty, "Pp.Accent");
        }
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            Toggle(profile.Id);
        };
        return border;
    }

    private void Toggle(string id)
    {
        if (!_selected.Remove(id)) _selected.Add(id);
        _userTouched = true;
        _import = null; // un choix de profils remplace le plan importé
        var offset = _scroll?.VerticalOffset ?? 0;
        Render();
        _scroll?.ScrollToVerticalOffset(offset);
    }

    // ------------------------------------------------------------------ Import

    private async Task ImportAsync()
    {
        if (_running) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L("Import a Timonier configuration"),
            Filter = L("Timonier configuration (*.json)|*.json|All files (*.*)|*.*"),
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        ImportedPlan imported;
        try
        {
            imported = await Task.Run(() => ProfileFile.Read(dialog.FileName, AppHost.Registry));
        }
        catch (ValidationException ex)
        {
            await AppHost.Dialogs.AlertAsync(L("File rejected"), ex.Message);
            return;
        }
        catch (Exception ex)
        {
            Log.Warn("Profiles", "import : " + ex.Message);
            await AppHost.Dialogs.AlertAsync(L("Import failed"), L("The file couldn't be read: {0}", ex.Message));
            return;
        }
        if (imported.Tweaks.Count == 0 && imported.Apps.Count == 0)
        {
            var detail = imported.Notices.Count > 0 ? "\n\n" + string.Join("\n", imported.Notices) : "";
            await AppHost.Dialogs.AlertAsync(L("Nothing to import"), L("This file doesn't contain any setting or app usable on this PC.{0}", detail));
            return;
        }
        _import = imported;
        _selected.Clear();
        foreach (var id in imported.Profiles)
            if (ProfileCatalog.Find(id) is { } p && IsOffered(p)) _selected.Add(id);
        _defaultsApplied = _userTouched = true;
        GoTo(Step.Review);
    }

    private async Task ExportAsync()
    {
        if (_plan is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L("Export the configuration"),
            Filter = L("Timonier configuration (*.json)|*.json"),
            FileName = L("pc-configuration-{0}.json", DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        // Le plan voulu (ce qui est coché, déjà en place ou indisponible ici) : l'autre PC pourra en profiter.
        var tweaks = _plan.Tweaks.Where(t => t.Selected || t.AtTarget).Concat(_plan.Unavailable)
            .Select(t => new KeyValuePair<string, string>(t.Tweak.Id, t.Target));
        var apps = _plan.Apps.Where(a => a.Selected || (a.Installed && a.Preselected)).Select(a => a.Id);
        var profiles = _import?.Profiles ?? [.. _selected];
        try
        {
            var json = ProfileFile.Serialize(profiles, tweaks, apps);
            await File.WriteAllTextAsync(dialog.FileName, json, new UTF8Encoding(false));
            AppHost.Toasts.Show(L("Configuration exported: {0}. It contains no PC name or account.", Path.GetFileName(dialog.FileName)), ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Warn("Profiles", "export : " + ex.Message);
            AppHost.Toasts.Show(L("Export failed: {0}", ex.Message), ToastKind.Error);
        }
    }
}
