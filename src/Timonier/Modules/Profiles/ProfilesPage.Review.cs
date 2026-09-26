using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using static Timonier.Modules.Profiles.ProfilesUi;

namespace Timonier.Modules.Profiles;

/// <summary>Étape 3 : calcul et vérification du plan.</summary>
public sealed partial class ProfilesPage
{
    private const string RestorePointAction = "maintenance.restorepoint.create";
    private const string WingetInstallAction = "apps.winget.install";

    private ProfilePlan? _plan;
    private string? _planError;
    private CancellationTokenSource? _planCts;
    private bool _createRestorePoint = true;
    private TextBlock? _summary;
    private Button? _applyButton;

    private async void ComputePlan()
    {
        _planCts?.Cancel();
        var cts = _planCts = new CancellationTokenSource();
        _plan = null;
        _planError = null;
        try
        {
            // Les recommandations dépendent du matériel : on attend la fin de sa détection (quelques secondes au plus).
            for (var waited = 0; !AppHost.Profile.HardwareLoaded && waited < 20000; waited += 250)
                await Task.Delay(250, cts.Token);
            var profiles = ProfileCatalog.All.Where(p => _selected.Contains(p.Id)).ToList();
            var plan = await ProfilePlanner.BuildAsync(profiles, _import, cts.Token);
            if (cts.IsCancellationRequested) return;
            _plan = plan;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Log.Error("Profiles", "calcul du plan", ex);
            _planError = ex.Message;
        }
        if (_step == Step.Review && ReferenceEquals(cts, _planCts)) Render();
    }

    private void RenderReview()
    {
        _summary = null;
        _applyButton = null;
        NavLeft(Button(L("Back"), "", "Pp.Button", (_, _) => { _planCts?.Cancel(); GoTo(Step.Choose); }));

        if (_planError is not null)
        {
            _body.Children.Add(StateCard("", L("The plan couldn't be calculated"), _planError,
                action: Button(L("Try again"), "", "Pp.Button", (_, _) => { ComputePlan(); Render(); })));
            return;
        }
        if (_plan is not { } plan)
        {
            _body.Children.Add(StateCard("", L("Calculating the plan…"),
                AppHost.Profile.HardwareLoaded
                    ? L("Reading the current state of each affected setting (without changing anything).")
                    : L("Finishing hardware detection, then reading the current state of each setting (without changing anything)."),
                busy: true));
            return;
        }

        // --- Résumé et options
        var top = new StackPanel();
        top.Children.Add(Text(L("Plan summary"), "Pp.CardTitle"));
        _summary = Text("");
        _summary.Margin = new Thickness(0, 4, 0, 8);
        top.Children.Add(_summary);
        top.Children.Add(Caption(_import is not null
            ? L("Plan imported from “{0}”: each line was checked for this PC. Uncheck what you don't want.", _import.FileName)
            : L("Profiles: {0}. Uncheck what you don't want to apply.", string.Join(", ", plan.Profiles.Select(p => p.Title)))));
        top.Children.Add(Divider(10, 8));
        if (AppHost.Registry.GetAction(RestorePointAction) is not null)
        {
            var restore = new CheckBox { IsChecked = _createRestorePoint, Content = L("Create a system restore point before applying (recommended)") };
            restore.Click += (_, _) => _createRestorePoint = restore.IsChecked == true;
            top.Children.Add(restore);
            top.Children.Add(Caption(L("It lets you return to the current state of Windows from the recovery options, in addition to Timonier's History.")));
        }
        else
        {
            top.Children.Add(Caption(L("Creating a restore point isn't available in this version of Timonier. Applied settings can still be undone one by one from History.")));
        }
        _body.Children.Add(Card(top));

        foreach (var notice in plan.Notices)
            _body.Children.Add(InfoBar(notice, "", "Pp.InfoBar.Warning"));
        if (_import is not null)
            _body.Children.Add(InfoBar(L("An imported file can come from anywhere: only settings known to Timonier and apps in winget format were kept, and nothing will be applied without your confirmation."), ""));
        foreach (var p in plan.Profiles.Where(p => p.Notes is not null && p.Id != ProfileCatalog.LowEnd))
        {
            var link = p.LinkPageId is { } pageId && AppHost.Registry.GetPage(pageId) is not null
                ? Button(p.LinkLabel ?? L("Open"), null, "Pp.Button", (_, _) => AppHost.Navigator.Navigate(pageId))
                : null;
            _body.Children.Add(InfoBar($"{p.Title} — {p.Notes}","", "Pp.InfoBar", link));
        }

        var main = plan.Tweaks.Where(t => !t.AtTarget || t.HasConflict).ToList();
        var done = plan.Tweaks.Where(t => t.AtTarget && !t.HasConflict).ToList();
        var conflicts = main.Count(t => t.HasConflict);
        if (conflicts > 0)
            _body.Children.Add(InfoBar(
                LP(conflicts, "{0} setting is requested differently by two profiles. By default, the choice of the profile highest in the profile list wins; you can change it on the relevant line.",
                    "{0} settings are requested differently by two profiles. By default, the choice of the profile highest in the profile list wins; you can change it on the relevant line."),
                ""));

        // --- Réglages
        var toChange = main.Count(t => !t.AtTarget);
        var preChecked = main.Count(t => t.Selected && !t.AtTarget);
        _body.Children.Add(Section(toChange == preChecked ? L("Settings to change ({0})", toChange) : LP(preChecked, "Settings to change ({0} of {1} checked)", "Settings to change ({0} of {1} checked)", toChange)));
        if (main.Count == 0)
        {
            _body.Children.Add(StateCard("", plan.Tweaks.Count == 0 ? L("No settings in this plan") : L("Everything is already in place"),
                plan.Tweaks.Count == 0
                    ? L("The chosen profiles don't request any setting available on this PC.")
                    : L("This PC is already configured as the chosen profiles request.")));
        }
        foreach (var group in main.GroupBy(t => t.CategoryTitle))
        {
            var header = Caption(group.Key.ToUpperInvariant(), "Pp.TextSecondary");
            header.FontWeight = FontWeights.SemiBold;
            header.Margin = new Thickness(2, 6, 0, 6);
            _body.Children.Add(header);
            var list = new StackPanel();
            var first = true;
            foreach (var row in group)
            {
                if (!first) list.Children.Add(Divider(8, 8));
                list.Children.Add(TweakRow(row));
                first = false;
            }
            _body.Children.Add(Card(list));
        }

        if (done.Count > 0)
        {
            var list = new StackPanel();
            foreach (var row in done)
                list.Children.Add(SimpleRow("", "Pp.Success", row.Tweak.Title, $"{row.CategoryTitle} · {row.TargetLabel}"));
            _body.Children.Add(Collapsible(L("Already in place ({0})", done.Count), Card(list, 0)));
        }
        if (plan.Unavailable.Count > 0)
        {
            var list = new StackPanel();
            foreach (var row in plan.Unavailable)
                list.Children.Add(SimpleRow("", "Pp.TextTertiary", row.Tweak.Title, L("Not available on this PC: {0}", row.Unavailable)));
            _body.Children.Add(Collapsible(L("Not available on this PC ({0})", plan.Unavailable.Count), Card(list, 0)));
        }

        // --- Applications
        RenderApps(plan);

        var export = Button(L("Export this plan…"), "", "Pp.SubtleButton", (_, _) => _ = ExportAsync());
        NavLeft(export);
        _applyButton = Button(L("Apply"), "", "Pp.AccentButton", (_, _) => _ = StartApplyAsync());
        NavRight(_applyButton);
        UpdateSummary();
    }

