using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Timonier.Core.Platform;
using Timonier.Core.Security;
using Timonier.Core.Settings;
using Timonier.UI.Controls;
using Timonier.UI.Services;
using static Timonier.Modules.GuidedAccess.GuidedUi;

namespace Timonier.Modules.GuidedAccess;

/// <summary>
/// Page « Accès guidé » : choix de l'application (fenêtres ouvertes ou lancement d'un .exe), restrictions, code de sortie,
/// limites honnêtes. La liste des fenêtres est lue hors du thread UI à chaque affichage de la page ; aucun minuteur.
/// </summary>
public sealed class GuidedPage : UserControl, INavigationAware
{
    private const string LastExeKey = "guided.lastExe";

    private readonly GuidedOptions _options = GuidedOptions.Load();
    private readonly StackPanel _list = new();
    private readonly TextBlock _listCount = Text("", "Pp.Caption", wrap: false);
    private readonly Button _refresh;
    private readonly Button _launch;
    private readonly Button _relaunchLast;
    private readonly ContentControl _startTile = new() { Focusable = false, Margin = new Thickness(0, 0, 14, 0) };
    private readonly TextBlock _startTitle = Text("", "Pp.CardTitle");
    private readonly WrapPanel _startChips = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _start;
    private readonly TextBlock _pinTitle = Text("", "Pp.CardTitle");
    private readonly TextBlock _pinDetail = Text("", "Pp.Caption");
    private readonly ContentControl _pinTile = new() { Focusable = false, Margin = new Thickness(0, 0, 14, 0) };
    private readonly Button _pinSet;
    private readonly Button _pinRemove;
    private readonly Border _pinCard;

    private List<AppWindow> _windows = [];
    private AppWindow? _selected;
    private AppWindow? _launched;
    private bool _busy;
    private bool _loading;
    private string? _lastError;

    public GuidedPage()
    {
        var stack = PageScaffold.Create(this, L("Accès guidé"),
            L("Verrouillez le PC sur une seule application jusqu'à la saisie d'un code, comme l'accès guidé de l'iPhone."),
            GuidedAccessModule.Glyph);

        _refresh = Button(L("Actualiser"), "", "Pp.SubtleButton", (_, _) => _ = RefreshAsync());
        _launch = Button(L("Lancer une application…"), "", "Pp.Button", (_, _) => _ = LaunchAsync(null));
        _relaunchLast = Button("", "", "Pp.SubtleButton", (_, _) => _ = LaunchAsync(LastExe()));
        _start = Button(L("Démarrer l'accès guidé"), "", "Pp.AccentButton", (_, _) => _ = StartAsync());
        _start.MinWidth = 200;
        _pinSet = Button(L("Définir le code"), "", "Pp.Button", (_, _) => _ = SetPinAsync());
        _pinRemove = Button(L("Supprimer"), "", "Pp.SubtleButton", (_, _) => _ = RemovePinAsync());

        stack.Children.Add(BuildHero());
        stack.Children.Add(BuildStartCard());

        stack.Children.Add(PageScaffold.Section(L("1. Application à verrouiller")));
        stack.Children.Add(BuildToolbar());
        stack.Children.Add(_list);

        stack.Children.Add(PageScaffold.Section(L("2. Restrictions")));
        stack.Children.Add(BuildOptions());

        stack.Children.Add(PageScaffold.Section(L("3. Code de sortie")));
        _pinCard = BuildPinCard();
        stack.Children.Add(_pinCard);

        stack.Children.Add(PageScaffold.Section(L("Confidentialité")));
        stack.Children.Add(PageScaffold.InfoBar(
            L("Le filtre clavier se contente d'ignorer les combinaisons bloquées : aucune frappe n'est enregistrée, conservée ni transmise. Aucun réglage de Windows n'est modifié : la barre des tâches et les raccourcis retrouvent leur état normal dès la fin de la session."),
            "", "Pp.InfoBar.Success"));

        stack.Children.Add(PageScaffold.Section(L("Limites")));
        stack.Children.Add(BuildLimits());

        UpdatePin();
        UpdateStart();
        ShowListState();

        Loaded += OnLoaded;
        Unloaded += (_, _) => GuidedSession.StateChanged -= OnSessionChanged;
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter as string == "section:pin")
            Dispatcher.InvokeAsync(() => _pinCard.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        GuidedSession.StateChanged -= OnSessionChanged;
        GuidedSession.StateChanged += OnSessionChanged;
        try
        {
            if (TaskbarGuard.RecoverIfNeeded())
                AppHost.Toasts.Show(L("La barre des tâches, restée masquée après une session interrompue, a été réaffichée."), ToastKind.Info);
        }
        catch (Exception ex) { Log.Warn("GuidedAccess", "restauration de la barre des tâches : " + ex.Message); }
        UpdatePin();
        _ = RefreshAsync();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        UpdateStart();
        if (GuidedSession.Current is null && IsLoaded) _ = RefreshAsync();
    }

