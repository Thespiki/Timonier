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
        var stack = PageScaffold.Create(this, L("Paramètres de Timonier"),
            L("Apparence, sécurité et comportement de l'application. Ces options ne modifient pas Windows."), AppPagesModule.SettingsGlyph);

        AddSection(stack, "appearance", L("Apparence"), BuildAppearance());
        AddSection(stack, "behavior", L("Comportement"), BuildBehavior());
        AddSection(stack, "security", L("Sécurité"), BuildSecurity());
        AddSection(stack, "data", L("Données et réinitialisation"), BuildData());
        AddSection(stack, "about", L("À propos"), BuildAbout());
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
        theme.Add(LC("theme", "Système"), "");
        theme.Add(LC("theme", "Clair"), "");
        theme.Add(LC("theme", "Sombre"), "");
        theme.Select(S.Theme switch { ThemePreference.Light => 1, ThemePreference.Dark => 2, _ => 0 }, notify: false);
        theme.SelectionChanged += (_, i) =>
        {
            SettingsStore.Update(s => s.Theme = i switch { 1 => ThemePreference.Light, 2 => ThemePreference.Dark, _ => ThemePreference.System });
            ThemeManager.Apply();
        };

        var animations = BoundToggle(L("Réduire les animations"), S.ReduceAnimations, (s, v) => s.ReduceAnimations = v);

        return RowsCard(
            AppUi.SettingRow("", L("Thème de Timonier"),
                L("« Système » suit le mode clair ou sombre choisi dans Paramètres Windows › Personnalisation › Couleurs. N'affecte que Timonier."), theme),
            AppUi.SettingRow("", L("Réduire les animations"),
                L("Supprime les fondus lors des changements de page : interface plus sobre, un peu plus légère sur les PC modestes."), animations));
    }

    // ------------------------------------------------------------------ Comportement

    private UIElement BuildBehavior()
    {
        var advanced = BoundToggle(L("Mode avancé"), S.AdvancedMode, (s, v) => s.AdvancedMode = v, on =>
            AppHost.Toasts.Show(on
                ? L("Mode avancé activé. Les pages déjà ouvertes l'afficheront au prochain lancement de Timonier.")
                : L("Mode avancé désactivé. Les pages déjà ouvertes seront mises à jour au prochain lancement de Timonier."), ToastKind.Info));

        var confirm = BoundToggle(L("Confirmer avant les actions administrateur"), S.ConfirmBeforeAdminActions, (s, v) => s.ConfirmBeforeAdminActions = v);

        var background = BoundToggle(L("Rester actif en arrière-plan si nécessaire"), S.AllowBackground, (s, v) => s.AllowBackground = v);

        var startup = AppUi.Toggle(L("Démarrer avec Windows"), StartupRegistration.IsEnabled());
        var suppress = false;
        async void StartupChanged(bool on)
        {
            if (suppress) return;
            try
            {
                await Task.Run(() => { if (on) StartupRegistration.Enable(); else StartupRegistration.Disable(); });
                SettingsStore.Update(s => s.StartWithWindows = on);
                AppHost.Toasts.Show(on ? L("Timonier démarrera avec Windows, dans la zone de notification.") : L("Timonier ne démarrera plus avec Windows."), ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Error("AppPages", "démarrage avec Windows", ex);
                AppHost.Toasts.Show(L("Impossible de modifier le démarrage automatique : {0}", ex.Message), ToastKind.Error);
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
            AppUi.SettingRow("", L("Mode avancé"),
                L("Affiche aussi les réglages réservés aux utilisateurs avertis (risque plus élevé). Les pages déjà ouvertes en tiennent compte au prochain lancement."), advanced),
            AppUi.SettingRow("", L("Expliquer avant de demander les droits administrateur"),
                L("Avant l'invite UAC, Timonier indique pourquoi les droits sont nécessaires et combien de temps la session reste ouverte. L'invite UAC et les confirmations des actions sensibles restent toujours affichées."), confirm),
            AppUi.SettingRow("", L("Rester actif en arrière-plan si nécessaire"),
                L("Uniquement quand une fonction en cours l'exige (accès guidé, tâche en cours…). Sans raison, fermer la fenêtre quitte complètement Timonier."), background),
            AppUi.SettingRow("", L("Démarrer avec Windows"),
                L("Lance Timonier discrètement dans la zone de notification à l'ouverture de votre session. Utile surtout si vous utilisez l'accès guidé ; sinon, il occupe un peu de mémoire sans bénéfice."), startup, _startupStatus));
    }

    private void UpdateStartupStatus()
    {
        if (_startupStatus is null) return;
        string text;
        if (!StartupRegistration.IsEnabled()) text = L("État actuel : désactivé (aucune entrée « Timonier » dans HKCU\\…\\CurrentVersion\\Run).");
        else if (StartupRegistration.DisabledByTaskManager()) text = L("État actuel : entrée présente mais désactivée depuis le Gestionnaire des tâches. Réactivez l'interrupteur pour la rétablir.");
        else if (StartupRegistration.PointsElsewhere()) text = L("État actuel : activé, mais l'entrée lance une autre copie de Timonier. Désactivez puis réactivez pour utiliser celle-ci.");
        else text = L("État actuel : activé (entrée « Timonier » dans HKCU\\…\\CurrentVersion\\Run).");
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
            pinButtons.Children.Add(AppUi.Button(L("Modifier"), "", "Pp.Button", async (_, _) => await ChangePinAsync()));
            var remove = AppUi.Button(L("Supprimer"), "", "Pp.SubtleButton", async (_, _) => await RemovePinAsync());
            remove.Margin = new Thickness(8, 0, 0, 0);
            pinButtons.Children.Add(remove);
        }
        else pinButtons.Children.Add(AppUi.Button(L("Définir un code"), "", "Pp.AccentButton", async (_, _) => await SetPinAsync()));
        var pinState = hasPin
            ? AppUi.Badge(L("Code actif"), "Success", "")
            : AppUi.Badge(L("Aucun code"), "Neutral");
        pinState.HorizontalAlignment = HorizontalAlignment.Left;
        pinState.Margin = new Thickness(0, 6, 0, 0);

        // Session administrateur
        var minutes = Math.Clamp(S.BrokerIdleMinutes, 1, 30);
        var value = AppUi.Text(MinutesLabel(minutes), "Pp.Body", wrap: false);
        value.Width = 64;
        value.TextAlignment = TextAlignment.Right;
        value.Margin = new Thickness(0, 0, 10, 0);
        var slider = new Slider { Minimum = 1, Maximum = 30, Value = minutes, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 220, SmallChange = 1, LargeChange = 5 };
        System.Windows.Automation.AutomationProperties.SetName(slider, L("Délai de fermeture automatique de la session administrateur, en minutes"));
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
        _closeBroker = AppUi.Button(L("Fermer maintenant"), "", "Pp.Button", async (_, _) => await CloseBrokerAsync());
        UpdateBrokerStatus();

        return RowsCard(
            AppUi.SettingRow("", L("Code PIN de Timonier"),
                L("Demandé à chaque ouverture de Timonier (au moins 4 caractères). Protection légère contre l'usage par une autre personne sur votre session : elle ne remplace pas le mot de passe de votre compte Windows. Le code est conservé uniquement sous forme de hachage."), pinButtons, pinState),
            AppUi.SettingRow("", L("Fermeture automatique de la session administrateur"),
                L("Après ce délai sans activité, le processus administrateur se ferme et une nouvelle invite UAC sera nécessaire. S'applique à la prochaine ouverture de session administrateur."), sliderRow),
            AppUi.SettingRow("", L("Session administrateur"), null, _closeBroker, _brokerStatus));
    }

    private static string MinutesLabel(int m) => L("{0} min", m);

    private void UpdateBrokerStatus()
    {
        if (_brokerStatus is null || _closeBroker is null) return;
        var b = AppHost.Broker;
        _brokerStatus.Text = b.IsRunning
            ? b.StartedAt is { } at
                ? L("Active depuis {0}. Fermez-la dès que vous avez terminé vos modifications.", at.ToString("t", Core.Localization.Loc.Culture))
                : L("Active. Fermez-la dès que vous avez terminé vos modifications.")
            : L("Inactive : Timonier fonctionne actuellement avec vos droits d'utilisateur standard.");
        _closeBroker.IsEnabled = b.IsRunning && !_busy;
    }

    private async Task CloseBrokerAsync()
    {
        if (_closeBroker is null) return;
        _closeBroker.IsEnabled = false;
        try
        {
            await AppHost.Broker.StopAsync();
            AppHost.Toasts.Show(L("Session administrateur fermée."), ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error("AppPages", "fermeture du broker", ex);
            AppHost.Toasts.Show(L("Impossible de fermer la session administrateur : {0}", ex.Message), ToastKind.Error);
        }
        UpdateBrokerStatus();
    }

    // ------------------------------------------------------------------ Code PIN

    private static string? ValidateNewPin(string pin)
    {
        if (pin.Length < MinPinLength) return L("Le code doit contenir au moins {0} caractères.", MinPinLength);
        if (pin.Length > MaxPinLength) return L("Le code ne peut pas dépasser {0} caractères.", MaxPinLength);
        if (pin.Any(char.IsControl)) return L("Le code contient des caractères non autorisés.");
        return null;
    }

    /// <summary>Demande le code actuel et le vérifie (PBKDF2, hors du thread UI). Faux si annulé ou incorrect.</summary>
    private async Task<bool> VerifyCurrentPinAsync(string title)
    {
        var current = await AppHost.Dialogs.PromptAsync(title, L("Saisissez le code actuel de Timonier."), null, password: true,
            v => v.Length == 0 ? L("Saisissez le code.") : null);
        if (current is null) return false;
        var stored = S.AppPinHash;
        var ok = await Task.Run(() => PinHasher.Verify(current, stored));
        if (!ok) AppHost.Toasts.Show(L("Code actuel incorrect."), ToastKind.Error);
        return ok;
    }

    private async Task<string?> AskNewPinAsync(string title)
    {
        var pin = await AppHost.Dialogs.PromptAsync(title,
            L("Choisissez un code d'au moins {0} caractères (chiffres, lettres ou symboles). Il sera demandé à chaque ouverture de Timonier.", MinPinLength),
            null, password: true, ValidateNewPin);
        if (pin is null) return null;
        var again = await AppHost.Dialogs.PromptAsync(title, L("Saisissez le même code une seconde fois pour confirmer."), null, password: true,
            v => v == pin ? null : L("Les deux codes ne correspondent pas."));
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
            if (await AskNewPinAsync(L("Définir un code PIN")) is { } pin)
                await SavePinAsync(pin, L("Code PIN défini. Il sera demandé à la prochaine ouverture de Timonier."));
        }
        finally { _busy = false; }
    }

    private async Task ChangePinAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (!await VerifyCurrentPinAsync(L("Modifier le code PIN"))) return;
            if (await AskNewPinAsync(L("Nouveau code PIN")) is { } pin)
                await SavePinAsync(pin, L("Code PIN modifié."));
        }
        finally { _busy = false; }
    }

    private async Task RemovePinAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (!await VerifyCurrentPinAsync(L("Supprimer le code PIN"))) return;
            await SavePinAsync(null, L("Code PIN supprimé : Timonier s'ouvrira sans code."));
        }
        finally { _busy = false; }
    }

    // ------------------------------------------------------------------ Données

    private UIElement BuildData()
    {
        var folders = new StackPanel { Orientation = Orientation.Horizontal };
        folders.Children.Add(AppUi.Button(L("Données"), "", "Pp.Button", (_, _) => TransparencyPage.OpenFolder(AppPaths.LocalData)));
        var logs = AppUi.Button(L("Journaux de diagnostic"), "", "Pp.Button", (_, _) => TransparencyPage.OpenFolder(AppPaths.Logs));
        logs.Margin = new Thickness(8, 0, 0, 0);
        folders.Children.Add(logs);

        var reset = AppUi.Button(L("Réinitialiser…"), "", "Pp.DangerButton", async (_, _) => await ResetAsync());

        var privacy = AppUi.Button(L("Voir le détail"), "", "Pp.Button", (_, _) => AppHost.Navigator.Navigate(AppPagesModule.TransparencyPageId, "tab:security"));

        return RowsCard(
            AppUi.SettingRow("", L("Dossiers de Timonier"),
                L("Préférences, journal, cache et journaux de diagnostic sont rangés dans votre profil (AppData\\Local\\Timonier). Rien n'est envoyé hors de ce PC."), folders),
            AppUi.SettingRow("", L("Données conservées"), L("Liste des fichiers et clés de registre utilisés, avec leur taille."), privacy),
            AppUi.SettingRow("", L("Réinitialiser Timonier"),
                L("Rétablit les préférences par défaut (thème, options, délai, code PIN, démarrage avec Windows) et vide le cache. Le journal des modifications est conservé : vous pourrez toujours annuler."), reset));
    }

    private async Task ResetAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var content = new StackPanel { MaxWidth = 520 };
            content.Children.Add(AppUi.Text(L("Les éléments suivants reviendront à leur état par défaut :")));
            content.Children.Add(AppUi.Bullets(
                L("Thème, animations, mode avancé, confirmations et fonctionnement en arrière-plan"),
                L("Délai de la session administrateur (5 min)"),
                L("Code PIN de Timonier (supprimé)"),
                L("Démarrage avec Windows (désactivé)"),
                L("Cache : portrait matériel et données temporaires (recréés automatiquement)")));
            var kept = AppUi.Text(L("Sont conservés :"));
            kept.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(kept);
            content.Children.Add(AppUi.Bullets(
                L("Le journal des modifications (utilisateur et administrateur), pour pouvoir toujours annuler"),
                L("Les informations nécessaires pour retirer une configuration en place (mode kiosque, accès guidé et son code, fond d'écran précédent…)"),
                L("Toutes les modifications déjà appliquées à Windows : la réinitialisation ne touche qu'à Timonier")));

            if (!await AppHost.Dialogs.ShowAsync(L("Réinitialiser Timonier ?"), content, L("Réinitialiser"), L("Annuler"), danger: true)) return;
            if (!string.IsNullOrEmpty(S.AppPinHash) && !await VerifyCurrentPinAsync(L("Réinitialiser Timonier"))) return;

            SettingsStore.Reset();
            AppHost.Broker.IdleMinutes = S.BrokerIdleMinutes;
            ThemeManager.Apply();

            var errors = await Task.Run(() =>
            {
                var list = new List<string>();
                try { if (StartupRegistration.IsEnabled()) StartupRegistration.Disable(); }
                catch (Exception ex) { list.Add(L("démarrage avec Windows : {0}", ex.Message)); }
                try { if (File.Exists(DataInventory.ProfileCacheFile)) File.Delete(DataInventory.ProfileCacheFile); }
                catch (Exception ex) { list.Add(L("portrait matériel : {0}", ex.Message)); }
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
                catch (Exception ex) { list.Add(L("cache : {0}", ex.Message)); }
                return list;
            });
            Log.Info("AppPages", "réinitialisation des préférences" + (errors.Count > 0 ? " (partielle) : " + string.Join(" ; ", errors) : ""));
            if (errors.Count == 0) AppHost.Toasts.Show(L("Timonier a été réinitialisé. Le journal des modifications est conservé."), ToastKind.Success);
            else AppHost.Toasts.Show(L("Réinitialisation partielle : {0}", string.Join(" ; ", errors)), ToastKind.Warning);
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
            ?? asm.GetName().Version?.ToString() ?? LC("version", "inconnue");

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var tile = AppUi.GlyphTile("", size: 48);
        tile.Margin = new Thickness(0, 0, 14, 0);
        head.Children.Add(tile);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(AppUi.Text("Timonier", "Pp.CardTitle", wrap: false));
        names.Children.Add(AppUi.Caption(L("Version {0}", version)));
        head.Children.Add(names);
        var badges = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        var local = AppUi.Badge(L("100 % local"), "Success", "");
        local.Margin = new Thickness(0, 0, 6, 0);
        badges.Children.Add(local);
        badges.Children.Add(AppUi.Badge(L("Aucune télémétrie"), "Success", ""));
        head.Children.Add(badges);

        var s = new StackPanel();
        s.Children.Add(head);
        s.Children.Add(PageScaffold.KeyValue(".NET", RuntimeInformation.FrameworkDescription));
        s.Children.Add(PageScaffold.KeyValue("Windows", AppHost.Profile.WindowsLabel));
        s.Children.Add(PageScaffold.KeyValue(L("Édition"), AppHost.Profile.EditionLabel));
        s.Children.Add(PageScaffold.KeyValue(L("Architecture"), RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()));
        string exe;
        try { exe = AppPaths.ExecutablePath; } catch { exe = LC("location", "inconnu"); }
        s.Children.Add(PageScaffold.KeyValue(L("Emplacement"), exe));
        s.Children.Add(PageScaffold.KeyValue(L("Confidentialité"), L("Timonier ne collecte ni n'envoie aucune donnée. Il n'a ni compte, ni publicité, ni mise à jour automatique.")));

        var keysTitle = AppUi.Text(L("Raccourcis clavier"), "Pp.Body");
        keysTitle.FontWeight = FontWeights.SemiBold;
        keysTitle.Margin = new Thickness(0, 16, 0, 6);
        s.Children.Add(keysTitle);
        (string Keys, string Text)[] shortcuts =
        [
            ("Ctrl+K · Ctrl+F · F3", L("Rechercher un réglage, une page ou une fonction")),
            ("↑ · ↓", L("Parcourir les résultats de recherche")),
            (LC("key", "Entrée"), L("Ouvrir le résultat sélectionné")),
            (LC("key", "Échap"), L("Fermer la recherche ou la boîte de dialogue")),
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
        var transparency = AppUi.Button(L("Transparence"), AppPagesModule.TransparencyGlyph, "Pp.Button", (_, _) => AppHost.Navigator.Navigate(AppPagesModule.TransparencyPageId));
        transparency.Margin = new Thickness(0, 0, 8, 0);
        links.Children.Add(transparency);
        links.Children.Add(AppUi.Button(L("Journal des modifications"), AppPagesModule.JournalGlyph, "Pp.Button", (_, _) => AppHost.Navigator.Navigate(AppPagesModule.JournalPageId)));
        s.Children.Add(links);
        return AppUi.Card(s);
    }
}
