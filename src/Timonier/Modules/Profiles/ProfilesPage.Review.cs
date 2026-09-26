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
        NavLeft(Button(L("Précédent"), "", "Pp.Button", (_, _) => { _planCts?.Cancel(); GoTo(Step.Choose); }));

        if (_planError is not null)
        {
            _body.Children.Add(StateCard("", L("Le plan n'a pas pu être calculé"), _planError,
                action: Button(L("Réessayer"), "", "Pp.Button", (_, _) => { ComputePlan(); Render(); })));
            return;
        }
        if (_plan is not { } plan)
        {
            _body.Children.Add(StateCard("", L("Calcul du plan…"),
                AppHost.Profile.HardwareLoaded
                    ? L("Lecture de l'état actuel de chaque réglage concerné (sans rien modifier).")
                    : L("Fin de la détection du matériel, puis lecture de l'état actuel de chaque réglage (sans rien modifier)."),
                busy: true));
            return;
        }

        // --- Résumé et options
        var top = new StackPanel();
        top.Children.Add(Text(L("Résumé du plan"), "Pp.CardTitle"));
        _summary = Text("");
        _summary.Margin = new Thickness(0, 4, 0, 8);
        top.Children.Add(_summary);
        top.Children.Add(Caption(_import is not null
            ? L("Plan importé depuis « {0} » : chaque ligne a été vérifiée pour ce PC. Décochez ce que vous ne voulez pas.", _import.FileName)
            : L("Profils : {0}. Décochez ce que vous ne voulez pas appliquer.", string.Join(", ", plan.Profiles.Select(p => p.Title)))));
        top.Children.Add(Divider(10, 8));
        if (AppHost.Registry.GetAction(RestorePointAction) is not null)
        {
            var restore = new CheckBox { IsChecked = _createRestorePoint, Content = L("Créer un point de restauration système avant d'appliquer (recommandé)") };
            restore.Click += (_, _) => _createRestorePoint = restore.IsChecked == true;
            top.Children.Add(restore);
            top.Children.Add(Caption(L("Il permet de revenir à l'état actuel de Windows depuis les options de récupération, en plus du Journal de Timonier.")));
        }
        else
        {
            top.Children.Add(Caption(L("La création d'un point de restauration n'est pas disponible dans cette version de Timonier. Les réglages appliqués restent annulables un par un depuis le Journal.")));
        }
        _body.Children.Add(Card(top));

        foreach (var notice in plan.Notices)
            _body.Children.Add(InfoBar(notice, "", "Pp.InfoBar.Warning"));
        if (_import is not null)
            _body.Children.Add(InfoBar(L("Un fichier importé peut venir de n'importe où : seuls des réglages connus de Timonier et des applications au format winget ont été retenus, et rien ne sera appliqué sans votre confirmation."), ""));
        foreach (var p in plan.Profiles.Where(p => p.Notes is not null && p.Id != ProfileCatalog.LowEnd))
        {
            var link = p.LinkPageId is { } pageId && AppHost.Registry.GetPage(pageId) is not null
                ? Button(p.LinkLabel ?? L("Ouvrir"), null, "Pp.Button", (_, _) => AppHost.Navigator.Navigate(pageId))
                : null;
            _body.Children.Add(InfoBar($"{p.Title} — {p.Notes}","", "Pp.InfoBar", link));
        }

        var main = plan.Tweaks.Where(t => !t.AtTarget || t.HasConflict).ToList();
        var done = plan.Tweaks.Where(t => t.AtTarget && !t.HasConflict).ToList();
        var conflicts = main.Count(t => t.HasConflict);
        if (conflicts > 0)
            _body.Children.Add(InfoBar(
                LP(conflicts, "{0} réglage est demandé différemment par deux profils. Par défaut, le choix du profil le plus haut dans la liste des profils l'emporte ; vous pouvez le changer sur la ligne concernée.",
                    "{0} réglages sont demandés différemment par deux profils. Par défaut, le choix du profil le plus haut dans la liste des profils l'emporte ; vous pouvez le changer sur la ligne concernée."),
                ""));

        // --- Réglages
        var toChange = main.Count(t => !t.AtTarget);
        var preChecked = main.Count(t => t.Selected && !t.AtTarget);
        _body.Children.Add(Section(toChange == preChecked ? L("Réglages à modifier ({0})", toChange) : LP(preChecked, "Réglages à modifier ({0} coché sur {1})", "Réglages à modifier ({0} cochés sur {1})", toChange)));
        if (main.Count == 0)
        {
            _body.Children.Add(StateCard("", plan.Tweaks.Count == 0 ? L("Aucun réglage dans ce plan") : L("Tout est déjà en place"),
                plan.Tweaks.Count == 0
                    ? L("Les profils choisis ne demandent aucun réglage disponible sur ce PC.")
                    : L("Ce PC est déjà configuré comme le demandent les profils choisis.")));
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
            _body.Children.Add(Collapsible(L("Déjà en place ({0})", done.Count), Card(list, 0)));
        }
        if (plan.Unavailable.Count > 0)
        {
            var list = new StackPanel();
            foreach (var row in plan.Unavailable)
                list.Children.Add(SimpleRow("", "Pp.TextTertiary", row.Tweak.Title, L("Non disponible sur ce PC : {0}", row.Unavailable)));
            _body.Children.Add(Collapsible(L("Non disponible sur ce PC ({0})", plan.Unavailable.Count), Card(list, 0)));
        }

        // --- Applications
        RenderApps(plan);

        var export = Button(L("Exporter ce plan…"), "", "Pp.SubtleButton", (_, _) => _ = ExportAsync());
        NavLeft(export);
        _applyButton = Button(L("Appliquer"), "", "Pp.AccentButton", (_, _) => _ = StartApplyAsync());
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
        if (t.RequiresAdmin) badges.Children.Add(Badge(L("Administrateur"), "Neutral", "", L("Appliqué par la session administrateur (une seule autorisation pour tout le plan).")));
        if (t.Effect.HasFlag(ApplyEffect.Reboot)) badges.Children.Add(Badge(L("Redémarrage"), "Info", ""));
        else if (t.Effect.HasFlag(ApplyEffect.SignOut)) badges.Children.Add(Badge(L("Déconnexion"), "Info", ""));
        else if (t.Effect.HasFlag(ApplyEffect.RestartExplorer)) badges.Children.Add(Badge(L("Explorateur relancé"), "Info"));
        if (t.Risk == RiskLevel.Moderate) badges.Children.Add(Badge(L("Risque modéré"), "Warning", ""));
        if (t.Risk == RiskLevel.Advanced) badges.Children.Add(Badge(L("Avancé"), "Danger", "", L("Réservé aux utilisateurs avertis : jamais coché d'office.")));
        if (!t.IsReversible) badges.Children.Add(Badge(L("Non annulable"), "Danger"));
        if (row.HasConflict) badges.Children.Add(Badge(L("Profils en désaccord"), "Accent"));
        if (row.LessSafe)
            badges.Children.Add(Badge(L("Sécurité : à vérifier"), "Warning","",
                L("Option venue du fichier importé, que Timonier ne propose pas pour ce PC : elle peut affaiblir sa protection. Laissée décochée.")));
        else if (row.OptIn && t.Risk != RiskLevel.Advanced) badges.Children.Add(Badge(L("À cocher si besoin"), "Neutral", null, L("Utile, mais peut gêner certains logiciels : laissé décoché par défaut.")));
        if (badges.Children.Count > 0) text.Children.Add(badges);

        // Blocage de l'accès au compte, aux contacts, au calendrier, aux e-mails… : dire ce qui cessera de fonctionner.
        if (ProfileCatalog.IsPersonalDataPermission(t.Id) && row.Target == TweakDefinition.Off)
        {
            var impact = Caption(row.OptIn
                ? L("Laissé décoché : le bloquer empêche Courrier, Calendrier, Outlook ou Lien avec Windows d'accéder à ces données.")
                : L("Le bloquer empêche Courrier, Calendrier, Outlook ou Lien avec Windows d'accéder à ces données."), "Pp.TextSecondary");
            impact.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(impact);
        }

        if (t.Warning is not null)
        {
            var warn = Caption("⚠ " + t.Warning, "Pp.Warning");
            warn.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(warn);
        }
        var sources = Caption(L("Demandé par : {0}", string.Join(", ", row.Wants.Select(w => w.SourceTitle).Distinct())), "Pp.TextTertiary");
        sources.Margin = new Thickness(0, 4, 0, 0);
        text.Children.Add(sources);
        grid.Children.Add(text);

        void RefreshChange()
        {
            change.Inlines.Clear();
            change.Inlines.Add(new Run(L("Actuellement : {0}", row.CurrentLabel) + "   →   "));
            var target = new Run(row.TargetLabel) { FontWeight = FontWeights.SemiBold };
            target.SetResourceReference(TextElement.ForegroundProperty, "Pp.TextPrimary");
            change.Inlines.Add(target);
            if (row.AtTarget) change.Inlines.Add(new Run("  " + L("(déjà en place)")));
        }
        RefreshChange();

        if (row.HasConflict)
        {
            var combo = new ComboBox { MinWidth = 190, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(combo, L("Option retenue pour {0}", t.Title));
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
        _body.Children.Add(Section(LP(candidates.Count(a => a.Selected), "Applications ({0} cochée)", "Applications ({0} cochées)")));
        if (plan.Apps.Count == 0 && plan.SkippedHeavyApps == 0)
        {
            _body.Children.Add(StateCard("", L("Aucune application proposée"), L("Les profils choisis n'installent pas d'application. Le catalogue complet est dans la page Applications.")));
            return;
        }

        var hasWinget = AppHost.Registry.GetAction(WingetInstallAction) is not null;
        var intro = Caption(hasWinget
            ? L("Installées une par une avec winget depuis leur source officielle (téléchargement Internet). Installer une application vaut acceptation de sa licence. Celles qui sont cochées ont été choisies par les profils ; les autres sont des suggestions.")
            : L("Le module Applications n'est pas disponible : les applications ne pourront pas être installées depuis cet assistant."));
        intro.Margin = new Thickness(0, 0, 0, 8);
        _body.Children.Add(intro);

        // Choix des profils (et identifiants importés) en évidence ; simples suggestions repliées.
        var main = candidates.Where(a => a.Preselected || !a.InCatalog).ToList();
        var suggestions = candidates.Except(main).ToList();
        if (main.Count > 0) _body.Children.Add(Card(AppList(main, hasWinget)));
        else if (suggestions.Count > 0) _body.Children.Add(Caption(L("Aucune application n'est cochée d'office : voyez les suggestions ci-dessous si besoin.")));
        if (suggestions.Count > 0)
            _body.Children.Add(Collapsible(L("Autres suggestions ({0})", suggestions.Count), Card(AppList(suggestions, hasWinget), 0)));

        if (plan.SkippedHeavyApps > 0)
            _body.Children.Add(InfoBar(LP(plan.SkippedHeavyApps,
                "{0} application exigeante n'est pas proposée sur ce PC modeste. Vous la trouverez quand même dans la page Applications.",
                "{0} applications exigeantes ne sont pas proposées sur ce PC modeste. Vous les trouverez quand même dans la page Applications."), ""));
        if (installed.Count > 0)
        {
            var list = new StackPanel();
            foreach (var app in installed)
                list.Children.Add(SimpleRow("", "Pp.Success", app.Name, L("Déjà installée")));
            _body.Children.Add(Collapsible(L("Déjà installées ({0})", installed.Count), Card(list, 0)));
        }
        else if (!plan.InstalledAppsKnown)
        {
            _body.Children.Add(Caption(L("La liste des programmes installés n'a pas pu être lue : winget ignorera simplement une application déjà présente.")));
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
                    head.Children.Add(Badge(L("Hors catalogue vérifié"), "Warning", "",
                        L("Identifiant venu du fichier importé : Windows demandera une confirmation administrateur supplémentaire.")));
                text.Children.Add(head);
                if (app.Description is not null) text.Children.Add(Caption(app.Description));
                var src = Caption(app.InCatalog
                    ? L("Proposé par : {0}", string.Join(", ", app.Sources))
                    : L("Identifiant winget : {0} · {1}", app.Id, string.Join(", ", app.Sources)), "Pp.TextTertiary");
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
            tweaks.Count == 0 ? L("Aucun réglage")
                : admin > 0 ? LP(tweaks.Count, "{0} réglage (dont {1} avec les droits d'administrateur)", "{0} réglages (dont {1} avec les droits d'administrateur)", admin)
                : LP(tweaks.Count, "{0} réglage", "{0} réglages"),
            apps.Count == 0 ? L("aucune application") : LP(apps.Count, "{0} application", "{0} applications"),
        };
        var text = string.Join(", ", parts) + ".";
        if (EffectsText(effects) is { } fx) text += " " + L("Effets : {0}.", fx);
        if (irreversible > 0) text += " " + LP(irreversible, "{0} changement non annulable.", "{0} changements non annulables.");
        if (admin > 0 || apps.Count > 0) text += " " + L("Windows demandera au plus une fois l'autorisation administrateur (UAC) pour l'ensemble.");
        _summary.Text = text;
        if (_applyButton is not null)
        {
            var total = tweaks.Count + apps.Count;
            _applyButton.IsEnabled = total > 0;
            _applyButton.ToolTip = total > 0 ? null : L("Cochez au moins un réglage ou une application.");
        }
    }

    private static string? EffectsText(ApplyEffect effects)
    {
        var list = new List<string>();
        if (effects.HasFlag(ApplyEffect.Reboot)) list.Add(L("redémarrage du PC"));
        if (effects.HasFlag(ApplyEffect.SignOut)) list.Add(L("déconnexion de la session"));
        if (effects.HasFlag(ApplyEffect.RestartExplorer)) list.Add(L("relance de l'Explorateur"));
        return list.Count == 0 ? null : string.Join(", ", list);
    }
}