    private UIElement TweakRow(PlanTweak row)
    {
        var t = row.Tweak;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new CheckBox { IsChecked = row.Selected && !row.AtTarget, IsEnabled = !row.AtTarget, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
        System.Windows.Automation.AutomationProperties.SetName(check, t.Title);
        grid.Children.Add(check);

        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        var title = Text(t.Title);
        title.FontWeight = FontWeights.SemiBold;
        text.Children.Add(title);
        var change = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }.Styled("Pp.Caption");
        text.Children.Add(change);

        var badges = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (t.RequiresAdmin) badges.Children.Add(Badge(L("Administrator"), "Neutral", "", L("Applied by the admin session (a single authorization for the whole plan).")));
        if (t.Effect.HasFlag(ApplyEffect.Reboot)) badges.Children.Add(Badge(LC("noun (badge)", "Restart"), "Info", ""));
        else if (t.Effect.HasFlag(ApplyEffect.SignOut)) badges.Children.Add(Badge(LC("noun (badge)", "Sign out"), "Info", ""));
        else if (t.Effect.HasFlag(ApplyEffect.RestartExplorer)) badges.Children.Add(Badge(L("File Explorer restarted"), "Info"));
        if (t.Risk == RiskLevel.Moderate) badges.Children.Add(Badge(L("Moderate risk"), "Warning", ""));
        if (t.Risk == RiskLevel.Advanced) badges.Children.Add(Badge(L("Advanced"), "Danger", "", L("For advanced users only: never checked by default.")));
        if (!t.IsReversible) badges.Children.Add(Badge(L("Can't be undone"), "Danger"));
        if (row.HasConflict) badges.Children.Add(Badge(L("Profiles disagree"), "Accent"));
        if (row.LessSafe)
            badges.Children.Add(Badge(L("Security: check this"), "Warning","",
                L("Option from the imported file that Timonier doesn't offer for this PC: it may weaken its protection. Left unchecked.")));
        else if (row.OptIn && t.Risk != RiskLevel.Advanced) badges.Children.Add(Badge(L("Check if needed"), "Neutral", null, L("Useful, but may interfere with some software: left unchecked by default.")));
        if (badges.Children.Count > 0) text.Children.Add(badges);

        // Blocage de l'accès au compte, aux contacts, au calendrier, aux e-mails… : dire ce qui cessera de fonctionner.
        if (ProfileCatalog.IsPersonalDataPermission(t.Id) && row.Target == TweakDefinition.Off)
        {
            var impact = Caption(row.OptIn
                ? L("Left unchecked: blocking it prevents Mail, Calendar, Outlook or Phone Link from accessing this data.")
                : L("Blocking it prevents Mail, Calendar, Outlook or Phone Link from accessing this data."), "Pp.TextSecondary");
            impact.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(impact);
        }

        if (t.Warning is not null)
        {
            var warn = Caption("⚠ " + t.Warning, "Pp.Warning");
            warn.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(warn);
        }
        var sources = Caption(L("Requested by: {0}", string.Join(", ", row.Wants.Select(w => w.SourceTitle).Distinct())), "Pp.TextTertiary");
        sources.Margin = new Thickness(0, 4, 0, 0);
        text.Children.Add(sources);
        grid.Children.Add(text);

        void RefreshChange()
        {
            change.Inlines.Clear();
            change.Inlines.Add(new Run(L("Currently: {0}", row.CurrentLabel) + "   →   "));
            var target = new Run(row.TargetLabel) { FontWeight = FontWeights.SemiBold };
            target.SetResourceReference(TextElement.ForegroundProperty, "Pp.TextPrimary");
            change.Inlines.Add(target);
            if (row.AtTarget) change.Inlines.Add(new Run("  " + L("(already in place)")));
        }
        RefreshChange();

        if (row.HasConflict)
        {
            var combo = new ComboBox { MinWidth = 190, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(combo, L("Option selected for {0}", t.Title));
            foreach (var option in row.Wants.Select(w => w.Option).Distinct())
            {
                var by = string.Join(", ", row.Wants.Where(w => w.Option == option).Select(w => w.SourceTitle));
                combo.Items.Add(new ComboBoxItem { Content = $"{t.GetOption(option)?.Label ?? option} ({by})", Tag = option });
            }
            combo.SelectedIndex = Math.Max(0, combo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == row.Target));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not ComboBoxItem { Tag: string option }) return;
                row.Target = option;
                check.IsEnabled = !row.AtTarget;
                row.Selected = !row.AtTarget && (check.IsChecked == true || !row.OptIn);
                check.IsChecked = row.Selected;
                RefreshChange();
                UpdateSummary();
            };
            Grid.SetColumn(combo, 2);
            grid.Children.Add(combo);
        }

        check.Click += (_, _) =>
        {
            row.Selected = check.IsChecked == true;
            UpdateSummary();
        };
        return grid;
    }

    private static UIElement SimpleRow(string glyph, string brush, string title, string detail)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var icon = Icon(glyph, 12, brush);
        icon.Margin = new Thickness(0, 3, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        var stack = new StackPanel();
        stack.Children.Add(Text(title));
        stack.Children.Add(Caption(detail));
        dock.Children.Add(stack);
        return dock;
    }

    private void RenderApps(ProfilePlan plan)
    {
        var candidates = plan.Apps.Where(a => !a.Installed).ToList();
        var installed = plan.Apps.Where(a => a.Installed).ToList();
        _body.Children.Add(Section(LP(candidates.Count(a => a.Selected), "Apps ({0} checked)", "Apps ({0} checked)")));
        if (plan.Apps.Count == 0 && plan.SkippedHeavyApps == 0)
        {
            _body.Children.Add(StateCard("", L("No apps offered"), L("The chosen profiles don't install any apps. The full catalog is on the Apps page.")));
            return;
        }

        var hasWinget = AppHost.Registry.GetAction(WingetInstallAction) is not null;
        var intro = Caption(hasWinget
            ? L("Installed one by one with winget from their official source (downloaded from the internet). Installing an app means accepting its license. Checked apps were chosen by the profiles; the others are suggestions.")
            : L("The Apps module isn't available: apps can't be installed from this wizard."));
        intro.Margin = new Thickness(0, 0, 0, 8);
        _body.Children.Add(intro);

        // Choix des profils (et identifiants importés) en évidence ; simples suggestions repliées.
        var main = candidates.Where(a => a.Preselected || !a.InCatalog).ToList();
        var suggestions = candidates.Except(main).ToList();
        if (main.Count > 0) _body.Children.Add(Card(AppList(main, hasWinget)));
        else if (suggestions.Count > 0) _body.Children.Add(Caption(L("No apps are checked by default: see the suggestions below if needed.")));
        if (suggestions.Count > 0)
            _body.Children.Add(Collapsible(L("Other suggestions ({0})", suggestions.Count), Card(AppList(suggestions, hasWinget), 0)));

        if (plan.SkippedHeavyApps > 0)
            _body.Children.Add(InfoBar(LP(plan.SkippedHeavyApps,
                "{0} demanding app isn't offered on this low-end PC. You can still find it on the Apps page.",
                "{0} demanding apps aren't offered on this low-end PC. You can still find them on the Apps page."), ""));
        if (installed.Count > 0)
        {
            var list = new StackPanel();
            foreach (var app in installed)
                list.Children.Add(SimpleRow("", "Pp.Success", app.Name, L("Already installed")));
            _body.Children.Add(Collapsible(L("Already installed ({0})", installed.Count), Card(list, 0)));
        }
        else if (!plan.InstalledAppsKnown)
        {
            _body.Children.Add(Caption(L("The list of installed programs couldn't be read: winget will simply skip an app that's already there.")));
        }
    }

    private StackPanel AppList(IEnumerable<PlanApp> apps, bool hasWinget)
    {
        var list = new StackPanel();
        {
            var first = true;
            foreach (var app in apps)
            {
                if (!first) list.Children.Add(Divider(6, 6));
                first = false;
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var check = new CheckBox { IsChecked = app.Selected, IsEnabled = hasWinget, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
                System.Windows.Automation.AutomationProperties.SetName(check, app.Name);
                check.Click += (_, _) => { app.Selected = check.IsChecked == true; UpdateSummary(); };
                grid.Children.Add(check);
                var text = new StackPanel();
                Grid.SetColumn(text, 1);
                var head = new WrapPanel();
                var name = Text(app.Name, wrap: false);
                name.FontWeight = FontWeights.SemiBold;
                name.Margin = new Thickness(0, 0, 8, 0);
                head.Children.Add(name);
                if (!app.InCatalog)
                    head.Children.Add(Badge(L("Outside the verified catalog"), "Warning", "",
                        L("ID from the imported file: Windows will ask for an additional administrator confirmation.")));
                text.Children.Add(head);
                if (app.Description is not null) text.Children.Add(Caption(app.Description));
                var src = Caption(app.InCatalog
                    ? L("Suggested by: {0}", string.Join(", ", app.Sources))
                    : L("winget ID: {0} · {1}", app.Id, string.Join(", ", app.Sources)), "Pp.TextTertiary");
                src.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(src);
                grid.Children.Add(text);
                list.Children.Add(grid);
            }
        }
        return list;
    }

    private void UpdateSummary()
    {
        if (_plan is not { } plan || _summary is null) return;
        var tweaks = plan.SelectedTweaks.ToList();
        var apps = plan.SelectedApps.ToList();
        var admin = tweaks.Count(t => t.Tweak.RequiresAdmin);
        var effects = tweaks.Aggregate(ApplyEffect.None, (e, t) => e | t.Tweak.Effect);
        var irreversible = tweaks.Count(t => !t.Tweak.IsReversible);

        var parts = new List<string>
        {
            tweaks.Count == 0 ? L("No settings")
                : admin > 0 ? LP(tweaks.Count, "{0} setting ({1} with administrator rights)", "{0} settings ({1} with administrator rights)", admin)
                : LP(tweaks.Count, "{0} setting", "{0} settings"),
            apps.Count == 0 ? L("no apps") : LP(apps.Count, "{0} app", "{0} apps"),
        };
        var text = string.Join(", ", parts) + ".";
        if (EffectsText(effects) is { } fx) text += " " + L("Effects: {0}.", fx);
        if (irreversible > 0) text += " " + LP(irreversible, "{0} change can't be undone.", "{0} changes can't be undone.");
        if (admin > 0 || apps.Count > 0) text += " " + L("Windows will ask for administrator permission (UAC) at most once for everything.");
        _summary.Text = text;
        if (_applyButton is not null)
        {
            var total = tweaks.Count + apps.Count;
            _applyButton.IsEnabled = total > 0;
            _applyButton.ToolTip = total > 0 ? null : L("Check at least one setting or app.");
        }
    }

    private static string? EffectsText(ApplyEffect effects)
    {
        var list = new List<string>();
        if (effects.HasFlag(ApplyEffect.Reboot)) list.Add(L("restarting the PC"));
        if (effects.HasFlag(ApplyEffect.SignOut)) list.Add(L("signing out"));
        if (effects.HasFlag(ApplyEffect.RestartExplorer)) list.Add(L("restarting File Explorer"));
        return list.Count == 0 ? null : string.Join(", ", list);
    }
}
