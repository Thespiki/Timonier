using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Timonier.Core.Platform;
using Timonier.Core.Security;
using Timonier.Core.Settings;
using Timonier.UI.Controls;
using Timonier.UI.Services;
using Timonier.UI.Theme;

namespace Timonier.Modules.AppPages;

/// <summary>Paramètres de Timonier lui-même (apparence, comportement, sécurité, données, à propos).</summary>
public sealed class SettingsPage : UserControl, INavigationAware
{
    private const int MinPinLength = 4;
    private const int MaxPinLength = 64;

    private readonly Dictionary<string, FrameworkElement> _anchors = [];
    private TextBlock? _brokerStatus;
    private Button? _closeBroker;
    private TextBlock? _startupStatus;
    private bool _busy;

    public SettingsPage()
    {
        Build();
        Loaded += (_, _) => { AppHost.Broker.StateChanged += OnBrokerStateChanged; UpdateBrokerStatus(); UpdateStartupStatus(); };
        Unloaded += (_, _) => AppHost.Broker.StateChanged -= OnBrokerStateChanged;
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is string s && s.StartsWith("section:", StringComparison.Ordinal) && _anchors.TryGetValue(s[8..], out var target))
            AppUi.ScrollTo(target);
    }

    private void OnBrokerStateChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(UpdateBrokerStatus);

    private static AppSettings S => AppHost.Settings;

    // ================================================================== Construction

    private void Build()
    {
        _anchors.Clear();
        var stack = PageScaffold.Create(this, L("Timonier settings"),
            L("Appearance, security, and app behavior. These options don't change Windows."), AppPagesModule.SettingsGlyph);

        AddSection(stack, "appearance", L("Appearance"), BuildAppearance());
        AddSection(stack, "behavior", L("Behavior"), BuildBehavior());
        AddSection(stack, "security", L("Security"), BuildSecurity());
        AddSection(stack, "data", L("Data & reset"), BuildData());
        AddSection(stack, "about", L("About"), BuildAbout());
    }

    private void AddSection(StackPanel stack, string key, string title, UIElement content)
    {
        var header = AppUi.Section(title);
        _anchors[key] = header;
        stack.Children.Add(header);
        stack.Children.Add(content);
    }

    private static Border RowsCard(params UIElement[] rows)
    {
        var s = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) s.Children.Add(AppUi.Divider(new Thickness(0, 12, 0, 12)));
            s.Children.Add(rows[i]);
        }
        return AppUi.Card(s);
    }

    /// <summary>Interrupteur lié à une préférence (enregistrée immédiatement).</summary>
    private static CheckBox BoundToggle(string name, bool value, Action<AppSettings, bool> set, Action<bool>? after = null)
    {
        var t = AppUi.Toggle(name, value);
        void Changed(bool on)
        {
            SettingsStore.Update(s => set(s, on));
            after?.Invoke(on);
        }
        t.Checked += (_, _) => Changed(true);
        t.Unchecked += (_, _) => Changed(false);
        return t;
    }

    // ------------------------------------------------------------------ Apparence

    private UIElement BuildAppearance()
    {
        var theme = new SegmentedBar();
        theme.Add(LC("theme", "System"), "");
        theme.Add(LC("theme", "Light"), "");
        theme.Add(LC("theme", "Dark"), "");
        theme.Select(S.Theme switch { ThemePreference.Light => 1, ThemePreference.Dark => 2, _ => 0 }, notify: false);
        theme.SelectionChanged += (_, i) =>
        {
            SettingsStore.Update(s => s.Theme = i switch { 1 => ThemePreference.Light, 2 => ThemePreference.Dark, _ => ThemePreference.System });
            ThemeManager.Apply();
        };

        var animations = BoundToggle(L("Reduce animations"), S.ReduceAnimations, (s, v) => s.ReduceAnimations = v);

        return RowsCard(
            BuildLanguageRow(),
            AppUi.SettingRow("", L("Timonier theme"),
                L("“System” follows the light or dark mode chosen in Windows Settings › Personalization › Colors. Affects Timonier only."), theme),
            AppUi.SettingRow("", L("Reduce animations"),
                L("Removes fades when switching pages: a simpler interface, slightly lighter on low-end PCs."), animations));
    }

    /// <summary>Choix de la langue : automatique (langue de Windows) ou une langue incorporée, appliquée au prochain lancement.</summary>
    private static UIElement BuildLanguageRow()
    {
        var combo = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Right };
        var auto = Core.Localization.Loc.AutomaticLanguage;
        combo.Items.Add(new ComboBoxItem { Content = L("Automatic ({0})", auto.NativeName), Tag = "auto" });
        foreach (var lang in Core.Localization.Languages.All.Where(l => Core.Localization.Loc.IsAvailable(l.Code))
                     .OrderBy(l => l.NativeName, StringComparer.Create(Core.Localization.Loc.Culture, true)))
            combo.Items.Add(new ComboBoxItem { Content = lang.NativeName, Tag = lang.Code, ToolTip = lang.EnglishName });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, S.Language, StringComparison.OrdinalIgnoreCase))
                             ?? combo.Items[0];

        var restart = new Button { Content = LC("app restart", "Restart now"), Style = (Style)Application.Current.FindResource("Pp.AccentButton"),
            Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        restart.Click += async (_, _) => { if (Application.Current is App app) await app.RestartAsync(); };

        var note = AppUi.Caption(L("The new language applies the next time Timonier starts."), tertiary: true);
        note.Margin = new Thickness(0, 4, 0, 0);
        note.Visibility = Visibility.Collapsed;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string code }) return;
            SettingsStore.Update(s => s.Language = code);
            var effective = code == "auto" ? Core.Localization.Loc.AutomaticLanguage.Code : code;
            var changed = !string.Equals(effective, Core.Localization.Loc.Language, StringComparison.OrdinalIgnoreCase);
            note.Visibility = restart.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
        };

        var quality = new Button { Content = L("Translations made with AI assistance: suggest a correction"),
            Style = (Style)Application.Current.FindResource("Pp.LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        quality.Click += (_, _) =>
        {
            try { ProcessRunner.OpenUrl("https://github.com/Thespiki/Timonier/blob/main/docs/I18N.md"); }
            catch (Exception ex) { AppHost.Toasts.Show(L("Couldn't open the browser: {0}", ex.Message), ToastKind.Error); }
        };

        var below = new StackPanel();
        below.Children.Add(note);
        below.Children.Add(restart);
        below.Children.Add(quality);
        return AppUi.SettingRow("", L("Language"),
            L("“Automatic” follows the Windows display language (English if it isn't translated yet)."), combo, below);
    }

    // ------------------------------------------------------------------ Comportement

    private UIElement BuildBehavior()
    {
        var advanced = BoundToggle(L("Advanced mode"), S.AdvancedMode, (s, v) => s.AdvancedMode = v, on =>
            AppHost.Toasts.Show(on
                ? L("Advanced mode on. Pages that are already open will show it the next time Timonier starts.")
                : L("Advanced mode off. Pages that are already open will be updated the next time Timonier starts."), ToastKind.Info));

        var confirm = BoundToggle(L("Confirm before admin actions"), S.ConfirmBeforeAdminActions, (s, v) => s.ConfirmBeforeAdminActions = v);

        var background = BoundToggle(L("Stay running in the background when needed"), S.AllowBackground, (s, v) => s.AllowBackground = v);

        var startup = AppUi.Toggle(L("Start with Windows"), StartupRegistration.IsEnabled());
        var suppress = false;
        async void StartupChanged(bool on)
        {
            if (suppress) return;
            try
            {
                await Task.Run(() => { if (on) StartupRegistration.Enable(); else StartupRegistration.Disable(); });
                SettingsStore.Update(s => s.StartWithWindows = on);
                AppHost.Toasts.Show(on ? L("Timonier will start with Windows, in the notification area.") : L("Timonier will no longer start with Windows."), ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Error("AppPages", "démarrage avec Windows", ex);
                AppHost.Toasts.Show(L("Couldn't change automatic startup: {0}", ex.Message), ToastKind.Error);
                suppress = true;
                startup.IsChecked = !on;
                suppress = false;
            }
            UpdateStartupStatus();
        }
        startup.Checked += (_, _) => StartupChanged(true);
        startup.Unchecked += (_, _) => StartupChanged(false);
        _startupStatus = AppUi.Caption("", tertiary: true);
        _startupStatus.Margin = new Thickness(0, 4, 0, 0);

        return RowsCard(
            AppUi.SettingRow("", L("Advanced mode"),
                L("Also shows settings reserved for experienced users (higher risk). Pages that are already open take it into account the next time Timonier starts."), advanced),
            AppUi.SettingRow("", L("Explain before asking for administrator rights"),
                L("Before the UAC prompt, Timonier explains why the rights are needed and how long the session stays open. The UAC prompt and confirmations for sensitive actions are always shown."), confirm),
            AppUi.SettingRow("", L("Stay running in the background when needed"),
                L("Only when a running feature requires it (guided access, task in progress…). Otherwise, closing the window quits Timonier completely."), background),
            AppUi.SettingRow("", L("Start with Windows"),
                L("Quietly starts Timonier in the notification area when you sign in. Mainly useful if you use guided access; otherwise, it uses a bit of memory for no benefit."), startup, _startupStatus));
    }

    private void UpdateStartupStatus()
    {
        if (_startupStatus is null) return;
        string text;
        if (!StartupRegistration.IsEnabled()) text = L("Current status: off (no “Timonier” entry in HKCU\\…\\CurrentVersion\\Run).");
        else if (StartupRegistration.DisabledByTaskManager()) text = L("Current status: entry present but disabled from Task Manager. Turn the switch back on to restore it.");
        else if (StartupRegistration.PointsElsewhere()) text = L("Current status: on, but the entry launches another copy of Timonier. Turn it off and back on to use this one.");
        else text = L("Current status: on (“Timonier” entry in HKCU\\…\\CurrentVersion\\Run).");
        _startupStatus.Text = text;
    }

    // ------------------------------------------------------------------ Sécurité

    private UIElement BuildSecurity()
    {
        // Code PIN
        var hasPin = !string.IsNullOrEmpty(S.AppPinHash);
        var pinButtons = new StackPanel { Orientation = Orientation.Horizontal };
        if (hasPin)
        {
            pinButtons.Children.Add(AppUi.Button(L("Change"), "", "Pp.Button", async (_, _) => await ChangePinAsync()));
            var remove = AppUi.Button(L("Remove"), "", "Pp.SubtleButton", async (_, _) => await RemovePinAsync());
            remove.Margin = new Thickness(8, 0, 0, 0);
            pinButtons.Children.Add(remove);
        }
        else pinButtons.Children.Add(AppUi.Button(L("Set PIN"), "", "Pp.AccentButton", async (_, _) => await SetPinAsync()));
        var pinState = hasPin
            ? AppUi.Badge(L("PIN set"), "Success", "")
            : AppUi.Badge(L("No PIN"), "Neutral");
        pinState.HorizontalAlignment = HorizontalAlignment.Left;
        pinState.Margin = new Thickness(0, 6, 0, 0);

        // Session administrateur
        var minutes = Math.Clamp(S.BrokerIdleMinutes, 1, 30);
        var value = AppUi.Text(MinutesLabel(minutes), "Pp.Body", wrap: false);
        value.Width = 64;
        value.TextAlignment = TextAlignment.Right;
        value.Margin = new Thickness(0, 0, 10, 0);
        var slider = new Slider { Minimum = 1, Maximum = 30, Value = minutes, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 220, SmallChange = 1, LargeChange = 5 };
        System.Windows.Automation.AutomationProperties.SetName(slider, L("Time before the admin session closes automatically, in minutes"));
        var save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        save.Tick += (_, _) =>
        {
            save.Stop();
            var v = (int)slider.Value;
            SettingsStore.Update(s => s.BrokerIdleMinutes = v);
            AppHost.Broker.IdleMinutes = v;
        };
        slider.ValueChanged += (_, e) =>
        {
            value.Text = MinutesLabel((int)e.NewValue);
            save.Stop();
            save.Start();
        };
        var sliderRow = new StackPanel { Orientation = Orientation.Horizontal };
        sliderRow.Children.Add(value);
        sliderRow.Children.Add(slider);

        _brokerStatus = AppUi.Caption("", tertiary: true);
        _brokerStatus.Margin = new Thickness(0, 4, 0, 0);
        _closeBroker = AppUi.Button(L("Close now"), "", "Pp.Button", async (_, _) => await CloseBrokerAsync());
        UpdateBrokerStatus();

        return RowsCard(
            AppUi.SettingRow("", L("Timonier PIN"),
                L("Asked for every time Timonier opens (at least 4 characters). Light protection against use by someone else on your session: it doesn't replace your Windows account password. The PIN is stored only as a hash."), pinButtons, pinState),
            AppUi.SettingRow("", L("Close admin session automatically"),
                L("After this period of inactivity, the administrator process closes and a new UAC prompt will be needed. Applies the next time the admin session opens."), sliderRow),
            AppUi.SettingRow("", L("Admin session"), null, _closeBroker, _brokerStatus));
    }

    private static string MinutesLabel(int m) => L("{0} min", m);

    private void UpdateBrokerStatus()
    {
        if (_brokerStatus is null || _closeBroker is null) return;
        var b = AppHost.Broker;
        _brokerStatus.Text = b.IsRunning
            ? b.StartedAt is { } at
                ? L("Active since {0}. Close it as soon as you've finished making changes.", at.ToString("t", Core.Localization.Loc.Culture))
                : L("Active. Close it as soon as you've finished making changes.")
            : L("Inactive: Timonier is currently running with your standard user rights.");
        _closeBroker.IsEnabled = b.IsRunning && !_busy;
    }

    private async Task CloseBrokerAsync()
    {
        if (_closeBroker is null) return;
        _closeBroker.IsEnabled = false;
        try
        {
            await AppHost.Broker.StopAsync();
            AppHost.Toasts.Show(L("Admin session closed."), ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error("AppPages", "fermeture du broker", ex);
            AppHost.Toasts.Show(L("Couldn't close the admin session: {0}", ex.Message), ToastKind.Error);
        }
        UpdateBrokerStatus();
    }

    // ------------------------------------------------------------------ Code PIN

    private static string? ValidateNewPin(string pin)
    {
        if (pin.Length < MinPinLength) return L("The PIN must be at least {0} characters long.", MinPinLength);
        if (pin.Length > MaxPinLength) return L("The PIN can't be longer than {0} characters.", MaxPinLength);
        if (pin.Any(char.IsControl)) return L("The PIN contains characters that aren't allowed.");
        return null;
    }

    /// <summary>Demande le code actuel et le vérifie (PBKDF2, hors du thread UI). Faux si annulé ou incorrect.</summary>
    private async Task<bool> VerifyCurrentPinAsync(string title)
    {
        var current = await AppHost.Dialogs.PromptAsync(title, L("Enter the current Timonier PIN."), null, password: true,
            v => v.Length == 0 ? L("Enter the PIN.") : null);
        if (current is null) return false;
        var stored = S.AppPinHash;
        var ok = await Task.Run(() => PinHasher.Verify(current, stored));
        if (!ok) AppHost.Toasts.Show(L("The current PIN is incorrect."), ToastKind.Error);
        return ok;
    }

    private async Task<string?> AskNewPinAsync(string title)
    {
        var pin = await AppHost.Dialogs.PromptAsync(title,
            L("Choose a PIN of at least {0} characters (digits, letters, or symbols). It will be asked for every time Timonier opens.", MinPinLength),
            null, password: true, ValidateNewPin);
        if (pin is null) return null;
        var again = await AppHost.Dialogs.PromptAsync(title, L("Enter the same PIN again to confirm."), null, password: true,
            v => v == pin ? null : L("The two PINs don't match."));
        return again is null ? null : pin;
    }

    private async Task SavePinAsync(string? pin, string message)
    {
        var hash = pin is null ? null : await Task.Run(() => PinHasher.Hash(pin));
        SettingsStore.Update(s => s.AppPinHash = hash);
        AppHost.Toasts.Show(message, ToastKind.Success);
        Rebuild("security");
    }

    private async Task SetPinAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (await AskNewPinAsync(L("Set a PIN")) is { } pin)
                await SavePinAsync(pin, L("PIN set. It will be asked for the next time Timonier opens."));
        }
        finally { _busy = false; }
    }

    private async Task ChangePinAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (!await VerifyCurrentPinAsync(L("Change PIN"))) return;
            if (await AskNewPinAsync(L("New PIN")) is { } pin)
                await SavePinAsync(pin, L("PIN changed."));
        }
        finally { _busy = false; }
    }

    private async Task RemovePinAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (!await VerifyCurrentPinAsync(L("Remove PIN"))) return;
            await SavePinAsync(null, L("PIN removed: Timonier will open without a PIN."));
        }
        finally { _busy = false; }
    }

    // ------------------------------------------------------------------ Données

    private UIElement BuildData()
    {
        var folders = new StackPanel { Orientation = Orientation.Horizontal };
        folders.Children.Add(AppUi.Button(L("Data"), "", "Pp.Button", (_, _) => TransparencyPage.OpenFolder(AppPaths.LocalData)));
        var logs = AppUi.Button(L("Diagnostic logs"), "", "Pp.Button", (_, _) => TransparencyPage.OpenFolder(AppPaths.Logs));
        logs.Margin = new Thickness(8, 0, 0, 0);
        folders.Children.Add(logs);

        var reset = AppUi.Button(L("Reset…"), "", "Pp.DangerButton", async (_, _) => await ResetAsync());

        var privacy = AppUi.Button(L("View details"), "", "Pp.Button", (_, _) => AppHost.Navigator.Navigate(AppPagesModule.TransparencyPageId, "tab:security"));

        return RowsCard(
            AppUi.SettingRow("", L("Timonier folders"),
                L("Preferences, history, cache, and diagnostic logs are stored in your profile (AppData\\Local\\Timonier). Nothing is sent off this PC."), folders),
            AppUi.SettingRow("", L("Stored data"), L("List of files and registry keys used, with their size."), privacy),
            AppUi.SettingRow("", L("Reset Timonier"),
                L("Restores default preferences (theme, options, timeout, PIN, start with Windows) and clears the cache. The change history is kept: you can still undo."), reset));
    }

    private async Task ResetAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var content = new StackPanel { MaxWidth = 520 };
            content.Children.Add(AppUi.Text(L("The following will return to their default state:")));
            content.Children.Add(AppUi.Bullets(
                L("Theme, animations, advanced mode, confirmations, and background operation"),
                L("Admin session timeout (5 min)"),
                L("Timonier PIN (removed)"),
                L("Start with Windows (off)"),
                L("Cache: hardware profile and temporary data (re-created automatically)")));
            var kept = AppUi.Text(L("Kept:"));
            kept.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(kept);
            content.Children.Add(AppUi.Bullets(
                L("The change history (user and admin), so you can always undo"),
                L("The information needed to remove an active configuration (kiosk mode, guided access and its PIN, previous wallpaper…)"),
                L("All changes already applied to Windows: the reset only affects Timonier")));

            if (!await AppHost.Dialogs.ShowAsync(L("Reset Timonier?"), content, L("Reset"), L("Undo"), danger: true)) return;
            if (!string.IsNullOrEmpty(S.AppPinHash) && !await VerifyCurrentPinAsync(L("Reset Timonier"))) return;

            SettingsStore.Reset();
            AppHost.Broker.IdleMinutes = S.BrokerIdleMinutes;
            ThemeManager.Apply();

            var errors = await Task.Run(() =>
            {
                var list = new List<string>();
                try { if (StartupRegistration.IsEnabled()) StartupRegistration.Disable(); }
                catch (Exception ex) { list.Add(L("start with Windows: {0}", ex.Message)); }
                try { if (File.Exists(DataInventory.ProfileCacheFile)) File.Delete(DataInventory.ProfileCacheFile); }
                catch (Exception ex) { list.Add(L("hardware profile: {0}", ex.Message)); }
                try
                {
                    if (Directory.Exists(DataInventory.CacheDir))
                    {
                        foreach (var f in Directory.EnumerateFiles(DataInventory.CacheDir, "*", SearchOption.AllDirectories))
                            try { File.Delete(f); } catch { /* fichier en cours d'utilisation : ignoré */ }
                        foreach (var d in Directory.EnumerateDirectories(DataInventory.CacheDir))
                            try { Directory.Delete(d, true); } catch { /* ignoré */ }
                    }
                }
                catch (Exception ex) { list.Add(L("cache: {0}", ex.Message)); }
                return list;
            });
            Log.Info("AppPages", "réinitialisation des préférences" + (errors.Count > 0 ? " (partielle) : " + string.Join(" ; ", errors) : ""));
            if (errors.Count == 0) AppHost.Toasts.Show(L("Timonier has been reset. The change history is kept."), ToastKind.Success);
            else AppHost.Toasts.Show(L("Partial reset: {0}", string.Join(" ; ", errors)), ToastKind.Warning);
            Rebuild("data");
        }
        finally { _busy = false; }
    }

    private void Rebuild(string anchor)
    {
        Build();
        UpdateStartupStatus();
        UpdateBrokerStatus();
        if (_anchors.TryGetValue(anchor, out var target)) AppUi.ScrollTo(target);
    }

    // ------------------------------------------------------------------ À propos

    private static UIElement BuildAbout()
    {
        var asm = typeof(AppHost).Assembly;
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
            ?? asm.GetName().Version?.ToString() ?? LC("version", "unknown");

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var tile = AppUi.GlyphTile("", size: 48);
        tile.Margin = new Thickness(0, 0, 14, 0);
        head.Children.Add(tile);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(AppUi.Text("Timonier", "Pp.CardTitle", wrap: false));
        names.Children.Add(AppUi.Caption(L("Version {0}", version)));
        head.Children.Add(names);
        var badges = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        var local = AppUi.Badge(L("100% local"), "Success", "");
        local.Margin = new Thickness(0, 0, 6, 0);
        badges.Children.Add(local);
        badges.Children.Add(AppUi.Badge(L("No telemetry"), "Success", ""));
        head.Children.Add(badges);

        var s = new StackPanel();
        s.Children.Add(head);
        s.Children.Add(PageScaffold.KeyValue(".NET", RuntimeInformation.FrameworkDescription));
        s.Children.Add(PageScaffold.KeyValue("Windows", AppHost.Profile.WindowsLabel));
        s.Children.Add(PageScaffold.KeyValue(L("Edition"), AppHost.Profile.EditionLabel));
        s.Children.Add(PageScaffold.KeyValue(L("Architecture"), RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()));
        string exe;
        try { exe = AppPaths.ExecutablePath; } catch { exe = LC("location", "unknown"); }
        s.Children.Add(PageScaffold.KeyValue(LC("file path", "Location"), exe));
        s.Children.Add(PageScaffold.KeyValue(L("Privacy"), L("Timonier doesn't collect or send any data. It has no account, no ads, and no automatic updates.")));

        var keysTitle = AppUi.Text(L("Keyboard shortcuts"), "Pp.Body");
        keysTitle.FontWeight = FontWeights.SemiBold;
        keysTitle.Margin = new Thickness(0, 16, 0, 6);
        s.Children.Add(keysTitle);
        (string Keys, string Text)[] shortcuts =
        [
            ("Ctrl+K · Ctrl+F · F3", L("Search for a setting, page, or feature")),
            ("↑ · ↓", L("Browse search results")),
            (LC("key", "Enter"), L("Open the selected result")),
            (LC("key", "Esc"), L("Close search or the dialog")),
        ];
        foreach (var (keys, text) in shortcuts)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var keyBox = new Border { Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
            keyBox.Themed(Border.BackgroundProperty, "Pp.ControlFill");
            keyBox.Themed(Border.BorderBrushProperty, "Pp.ControlStroke");
            keyBox.Child = AppUi.Text(keys, "Pp.Caption", wrap: false);
            row.Children.Add(keyBox);
            var t = AppUi.Text(text);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            s.Children.Add(row);
        }

        var links = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var transparency = AppUi.Button(L("Transparency"), AppPagesModule.TransparencyGlyph, "Pp.Button", (_, _) => AppHost.Navigator.Navigate(AppPagesModule.TransparencyPageId));
        transparency.Margin = new Thickness(0, 0, 8, 0);
        links.Children.Add(transparency);
        links.Children.Add(AppUi.Button(L("Change history"), AppPagesModule.JournalGlyph, "Pp.Button", (_, _) => AppHost.Navigator.Navigate(AppPagesModule.JournalPageId)));
        s.Children.Add(links);
        return AppUi.Card(s);
    }
}
