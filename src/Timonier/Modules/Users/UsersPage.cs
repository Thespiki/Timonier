using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Localization;
using Timonier.Core.Platform;
using Timonier.UI.Controls;
using Timonier.UI.Services;
using static Timonier.Modules.Users.UsersUi;

namespace Timonier.Modules.Users;

/// <summary>
/// Page « Utilisateurs » : comptes locaux, plages horaires, restrictions par compte, contrôle parental Microsoft et
/// écran de connexion. Les comptes sont lus hors du thread UI à l'arrivée sur la page (pas de minuteur) ; les réglages
/// déclaratifs sont créés après le premier affichage.
/// </summary>
public sealed class UsersPage : UserControl, INavigationAware
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private readonly ScrollViewer _scroll;
    private readonly AccountsPanel _accounts = new();
    private readonly LogonHoursPanel _hours = new();
    private readonly RestrictionsPanel _restrictions = new();
    private readonly TextBlock _familyHeading;
    private readonly TextBlock _signInHeading;
    private readonly ContentControl _tweaksHost = new();
    private readonly TextBlock _metricActive = Metric();
    private readonly TextBlock _metricAdmins = Metric();
    private readonly TextBlock _metricRestricted = Metric();
    private readonly TextBlock _you = Caption("");
    private readonly TextBlock _lockoutState = Caption(L("Lecture de la stratégie…"));
    private readonly ComboBox _lockoutChoice = new() { MinWidth = 220 };
    private readonly Button _lockoutApply;
    private readonly ProgressBar _lockoutBusy = BusyBar();
    private TweakListView? _tweaks;
    private List<LocalAccount>? _data;
    private DateTime _loadedAt;
    private bool _loading, _tweaksStarted;
    private string? _pending;

    public UsersPage()
    {
        Focusable = false;
        var stack = new StackPanel();
        stack.SetResourceReference(StyleProperty, "Pp.PageStack");
        _scroll = new ScrollViewer { Content = stack };
        _scroll.SetResourceReference(StyleProperty, "Pp.PageScroll");
        Content = _scroll;

        stack.Children.Add(new PageHeader
        {
            Title = L("Utilisateurs et contrôle parental"),
            Subtitle = L("Comptes de ce PC, heures de connexion et restrictions pour les enfants, écran de connexion."),
            Glyph = UsersModule.Glyph,
        });
        stack.Children.Add(BuildSummary());
        stack.Children.Add(BuildNavBar());

        _accounts.Changed += () => _ = ReloadAsync();
        _accounts.RefreshRequested += () => _ = ReloadAsync();
        _accounts.NavigateRequested += (section, sid) => GoTo(section, sid);
        _hours.Changed += () => _ = ReloadAsync();
        _hours.CreateRequested += () => _ = _accounts.CreateAsync();
        stack.Children.Add(_accounts);
        stack.Children.Add(_hours);
        stack.Children.Add(_restrictions);

        stack.Children.Add(SectionHeader(L("Contrôle parental Microsoft"), out _familyHeading));
        stack.Children.Add(BuildFamilyCard());

        stack.Children.Add(SectionHeader(L("Ouverture de session et verrouillage"), out _signInHeading));
        _lockoutApply = MakeButton(L("Appliquer"), GlyphAdmin, "Pp.Button", async (_, _) => await ApplyLockoutAsync());
        stack.Children.Add(BuildLockoutCard());
        stack.Children.Add(BuildSecurityLink());
        stack.Children.Add(_tweaksHost);

        Loaded += OnLoaded;
    }

    // ================================================================== Cycle de vie

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_tweaksStarted)
        {
            _tweaksStarted = true;
            _ = Dispatcher.InvokeAsync(BuildTweaks, DispatcherPriority.Background);
            _ = LoadLockoutAsync();
        }
        if (_data is null || DateTime.Now - _loadedAt > StaleAfter) await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_loading) return;
        _loading = true;
        _accounts.ShowLoading();
        List<LocalAccount>? list = null;
        string? error = null;
        try
        {
            var sid = AppHost.Profile.UserSid;
            list = await Task.Run(() => LocalAccounts.Enumerate(string.IsNullOrEmpty(sid) ? null : sid));
        }
        catch (Exception ex)
        {
            Log.Error("Users", "énumération des comptes", ex);
            error = ex.Message;
        }
        _loading = false;
        _data = list;
        _loadedAt = DateTime.Now;
        _accounts.Update(list, error);
        _hours.Update(list);
        _restrictions.Update(list);
        UpdateSummary();
        HandlePending();
    }

    private void BuildTweaks()
    {
        try
        {
            _tweaks = new TweakListView(AppHost.Registry.TweaksIn(UsersModule.Category));
            _tweaksHost.Content = _tweaks;
        }
        catch (Exception ex)
        {
            Log.Error("Users", "liste des réglages", ex);
        }
        HandlePending();
    }

    // ================================================================== Navigation

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not string s || s.Length == 0) return;
        _pending = s;
        HandlePending();
    }

    private void HandlePending()
    {
        if (_pending is not { } p) return;
        switch (p)
        {
            case "action:create":
                if (_data is null) return; // attend la liste (vérification des noms existants)
                _pending = null;
                ScrollTo(_accounts.Heading);
                _ = Dispatcher.InvokeAsync(() => _accounts.CreateAsync(), DispatcherPriority.Loaded);
                return;
            case "section:accounts": _pending = null; ScrollTo(_accounts.Heading); return;
            case "section:hours": _pending = null; ScrollTo(_hours.Heading); return;
            case "section:restrictions": _pending = null; ScrollTo(_restrictions.Heading); return;
            case "section:family": _pending = null; ScrollTo(_familyHeading); return;
            case "section:signin": _pending = null; ScrollTo(_signInHeading); return;
        }
        if (!p.StartsWith("tweak:", StringComparison.Ordinal)) { _pending = null; return; }
        if (_tweaks is null) return;
        _pending = null;
        _tweaks.Highlight(p["tweak:".Length..]);
    }

    private void GoTo(string section, string sid)
    {
        if (section == "section:hours") { _hours.Select(sid); ScrollTo(_hours.Heading); }
        else if (section == "section:restrictions") { _restrictions.Select(sid); ScrollTo(_restrictions.Heading); }
    }

    private void ScrollTo(FrameworkElement target) =>
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var top = target.TransformToAncestor(_scroll).Transform(new Point(0, 0)).Y + _scroll.VerticalOffset;
                _scroll.ScrollToVerticalOffset(Math.Max(0, top - 12));
            }
            catch (InvalidOperationException) { target.BringIntoView(); }
        }, DispatcherPriority.Loaded);

    // ================================================================== Résumé et barre de navigation

    private static TextBlock Metric()
    {
        var t = Text("—", "Pp.Metric");
        return t;
    }

    private Border BuildSummary()
    {
        var grid = new Grid();
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        void Cell(int col, TextBlock metric, string label, string glyph)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icon(glyph, 16, "Pp.AccentText");
            icon.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(icon);
            row.Children.Add(metric);
            s.Children.Add(row);
            s.Children.Add(Caption(label));
            Grid.SetColumn(s, col);
            grid.Children.Add(s);
        }
        Cell(0, _metricActive, L("comptes actifs"), GlyphUser);
        Cell(1, _metricAdmins, L("administrateurs"), GlyphAdmin);
        Cell(2, _metricRestricted, L("avec horaires limités"), GlyphClock);
        _you.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_you, 3);
        grid.Children.Add(_you);
        return Card(grid, new Thickness(18, 14, 18, 14));
    }

    private void UpdateSummary()
    {
        if (_data is null)
        {
            _metricActive.Text = _metricAdmins.Text = _metricRestricted.Text = "—";
            _you.Text = "";
            return;
        }
        var visible = _data.Where(a => !a.IsSystemAccount).ToList();
        _metricActive.Text = visible.Count(a => a.Enabled).ToString("N0", Loc.Culture);
        _metricAdmins.Text = visible.Count(a => a.Enabled && a.IsAdmin).ToString("N0", Loc.Culture);
        _metricRestricted.Text = visible.Count(a => a.Enabled && a.HasLogonRestriction).ToString("N0", Loc.Culture);
        var me = _data.FirstOrDefault(a => a.IsCurrent);
        _you.Text = me is null
            ? L("Vous êtes connecté avec un compte de domaine ou Microsoft Entra : seuls les comptes locaux sont gérés ici.")
            : me.IsAdmin
                ? L("Vous utilisez « {0} », un compte administrateur. Pour l'usage quotidien, un compte standard limite les dégâts d'un logiciel malveillant.", me.Name)
                : L("Vous utilisez « {0} », un compte standard : les modifications demanderont le mot de passe d'un administrateur.", me.Name);
    }

    private WrapPanel BuildNavBar()
    {
        var bar = new WrapPanel { Margin = new Thickness(-8, 10, 0, 0) };
        void Add(string text, string glyph, Func<FrameworkElement> target)
        {
            var b = MakeButton(text, glyph, "Pp.SubtleButton", (_, _) => ScrollTo(target()));
            b.Margin = new Thickness(0, 0, 2, 0);
            bar.Children.Add(b);
        }
        Add(L("Comptes"), GlyphUser, () => _accounts.Heading);
        Add(L("Plages horaires"), GlyphClock, () => _hours.Heading);
        Add(L("Restrictions"),GlyphBlock, () => _restrictions.Heading);
        Add(L("Contrôle parental"), GlyphFamily, () => _familyHeading);
        Add(L("Connexion et verrouillage"), GlyphLock, () => _signInHeading);
        return bar;
    }

    // ================================================================== Contrôle parental Microsoft

    private Border BuildFamilyCard()
    {
        var root = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(IconTile(GlyphFamily, 40));
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        text.Children.Add(Text(L("Famille Microsoft"), "Pp.CardTitle"));
        var body = Caption(
            L("Le contrôle parental de Microsoft gère, depuis votre téléphone ou family.microsoft.com : le temps d'écran par jour, les limites par application et par jeu, le filtrage web dans Microsoft Edge, l'approbation des achats et des rapports d'activité. Le parent et l'enfant ont chacun besoin d'un compte Microsoft, et l'enfant doit se connecter à ce PC avec le sien."));
        body.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(body);
        var open = MakeButton(L("Ouvrir Famille dans les Paramètres"), GlyphOpen, "Pp.AccentButton", (_, _) => OpenUri("ms-settings:family-group"));
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 10, 0, 0);
        text.Children.Add(open);
        Grid.SetColumn(text, 1);
        head.Children.Add(text);
        root.Children.Add(head);

        root.Children.Add(Divider(new Thickness(0, 14, 0, 8)));
        root.Children.Add(Text(L("Conseils pour un PC familial"), "Pp.CardTitle"));

        void Tip(string glyph, string title, string detail, params Button[] buttons)
        {
            var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Icon(glyph, 16, "Pp.AccentText");
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(2, 2, 14, 0);
            g.Children.Add(icon);
            var s = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            s.Children.Add(Text(title));
            s.Children.Add(Caption(detail));
            Grid.SetColumn(s, 1);
            g.Children.Add(s);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var b in buttons)
            {
                b.Margin = new Thickness(4, 0, 0, 0);
                actions.Children.Add(b);
            }
            Grid.SetColumn(actions, 2);
            g.Children.Add(actions);
            root.Children.Add(g);
        }

        Tip(GlyphUser, L("Un compte standard pour chaque enfant"),
            L("Il ne peut ni installer de logiciels ni modifier Windows, et les plages horaires et restrictions ci-dessus s'y appliquent."),
            MakeButton(L("Créer un compte"), GlyphAdd, "Pp.Button", async (_, _) => await _accounts.CreateAsync()));
        var dns = PageButton(L("Réseau"), "network", null);
        Tip("", L("Un DNS familial"),
            L("Des résolveurs DNS gratuits (Cloudflare for Families, CleanBrowsing…) bloquent les sites pour adultes sur tout le PC, quel que soit le navigateur."), dns is null ? [] : [dns]);
        var guided = PageButton(L("Accès guidé"), "guided", null);
        var kiosk = PageButton(L("Kiosque"), "kiosk", null);
        Tip("", L("Accès guidé ou mode kiosque"),
            L("Pour un jeune enfant : limiter la session à une seule application, en plein écran, sans accès au reste du PC."),
            [.. new[] { guided, kiosk }.OfType<Button>()]);
        Tip(GlyphClock, L("Plages horaires sans compte Microsoft"),
            L("Les plages horaires de cette page fonctionnent avec un simple compte local, sans connexion Internet."),
            MakeButton(L("Définir"), null, "Pp.Button", (_, _) => ScrollTo(_hours.Heading)));
        return Card(root, new Thickness(18, 16, 18, 16));
    }

    private static Button? PageButton(string text, string pageId, object? parameter)
    {
        if (AppHost.Registry.GetPage(pageId) is null) return null;
        return MakeButton(text, null, "Pp.Button", (_, _) => TryNavigate(pageId, parameter));
    }

    // ================================================================== Verrouillage des comptes

    private static readonly (int Value, string Label)[] LockoutChoices =
    [
        (3, L("Après 3 essais erronés")), (5, L("Après 5 essais erronés")), (10, L("Après 10 essais (Windows 11)")),
        (15, L("Après 15 essais erronés")), (20, L("Après 20 essais erronés")), (30, L("Après 30 essais erronés")),
        (50, L("Après 50 essais erronés")), (0, L("Jamais (déconseillé)")),
    ];

    private Border BuildLockoutCard()
    {
        foreach (var (value, label) in LockoutChoices) _lockoutChoice.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        System.Windows.Automation.AutomationProperties.SetName(_lockoutChoice, L("Seuil de verrouillage"));
        _lockoutChoice.IsEnabled = false;
        _lockoutApply.IsEnabled = false;
        _lockoutChoice.SelectionChanged += (_, _) => _lockoutApply.IsEnabled = _lockoutChoice.SelectedItem is ComboBoxItem;

        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(_lockoutChoice);
        _lockoutApply.Margin = new Thickness(8, 0, 0, 0);
        controls.Children.Add(_lockoutApply);
        controls.Children.Add(_lockoutBusy);

        var s = new StackPanel();
        s.Children.Add(SettingRow(GlyphLock, L("Verrouillage après des mots de passe erronés"),
            L("Bloque temporairement un compte local après plusieurs mots de passe faux d'affilée : protège contre qui essaierait de deviner un mot de passe ou un code sur le PC."), controls,
            AppHost.Profile.IsDomainJoined ? L("PC joint à un domaine : la stratégie du domaine remplace ce réglage local.") : null));
        _lockoutState.Margin = new Thickness(32, 0, 0, 4);
        s.Children.Add(_lockoutState);
        return Card(s, new Thickness(16, 10, 16, 10));
    }

    private async Task LoadLockoutAsync()
    {
        var (lockout, pwd) = await Task.Run(() =>
        {
            try { return (NetApi.GetLockoutPolicy(), NetApi.GetPasswordPolicy()); }
            catch (Exception ex) { Log.Warn("Users", "stratégie de verrouillage : " + ex.Message); return ((NetApi.LockoutPolicy?)null, (NetApi.PasswordPolicy?)null); }
        });
        _lockoutChoice.IsEnabled = true;
        if (lockout is null)
        {
            _lockoutState.Text = L("Stratégie actuelle illisible.");
            return;
        }
        var parts = new List<string>
        {
            lockout.Threshold == 0
                ? L("Actuellement : aucun verrouillage, les essais ne sont pas limités.")
                : LP(lockout.Threshold, "Actuellement : verrouillage après {0} essai erroné, pendant {1} min.", "Actuellement : verrouillage après {0} essais erronés, pendant {1} min.", lockout.DurationMinutes),
        };
        if (pwd is not null)
            parts.Add(pwd.MinLength == 0 ? L("Aucune longueur minimale de mot de passe.") : LP(pwd.MinLength, "Mots de passe d'au moins {0} caractère.", "Mots de passe d'au moins {0} caractères."));
        parts.Add(L("Le code PIN Windows Hello a sa propre protection contre les essais répétés."));
        _lockoutState.Text = string.Join(" ", parts);
        // Retire une éventuelle entrée « (actuel) » ajoutée lors d'une lecture précédente.
        foreach (var stale in _lockoutChoice.Items.OfType<ComboBoxItem>().Where(i => !i.IsEnabled).ToList()) _lockoutChoice.Items.Remove(stale);
        var index =Array.FindIndex(LockoutChoices, c => c.Value == lockout.Threshold);
        if (index < 0)
        {
            _lockoutChoice.Items.Insert(0, new ComboBoxItem { Content = LP(lockout.Threshold, "Après {0} essai (actuel)", "Après {0} essais (actuel)"), Tag = lockout.Threshold, IsEnabled = false });
            index = 0;
        }
        _lockoutChoice.SelectedIndex = index;
        _lockoutApply.IsEnabled = false;
    }

    private async Task ApplyLockoutAsync()
    {
        if (_lockoutChoice.SelectedItem is not ComboBoxItem { Tag: int value }) return;
        if (value == 0 && !await AppHost.Dialogs.ConfirmAsync(L("Désactiver le verrouillage"),
                L("Sans verrouillage, une personne peut essayer autant de mots de passe qu'elle veut sur ce PC. Continuer ?"), L("Désactiver"), danger: true))
            return;
        _lockoutApply.IsEnabled = false;
        _lockoutChoice.IsEnabled = false;
        _lockoutBusy.Visibility = Visibility.Visible;
        try
        {
            var outcome = await UsersUi.RunAsync("users.lockout.set", new() { ["threshold"] = value.ToString(CultureInfo.InvariantCulture) });
            if (outcome.Success) await LoadLockoutAsync();
        }
        finally
        {
            _lockoutBusy.Visibility = Visibility.Collapsed;
            _lockoutChoice.IsEnabled = true;
        }
    }

    private static UIElement BuildSecurityLink()
    {
        if (AppHost.Registry.GetTweak("security.logon.cad") is null) return new FrameworkElement();
        var go = MakeButton(L("Ouvrir dans Sécurité"), null, "Pp.Button", (_, _) => TryNavigate("security", "tweak:security.logon.cad"));
        return Card(SettingRow("", L("Ctrl+Alt+Suppr avant la connexion"),
            L("Exiger la séquence sécurisée avant de saisir un mot de passe se règle dans la page Sécurité, avec les autres protections de l'ouverture de session."), go), new Thickness(16, 10, 16, 10));
    }
}
