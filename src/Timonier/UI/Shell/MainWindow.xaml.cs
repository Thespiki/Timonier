using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Timonier.Core.Catalog;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Search;
using Timonier.Core.Security;
using Timonier.Core.Settings;
using Timonier.UI.Controls;
using Timonier.UI.Pages;
using Timonier.UI.Services;

namespace Timonier.UI.Shell;

public partial class MainWindow : Window, INavigator, IDialogService, IToastService
{
    private readonly Dictionary<string, FrameworkElement> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadioButton> _navButtons = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private bool _navigating;
    private int _lockFailures;
    private DateTime _lockUntil;

    public MainWindow()
    {
        InitializeComponent();
        // Arabe, hébreu : toute l'interface passe en miroir (les pages héritent de la direction de la fenêtre).
        FlowDirection = Core.Localization.Loc.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (!AppHost.Profile.IsWindows11) SetResourceReference(BackgroundProperty, "Pp.WindowBackground");

        AppHost.Navigator = this;
        AppHost.Dialogs = this;
        AppHost.Toasts = this;

        _searchDebounce.Tick += async (_, _) => { _searchDebounce.Stop(); await RunSearchAsync(); };
        AppHost.HardwareLoaded += (_, _) => UpdatePcCard();
        AppHost.Broker.StateChanged += (_, _) => Dispatcher.InvokeAsync(UpdateAdminBar);
        AppHost.Broker.BeforeElevation = ConfirmElevationAsync;
        SystemEffects.RebootPendingChanged += (_, _) => Dispatcher.InvokeAsync(() =>
            RebootBar.Visibility = SystemEffects.RebootPending ? Visibility.Visible : Visibility.Collapsed);

        PreviewKeyDown += OnWindowPreviewKeyDown;
        Closing += OnClosing;
        Loaded += OnLoaded;

        UpdatePcCard();
        BuildNavigation();
        UpdateAdminBar();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var start = AppHost.Settings.LastPageId is { } last && IsKnownPage(last) ? last
                  : IsKnownPage("dashboard") ? "dashboard" : _navButtons.Keys.FirstOrDefault();
        if (start is not null) Navigate(start);

        if (!string.IsNullOrEmpty(AppHost.Settings.AppPinHash)) Lock();
    }

    // ================================================================== Carte PC

    private void UpdatePcCard()
    {
        var p = AppHost.Profile;
        PcName.Text = p.MachineName;
        PcModel.Text = p.HardwareLoaded || p.Model.Length > 0
            ? $"{p.Manufacturer} · {p.Model}".Trim(' ', '·')
            : L("Scanning hardware…");
        PcWindows.Text = $"{p.ProductName} {p.DisplayVersion}";
    }

