using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Timonier.Core.Platform;
using Timonier.Core.Security;
using Timonier.Core.Settings;
using Timonier.UI.Controls;
using Timonier.UI.Services;
using static Timonier.Modules.Kiosk.KioskUi;

namespace Timonier.Modules.Kiosk;

public sealed partial class KioskPage
{
    private static readonly string[] StepTitles = ["Compte kiosque", "Application", "Options", "Appliquer"];
    private static readonly (int Minutes, string Label)[] IdleChoices =
        [(0, "Jamais"), (2, "2 minutes"), (5, "5 minutes"), (10, "10 minutes"), (15, "15 minutes"), (30, "30 minutes"), (60, "1 heure")];

    // --- État de l'assistant
    private int _step;
    private KioskAccount? _account;
    private List<KioskAccount>? _accounts;
    private string? _accountsError;
    private string _mode;
    private List<StoreApp>? _storeApps;
    private bool _storeLoading;
    private string? _storeError;
    private StoreApp? _storeApp;
    private string _storeFilter = "";
    private string? _exe;
    private string _url = "https://";
    private bool _publicBrowsing;
    private int _idle;
    private readonly HashSet<string> _restrictions = [.. KioskRestrictions.All.Where(r => r.Default).Select(r => r.Key)];
    private bool _autologonEnabled;
    private string _autologonPassword = "";
    private bool _busy;

    // --- Éléments de l'assistant
    private readonly Grid _stepper = new();
    private readonly Border _stepHost = new();
    private Button _back = null!;
    private Button _next = null!;
    private readonly TextBlock _hint = Caption("");

    private Border BuildWizard()
    {
        var stack = new StackPanel();
        _stepper.Margin = new Thickness(0, 4, 0, 4);
        stack.Children.Add(_stepper);
        stack.Children.Add(Divider(new Thickness(-16, 12, -16, 16)));
        stack.Children.Add(_stepHost);
        stack.Children.Add(Divider(new Thickness(-16, 16, -16, 12)));

        var footer = new DockPanel();
        var back = Button("Précédent", "", "Pp.Button", (_, _) => ShowStep(_step - 1));
        DockPanel.SetDock(back, Dock.Left);
        footer.Children.Add(back);
        var next = Button("Suivant", "", "Pp.AccentButton", (_, _) => ShowStep(_step + 1));
        DockPanel.SetDock(next, Dock.Right);
        footer.Children.Add(next);
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.HorizontalAlignment = HorizontalAlignment.Right;
        _hint.TextAlignment = TextAlignment.Right;
        _hint.Margin = new Thickness(16, 0, 12, 0);
        footer.Children.Add(_hint);
        stack.Children.Add(footer);
        _back = back;
        _next = next;
        return PageScaffold.Card(stack);
    }

    // ================================================================== Navigation entre étapes

    private bool StepValid(int index) => index switch
    {
        0 => _account is not null,
        1 => AppProblem() is null,
        2 => true,
        _ => false,
    };

    private string? AppProblem() => _mode switch
    {
        KioskModes.Store when !_profile.SupportsAssignedAccess => "L'accès attribué n'est pas disponible sur cette édition.",
        KioskModes.Store => _storeApp is null ? "Choisissez une application dans la liste." : null,
        KioskModes.Edge when _edgePath is null => "Microsoft Edge n'est pas installé.",
        KioskModes.Edge => KioskRules.TryUrl(_url, out var e) ? null : e,
        KioskModes.Win32 when _exe is null => "Choisissez le programme à lancer.",
        KioskModes.Win32 => File.Exists(_exe) ? KioskRules.ExeLocationProblem(_exe!, _account?.ProfilePath) : "Le programme choisi est introuvable.",
        _ => "Choisissez un type d'application.",
    };

    private bool CanReach(int index)
    {
        for (var i = 0; i < index; i++) if (!StepValid(i)) return false;
        return true;
    }

    private void ShowStep(int index)
    {
        index = Math.Clamp(index, 0, 3);
        if (!CanReach(index)) return;
        _step = index;
        _stepHost.Child = index switch
        {
            0 => BuildAccountStep(),
            1 => BuildAppStep(),
            2 => BuildOptionsStep(),
            _ => BuildApplyStep(),
        };
        UpdateChrome();
    }

    /// <summary>Met à jour l'indicateur d'étapes et les boutons Précédent/Suivant.</summary>
    private void UpdateChrome()
    {
        RenderStepper();
        _back.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        _back.IsEnabled = !_busy;
        _next.Visibility = _step == 3 ? Visibility.Collapsed : Visibility.Visible;
        var valid = StepValid(_step);
        _next.IsEnabled = valid && !_busy;
        _hint.Text = _step switch
        {
            0 when !valid => "Choisissez ou créez un compte standard.",
            1 when !valid => AppProblem() ?? "",
            _ => "",
        };
    }