    // ================================================================== Construction

    private Border BuildHero()
    {
        var root = new StackPanel();
        root.Children.Add(Text(L("Prêtez votre PC en toute sérénité"), "Pp.CardTitle").Also(t => t.FontWeight = FontWeights.SemiBold));
        root.Children.Add(Text(L("Idéal pour laisser un enfant sur un jeu éducatif, faire une démonstration ou proposer une borne de consultation : seule l'application choisie reste utilisable, la touche Windows et les changements d'application sont bloqués."), "Pp.Caption")
            .Also(t => t.Margin = new Thickness(0, 4, 0, 14)));

        var steps = new UniformGrid { Columns = 3 };
        steps.Children.Add(Step("1", L("Choisissez l'application"),L("Une fenêtre déjà ouverte, ou lancez-la depuis cette page.")));
        steps.Children.Add(Step("2", L("Réglez les restrictions"), L("Raccourcis bloqués, barre des tâches masquée, limite de temps…")));
        steps.Children.Add(Step("3", L("Démarrez"), L("Timonier se retire ; l'application reste seule à l'écran.")));
        root.Children.Add(steps);

        root.Children.Add(Divider(14, 12));
        var exit = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        exit.Children.Add(Icon("", 14, "Pp.AccentText").Also(i => i.Margin = new Thickness(0, 0, 8, 0)));
        // Phrase entière traduite d'un bloc ; {0} et {1} sont remplacés par les touches dessinées.
        var sentence = L("Pour quitter : {0} × 3 en moins de 1,5 s ou {1} puis saisissez votre code.");
        var pieces = System.Text.RegularExpressions.Regex.Split(sentence, @"(\{[01]\})");
        var firstText = true;
        foreach (var piece in pieces)
        {
            if (piece == "{0}") exit.Children.Add(KeyCap(LC("key", "Échap")));
            else if (piece == "{1}")
            {
                foreach (var k in new[] { LC("key", "Ctrl"), "Alt", LC("key", "Maj"), "P" }) exit.Children.Add(KeyCap(k));
            }
            else if (piece.Trim().Length > 0)
            {
                var style = firstText ? "Pp.Body" : "Pp.Caption";
                var margin = new Thickness(firstText ? 0 : 4, 0, 8, 0);
                exit.Children.Add(Text(piece.Trim(), style, wrap: false).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = margin; }));
                firstText = false;
            }
        }
        root.Children.Add(exit);
        return PageScaffold.Card(root).Also(c => c.Padding = new Thickness(20, 16, 20, 16));
    }

    private static FrameworkElement Step(string number, string title, string detail)
    {
        var n = new TextBlock { Text = number, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        n.SetResourceReference(TextBlock.ForegroundProperty, "Pp.AccentText");
        var circle = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Child = n, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
        circle.SetResourceReference(Border.BackgroundProperty, "Pp.AccentSubtle");
        var text = new StackPanel();
        text.Children.Add(Text(title, "Pp.Body").Also(t => t.FontWeight = FontWeights.SemiBold));
        text.Children.Add(Text(detail, "Pp.Caption"));
        var dock = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        DockPanel.SetDock(circle, Dock.Left);
        dock.Children.Add(circle);
        dock.Children.Add(text);
        return dock;
    }

    private Border BuildStartCard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_startTile);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(L("Application verrouillée"), "Pp.Caption"));
        _startTitle.FontWeight = FontWeights.SemiBold;
        _startTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _startTitle.TextWrapping = TextWrapping.NoWrap;
        text.Children.Add(_startTitle);
        text.Children.Add(_startChips);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        _start.VerticalAlignment = VerticalAlignment.Center;
        _start.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(_start, 2);
        grid.Children.Add(_start);
        return PageScaffold.Card(grid).Also(c => { c.Padding = new Thickness(18, 14, 18, 14); c.Margin = new Thickness(0, 8, 0, 0); });
    }

    private FrameworkElement BuildToolbar()
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        _relaunchLast.Margin = new Thickness(0, 0, 8, 0);
        _refresh.Margin = new Thickness(0, 0, 8, 0);
        right.Children.Add(_relaunchLast);
        right.Children.Add(_refresh);
        right.Children.Add(_launch);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Text(L("Fenêtres ouvertes"), "Pp.Body", wrap: false).Also(t => t.FontWeight = FontWeights.SemiBold));
        left.Children.Add(_listCount);
        dock.Children.Add(left);
        UpdateRelaunchLast();
        return dock;
    }

    private Border BuildOptions()
    {
        var stack = new StackPanel();
        var laptop = AppHost.Profile?.IsLaptopLike == true;

        AddOption(stack, "", L("Bloquer la touche Windows et les raccourcis système"),
            L("Touche Windows (menu Démarrer, Win+D, Win+R, Win+Tab…), Alt+Tab, Alt+Échap, Ctrl+Échap, Ctrl+Maj+Échap (Gestionnaire des tâches), Alt+Espace et touches de lancement du clavier (courrier, navigateur, calculatrice)."),
            () => _options.BlockShortcuts, v => _options.BlockShortcuts = v);
        AddOption(stack, "", L("Bloquer aussi Alt+F4"),
            L("Empêche de fermer l'application au clavier. Sa croix reste utilisable à la souris : si l'application se ferme, un écran propose de la relancer ou de saisir le code."),
            () => _options.BlockAltF4, v => _options.BlockAltF4 = v, indent: true);
        AddOption(stack, "", L("Garder l'application au premier plan"),
            L("Si une autre fenêtre prend la main (clic ailleurs, notification, fenêtre surgissante), l'application est ramenée devant ; sa réduction est annulée. Les boîtes de dialogue de l'application elle-même restent autorisées."),
            () => _options.KeepForeground, v => _options.KeepForeground = v);
        AddOption(stack, "", L("Masquer la barre des tâches"),
            L("Masque temporairement la barre des tâches (et celles des écrans secondaires). Elle réapparaît à la fin de la session, même en cas d'erreur ; si Timonier était arrêté de force, elle est réaffichée à son prochain lancement."),
            () => _options.HideTaskbar, v => _options.HideTaskbar = v);
        AddOption(stack, "", L("Agrandir l'application"),
            L("La fenêtre est maximisée au démarrage et après chaque réduction. L'emplacement de la barre des tâches reste réservé par Windows : pour un vrai plein écran, utilisez celui de l'application (F11 dans un navigateur)."),
            () => _options.Maximize, v => _options.Maximize = v);
        AddOption(stack, "", L("Autoriser les touches de volume et multimédia"),
            L("Volume, muet, lecture/pause, piste suivante.") + " "
            + (laptop
                ? L("Sur ce portable, les touches de luminosité sont gérées par le matériel : elles restent toujours actives.")
                : L("Les touches de luminosité, gérées par le matériel, ne sont jamais bloquées.")),
            () => _options.AllowMediaKeys, v => _options.AllowMediaKeys = v);
        stack.Children.Add(BuildTimeLimit());
        return PageScaffold.Card(stack).Also(c => c.Padding = new Thickness(18, 6, 18, 8));
    }

    private void AddOption(Panel host, string glyph, string title, string detail, Func<bool> get, Action<bool> set, bool indent = false)
    {
        if (host.Children.Count > 0) host.Children.Add(Divider(0, 0).Also(d => d.Margin = new Thickness(indent ? 50 : 0, 0, 0, 0)));
        var box = new CheckBox { IsChecked = get(), Margin = new Thickness(16, 0, 0, 0) }.Styled("Pp.ToggleSwitch");
        System.Windows.Automation.AutomationProperties.SetName(box, title);
        box.Click += (_, _) =>
        {
            set(box.IsChecked == true);
            _options.Save();
            UpdateStart();
        };
        var dock = new DockPanel { Margin = new Thickness(indent ? 50 : 0, 12, 0, 12) };
        DockPanel.SetDock(box, Dock.Right);
        dock.Children.Add(box);
        if (!indent)
        {
            var tile = GlyphTile(glyph, 36);
            tile.Margin = new Thickness(0, 0, 14, 0);
            tile.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(tile, Dock.Left);
            dock.Children.Add(tile);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(title, "Pp.CardTitle"));
        text.Children.Add(Text(detail, "Pp.Caption").Also(t => t.Margin = new Thickness(0, 2, 0, 0)));
        dock.Children.Add(text);
        host.Children.Add(dock);
    }

    private FrameworkElement BuildTimeLimit()
    {
        var host = new StackPanel();
        host.Children.Add(Divider(0, 0));
        var combo = new ComboBox { Width = 130, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var m in GuidedOptions.TimeChoices)
            combo.Items.Add(new ComboBoxItem { Content = m < 60 ? LP(m, "{0} minute", "{0} minutes") : DurationLabel(m), Tag = m });
        System.Windows.Automation.AutomationProperties.SetName(combo, L("Durée de la limite de temps"));
        var box = new CheckBox { IsChecked = _options.TimeLimitMinutes > 0, Margin = new Thickness(16, 0, 0, 0) }.Styled("Pp.ToggleSwitch");
        System.Windows.Automation.AutomationProperties.SetName(box, L("Limite de temps"));
        var selected = _options.TimeLimitMinutes > 0 ? _options.TimeLimitMinutes : 30;
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(GuidedOptions.TimeChoices, selected));
        combo.Visibility = box.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        void Apply()
        {
            _options.TimeLimitMinutes = box.IsChecked == true && combo.SelectedItem is ComboBoxItem { Tag: int m } ? m : 0;
            combo.Visibility = box.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            _options.Save();
            UpdateStart();
        }
        box.Click += (_, _) => Apply();
        combo.SelectionChanged += (_, _) => { if (box.IsChecked == true) Apply(); };

        var dock = new DockPanel { Margin = new Thickness(0, 12, 0, 8) };
        DockPanel.SetDock(box, Dock.Right);
        dock.Children.Add(box);
        var tile = GlyphTile("", 36);
        tile.Margin = new Thickness(0, 0, 14, 0);
        tile.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(tile, Dock.Left);
        dock.Children.Add(tile);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(L("Limite de temps"), "Pp.CardTitle"));
        text.Children.Add(Text(LP(GuidedOverlay.ExtendMinutes,
                "À l'échéance, un écran recouvre l'application : le code permet de quitter ou de prolonger de {0} minute.",
                "À l'échéance, un écran recouvre l'application : le code permet de quitter ou de prolonger de {0} minutes."), "Pp.Caption").Also(t => t.Margin = new Thickness(0, 2, 0, 0)));
        text.Children.Add(combo);
        dock.Children.Add(text);
        host.Children.Add(dock);
        return host;
    }

    private Border BuildPinCard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_pinTile);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _pinTitle.FontWeight = FontWeights.SemiBold;
        text.Children.Add(_pinTitle);
        text.Children.Add(_pinDetail.Also(t => t.Margin = new Thickness(0, 2, 0, 0)));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        _pinRemove.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(_pinRemove);
        buttons.Children.Add(_pinSet);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return PageScaffold.Card(grid).Also(c => c.Padding = new Thickness(18, 14, 18, 14));
    }

    private FrameworkElement BuildLimits()
    {
        var stack = new StackPanel();
        var items = new List<string>
        {
            L("Ctrl+Alt+Suppr et Win+L ne peuvent pas être bloqués (séquence sécurisée de Windows) : verrouiller le PC, fermer la session ou ouvrir le Gestionnaire des tâches restent possibles depuis cet écran."),
            L("Un administrateur peut mettre fin à l'accès guidé en arrêtant Timonier depuis le Gestionnaire des tâches."),
            L("Les demandes d'autorisation (UAC) s'affichent sur le bureau sécurisé, au-dessus de tout, et les notifications système peuvent apparaître."),
            L("Si l'application choisie s'exécute en tant qu'administrateur, le filtre clavier ne s'applique pas à elle (isolation des privilèges de Windows)."),
            L("Les fonctions de l'application restent disponibles (liens, boîte « Ouvrir un fichier »…) : choisissez une application adaptée à la personne."),
        };
        if (AppHost.Profile?.HasTouch == true)
            items.Add(L("Écran tactile : les balayages depuis les bords de l'écran (notifications, widgets) ne sont pas bloqués."));
        var first = true;
        foreach (var item in items)
        {
            var dock = new DockPanel { Margin = new Thickness(0, first ? 0 : 8, 0, 0) };
            first = false;
            var bullet = Icon("", 13, "Pp.TextSecondary");
            bullet.VerticalAlignment = VerticalAlignment.Top;
            bullet.Margin = new Thickness(0, 2, 10, 0);
            DockPanel.SetDock(bullet, Dock.Left);
            dock.Children.Add(bullet);
            dock.Children.Add(Text(item, "Pp.Body"));
            stack.Children.Add(dock);
        }

        stack.Children.Add(Divider(14, 12));
        var kiosk = new DockPanel();
        var open = Button(L("Ouvrir le mode kiosque"), "", "Pp.Button", (_, _) => OpenKiosk());
        open.VerticalAlignment = VerticalAlignment.Center;
        open.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(open, Dock.Right);
        kiosk.Children.Add(open);
        var kt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        kt.Children.Add(Text(L("Besoin d'un verrouillage plus solide ?"), "Pp.CardTitle").Also(t => t.FontWeight = FontWeights.SemiBold));
        kt.Children.Add(Text(L("Le mode kiosque de Windows (accès affecté) réserve un compte à une seule application, y compris après un redémarrage."), "Pp.Caption"));
        kiosk.Children.Add(kt);
        stack.Children.Add(kiosk);
        return PageScaffold.Card(stack).Also(c => c.Padding = new Thickness(18, 14, 18, 14));
    }

    private static void OpenKiosk()
    {
        if (AppHost.Registry.GetPage("kiosk") is null)
        {
            AppHost.Toasts.Show(L("La page Mode kiosque n'est pas disponible dans cette version."), ToastKind.Warning);
            return;
        }
        AppHost.Navigator.Navigate("kiosk");
    }

    // ================================================================== Liste des fenêtres

    private async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        _lastError = null;
        _refresh.IsEnabled = false;
        if (_windows.Count == 0) ShowListState();
        try
        {
            var launched = _launched;
            var list = await Task.Run(WindowCatalog.Enumerate);
            // Une application lancée ici garde son chemin (permet « Relancer » pendant la session).
            if (launched is not null)
            {
                var index = list.FindIndex(w => w.Handle == launched.Handle);
                if (index >= 0) list[index] = list[index] with { LaunchedPath = launched.LaunchedPath };
                else _launched = null;
            }
            _windows = [.. list.OrderByDescending(w => w.LaunchedPath is not null).ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)];
            if (_selected is not null)
                _selected = _windows.FirstOrDefault(w => w.Handle == _selected.Handle);
        }
        catch (Exception ex)
        {
            Log.Error("GuidedAccess", "énumération des fenêtres", ex);
            _lastError = ex.Message;
        }
        finally
        {
            _loading = false;
            _refresh.IsEnabled = !_busy;
        }
        ShowListState();
        UpdateStart();
        UpdateRelaunchLast();
    }

    private void ShowListState()
    {
        _list.Children.Clear();
        if (_lastError is not null)
        {
            _list.Children.Add(StateBlock("", L("Impossible de lister les fenêtres"), _lastError));
            _listCount.Text = "";
            return;
        }
        if (_loading && _windows.Count == 0)
        {
            _list.Children.Add(StateBlock("", L("Recherche des fenêtres ouvertes…"), null, busy: true));
            _listCount.Text = "";
            return;
        }
        if (_windows.Count == 0)
        {
            _list.Children.Add(StateBlock("", L("Aucune fenêtre d'application ouverte"),
                L("Ouvrez l'application voulue puis cliquez sur « Actualiser », ou utilisez « Lancer une application… ».")));
            _listCount.Text = "";
            return;
        }
        _listCount.Text = LP(_windows.Count, "{0} fenêtre · cliquez pour choisir", "{0} fenêtres · cliquez pour choisir");
        foreach (var w in _windows) _list.Children.Add(WindowRow(w));
    }

    private Border WindowRow(AppWindow w)
    {
        var selected = _selected?.Handle == w.Handle;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = w.Icon is { } img ? ImageTile(img, 38) : GlyphTile("", 38, "Pp.CardSecondary", "Pp.TextSecondary");
        tile.Margin = new Thickness(0, 0, 14, 0);
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text(string.IsNullOrWhiteSpace(w.Title) ? w.ProcessName : w.Title, "Pp.CardTitle", wrap: false);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.ToolTip = w.Title;
        text.Children.Add(title);
        text.Children.Add(Text(w.Subtitle, "Pp.Caption", wrap: false).Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (w.LaunchedPath is not null) right.Children.Add(Badge(L("Lancée par Timonier"), "Info", ""));
        if (selected) right.Children.Add(Badge(L("Choisie"),"Accent", ""));
        else right.Children.Add(Icon("", 12, "Pp.TextTertiary").Also(i => i.Margin = new Thickness(4, 0, 4, 0)));
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var row = new Border { Child = grid, Cursor = Cursors.Hand, Focusable = true, Padding = new Thickness(14, 10, 14, 10) }.Styled("Pp.CardInteractive");
        row.Padding = new Thickness(14, 10, 14, 10);
        if (selected)
        {
            row.SetResourceReference(Border.BorderBrushProperty, "Pp.Accent");
            row.BorderThickness = new Thickness(1.5);
        }
        var rowName = w.Title.Length > 0 ? w.Title : w.ProcessName;
        System.Windows.Automation.AutomationProperties.SetName(row, selected ? L("{0} (choisie)", rowName) : rowName);
        row.MouseLeftButtonUp += (_, _) => Select(w);
        row.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { e.Handled = true; Select(w); }
        };
        return row;
    }

    private void Select(AppWindow w)
    {
        if (_busy) return;
        _selected = w;
        ShowListState();
        UpdateStart();
    }

    // ================================================================== Lancement d'une application

    private static string? LastExe()
    {
        var path = SettingsStore.Current.ModuleData.GetValueOrDefault(LastExeKey);
        return path is not null && File.Exists(path) ? path : null;
    }

    private void UpdateRelaunchLast()
    {
        if (LastExe() is { } path)
        {
            SetContent(_relaunchLast, L("Relancer {0}", Path.GetFileNameWithoutExtension(path)), "");
            _relaunchLast.ToolTip = path;
            _relaunchLast.Visibility = Visibility.Visible;
        }
        else _relaunchLast.Visibility = Visibility.Collapsed;
    }

    private async Task LaunchAsync(string? path)
    {
        if (_busy) return;
        if (path is null)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = L("Choisir l'application à verrouiller"),
                Filter = L("Applications (*.exe)") + "|*.exe",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            path = dialog.FileName;
        }

        string exe;
        try { exe = Validate.ExistingLocalFile(path, ".exe"); }
        catch (ValidationException ex)
        {
            AppHost.Toasts.Show(ex.Message, ToastKind.Error);
            return;
        }

        SetBusy(true);
        try
        {
            var window = await AppLauncher.LaunchAsync(exe);
            _launched = window;
            _selected = window;
            SettingsStore.Current.ModuleData[LastExeKey] = exe;
            SettingsStore.Save();
            AppHost.Toasts.Show(L("« {0} » est lancée et choisie pour l'accès guidé.", Path.GetFileNameWithoutExtension(exe)), ToastKind.Success);
            // Revenir sur Timonier pour démarrer la session.
            Window.GetWindow(this)?.Activate();
        }
        catch (Exception ex)
        {
            Log.Warn("GuidedAccess", "lancement : " + ex.Message);
            AppHost.Toasts.Show(ex is InvalidOperationException ? ex.Message : L("Impossible de lancer l'application : {0}", ex.Message), ToastKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
        await RefreshAsync();
    }

    // ================================================================== Démarrage

    private void UpdateStart()
    {
        _startChips.Children.Clear();
        if (_selected is { } w)
        {
            _startTile.Content = w.Icon is { } img ? ImageTile(img, 44) : GlyphTile("", 44);
            _startTitle.Text = string.IsNullOrWhiteSpace(w.Title) ? w.ProcessName : w.Title;
            _startTitle.ToolTip = w.Title;
        }
        else
        {
            _startTile.Content = GlyphTile(GuidedAccessModule.Glyph, 44, "Pp.CardSecondary", "Pp.TextTertiary");
            _startTitle.Text = L("Aucune application choisie");
            _startTitle.ToolTip = null;
        }
        _startChips.Children.Add(GuidedPin.IsSet ? Badge(L("Code défini"), "Success", "") : Badge(L("Code à définir"), "Warning", ""));
        if (_options.BlockShortcuts) _startChips.Children.Add(Badge(L("Raccourcis bloqués"), "Neutral", ""));
        if (_options.HideTaskbar) _startChips.Children.Add(Badge(L("Barre des tâches masquée"), "Neutral", ""));
        if (_options.TimeLimitMinutes > 0) _startChips.Children.Add(Badge(L("Limite : {0}", DurationLabel(_options.TimeLimitMinutes)),"Info", ""));
        foreach (FrameworkElement chip in _startChips.Children) chip.Margin = new Thickness(0, 0, 6, 4);

        _start.IsEnabled = !_busy && _selected is not null && GuidedSession.Current is null;
        _start.ToolTip = _selected is null ? L("Choisissez d'abord une application dans la liste.") : null;
        ToolTipService.SetShowOnDisabled(_start, true);
    }

    private static string DurationLabel(int minutes) =>
        minutes < 60 ? L("{0} min", minutes) : minutes % 60 == 0 ? L("{0} h", minutes / 60) : L("{0} h {1:00}", minutes / 60, minutes % 60);

    private async Task StartAsync()
    {
        if (_busy || _selected is not { } target) return;
        if (GuidedSession.Current is not null)
        {
            AppHost.Toasts.Show(L("Un accès guidé est déjà actif."), ToastKind.Warning);
            return;
        }
        if (!GuidedPin.IsSet)
        {
            await AppHost.Dialogs.AlertAsync(L("Définissez d'abord un code"),
                L("Un code de sortie est obligatoire : c'est lui qui permettra de quitter l'accès guidé."));
            if (!await SetPinAsync() || !GuidedPin.IsSet) return;
        }
        if (!GuidedNative.IsWindow(target.Handle))
        {
            AppHost.Toasts.Show(L("Cette fenêtre a été fermée entre-temps. La liste est actualisée."), ToastKind.Warning);
            _selected = null;
            await RefreshAsync();
            return;
        }

        var name = string.IsNullOrWhiteSpace(target.Title) ? target.ProcessName : target.Title;
        var details = new List<string>();
        if (_options.BlockShortcuts) details.Add(L("touche Windows et raccourcis système bloqués"));
        if (_options.KeepForeground) details.Add(L("application maintenue au premier plan"));
        if (_options.HideTaskbar) details.Add(L("barre des tâches masquée"));
        if (_options.TimeLimitMinutes > 0) details.Add(L("limite de {0}", DurationLabel(_options.TimeLimitMinutes)));
        var message = details.Count > 0
            ? L("« {0} » sera la seule application utilisable ({1}).\n\nPour quitter : appuyez 3 fois sur Échap en moins de 1,5 seconde, ou sur Ctrl+Alt+Maj+P, puis saisissez votre code.\n\nTimonier se masque pendant la session et réapparaît à la fin.", name, string.Join(", ", details))
            : L("« {0} » sera la seule application utilisable.\n\nPour quitter : appuyez 3 fois sur Échap en moins de 1,5 seconde, ou sur Ctrl+Alt+Maj+P, puis saisissez votre code.\n\nTimonier se masque pendant la session et réapparaît à la fin.", name);
        if (!await AppHost.Dialogs.ConfirmAsync(L("Démarrer l'accès guidé ?"), message, L("Démarrer"))) return;

        try
        {
            GuidedSession.Start(target, _options);
        }
        catch (Exception ex)
        {
            Log.Error("GuidedAccess", "démarrage de la session", ex);
            AppHost.Toasts.Show(L("L'accès guidé n'a pas pu démarrer : {0}", ex.Message), ToastKind.Error);
        }
    }

    // ================================================================== Code de sortie

    private void UpdatePin()
    {
        var set = GuidedPin.IsSet;
        _pinTile.Content = set ? GlyphTile("", 40, "Pp.SuccessBackground", "Pp.Success") : GlyphTile("", 40, "Pp.WarningBackground", "Pp.Warning");
        _pinTitle.Text = set ? L("Code de sortie défini") : L("Aucun code de sortie");
        _pinDetail.Text = set
            ? L("Demandé pour quitter l'accès guidé. Il est conservé uniquement sous forme hachée (PBKDF2) dans vos préférences Timonier, indépendamment du code de verrouillage de Timonier.")
            : L("Obligatoire pour démarrer : choisissez un code de {0} à {1} chiffres, facile à retenir pour vous et difficile à deviner.", GuidedPin.MinLength, GuidedPin.MaxLength);
        SetContent(_pinSet, set ? L("Modifier le code") : L("Définir le code"), "");
        _pinSet.SetResourceReference(StyleProperty, set ? "Pp.Button" : "Pp.AccentButton");
        _pinRemove.Visibility = set ? Visibility.Visible : Visibility.Collapsed;
        UpdateStart();
    }

    /// <summary>Définit ou modifie le code (le code actuel est demandé s'il existe). Renvoie true si un code a été enregistré.</summary>
    private async Task<bool> SetPinAsync()
    {
        if (_busy) return false;
        if (GuidedPin.IsSet && !await AskCurrentPinAsync(L("Modifier le code"))) return false;

        var first = await AppHost.Dialogs.PromptAsync(L("Nouveau code"),
            L("Choisissez un code de {0} à {1} chiffres. Il sera demandé pour quitter l'accès guidé.", GuidedPin.MinLength, GuidedPin.MaxLength),
            password: true, validate: GuidedPin.FormatError);
        if (first is null) return false;
        var second = await AppHost.Dialogs.PromptAsync(L("Confirmer le code"), L("Saisissez le même code une seconde fois."),
            password: true, validate: s => s == first ? null : L("Les deux codes ne correspondent pas."));
        if (second is null) return false;

        SetBusy(true);
        try
        {
            await GuidedPin.SetAsync(first);
            AppHost.Toasts.Show(L("Code de l'accès guidé enregistré."), ToastKind.Success);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("GuidedAccess", "enregistrement du code", ex);
            AppHost.Toasts.Show(L("Le code n'a pas pu être enregistré : {0}", ex.Message), ToastKind.Error);
            return false;
        }
        finally
        {
            SetBusy(false);
            UpdatePin();
        }
    }

    private async Task RemovePinAsync()
    {
        if (_busy || !GuidedPin.IsSet) return;
        if (!await AskCurrentPinAsync(L("Supprimer le code"))) return;
        GuidedPin.Clear();
        UpdatePin();
        AppHost.Toasts.Show(L("Code supprimé : un nouveau code sera demandé avant le prochain accès guidé."), ToastKind.Info);
    }

    private static async Task<bool> AskCurrentPinAsync(string title)
    {
        if (GuidedPin.LockRemaining > TimeSpan.Zero)
        {
            AppHost.Toasts.Show(L("Trop d'essais incorrects. Réessayez dans {0} s.", (int)Math.Ceiling(GuidedPin.LockRemaining.TotalSeconds)), ToastKind.Warning);
            return false;
        }
        var result = await AppHost.Dialogs.PromptAsync(title, L("Saisissez le code actuel de l'accès guidé."), password: true, validate: s =>
        {
            if (GuidedPin.LockRemaining > TimeSpan.Zero)
                return L("Trop d'essais incorrects. Réessayez dans {0} s.", (int)Math.Ceiling(GuidedPin.LockRemaining.TotalSeconds));
            return GuidedPin.Verify(s) ? null : GuidedPin.LockRemaining > TimeSpan.Zero
                ? L("Trop d'essais incorrects. Réessayez dans {0} s.", (int)Math.Ceiling(GuidedPin.LockRemaining.TotalSeconds))
                : L("Code incorrect.");
        });
        return result is not null;
    }

    // ================================================================== Divers

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _launch.IsEnabled = !busy;
        _relaunchLast.IsEnabled = !busy;
        _refresh.IsEnabled = !busy && !_loading;
        _pinSet.IsEnabled = !busy;
        _pinRemove.IsEnabled = !busy;
        UpdateStart();
    }
}

internal static class GuidedFluent
{
    /// <summary>Configure un élément en ligne (construction d'interface en code).</summary>
    public static T Also<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