    private void PcCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (IsKnownPage("dashboard")) Navigate("dashboard");
    }

    // ================================================================== Navigation

    private sealed record NavEntry(string Id, string Title, string Glyph, NavSection Section, int Order, Func<FrameworkElement> Factory, string? CategoryId);

    private List<NavEntry> _entries = [];

    private void BuildNavigation()
    {
        var registry = AppHost.Registry;
        _entries =
        [
            .. registry.Pages.Select(p => new NavEntry(p.Id, p.Title, p.Glyph, p.Section, p.Order, p.Factory, p.CategoryId)),
            .. registry.Categories
                .Where(c => registry.Pages.All(p => p.CategoryId != c.Id))
                .Select((c, i) => new NavEntry("category:" + c.Id, c.Title, c.Glyph, NavSection.Settings, 500 + i, () => new CategoryPage(c), c.Id)),
        ];

        NavHost.Children.Clear();
        _navButtons.Clear();
        foreach (var section in _entries.GroupBy(e => e.Section).OrderBy(g => g.Key))
        {
            var header = section.Key switch
            {
                NavSection.Settings => L("Settings"),
                NavSection.Control => L("Control and security"),
                NavSection.Tools => L("Tools"),
                NavSection.App => "Timonier",
                _ => null,
            };
            if (header is not null) NavHost.Children.Add(new TextBlock { Text = header, Style = (Style)FindResource("Pp.NavHeader") });

            foreach (var entry in section.OrderBy(e => e.Order).ThenBy(e => e.Title, StringComparer.Create(Core.Localization.Loc.Culture, false)))
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new TextBlock { Text = entry.Glyph, Style = (Style)FindResource("Pp.Icon"), Margin = new Thickness(0, 0, 14, 0) });
                content.Children.Add(new TextBlock { Text = entry.Title, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                var button = new RadioButton { Content = content, GroupName = "nav", Style = (Style)FindResource("Pp.NavItem"), Tag = entry.Id };
                button.SetResourceReference(ForegroundProperty, "Pp.TextPrimary");
                AutomationPropertiesHelper.SetName(button, entry.Title);
                button.Checked += (_, _) => { if (!_navigating) Navigate(entry.Id); };
                _navButtons[entry.Id] = button;
                NavHost.Children.Add(button);
            }
        }
    }

    private bool IsKnownPage(string id) => _entries.Any(e => e.Id == id);

    public string? CurrentPageId { get; private set; }

    /// <summary>Contenu de la page affichée (mode capture).</summary>
    internal DependencyObject? PageHostContent => PageHost.Content as DependencyObject;

    public void Navigate(string pageId, object? parameter = null)
    {
        // Une catégorie dotée d'une page dédiée est redirigée vers celle-ci.
        if (pageId.StartsWith("category:", StringComparison.Ordinal))
        {
            var redirect = AppHost.Registry.PageIdForCategory(pageId["category:".Length..]);
            if (IsKnownPage(redirect)) pageId = redirect;
        }
        var entry = _entries.FirstOrDefault(e => e.Id == pageId);
        if (entry is null)
        {
            Show(L("Page not found: {0}", pageId), ToastKind.Warning);
            return;
        }

        if (!_pages.TryGetValue(pageId, out var page))
        {
            try { page = entry.Factory(); }
            catch (Exception ex)
            {
                Log.Error("Nav", "création de la page " + pageId, ex);
                var err = new UserControl();
                var stack = PageScaffold.Create(err, entry.Title, null, entry.Glyph);
                stack.Children.Add(PageScaffold.InfoBar(L("This page couldn't be loaded: {0}", ex.Message), "", "Pp.InfoBar.Danger"));
                page = err;
            }
            _pages[pageId] = page;
        }

        if (!ReferenceEquals(PageHost.Content, page))
        {
            (PageHost.Content as INavigationAware)?.OnNavigatedFrom();
            PageHost.Content = page;
            if (!AppHost.Settings.ReduceAnimations)
                page.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(140)));
        }
        CurrentPageId = pageId;

        _navigating = true;
        if (_navButtons.TryGetValue(pageId, out var button)) button.IsChecked = true;
        _navigating = false;

        (page as INavigationAware)?.OnNavigatedTo(parameter);
        if (AppHost.Settings.LastPageId != pageId)
        {
            AppHost.Settings.LastPageId = pageId;
            SettingsStore.Save();
        }
    }

    // ================================================================== Recherche

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsLocked) return; // aucun raccourci derrière l'écran de verrouillage
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if ((ctrl && e.Key is Key.K or Key.F) || e.Key == Key.F3)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && DialogLayer.Visibility == Visibility.Visible)
        {
            CloseDialog(false);
            e.Handled = true;
        }
    }

    private void SearchBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (SearchBox.Text.Length > 0) SearchPopup.IsOpen = true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        if (SearchBox.Text.Trim().Length == 0)
        {
            SearchPopup.IsOpen = false;
            return;
        }
        _searchDebounce.Start();
    }

    private async Task RunSearchAsync()
    {
        var query = SearchBox.Text;
        if (query.Trim().Length == 0) return;
        var engine = await AppHost.GetSearchAsync();
        if (query != SearchBox.Text) return; // la saisie a changé entre-temps
        var hits = engine.Search(query, 14);
        SearchResults.Items.Clear();
        if (hits.Count == 0)
        {
            SearchResults.Items.Add(new ListBoxItem
            {
                Content = new TextBlock { Text = L("No results. Try another word (e.g., “camera”, “startup”, “telemetry”)."), Style = (Style)FindResource("Pp.Caption") },
                IsEnabled = false,
            });
        }
        foreach (var hit in hits) SearchResults.Items.Add(new ListBoxItem { Content = BuildResult(hit), Tag = hit });
        SearchResults.SelectedIndex = hits.Count > 0 ? 0 : -1;
        SearchPopup.IsOpen = true;
    }

    private FrameworkElement BuildResult(SearchHit hit)
    {
        var doc = hit.Document;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock { Text = doc.Glyph, Style = (Style)FindResource("Pp.Icon"), Margin = new Thickness(2, 0, 12, 0) });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = doc.Title, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("Pp.TextPrimary") });
        text.Children.Add(new TextBlock { Text = doc.Subtitle ?? "", Style = (Style)FindResource("Pp.Caption"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        FrameworkElement right;
        // Application directe depuis la recherche : jamais pour un réglage « Avancé » masqué hors mode avancé, et avec
        // la même confirmation que la carte du réglage (risque, avertissement, non annulable).
        if (hit.Intent is SearchIntent.TurnOn or SearchIntent.TurnOff && doc.Payload is TweakDefinition tweak
            && (AppHost.Settings.AdvancedMode || tweak.Risk != RiskLevel.Advanced)
            && tweak.GetOption(hit.Intent == SearchIntent.TurnOn ? TweakDefinition.On : TweakDefinition.Off) is { } quickOption)
        {
            var button = new Button
            {
                Content = quickOption.Label,
                Style = (Style)FindResource("Pp.AccentButton"),
                Padding = new Thickness(10, 3, 10, 3),
                MinHeight = 26,
                FontSize = 12,
                ToolTip = L("Apply directly, without opening the page"),
            };
            button.Click += async (_, e) =>
            {
                e.Handled = true;
                SearchPopup.IsOpen = false;
                if (IsLocked || !await TweakItemViewModel.ConfirmIfNeededAsync(tweak, quickOption)) return;
                var outcome = await AppHost.Engine.ApplyAsync(tweak, quickOption.Key);
                ShowOutcome(outcome);
            };
            right = button;
        }
        else
        {
            right = new TextBlock
            {
                Text = doc.Kind switch
                {
                    SearchEntryKind.Tweak => LC("search result", "Setting"),
                    SearchEntryKind.Page => LC("search result", "Page"),
                    SearchEntryKind.WindowsSetting => LC("search result", "Windows Settings"),
                    SearchEntryKind.Tool => LC("search result", "Tool"),
                    _ => LC("search result", "Feature"),
                },
                Style = (Style)FindResource("Pp.Caption"),
                Foreground = (Brush)FindResource("Pp.TextTertiary"),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        right.Margin = new Thickness(12, 0, 2, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when SearchResults.Items.Count > 0:
                SearchResults.SelectedIndex = Math.Min(SearchResults.Items.Count - 1, SearchResults.SelectedIndex + 1);
                SearchResults.ScrollIntoView(SearchResults.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when SearchResults.Items.Count > 0:
                SearchResults.SelectedIndex = Math.Max(0, SearchResults.SelectedIndex - 1);
                SearchResults.ScrollIntoView(SearchResults.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                if (SearchResults.SelectedItem is ListBoxItem { Tag: SearchHit hit }) Activate(hit);
                e.Handled = true;
                break;
            case Key.Escape:
                SearchPopup.IsOpen = false;
                SearchBox.Clear();
                PageHost.Focus();
                e.Handled = true;
                break;
        }
    }

    private void SearchResults_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) is not null) return;
        if (SearchResults.SelectedItem is ListBoxItem { Tag: SearchHit hit }) Activate(hit);
    }

    private void Activate(SearchHit hit)
    {
        SearchPopup.IsOpen = false;
        SearchBox.Clear();
        switch (hit.Document.Payload)
        {
            case TweakDefinition t:
                Navigate(AppHost.Registry.PageIdForCategory(t.Category), "tweak:" + t.Id);
                break;
            case PageInfo p:
                Navigate(p.Id);
                break;
            case CategoryInfo c:
                Navigate("category:" + c.Id);
                break;
            case SearchEntry entry:
                try
                {
                    if (entry.Execute is not null) entry.Execute();
                    else if (entry.PageId is not null) Navigate(entry.PageId, entry.PageParameter);
                }
                catch (Exception ex) { Show(L("Couldn't open: {0}", ex.Message), ToastKind.Error); }
                break;
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var d = child; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is T t) return t;
        return null;
    }

    // ================================================================== Session admin

    private async Task<bool> ConfirmElevationAsync()
    {
        if (!AppHost.Settings.ConfirmBeforeAdminActions) return true;
        return await Dispatcher.InvokeAsync(() => ConfirmAsync(L("Administrator permission"),
            L("This action changes protected Windows settings: Windows will ask for your permission (UAC).\n\nTimonier will open a limited admin session: it only runs the actions in its catalog, closes automatically after {0} min of inactivity, and can be closed at any time from the banner at the bottom left.", AppHost.Settings.BrokerIdleMinutes), L("Continue"))).Task.Unwrap();
    }

    private void UpdateAdminBar()
    {
        AdminBar.Visibility = AppHost.Broker.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        AdminText.Text = L("Admin session active\nCloses automatically after {0} min of inactivity", AppHost.Settings.BrokerIdleMinutes);
    }

    private async void CloseAdmin_Click(object sender, RoutedEventArgs e) => await AppHost.Broker.StopAsync();

    private async void Reboot_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync(L("Restart now?"), L("Save your work: the PC will restart in 5 seconds."), L("Restart")))
            await SystemEffects.RebootNowAsync();
    }

    // ================================================================== Fermeture

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Application.Current is not App app) return;
        e.Cancel = true;
        Hide();
        // Timonier reste parfois actif dans la zone de notification : le code sera redemandé à la réouverture.
        if (!string.IsNullOrEmpty(AppHost.Settings.AppPinHash)) Lock();
        if (app.ShouldStayInBackground()) app.OnMainWindowHidden();
        else _ = app.ExitAsync();
    }

    // ================================================================== Verrouillage

    private bool IsLocked => LockLayer.Visibility == Visibility.Visible;

    /// <summary>
    /// Verrouille : l'écran couvre la fenêtre et tout le reste est désactivé, pour que ni le clavier (Tab, raccourcis)
    /// ni la recherche ne puissent atteindre les pages et réglages situés dessous.
    /// </summary>
    private void Lock()
    {
        SearchPopup.IsOpen = false;
        LockLayer.Visibility = Visibility.Visible;
        foreach (UIElement child in RootGrid.Children)
            if (!ReferenceEquals(child, LockLayer)) child.IsEnabled = false;
        LockPin.Focus();
    }

    private void UnlockUi()
    {
        LockLayer.Visibility = Visibility.Collapsed;
        foreach (UIElement child in RootGrid.Children) child.IsEnabled = true;
        LockPin.Clear();
        LockError.Text = "";
        if (DialogLayer.Visibility == Visibility.Visible) DialogPrimary.Focus();
    }

    private void LockPin_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Unlock_Click(sender, e);
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (DateTime.UtcNow < _lockUntil)
        {
            LockError.Text = L("Too many attempts. Try again in {0} s.", (int)(_lockUntil - DateTime.UtcNow).TotalSeconds + 1);
            return;
        }
        if (PinHasher.Verify(LockPin.Password, AppHost.Settings.AppPinHash))
        {
            UnlockUi();
            _lockFailures = 0;
            return;
        }
        LockPin.Clear();
        _lockFailures++;
        if (_lockFailures >= 5) _lockUntil = DateTime.UtcNow.AddSeconds(30 * (_lockFailures - 4));
        LockError.Text = L("Incorrect code.");
    }

    // ================================================================== Dialogues

    private TaskCompletionSource<bool>? _dialog;
    private Func<bool>? _dialogValidator;
    private readonly SemaphoreSlim _dialogGate = new(1, 1);

    public async Task<bool> ShowAsync(string title, FrameworkElement content, string primary = "", string? secondary = "", bool danger = false)
    {
        if (primary.Length == 0) primary = L("OK");
        if (secondary is { Length: 0 }) secondary = L("Undo");
        await _dialogGate.WaitAsync();
        try
        {
            _dialog = new TaskCompletionSource<bool>();
            DialogTitle.Text = title;
            DialogContent.Content = content;
            DialogPrimary.Content = primary;
            DialogPrimary.Style = (Style)FindResource(danger ? "Pp.DangerButton" : "Pp.AccentButton");
            DialogSecondary.Content = secondary ?? "";
            DialogSecondary.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;
            DialogLayer.Visibility = Visibility.Visible;
            if (!IsVisible) (Application.Current as App)?.ShowMainWindow();
            DialogPrimary.Focus();
            return await _dialog.Task;
        }
        finally
        {
            _dialogValidator = null;
            _dialogGate.Release();
        }
    }

    public Task<bool> ConfirmAsync(string title, string message, string primary = "", string secondary = "", bool danger = false) =>
        ShowAsync(title, new TextBlock { Text = message, Style = (Style)FindResource("Pp.Body") },
            primary.Length == 0 ? L("Continue") : primary, secondary, danger);

    public Task AlertAsync(string title, string message) =>
        ShowAsync(title, new TextBlock { Text = message, Style = (Style)FindResource("Pp.Body") }, L("OK"), null);

    public async Task<string?> PromptAsync(string title, string message, string? initial = null, bool password = false, Func<string, string?>? validate = null)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = message, Style = (Style)FindResource("Pp.Body"), Margin = new Thickness(0, 0, 0, 10) });
        var textBox = new TextBox { Text = initial ?? "", Height = 32, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
        var passwordBox = new PasswordBox { Height = 32, FontSize = 14 };
        panel.Children.Add(password ? passwordBox : textBox);
        var error = new TextBlock { Style = (Style)FindResource("Pp.Caption"), Foreground = (Brush)FindResource("Pp.Danger"), Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(error);
        string Value() => password ? passwordBox.Password : textBox.Text;

        var gateTask = ShowAsync(title, panel, L("OK"), L("Undo"));
        _dialogValidator = () =>
        {
            var message2 = validate?.Invoke(Value());
            error.Text = message2 ?? "";
            return message2 is null;
        };
        await Dispatcher.InvokeAsync(() => { if (password) passwordBox.Focus(); else { textBox.Focus(); textBox.SelectAll(); } }, DispatcherPriority.Input);
        return await gateTask ? Value() : null;
    }

    private void DialogPrimary_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogValidator is not null && !_dialogValidator()) return;
        CloseDialog(true);
    }

    private void DialogSecondary_Click(object sender, RoutedEventArgs e) => CloseDialog(false);

    private void CloseDialog(bool result)
    {
        DialogLayer.Visibility = Visibility.Collapsed;
        DialogContent.Content = null;
        _dialog?.TrySetResult(result);
    }

    // ================================================================== Notifications

    public void Show(string message, ToastKind kind = ToastKind.Info, string? actionLabel = null, Action? action = null, TimeSpan? duration = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => Show(message, kind, actionLabel, action, duration));
            return;
        }

        var (glyph, brush, background) = kind switch
        {
            ToastKind.Success => ("", "Pp.Success", "Pp.CardBackground"),
            ToastKind.Warning => ("", "Pp.Warning", "Pp.CardBackground"),
            ToastKind.Error => ("", "Pp.Danger", "Pp.CardBackground"),
            _ => ("", "Pp.Info", "Pp.CardBackground"),
        };
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 8, 10),
            Margin = new Thickness(0, 8, 0, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.22 },
        };
        border.SetResourceReference(Border.BackgroundProperty, background);
        border.SetResourceReference(Border.BorderBrushProperty, "Pp.ControlStroke");

        var dock = new DockPanel();
        var icon = new TextBlock { Text = glyph, Style = (Style)FindResource("Pp.Icon"), Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);

        var close = new Button { Content = new TextBlock { Text = "", Style = (Style)FindResource("Pp.Icon"), FontSize = 10 }, Style = (Style)FindResource("Pp.SubtleButton"), VerticalAlignment = VerticalAlignment.Top, MinHeight = 0, Padding = new Thickness(6) };
        DockPanel.SetDock(close, Dock.Right);
        dock.Children.Add(close);

        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = message, Style = (Style)FindResource("Pp.Body") });
        if (actionLabel is not null && action is not null)
        {
            var actionButton = new Button { Content = actionLabel, Style = (Style)FindResource("Pp.LinkButton"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-2, 6, 0, 0) };
            actionButton.Click += (_, _) =>
            {
                RemoveToast(border);
                try { action(); } catch (Exception ex) { Show(ex.Message, ToastKind.Error); }
            };
            body.Children.Add(actionButton);
        }
        dock.Children.Add(body);
        border.Child = dock;
        close.Click += (_, _) => RemoveToast(border);

        while (ToastHost.Children.Count >= 3) ToastHost.Children.RemoveAt(0);
        ToastHost.Children.Add(border);
        if (!AppHost.Settings.ReduceAnimations)
            border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

        var timer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(action is null ? (kind == ToastKind.Error ? 9 : 5) : 10) };
        timer.Tick += (_, _) => { timer.Stop(); RemoveToast(border); };
        timer.Start();
    }

    private void RemoveToast(Border toast) => ToastHost.Children.Remove(toast);

    public void ShowOutcome(ApplyOutcome outcome)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => ShowOutcome(outcome));
            return;
        }
        if (outcome.Cancelled)
        {
            Show(outcome.Message, ToastKind.Info);
            return;
        }
        if (!outcome.Success)
        {
            Show(outcome.Message, ToastKind.Error);
            return;
        }

        if (outcome.JournalId is { } id)
        {
            Show(outcome.Message, ToastKind.Success, L("Undo"), () => _ = UndoAsync(id));
        }
        else if (outcome.Message.Length > 0)
        {
            Show(outcome.Message, ToastKind.Success);
        }

        if (outcome.Effect.HasFlag(ApplyEffect.Reboot))
        {
            SystemEffects.MarkReboot();
            Show(L("This change will take effect after the PC restarts."), ToastKind.Info);
        }
        else if (outcome.Effect.HasFlag(ApplyEffect.SignOut))
        {
            SystemEffects.MarkSignOut();
            Show(L("This change will take effect the next time you sign in."), ToastKind.Info, L("Sign out now"), async () =>
            {
                if (await ConfirmAsync(L("Sign out?"), L("Save your work before continuing."), L("Sign out")))
                    await SystemEffects.SignOutNowAsync();
            });
        }
        else if (outcome.Effect.HasFlag(ApplyEffect.RestartExplorer))
        {
            Show(L("File Explorer must be restarted to show this change."), ToastKind.Info, L("Restart Explorer"),
                () => _ = SystemEffects.RestartExplorerAsync());
        }
    }

    private async Task UndoAsync(Guid id)
    {
        var entry = AppHost.Engine.JournalAll().FirstOrDefault(e => e.Id == id);
        if (entry is null)
        {
            Show(L("History entry not found."), ToastKind.Warning);
            return;
        }
        var outcome = await AppHost.Engine.UndoAsync(entry);
        ShowOutcome(outcome with { JournalId = null });
    }
}

internal static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);
}