    private void RenderStepper()
    {
        _stepper.Children.Clear();
        _stepper.ColumnDefinitions.Clear();
        for (var i = 0; i < StepTitles.Length; i++)
        {
            _stepper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (i < StepTitles.Length - 1) _stepper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 16 });
        }
        for (var i = 0; i < StepTitles.Length; i++)
        {
            var done = i < _step && StepValid(i);
            var current = i == _step;
            var reachable = CanReach(i) && !_busy;

            var number = new TextBlock
            {
                Text = done ? "" : (i + 1).ToString(CultureInfo.InvariantCulture),
                FontSize = done ? 12 : 13, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            if (done) number.Styled("Pp.Icon").FontSize = 12;
            number.Brushed(TextBlock.ForegroundProperty, done || current ? "Pp.TextOnAccent" : "Pp.TextSecondary");
            var circle = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = number, BorderThickness = new Thickness(done || current ? 0 : 1.5) };
            circle.Brushed(Border.BackgroundProperty, done || current ? "Pp.Accent" : "Pp.ControlFill");
            circle.Brushed(Border.BorderBrushProperty, "Pp.ControlStroke");

            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var title = Text(StepTitles[i], "Pp.Body", wrap: false);
            title.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
            if (!current && !done) title.Brushed(TextBlock.ForegroundProperty, "Pp.TextSecondary");
            labels.Children.Add(title);
            var sub = Caption(StepSummary(i), tertiary: true);
            sub.TextWrapping = TextWrapping.NoWrap;
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sub.MaxWidth = 150;
            labels.Children.Add(sub);

            var item = Horizontal(circle, labels);
            var button = new Button
            {
                Content = item, Padding = new Thickness(6, 4, 10, 4), HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = reachable || current ? null : "Terminez d'abord les étapes précédentes.",
            }.Styled("Pp.SubtleButton");
            // Pas d'IsEnabled=false : l'état désactivé grise aussi l'étape en cours. Le clic n'est simplement pas branché.
            button.Opacity = reachable || current ? 1 : 0.55;
            System.Windows.Automation.AutomationProperties.SetName(button, $"Étape {i + 1} : {StepTitles[i]}");
            var target = i;
            if (reachable && !current) button.Click += (_, _) => ShowStep(target);
            else button.Cursor = System.Windows.Input.Cursors.Arrow;
            Grid.SetColumn(button, i * 2);
            _stepper.Children.Add(button);

            if (i < StepTitles.Length - 1)
            {
                var line = new Border { Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
                line.Brushed(Border.BackgroundProperty, i < _step ? "Pp.Accent" : "Pp.Divider");
                Grid.SetColumn(line, i * 2 + 1);
                _stepper.Children.Add(line);
            }
        }
    }

    private string StepSummary(int i) => i switch
    {
        0 => _account?.Name ?? "À choisir",
        1 => _mode switch
        {
            KioskModes.Store => _storeApp?.Name ?? "Application du Store",
            KioskModes.Edge => "Microsoft Edge",
            _ => _exe is null ? "Programme classique" : Path.GetFileName(_exe),
        },
        2 => $"{_restrictions.Count} restriction{(_restrictions.Count > 1 ? "s" : "")}{(_autologonEnabled ? " · auto" : "")}",
        _ => "Vérifier et lancer",
    };

    private static StackPanel StepIntro(string title, string text)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        s.Children.Add(Text(title, "Pp.CardTitle"));
        var c = Caption(text);
        c.Margin = new Thickness(0, 2, 0, 0);
        s.Children.Add(c);
        return s;
    }

    // ================================================================== Étape 1 : compte

    private readonly StackPanel _accountsList = new();
    private Border? _createForm;
    private Button? _createButton;

    private async Task LoadAccountsAsync(string? select = null)
    {
        _accounts = null;
        _accountsError = null;
        RenderAccounts();
        try
        {
            var current = _profile.UserSid;
            _accounts = await Task.Run(() => KioskAccounts.List(current));
            if (select is not null) _account = _accounts.FirstOrDefault(a => string.Equals(a.Name, select, StringComparison.OrdinalIgnoreCase) && IsEligible(a)) ?? _account;
            else if (_account is not null) _account = _accounts.FirstOrDefault(a => a.Sid == _account.Sid && IsEligible(a));
        }
        catch (Exception ex)
        {
            Log.Error("Kiosk", "liste des comptes", ex);
            _accountsError = ex.Message;
        }
        RenderAccounts();
        if (_step == 0) UpdateChrome();
    }

    private static bool IsEligible(KioskAccount a) => !a.IsAdmin && a.Enabled && !a.IsCurrent;

    private StackPanel BuildAccountStep()
    {
        var stack = new StackPanel();
        stack.Children.Add(StepIntro("Quel compte utilisera la borne ?",
            "Un compte local standard réservé à la borne. Vous gardez votre compte administrateur pour la gérer ; les administrateurs et votre propre compte ne peuvent pas être choisis."));

        var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var refresh = IconButton("", "Actualiser la liste des comptes", async (_, _) => await LoadAccountsAsync());
        DockPanel.SetDock(refresh, Dock.Right);
        tools.Children.Add(refresh);
        var create = Button("Créer un compte kiosque", "", "Pp.Button", (_, _) => ToggleCreateForm(true));
        create.HorizontalAlignment = HorizontalAlignment.Left;
        tools.Children.Add(create);
        _createButton = create;
        stack.Children.Add(tools);

        _createForm = BuildCreateForm();
        _createForm.Visibility = Visibility.Collapsed;
        stack.Children.Add(_createForm);
        if (_accountsList.Parent is Panel old) old.Children.Remove(_accountsList);
        stack.Children.Add(_accountsList);
        RenderAccounts();
        return stack;
    }

    private void RenderAccounts()
    {
        _accountsList.Children.Clear();
        if (_accountsError is not null)
        {
            _accountsList.Children.Add(State("", "Impossible de lister les comptes", _accountsError));
            return;
        }
        if (_accounts is null)
        {
            _accountsList.Children.Add(State("", "Lecture des comptes locaux…", busy: true));
            return;
        }
        var eligible = _accounts.Where(IsEligible).ToList();
        // Sans compte éligible, la création devient l'action principale de l'étape.
        _createButton?.SetResourceReference(StyleProperty, eligible.Count == 0 ? "Pp.AccentButton" : "Pp.Button");
        if (eligible.Count == 0)
            _accountsList.Children.Add(State("", "Aucun compte standard disponible", "Créez un compte kiosque dédié avec le bouton ci-dessus : c'est la méthode recommandée."));
        foreach (var a in _accounts)
        {
            var ok = IsEligible(a);
            var selected = ok && _account?.Sid == a.Sid;
            string caption;
            Border? badge = null;
            if (a.IsCurrent) { caption = "C'est le compte que vous utilisez en ce moment."; badge = Badge("Vous", "Neutral"); }
            else if (a.IsAdmin) { caption = "Un administrateur pourrait sortir du mode kiosque."; badge = Badge("Administrateur", "Warning", ""); }
            else if (!a.Enabled) { caption = "Ce compte est désactivé."; badge = Badge("Désactivé", "Neutral"); }
            else caption = a.HasProfile ? "Compte standard · déjà utilisé sur ce PC" : "Compte standard · jamais connecté (son profil sera créé automatiquement)";
            if (selected) badge = Badge("Choisi", "Accent", "");

            var row = Row(null, a.DisplayName, caption, badge);
            var tile = GlyphTile("", ok ? "Pp.AccentSubtle" : "Pp.NeutralBackground", ok ? "Pp.AccentText" : "Pp.Neutral", 32);
            tile.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(tile, Dock.Left);
            row.Children.Insert(row.Children.Count - 1, tile);
            var account = a;
            _accountsList.Children.Add(SelectableTile(row, selected, ok, ok ? () => SelectAccount(account) : null));
        }
    }

    private void SelectAccount(KioskAccount a)
    {
        _account = a;
        RenderAccounts();
        UpdateChrome();
    }

    private void ToggleCreateForm(bool show)
    {
        if (_createForm is null) return;
        _createForm.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && _createForm.Tag is TextBox first) first.Focus();
    }

    private Border BuildCreateForm()
    {
        var stack = new StackPanel();
        stack.Children.Add(Text("Nouveau compte kiosque", "Pp.CardTitle"));
        stack.Children.Add(Caption("Compte local standard (groupe Utilisateurs uniquement), sans expiration du mot de passe."));

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var name = new TextBox { MaxLength = 20, Text = "Kiosque" };
        var full = new TextBox { MaxLength = 64 };
        var pw = new PasswordBox { MaxLength = 127 };
        var pw2 = new PasswordBox { MaxLength = 127 };
        System.Windows.Automation.AutomationProperties.SetName(name, "Nom du compte");
        System.Windows.Automation.AutomationProperties.SetName(full, "Nom affiché");
        System.Windows.Automation.AutomationProperties.SetName(pw, "Mot de passe");
        System.Windows.Automation.AutomationProperties.SetName(pw2, "Confirmation du mot de passe");
        AddField(grid, 0, 0, "Nom du compte (20 caractères max.)", name);
        AddField(grid, 0, 2, "Nom affiché (facultatif)", full);
        AddField(grid, 2, 0, "Mot de passe (facultatif)", pw);
        AddField(grid, 2, 2, "Confirmation", pw2);
        stack.Children.Add(grid);

        var note = Caption("Sans mot de passe, n'importe qui peut ouvrir cette session depuis l'écran de connexion — ce qui est généralement le but d'une borne. "
                         + "Avec un mot de passe, activez l'ouverture automatique à l'étape « Options ».");
        note.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(note);

        var error = Caption("");
        error.Brushed(TextBlock.ForegroundProperty, "Pp.Danger");
        error.Visibility = Visibility.Collapsed;
        error.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(error);

        var progress = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 3, Visibility = Visibility.Collapsed, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Button? createBtn = null;
        var cancelBtn = Button("Annuler", null, "Pp.SubtleButton", (_, _) =>
        {
            pw.Clear();   // ne garde pas un mot de passe abandonné dans le formulaire
            pw2.Clear();
            ToggleCreateForm(false);
        });
        createBtn = Button("Créer le compte", "", "Pp.AccentButton", async (_, _) =>
        {
            error.Visibility = Visibility.Collapsed;
            string user;
            try { user = Validate.LocalUserName(name.Text); }
            catch (ValidationException ex) { ShowError(ex.Message); return; }
            if (_accounts?.Any(a => string.Equals(a.Name, user, StringComparison.OrdinalIgnoreCase)) == true) { ShowError($"Le compte « {user} » existe déjà : sélectionnez-le dans la liste."); return; }
            if (pw.Password != pw2.Password) { ShowError("Les deux mots de passe ne correspondent pas."); return; }

            createBtn!.IsEnabled = false;
            cancelBtn.IsEnabled = false;
            progress.Visibility = Visibility.Visible;
            try
            {
                var parameters = new Dictionary<string, string> { ["name"] = user };
                if (pw.Password.Length > 0) parameters["password"] = pw.Password;
                if (full.Text.Trim().Length > 0) parameters["fullName"] = full.Text.Trim();
                var outcome = await AppHost.Engine.RunActionAsync("kiosk.account.create", parameters);
                if (outcome.Success)
                {
                    AppHost.Toasts.ShowOutcome(outcome);
                    pw.Clear();
                    pw2.Clear();
                    ToggleCreateForm(false);
                    await LoadAccountsAsync(select: user);
                }
                else if (!outcome.Cancelled) ShowError(outcome.Message);
            }
            finally
            {
                createBtn.IsEnabled = true;
                cancelBtn.IsEnabled = true;
                progress.Visibility = Visibility.Collapsed;
            }
        });
        var buttons = Horizontal(createBtn, cancelBtn, progress);
        cancelBtn.Margin = new Thickness(8, 0, 0, 0);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(buttons);

        var border = new Border
        {
            Child = stack, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(1), Tag = name,
        };
        border.Brushed(Border.BackgroundProperty, "Pp.CardSecondary");
        border.Brushed(Border.BorderBrushProperty, "Pp.CardBorder");
        return border;

        void ShowError(string message)
        {
            error.Text = message;
            error.Visibility = Visibility.Visible;
        }
    }

    private static void AddField(Grid grid, int row, int column, string label, Control input)
    {
        while (grid.RowDefinitions.Count <= row + 1) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = FieldLabel(label);
        Grid.SetRow(l, row);
        Grid.SetColumn(l, column);
        Grid.SetRow(input, row + 1);
        Grid.SetColumn(input, column);
        grid.Children.Add(l);
        grid.Children.Add(input);
    }

    // ================================================================== Étape 2 : application

    private readonly StackPanel _storeList = new();

    private StackPanel BuildAppStep()
    {
        var stack = new StackPanel();
        stack.Children.Add(StepIntro("Quelle application doit s'afficher ?",
            $"Elle s'ouvrira seule, en plein écran, à chaque connexion du compte « {_account?.Name} »."));

        var modes = new UniformGrid { Columns = 3, Margin = new Thickness(-3, 0, -3, 8) };
        modes.Children.Add(ModeTile(KioskModes.Store, "", "Application du Store", "Accès attribué : plein écran, sans Bureau.",
            _profile.SupportsAssignedAccess ? null : "Édition non compatible"));
        modes.Children.Add(ModeTile(KioskModes.Edge, "", "Microsoft Edge", "Un site web en plein écran ou en navigation publique.",
            _edgePath is null ? "Edge absent" : null));
        modes.Children.Add(ModeTile(KioskModes.Win32, "", "Application classique", "Un programme .exe à la place du Bureau.", null));
        stack.Children.Add(modes);

        stack.Children.Add(_mode switch
        {
            KioskModes.Store => BuildStorePanel(),
            KioskModes.Edge => BuildEdgePanel(),
            _ => BuildWin32Panel(),
        });
        return stack;
    }

    private FrameworkElement ModeTile(string mode, string glyph, string title, string description, string? unavailable)
    {
        var s = new StackPanel();
        var head = new DockPanel();
        if (unavailable is not null)
        {
            var b = Badge(unavailable, "Neutral");
            b.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(b, Dock.Right);
            head.Children.Add(b);
        }
        var tile = GlyphTile(glyph, size: 32);
        tile.HorizontalAlignment = HorizontalAlignment.Left;
        head.Children.Add(tile);
        s.Children.Add(head);
        var t = Text(title, "Pp.Body");
        t.FontWeight = FontWeights.SemiBold;
        t.Margin = new Thickness(0, 10, 0, 2);
        s.Children.Add(t);
        s.Children.Add(Caption(description));
        var selected = _mode == mode;
        var border = SelectableTile(s, selected, unavailable is null, () =>
        {
            if (_mode == mode) return;
            _mode = mode;
            ShowStep(1);
        }, padY: 12);
        border.Margin = new Thickness(3, 0, 3, 0);
        return border;
    }

    // --- Store

    private StackPanel BuildStorePanel()
    {
        var stack = new StackPanel();
        var search = new TextBox { Tag = "Rechercher une application…", Text = _storeFilter, Margin = new Thickness(0, 4, 0, 10) }.Styled("Pp.SearchBox");
        System.Windows.Automation.AutomationProperties.SetName(search, "Rechercher une application");
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { timer.Stop(); _storeFilter = search.Text.Trim(); RenderStoreList(); };
        search.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        stack.Children.Add(search);

        var scroll = new ScrollViewer { MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) };
        if (_storeList.Parent is ScrollViewer oldScroll) oldScroll.Content = null;
        scroll.Content = _storeList;
        stack.Children.Add(scroll);

        var info = PageScaffold.InfoBar("L'application doit aussi être disponible pour le compte kiosque. Les applications fournies avec Windows le sont pour tous les comptes ; "
            + "pour une application installée depuis le Microsoft Store par vous seul, ouvrez une fois la session kiosque et installez-la depuis le Store. "
            + "Windows refuse aussi les comptes liés à un compte Microsoft pour cette méthode.", "");
        info.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(info);

        if (_storeApps is null && !_storeLoading) _ = LoadStoreAppsAsync();
        RenderStoreList();
        return stack;
    }

    private async Task LoadStoreAppsAsync()
    {
        _storeLoading = true;
        _storeError = null;
        RenderStoreList();
        try { _storeApps = await Task.Run(StoreApps.Load); }
        catch (Exception ex)
        {
            Log.Error("Kiosk", "applications du Store", ex);
            _storeError = ex.Message;
        }
        finally { _storeLoading = false; }
        RenderStoreList();
    }

    private void RenderStoreList()
    {
        _storeList.Children.Clear();
        if (_storeError is not null)
        {
            var retry = Button("Réessayer", "", "Pp.Button", async (_, _) => await LoadStoreAppsAsync());
            retry.HorizontalAlignment = HorizontalAlignment.Center;
            _storeList.Children.Add(State("", "Impossible de lister les applications", _storeError));
            _storeList.Children.Add(retry);
            return;
        }
        if (_storeApps is null)
        {
            _storeList.Children.Add(State("", "Recherche des applications installées…", busy: true));
            return;
        }
        var filter = _storeFilter;
        var matches = _storeApps.Where(a => filter.Length == 0 || a.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                                            || a.Aumid.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            _storeList.Children.Add(State("", "Aucune application trouvée", filter.Length > 0 ? "Essayez un autre nom." : null));
            return;
        }
        // La sélection reste visible en tête, même si elle ne correspond pas au filtre.
        if (_storeApp is not null && !matches.Contains(_storeApp)) matches.Insert(0, _storeApp);
        const int max = 60;
        foreach (var app in matches.Take(max))
        {
            var selected = _storeApp?.Aumid == app.Aumid;
            FrameworkElement icon = app.Logo is not null
                ? new Border
                {
                    Width = 32, Height = 32, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center,
                    Child = new Image { Source = app.Logo, Width = 24, Height = 24, Stretch = Stretch.Uniform },
                }.Brushed(Border.BackgroundProperty, "Pp.ControlFill")
                : GlyphTile("", size: 32);
            icon.Margin = new Thickness(0, 0, 12, 0);
            var row = Row(null, app.Name, app.Aumid, selected ? Badge("Choisie", "Accent", "") : null);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Insert(row.Children.Count - 1, icon);
            var a = app;
            var tile = SelectableTile(row, selected, true, () =>
            {
                _storeApp = a;
                RenderStoreList();
                UpdateChrome();
            }, padX: 12, padY: 8);
            _storeList.Children.Add(tile);
        }
        if (matches.Count > max)
            _storeList.Children.Add(Caption($"{matches.Count - max} autres applications : affinez la recherche.", tertiary: true));
    }

    // --- Edge

    private StackPanel BuildEdgePanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(FieldLabel("Adresse du site à afficher"));
        var url = new TextBox { Text = _url, MaxLength = 2048 };
        System.Windows.Automation.AutomationProperties.SetName(url, "Adresse du site");
        var urlError = Caption("");
        urlError.Brushed(TextBlock.ForegroundProperty, "Pp.Danger");
        urlError.Margin = new Thickness(0, 4, 0, 0);
        void CheckUrl()
        {
            var ok = KioskRules.TryUrl(_url, out var e);
            urlError.Text = ok || _url is "https://" or "http://" or "" ? "" : e ?? "";
            urlError.Visibility = urlError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        url.TextChanged += (_, _) => { _url = url.Text.Trim(); CheckUrl(); UpdateChrome(); };
        CheckUrl();
        stack.Children.Add(url);
        stack.Children.Add(urlError);

        stack.Children.Add(FieldLabel("Type de borne"));
        var kinds = new UniformGrid { Columns = 2, Margin = new Thickness(-3, 0, -3, 0) };
        kinds.Children.Add(EdgeKindTile(false, "", "Affichage public",
            "Une seule page en plein écran, sans barre d'adresse ni onglets : affichage dynamique ou borne interactive."));
        kinds.Children.Add(EdgeKindTile(true, "", "Navigation publique",
            "Navigation InPrivate limitée, avec barre d'adresse et onglets ; historique et cookies effacés à la fermeture."));
        stack.Children.Add(kinds);

        stack.Children.Add(FieldLabel("Retour à la page d'accueil après inactivité"));
        var idle = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (m, label) in IdleChoices) idle.Items.Add(new ComboBoxItem { Content = label, Tag = m });
        idle.SelectedIndex = Math.Max(0, Array.FindIndex(IdleChoices, c => c.Minutes == _idle));
        idle.SelectionChanged += (_, _) => { if (idle.SelectedItem is ComboBoxItem { Tag: int m }) _idle = m; };
        System.Windows.Automation.AutomationProperties.SetName(idle, "Délai d'inactivité");
        stack.Children.Add(idle);
        var idleNote = Caption("Sans interaction pendant ce délai, Edge revient à l'adresse de départ (et efface la session en navigation publique).");
        idleNote.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(idleNote);

        var how = Caption("Timonier lance Edge avec ses options officielles de mode kiosque (--kiosk, --edge-kiosk-type) à la place du Bureau de ce compte.");
        how.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(how);

        if (_profile.SupportsAssignedAccess) stack.Children.Add(BuildWindowsWizardGuide());
        return stack;
    }

    private FrameworkElement EdgeKindTile(bool publicBrowsing, string glyph, string title, string description)
    {
        var row = Row(glyph, title, description);
        var tile = SelectableTile(row, _publicBrowsing == publicBrowsing, true, () =>
        {
            if (_publicBrowsing == publicBrowsing) return;
            _publicBrowsing = publicBrowsing;
            ShowStep(1);
        });
        tile.Margin = new Thickness(3, 0, 3, 0);
        return tile;
    }

    private Border BuildWindowsWizardGuide()
    {
        var s = new StackPanel();
        s.Children.Add(Text("Vous préférez l'assistant kiosque de Windows ?", "Pp.CardTitle"));
        var intro = Caption("Il configure Edge par l'accès attribué et crée lui-même le compte kiosque. Dans ce cas, n'appliquez pas la configuration de Timonier :");
        intro.Margin = new Thickness(0, 2, 0, 6);
        s.Children.Add(intro);
        string[] steps =
        [
            "Ouvrez Paramètres › Comptes › Autres utilisateurs, puis dans « Configurer un kiosque », cliquez sur « Commencer ».",
            "Donnez un nom au compte kiosque : Windows le crée pour vous.",
            "Choisissez Microsoft Edge dans la liste des applications.",
            "Choisissez l'usage « panneau d'affichage numérique ou interactif » (affichage public) ou « navigateur public », puis collez l'adresse du site.",
            "Réglez le délai de redémarrage après inactivité et fermez les Paramètres. Déconnectez-vous pour tester la borne.",
        ];
        for (var i = 0; i < steps.Length; i++) s.Children.Add(NumberedStep(i + 1, steps[i]));
        var open = Button("Ouvrir l'assistant de Windows", "", "Pp.Button", (_, _) => OpenAssignedAccessSettings());
        var copy = Button("Copier l'adresse", "", "Pp.SubtleButton", (_, _) =>
        {
            if (!KioskRules.TryUrl(_url, out var e)) { AppHost.Toasts.Show(e ?? "Adresse invalide.", ToastKind.Warning); return; }
            try { Clipboard.SetText(_url); AppHost.Toasts.Show("Adresse copiée dans le Presse-papiers.", ToastKind.Success); }
            catch (Exception ex) { AppHost.Toasts.Show("Copie impossible : " + ex.Message, ToastKind.Error); }
        });
        copy.Margin = new Thickness(8, 0, 0, 0);
        var buttons = Horizontal(open, copy);
        buttons.Margin = new Thickness(0, 10, 0, 0);
        s.Children.Add(buttons);
        var b = new Border
        {
            Child = s, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 16, 0, 0), BorderThickness = new Thickness(1),
        };
        b.Brushed(Border.BackgroundProperty, "Pp.CardSecondary");
        b.Brushed(Border.BorderBrushProperty, "Pp.CardBorder");
        return b;
    }

    // --- Programme classique

    private StackPanel BuildWin32Panel()
    {
        var stack = new StackPanel();
        var pick = Button(_exe is null ? "Choisir un programme…" : "Choisir un autre programme…", "", _exe is null ? "Pp.AccentButton" : "Pp.Button", (_, _) => PickExe());
        pick.HorizontalAlignment = HorizontalAlignment.Left;
        pick.Margin = new Thickness(0, 4, 0, 10);
        stack.Children.Add(pick);

        if (_exe is not null)
        {
            string title = Path.GetFileName(_exe);
            string? version = null;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(_exe);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) title = info.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(info.CompanyName)) version = info.CompanyName.Trim();
            }
            catch { /* informations de version absentes */ }
            var row = Row("", title, _exe + (version is null ? "" : " · " + version));
            var box = new Border { Child = row, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
            box.Brushed(Border.BackgroundProperty, "Pp.CardSecondary");
            box.Brushed(Border.BorderBrushProperty, "Pp.CardBorder");
            stack.Children.Add(box);
            if (AppProblem() is { } problem) stack.Children.Add(PageScaffold.InfoBar(problem, "", "Pp.InfoBar.Warning"));
        }

        stack.Children.Add(Bullet("Le programme remplace le Bureau, la barre des tâches et le menu Démarrer pour ce compte uniquement ; les autres comptes ne sont pas touchés."));
        stack.Children.Add(Bullet("C'est une alternative légère à Shell Launcher (réservé aux éditions Entreprise et Éducation) : si le programme se ferme, il n'est pas relancé et l'écran reste noir."));
        stack.Children.Add(Bullet("Pour quitter la session : Ctrl+Alt+Suppr, puis « Se déconnecter »."));
        stack.Children.Add(Bullet("Choisissez un programme installé pour tous les utilisateurs (Program Files) : le compte kiosque n'a pas accès à vos dossiers personnels."));
        return stack;
    }

    private void PickExe()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Programme à lancer sur la borne",
            Filter = "Programmes (*.exe)|*.exe",
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            _exe = KioskRules.ExeProblemCheck(Validate.ExistingLocalFile(dialog.FileName, ".exe"));
        }
        catch (ValidationException ex)
        {
            AppHost.Toasts.Show(ex.Message, ToastKind.Warning);
            return;
        }
        ShowStep(1);
    }

    // ================================================================== Étape 3 : options

    private StackPanel BuildOptionsStep()
    {
        var stack = new StackPanel();
        stack.Children.Add(StepIntro("Verrouiller le compte kiosque",
            $"Stratégies appliquées au seul compte « {_account?.Name} ». Timonier mémorise les valeurs d'origine pour les restaurer au retrait."));
        if (_mode == KioskModes.Store)
        {
            var note = PageScaffold.InfoBar("Avec l'accès attribué, Windows bloque déjà le Bureau et les autres applications : ces restrictions sont une protection supplémentaire.", "");
            note.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(note);
        }

        var first = true;
        foreach (var r in KioskRestrictions.All)
        {
            if (!first) stack.Children.Add(Divider(new Thickness(0, 8, 0, 8)));
            first = false;
            var toggle = new CheckBox { IsChecked = _restrictions.Contains(r.Key) }.Styled("Pp.ToggleSwitch");
            System.Windows.Automation.AutomationProperties.SetName(toggle, r.Title);
            var key = r.Key;
            toggle.Checked += (_, _) => { _restrictions.Add(key); RenderStepper(); };
            toggle.Unchecked += (_, _) => { _restrictions.Remove(key); RenderStepper(); };
            stack.Children.Add(Row(null, r.Title, r.Description, toggle));
        }

        stack.Children.Add(Divider(new Thickness(-16, 16, -16, 16)));
        stack.Children.Add(Text("Ouverture de session automatique", "Pp.CardTitle"));
        var autoToggle = new CheckBox { IsChecked = _autologonEnabled }.Styled("Pp.ToggleSwitch");
        System.Windows.Automation.AutomationProperties.SetName(autoToggle, "Ouverture de session automatique");
        var autoRow = Row(null, $"Ouvrir automatiquement la session « {_account?.Name} » au démarrage",
            "La borne est prête sans intervention après une coupure de courant ou une mise à jour.", autoToggle);
        autoRow.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(autoRow);

        var details = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = _autologonEnabled ? Visibility.Visible : Visibility.Collapsed };
        details.Children.Add(FieldLabel($"Mot de passe du compte « {_account?.Name} » (laissez vide s'il n'en a pas)"));
        var pw = new PasswordBox { MaxLength = 127, Width = 320, HorizontalAlignment = HorizontalAlignment.Left, Password = _autologonPassword };
        System.Windows.Automation.AutomationProperties.SetName(pw, "Mot de passe du compte kiosque");
        pw.PasswordChanged += (_, _) => _autologonPassword = pw.Password;
        details.Children.Add(pw);
        var risk = PageScaffold.InfoBar("Risque physique : toute personne qui allume ce PC arrive directement sur la borne, et si elle parvient à en sortir, elle dispose d'une session ouverte. "
            + "Réservez cette option à un PC placé dans un lieu surveillé. Le mot de passe est conservé dans un secret protégé du système (LSA), jamais en clair dans le registre.",
            "", "Pp.InfoBar.Warning");
        risk.Margin = new Thickness(0, 10, 0, 0);
        details.Children.Add(risk);
        stack.Children.Add(details);
        autoToggle.Checked += (_, _) => { _autologonEnabled = true; details.Visibility = Visibility.Visible; RenderStepper(); };
        autoToggle.Unchecked += (_, _) => { _autologonEnabled = false; details.Visibility = Visibility.Collapsed; RenderStepper(); };
        return stack;
    }

    // ================================================================== Étape 4 : appliquer

    private StackPanel? _applyResult;

    private StackPanel BuildApplyStep()
    {
        var stack = new StackPanel();
        stack.Children.Add(StepIntro("Vérifiez puis appliquez",
            "Windows vous demandera une confirmation administrateur. Rien ne change pour votre propre compte."));

        stack.Children.Add(PageScaffold.KeyValue("Compte kiosque", _account is null ? "—" : _account.DisplayName + (_account.HasProfile ? "" : " (profil créé automatiquement)")));
        stack.Children.Add(PageScaffold.KeyValue("Mode", KioskModes.Label(_mode)));
        stack.Children.Add(PageScaffold.KeyValue("Application", _mode switch
        {
            KioskModes.Store => $"{_storeApp?.Name} ({_storeApp?.Aumid})",
            KioskModes.Edge => $"{_url} · {(_publicBrowsing ? "navigation publique" : "affichage public plein écran")}"
                               + (_idle > 0 ? $" · retour à l'accueil après {IdleChoices.First(c => c.Minutes == _idle).Label}" : ""),
            _ => _exe ?? "—",
        }));
        var names = KioskRestrictions.All.Where(r => _restrictions.Contains(r.Key)).Select(r => r.Title).ToList();
        stack.Children.Add(PageScaffold.KeyValue("Restrictions", names.Count == 0 ? "Aucune" : string.Join(" · ", names)));
        stack.Children.Add(PageScaffold.KeyValue("Ouverture automatique", _autologonEnabled ? "Oui (mot de passe protégé par LSA)" : "Non"));

        var test = PageScaffold.InfoBar("Avant de laisser la borne en libre-service : déconnectez-vous, ouvrez la session kiosque pour vérifier, puis revenez avec Ctrl+Alt+Suppr › « Se déconnecter ». "
            + "Gardez un compte administrateur dont vous connaissez le mot de passe.", "", "Pp.InfoBar.Warning");
        test.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(test);
        if (_state is { IsConfigured: true } && _account is not null && _state.Sid != _account.Sid)
            stack.Children.Add(PageScaffold.InfoBar($"Une borne est déjà configurée pour « {_state.User} » : désactivez-la d'abord (section « État et retrait »).", "", "Pp.InfoBar.Danger"));

        var status = Caption("");
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(12, 0, 0, 0);
        var progress = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 3, Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        var apply = Button("Appliquer la configuration", "", "Pp.AccentButton");
        apply.Click += async (_, _) => await ApplyAsync(apply, progress, status);
        var row = Horizontal(apply, progress, status);
        row.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(row);

        _applyResult = new StackPanel();
        stack.Children.Add(_applyResult);
        return stack;
    }

    private Dictionary<string, string> BuildApplyParameters()
    {
        var p = new Dictionary<string, string>
        {
            ["mode"] = _mode,
            ["user"] = _account!.Name,
            ["restrictions"] = string.Join(",", KioskRestrictions.All.Where(r => _restrictions.Contains(r.Key)).Select(r => r.Key)),
        };
        switch (_mode)
        {
            case KioskModes.Store: p["aumid"] = _storeApp!.Aumid; break;
            case KioskModes.Win32: p["exe"] = _exe!; break;
            case KioskModes.Edge:
                p["url"] = _url;
                p["edgeType"] = _publicBrowsing ? "public" : "fullscreen";
                p["idle"] = _idle.ToString(CultureInfo.InvariantCulture);
                break;
        }
        return p;
    }

    private async Task ApplyAsync(Button apply, ProgressBar progress, TextBlock status)
    {
        if (_account is null || AppProblem() is not null) return;
        _busy = true;
        apply.IsEnabled = false;
        progress.Visibility = Visibility.Visible;
        _applyResult?.Children.Clear();
        UpdateChrome();
        var reporter = new Progress<string>(s => status.Text = s);
        try
        {
            status.Text = "Configuration de la borne…";
            var outcome = await AppHost.Engine.RunActionAsync("kiosk.apply", BuildApplyParameters(), reporter);
            if (!outcome.Success)
            {
                status.Text = "";
                if (outcome.Cancelled) AppHost.Toasts.Show("Configuration annulée.", ToastKind.Info);
                else ShowApplyResult(false, outcome.Message);
                return;
            }

            string? autologonMessage = null;
            var autologonOk = false;
            if (_autologonEnabled)
            {
                status.Text = "Ouverture de session automatique…";
                var parameters = new Dictionary<string, string> { ["user"] = _account.Name };
                if (_autologonPassword.Length > 0) parameters["password"] = _autologonPassword;
                var auto = await AppHost.Engine.RunActionAsync("kiosk.autologon.set", parameters, reporter);
                autologonOk = auto.Success;
                autologonMessage = auto.Success ? null : "La borne est configurée, mais pas l'ouverture automatique : " + auto.Message;
                if (auto.Success) _autologonPassword = ""; // ne garde pas le mot de passe en mémoire plus que nécessaire
            }
            status.Text = "";

            SettingsStore.Update(st =>
            {
                st.ModuleData["kiosk.user"] = _account.Name;
                st.ModuleData["kiosk.mode"] = _mode;
                st.ModuleData["kiosk.target"] = _mode switch { KioskModes.Store => _storeApp!.Aumid, KioskModes.Edge => _url, _ => _exe! };
                st.ModuleData["kiosk.appliedAt"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                st.ModuleData["kiosk.autologon"] = autologonOk ? "1" : "0";
            });

            ShowApplyResult(true, outcome.Message, autologonMessage);
            AppHost.Toasts.Show("Mode kiosque configuré pour « " + _account.Name + " ».", ToastKind.Success);
            await RefreshStatusAsync();
        }
        finally
        {
            _busy = false;
            apply.IsEnabled = true;
            progress.Visibility = Visibility.Collapsed;
            UpdateChrome();
        }
    }

    private void ShowApplyResult(bool success, string message, string? warning = null)
    {
        if (_applyResult is null) return;
        _applyResult.Children.Clear();
        var bar = PageScaffold.InfoBar(message, success ? "" : "", success ? "Pp.InfoBar.Success" : "Pp.InfoBar.Danger");
        bar.Margin = new Thickness(0, 14, 0, 0);
        _applyResult.Children.Add(bar);
        if (warning is not null) _applyResult.Children.Add(PageScaffold.InfoBar(warning, "", "Pp.InfoBar.Warning"));
        if (!success) return;
        var signOut = Button("Se déconnecter pour tester", "", "Pp.Button", async (_, _) =>
        {
            if (!await AppHost.Dialogs.ConfirmAsync("Se déconnecter", "Enregistrez votre travail : toutes vos applications seront fermées. "
                + $"Sur l'écran de connexion, choisissez « {_account?.Name} » pour tester la borne.", "Se déconnecter")) return;
            await SystemEffects.SignOutNowAsync();
        });
        signOut.HorizontalAlignment = HorizontalAlignment.Left;
        signOut.Margin = new Thickness(0, 4, 0, 0);
        _applyResult.Children.Add(signOut);
    }
}
