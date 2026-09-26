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

    private static readonly string[] StepTitles = [L("Ce PC"), L("Profils"), L("Vérifier le plan"), L("Appliquer"), L("Rapport")];

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
        _root = PageScaffold.Create(this, L("Installation et profils"),
            L("Configurez ce PC en quelques étapes : choisissez des profils d'usage, vérifiez chaque changement, puis appliquez tout en une fois."),
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
            System.Windows.Automation.AutomationProperties.SetName(item, active ? L("Étape {0} : {1} (en cours)", i + 1, StepTitles[i])
                : done ? L("Étape {0} : {1} (terminée)", i + 1, StepTitles[i])
                : L("Étape {0} : {1}", i + 1, StepTitles[i]));

            // Retour possible vers une étape déjà franchie, tant que rien n'a été appliqué.
            if (done && _step is Step.Choose or Step.Review)
            {
                var target = (Step)i;
                item.Cursor = Cursors.Hand;
                item.ToolTip = L("Revenir à cette étape");
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
        var pending = L("Détection en cours…");

        var card = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var tile = GlyphTile("");
        tile.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(tile, Dock.Left);
        head.Children.Add(tile);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Text(string.IsNullOrWhiteSpace(pc.Model) ? L("Ce PC") : $"{pc.Manufacturer} {pc.Model}".Trim(), "Pp.CardTitle"));
        titles.Children.Add(Caption(L("Ce que Timonier a détecté. Les profils et les recommandations s'y adaptent.")));
        head.Children.Add(titles);
        card.Children.Add(head);
        card.Children.Add(Divider(4, 6));

        card.Children.Add(KeyValue("Windows", pc.WindowsLabel));
        card.Children.Add(KeyValue(L("Édition"), pc.IsHomeEdition
            ? L("{0} — certaines stratégies ne s'appliquent qu'aux éditions Professionnel et supérieures", pc.EditionLabel)
            : pc.EditionLabel));
        card.Children.Add(KeyValue(L("Type d'appareil"), hw ? (pc.IsVirtualMachine ? L("{0} (machine virtuelle)", pc.FormFactorLabel) : pc.FormFactorLabel) : pending));
        card.Children.Add(KeyValue(L("Batterie"), hw ? (pc.HasBattery ? L("Oui : le profil « Portable et autonomie » est proposé") : L("Non")) : pending));
        card.Children.Add(KeyValue(L("Processeur"), hw ? Fallback(pc.CpuName) : pending));
        card.Children.Add(KeyValue(L("Mémoire"), hw ? (pc.RamGb > 0 ? L("{0:0.#} Go", pc.RamGb) : L("Inconnue")) : pending));
        card.Children.Add(KeyValue(L("Disque système"), hw ? DiskLabel(pc) : pending));
        card.Children.Add(KeyValue(L("Gamme de performance"), hw
            ? (pc.Tier == PerformanceTier.Low ? L("{0} : le profil « PC modeste » est présélectionné", pc.TierLabel) : pc.TierLabel)
            : pending));

        Button? rename = null;
        // Membre d'un domaine : renommer localement romprait la relation d'approbation avec le domaine.
        if (AppHost.Registry.GetAction(RenameComputerAction.ActionId) is not null && !pc.IsDomainJoined)
        {
            rename = Button(L("Renommer…"), "", "Pp.Button");
            rename.Click += async (_, _) => await RenameAsync(rename);
        }
        var name = _pendingName is null ? Fallback(pc.MachineName) : L("{0}, deviendra « {1} » après le redémarrage", pc.MachineName, _pendingName);
        card.Children.Add(KeyValue(L("Nom du PC"), name, rename));
        _body.Children.Add(Card(card));

        if (!hw)
            _body.Children.Add(InfoBar(L("La détection du matériel se termine en arrière-plan ; cette fiche se mettra à jour d'elle-même."), ""));
        if (pc.IsManaged)
            _body.Children.Add(InfoBar(
                L("Ce PC est géré par une organisation (domaine, Microsoft Entra ID ou gestion des appareils). Ses stratégies peuvent remplacer ou annuler les changements faits ici. En cas de doute, demandez à votre service informatique."),
                "", "Pp.InfoBar.Warning"));
        if (!pc.IsUserAdmin)
            _body.Children.Add(InfoBar(
                L("Votre compte n'est pas administrateur : les réglages qui concernent tout le PC demanderont le mot de passe d'un administrateur."),
                ""));

        _body.Children.Add(Section(L("Ce que les profils ne font pas")));
        var honest = new StackPanel();
        foreach (var line in new[]
        {
            L("Ils n'installent aucun pilote et n'activent pas Windows."),
            L("Ils ne modifient pas votre compte Microsoft et ne créent aucun compte utilisateur."),
            L("Ils ne suppriment aucune application préinstallée ; ils installent seulement les applications que vous cochez."),
            L("Ils ne désactivent jamais l'antivirus, le pare-feu, le contrôle de compte d'utilisateur (UAC) ni les mises à jour de sécurité."),
            L("Chaque réglage appliqué est inscrit au Journal et peut y être annulé, sauf ceux marqués « Non annulable » dans le plan."),
            L("Rien n'est appliqué sans votre accord : l'étape « Vérifier le plan » montre chaque changement avant de commencer."),
        })
        {
            honest.Children.Add(Bullet(line));
        }
        _body.Children.Add(Card(honest));

        NavLeft(Button(L("Importer une configuration…"), "", "Pp.Button", (_, _) => _ = ImportAsync()));
        NavRight(Button(L("Choisir des profils"), "", "Pp.AccentButton", (_, _) => GoTo(Step.Choose)));
    }

    private static string Fallback(string value) => string.IsNullOrWhiteSpace(value) ? L("Inconnu") : value;

    // eMMC : classée « SSD » par Windows, détectée comme sur la page Performance.
    private static string DiskLabel(SystemProfile pc) => Performance.HardwareAdvice.IsEmmc(pc.SystemDisk) ? L("Mémoire eMMC") : pc.SystemDisk?.Media switch
    {
        DiskMedia.Nvme => "SSD NVMe",
        DiskMedia.Ssd => "SSD",
        DiskMedia.Hdd => L("Disque dur mécanique (HDD)"),
        _ => L("Type inconnu"),
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
        var name = await AppHost.Dialogs.PromptAsync(L("Renommer ce PC"),
            L("Nouveau nom (15 caractères au maximum : lettres sans accent, chiffres et trait d'union). Il sera pris en compte au prochain redémarrage ; un administrateur devra confirmer."),
            _pendingName ?? current, false, RenameComputerAction.Check);
        if (name is null) return;
        name = name.Trim();
        if (string.Equals(name, _pendingName ?? current, StringComparison.OrdinalIgnoreCase))
        {
            AppHost.Toasts.Show(L("C'est déjà le nom de ce PC."), ToastKind.Info);
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
        var intro = Caption(L("Cochez un ou plusieurs profils : ils se combinent. Si deux profils demandent une option différente pour le même réglage, vous choisirez à l'étape suivante. Rien n'est appliqué avant la vérification du plan."));
        intro.Margin = new Thickness(0, 0, 0, 12);
        _body.Children.Add(intro);

        if (_import is not null)
        {
            var back = Button(L("Revoir le plan importé"), null, "Pp.Button", (_, _) => GoTo(Step.Review));
            _body.Children.Add(InfoBar(L("Un plan importé depuis « {0} » est en cours. Cocher ou décocher un profil le remplacera par un plan calculé pour ce PC.", _import.FileName),
                "", "Pp.InfoBar", back));
        }
        if (!pc.HardwareLoaded)
            _body.Children.Add(InfoBar(L("Détection du matériel en cours : les profils proposés et présélectionnés peuvent encore s'ajuster."), ""));

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

        NavLeft(Button(L("Précédent"), "", "Pp.Button", (_, _) => GoTo(Step.Pc)),
                Button(L("Importer…"), "", "Pp.SubtleButton", (_, _) => _ = ImportAsync()));
        var count = _selected.Count;
        var next = Button(count == 0 ? L("Vérifier le plan") : LP(count, "Vérifier le plan ({0} profil)", "Vérifier le plan ({0} profils)"), "", "Pp.AccentButton",
            (_, _) => GoTo(Step.Review));
        next.IsEnabled = count > 0 || _import is not null;
        next.ToolTip = next.IsEnabled ? null : L("Cochez au moins un profil.");
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
        if (profile.Suggested?.Invoke(pc) == true) badges.Children.Add(Badge(L("Suggéré pour ce PC"), "Accent", ""));
        if (counts is var (tweaks, apps))
        {
            badges.Children.Add(Badge(tweaks == 0 ? L("Aucun réglage disponible") : LP(tweaks, "{0} réglage", "{0} réglages"), "Neutral", null,
                L("Réglages disponibles sur ce PC que ce profil peut modifier (avant comparaison avec l'état actuel).")));
            if (apps > 0) badges.Children.Add(Badge(LP(apps, "{0} application", "{0} applications"), "Neutral", ""));
        }
        if (profile.Id == ProfileCatalog.LowEnd && pc.HardwareLoaded && pc.Tier != PerformanceTier.Low)
            badges.Children.Add(Badge(L("Ce PC n'est pas modeste"), "Info", null, L("Seuls les allègements visuels sont proposés ; les réglages plus profonds restent réservés aux PC modestes.")));
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
            Title = L("Importer une configuration Timonier"),
            Filter = L("Configuration Timonier (*.json)|*.json|Tous les fichiers (*.*)|*.*"),
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
            await AppHost.Dialogs.AlertAsync(L("Fichier refusé"), ex.Message);
            return;
        }
        catch (Exception ex)
        {
            Log.Warn("Profiles", "import : " + ex.Message);
            await AppHost.Dialogs.AlertAsync(L("Import impossible"), L("Le fichier n'a pas pu être lu : {0}", ex.Message));
            return;
        }
        if (imported.Tweaks.Count == 0 && imported.Apps.Count == 0)
        {
            var detail = imported.Notices.Count > 0 ? "\n\n" + string.Join("\n", imported.Notices) : "";
            await AppHost.Dialogs.AlertAsync(L("Rien à importer"), L("Ce fichier ne contient aucun réglage ni aucune application utilisable sur ce PC.{0}", detail));
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
            Title = L("Exporter la configuration"),
            Filter = L("Configuration Timonier (*.json)|*.json"),
            FileName = L("configuration-pc-{0}.json", DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
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
            AppHost.Toasts.Show(L("Configuration exportée : {0}. Elle ne contient ni nom de PC ni compte.", Path.GetFileName(dialog.FileName)), ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Warn("Profiles", "export : " + ex.Message);
            AppHost.Toasts.Show(L("Export impossible : {0}", ex.Message), ToastKind.Error);
        }
    }
}
