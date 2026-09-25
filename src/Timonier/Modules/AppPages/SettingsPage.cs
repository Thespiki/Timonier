using System.Globalization;
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
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

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
        var stack = PageScaffold.Create(this, "Paramètres de Timonier",
            "Apparence, sécurité et comportement de l'application. Ces options ne modifient pas Windows.", AppPagesModule.SettingsGlyph);

        AddSection(stack, "appearance", "Apparence", BuildAppearance());
        AddSection(stack, "behavior", "Comportement", BuildBehavior());
        AddSection(stack, "security", "Sécurité", BuildSecurity());
        AddSection(stack, "data", "Données et réinitialisation", BuildData());
        AddSection(stack, "about", "À propos", BuildAbout());
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
        theme.Add("Système", "");
        theme.Add("Clair", "");
        theme.Add("Sombre", "");
        theme.Select(S.Theme switch { ThemePreference.Light => 1, ThemePreference.Dark => 2, _ => 0 }, notify: false);
        theme.SelectionChanged += (_, i) =>
        {
            SettingsStore.Update(s => s.Theme = i switch { 1 => ThemePreference.Light, 2 => ThemePreference.Dark, _ => ThemePreference.System });
            ThemeManager.Apply();
        };

        var animations = BoundToggle("Réduire les animations", S.ReduceAnimations, (s, v) => s.ReduceAnimations = v);

        return RowsCard(
            AppUi.SettingRow("", "Thème de Timonier",
                "« Système » suit le mode clair ou sombre choisi dans Paramètres Windows › Personnalisation › Couleurs. N'affecte que Timonier.", theme),
            AppUi.SettingRow("", "Réduire les animations",
                "Supprime les fondus lors des changements de page : interface plus sobre, un peu plus légère sur les PC modestes.", animations));
    }

    // ------------------------------------------------------------------ Comportement

    private UIElement BuildBehavior()
    {
        var advanced = BoundToggle("Mode avancé", S.AdvancedMode, (s, v) => s.AdvancedMode = v, on =>
            AppHost.Toasts.Show(on
                ? "Mode avancé activé. Les pages déjà ouvertes l'afficheront au prochain lancement de Timonier."
                : "Mode avancé désactivé. Les pages déjà ouvertes seront mises à jour au prochain lancement de Timonier.", ToastKind.Info));

        var confirm = BoundToggle("Confirmer avant les actions administrateur", S.ConfirmBeforeAdminActions, (s, v) => s.ConfirmBeforeAdminActions = v);

        var background = BoundToggle("Rester actif en arrière-plan si nécessaire", S.AllowBackground, (s, v) => s.AllowBackground = v);

        var startup = AppUi.Toggle("Démarrer avec Windows", StartupRegistration.IsEnabled());
        var suppress = false;
        async void StartupChanged(bool on)
        {
            if (suppress) return;
            try
            {
                await Task.Run(() => { if (on) StartupRegistration.Enable(); else StartupRegistration.Disable(); });
                SettingsStore.Update(s => s.StartWithWindows = on);
                AppHost.Toasts.Show(on ? "Timonier démarrera avec Windows, dans la zone de notification." : "Timonier ne démarrera plus avec Windows.", ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Error("AppPages", "démarrage avec Windows", ex);
                AppHost.Toasts.Show("Impossible de modifier le démarrage automatique : " + ex.Message, ToastKind.Error);
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
            AppUi.SettingRow("", "Mode avancé",
                "Affiche aussi les réglages réservés aux utilisateurs avertis (risque plus élevé). Les pages déjà ouvertes en tiennent compte au prochain lancement.", advanced),
            AppUi.SettingRow("", "Expliquer avant de demander les droits administrateur",
                "Avant l'invite UAC, Timonier indique pourquoi les droits sont nécessaires et combien de temps la session reste ouverte. "
                + "L'invite UAC et les confirmations des actions sensibles restent toujours affichées.", confirm),
            AppUi.SettingRow("", "Rester actif en arrière-plan si nécessaire",
                "Uniquement quand une fonction en cours l'exige (accès guidé, tâche en cours…). Sans raison, fermer la fenêtre quitte complètement Timonier.", background),
            AppUi.SettingRow("", "Démarrer avec Windows",
                "Lance Timonier discrètement dans la zone de notification à l'ouverture de votre session. Utile surtout si vous utilisez l'accès guidé ; "
                + "sinon, il occupe un peu de mémoire sans bénéfice.", startup, _startupStatus));
    }

    private void UpdateStartupStatus()
    {
        if (_startupStatus is null) return;
        string text;
        if (!StartupRegistration.IsEnabled()) text = "État actuel : désactivé (aucune entrée « Timonier » dans HKCU\\…\\CurrentVersion\\Run).";
        else if (StartupRegistration.DisabledByTaskManager()) text = "État actuel : entrée présente mais désactivée depuis le Gestionnaire des tâches. Réactivez l'interrupteur pour la rétablir.";
        else if (StartupRegistration.PointsElsewhere()) text = "État actuel : activé, mais l'entrée lance une autre copie de Timonier. Désactivez puis réactivez pour utiliser celle-ci.";
        else text = "État actuel : activé (entrée « Timonier » dans HKCU\\…\\CurrentVersion\\Run).";
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
            pinButtons.Children.Add(AppUi.Button("Modifier", "", "Pp.Button", async (_, _) => await ChangePinAsync()));
            var remove = AppUi.Button("Supprimer", "", "Pp.SubtleButton", async (_, _) => await RemovePinAsync());
            remove.Margin = new Thickness(8, 0, 0, 0);
            pinButtons.Children.Add(remove);
        }
        else pinButtons.Children.Add(AppUi.Button("Définir un code", "", "Pp.AccentButton", async (_, _) => await SetPinAsync()));
        var pinState = hasPin
            ? AppUi.Badge("Code actif", "Success", "")
            : AppUi.Badge("Aucun code", "Neutral");
        pinState.HorizontalAlignment = HorizontalAlignment.Left;
        pinState.Margin = new Thickness(0, 6, 0, 0);

        // Session administrateur
        var minutes = Math.Clamp(S.BrokerIdleMinutes, 1, 30);
        var value = AppUi.Text(MinutesLabel(minutes), "Pp.Body", wrap: false);
        value.Width = 64;
        value.TextAlignment = TextAlignment.Right;
        value.Margin = new Thickness(0, 0, 10, 0);
        var slider = new Slider { Minimum = 1, Maximum = 30, Value = minutes, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 220, SmallChange = 1, LargeChange = 5 };
        System.Windows.Automation.AutomationProperties.SetName(slider, "Délai de fermeture automatique de la session administrateur, en minutes");
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
        _closeBroker = AppUi.Button("Fermer maintenant", "", "Pp.Button", async (_, _) => await CloseBrokerAsync());
        UpdateBrokerStatus();

        return RowsCard(
            AppUi.SettingRow("", "Code PIN de Timonier",
                "Demandé à chaque ouverture de Timonier (au moins 4 caractères). Protection légère contre l'usage par une autre personne sur votre session : "
                + "elle ne remplace pas le mot de passe de votre compte Windows. Le code est conservé uniquement sous forme de hachage.", pinButtons, pinState),
            AppUi.SettingRow("", "Fermeture automatique de la session administrateur",
                "Après ce délai sans activité, le processus administrateur se ferme et une nouvelle invite UAC sera nécessaire. "
                + "S'applique à la prochaine ouverture de session administrateur.", sliderRow),
            AppUi.SettingRow("", "Session administrateur", null, _closeBroker, _brokerStatus));
    }

    private static string MinutesLabel(int m) => m <= 1 ? "1 min" : $"{m} min";

    private void UpdateBrokerStatus()
    {
        if (_brokerStatus is null || _closeBroker is null) return;
        var b = AppHost.Broker;
        _brokerStatus.Text = b.IsRunning
            ? $"Active{(b.StartedAt is { } at ? " depuis " + at.ToString("HH:mm", Fr) : "")}. Fermez-la dès que vous avez terminé vos modifications."
            : "Inactive : Timonier fonctionne actuellement avec vos droits d'utilisateur standard.";
        _closeBroker.IsEnabled = b.IsRunning && !_busy;
    }

    private async Task CloseBrokerAsync()
    {
        if (_closeBroker is null) return;
        _closeBroker.IsEnabled = false;
        try
        {
            await AppHost.Broker.StopAsync();
            AppHost.Toasts.Show("Session administrateur fermée.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error("AppPages", "fermeture du broker", ex);
            AppHost.Toasts.Show("Impossible de fermer la session administrateur : " + ex.Message, ToastKind.Error);
        }
        UpdateBrokerStatus();
    }

    // ------------------------------------------------------------------ Code PIN

    private static string? ValidateNewPin(string pin)
    {
        if (pin.Length < MinPinLength) return $"Le code doit contenir au moins {MinPinLength} caractères.";
        if (pin.Length > MaxPinLength) return $"Le code ne peut pas dépasser {MaxPinLength} caractères.";
        if (pin.Any(char.IsControl)) return "Le code contient des caractères non autorisés.";
        return null;
    }

    /// <summary>Demande le code actuel et le vérifie (PBKDF2, hors du thread UI). Faux si annulé ou incorrect.</summary>
    private async Task<bool> VerifyCurrentPinAsync(string title)
    {
        var current = await AppHost.Dialogs.PromptAsync(title, "Saisissez le code actuel de Timonier.", null, password: true,
            v => v.Length == 0 ? "Saisissez le code." : null);
        if (current is null) return false;
        var stored = S.AppPinHash;
        var ok = await Task.Run(() => PinHasher.Verify(current, stored));
        if (!ok) AppHost.Toasts.Show("Code actuel incorrect.", ToastKind.Error);
        return ok;
    }

    private async Task<string?> AskNewPinAsync(string title)
    {
        var pin = await AppHost.Dialogs.PromptAsync(title,
            $"Choisissez un code d'au moins {MinPinLength} caractères (chiffres, lettres ou symboles). Il sera demandé à chaque ouverture de Timonier.",
            null, password: true, ValidateNewPin);
        if (pin is null) return null;
        var again = await AppHost.Dialogs.PromptAsync(title, "Saisissez le même code une seconde fois pour confirmer.", null, password: true,
            v => v == pin ? null : "Les deux codes ne correspondent pas.");
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
            if (await AskNewPinAsync("Définir un code PIN") is { } pin)
                await SavePinAsync(pin, "Code PIN défini. Il sera demandé à la prochaine ouverture de Timonier.");
        }
        finally { _busy = false; }
    }

    private async Task ChangePinAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (!await VerifyCurrentPinAsync("Modifier le code PIN")) return;
            if (await AskNewPinAsync("Nouveau code PIN") is { } pin)
                await SavePinAsync(pin, "Code PIN modifié.");
        }
        finally { _busy = false; }
    }

    private async Task RemovePinAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (!await VerifyCurrentPinAsync("Supprimer le code PIN")) return;
            await SavePinAsync(null, "Code PIN supprimé : Timonier s'ouvrira sans code.");
        }
        finally { _busy = false; }
    }

    // ------------------------------------------------------------------ Données

    private UIElement BuildData()
    {
        var folders = new StackPanel { Orientation = Orientation.Horizontal };
        folders.Children.Add(AppUi.Button("Données", "", "Pp.Button", (_, _) => TransparencyPage.OpenFolder(AppPaths.LocalData)));
        var logs = AppUi.Button("Journaux de diagnostic", "", "Pp.Button", (_, _) => TransparencyPage.OpenFolder(AppPaths.Logs));
        logs.Margin = new Thickness(8, 0, 0, 0);
        folders.Children.Add(logs);

        var reset = AppUi.Button("Réinitialiser…", "", "Pp.DangerButton", async (_, _) => await ResetAsync());

        var privacy = AppUi.Button("Voir le détail", "", "Pp.Button", (_, _) => AppHost.Navigator.Navigate(AppPagesModule.TransparencyPageId, "tab:security"));

        return RowsCard(
            AppUi.SettingRow("", "Dossiers de Timonier",
                "Préférences, journal, cache et journaux de diagnostic sont rangés dans votre profil (AppData\\Local\\Timonier). Rien n'est envoyé hors de ce PC.", folders),
            AppUi.SettingRow("", "Données conservées", "Liste des fichiers et clés de registre utilisés, avec leur taille.", privacy),
            AppUi.SettingRow("", "Réinitialiser Timonier",
                "Rétablit les préférences par défaut (thème, options, délai, code PIN, démarrage avec Windows) et vide le cache. "
                + "Le journal des modifications est conservé : vous pourrez toujours annuler.", reset));
    }

    private async Task ResetAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var content = new StackPanel { MaxWidth = 520 };
            content.Children.Add(AppUi.Text("Les éléments suivants reviendront à leur état par défaut :"));
            content.Children.Add(AppUi.Bullets(
                "Thème, animations, mode avancé, confirmations et fonctionnement en arrière-plan",
                "Délai de la session administrateur (5 min)",
                "Code PIN de Timonier (supprimé)",
                "Démarrage avec Windows (désactivé)",
                "Cache : portrait matériel et données temporaires (recréés automatiquement)"));
            var kept = AppUi.Text("Sont conservés :");
            kept.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(kept);
            content.Children.Add(AppUi.Bullets(
                "Le journal des modifications (utilisateur et administrateur), pour pouvoir toujours annuler",
                "Les informations nécessaires pour retirer une configuration en place (mode kiosque, accès guidé et son code, fond d'écran précédent…)",
                "Toutes les modifications déjà appliquées à Windows : la réinitialisation ne touche qu'à Timonier"));

            if (!await AppHost.Dialogs.ShowAsync("Réinitialiser Timonier ?", content, "Réinitialiser", "Annuler", danger: true)) return;
            if (!string.IsNullOrEmpty(S.AppPinHash) && !await VerifyCurrentPinAsync("Réinitialiser Timonier")) return;

            var defaults = new AppSettings();
            SettingsStore.Update(s =>
            {
                s.Theme = defaults.Theme;
                s.AdvancedMode = defaults.AdvancedMode;
                s.BrokerIdleMinutes = defaults.BrokerIdleMinutes;
                s.AllowBackground = defaults.AllowBackground;
                s.StartWithWindows = false;
                s.AppPinHash = null;
                s.ConfirmBeforeAdminActions = defaults.ConfirmBeforeAdminActions;
                s.ReduceAnimations = defaults.ReduceAnimations;
            });
            AppHost.Broker.IdleMinutes = defaults.BrokerIdleMinutes;
            ThemeManager.Apply();

            var errors = await Task.Run(() =>
            {
                var list = new List<string>();
                try { if (StartupRegistration.IsEnabled()) StartupRegistration.Disable(); }
                catch (Exception ex) { list.Add("démarrage avec Windows : " + ex.Message); }
                try { if (File.Exists(DataInventory.ProfileCacheFile)) File.Delete(DataInventory.ProfileCacheFile); }
                catch (Exception ex) { list.Add("portrait matériel : " + ex.Message); }
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
                catch (Exception ex) { list.Add("cache : " + ex.Message); }
                return list;
            });
            Log.Info("AppPages", "réinitialisation des préférences" + (errors.Count > 0 ? " (partielle) : " + string.Join(" ; ", errors) : ""));
            if (errors.Count == 0) AppHost.Toasts.Show("Timonier a été réinitialisé. Le journal des modifications est conservé.", ToastKind.Success);
            else AppHost.Toasts.Show("Réinitialisation partielle : " + string.Join(" ; ", errors), ToastKind.Warning);
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
            ?? asm.GetName().Version?.ToString() ?? "inconnue";

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var tile = AppUi.GlyphTile("", size: 48);
        tile.Margin = new Thickness(0, 0, 14, 0);
        head.Children.Add(tile);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(AppUi.Text("Timonier", "Pp.CardTitle", wrap: false));
        names.Children.Add(AppUi.Caption($"Version {version}"));
        head.Children.Add(names);
        var badges = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        var local = AppUi.Badge("100 % local", "Success", "");
        local.Margin = new Thickness(0, 0, 6, 0);
        badges.Children.Add(local);
        badges.Children.Add(AppUi.Badge("Aucune télémétrie", "Success", ""));
        head.Children.Add(badges);

        var s = new StackPanel();
        s.Children.Add(head);
        s.Children.Add(PageScaffold.KeyValue(".NET", RuntimeInformation.FrameworkDescription));
        s.Children.Add(PageScaffold.KeyValue("Windows", AppHost.Profile.WindowsLabel));
        s.Children.Add(PageScaffold.KeyValue("Édition", AppHost.Profile.EditionLabel));
        s.Children.Add(PageScaffold.KeyValue("Architecture", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()));
        string exe;
        try { exe = AppPaths.ExecutablePath; } catch { exe = "inconnu"; }
        s.Children.Add(PageScaffold.KeyValue("Emplacement", exe));
        s.Children.Add(PageScaffold.KeyValue("Confidentialité", "Timonier ne collecte ni n'envoie aucune donnée. Il n'a ni compte, ni publicité, ni mise à jour automatique."));

        var keysTitle = AppUi.Text("Raccourcis clavier", "Pp.Body");
        keysTitle.FontWeight = FontWeights.SemiBold;
        keysTitle.Margin = new Thickness(0, 16, 0, 6);
        s.Children.Add(keysTitle);
        (string Keys, string Text)[] shortcuts =
        [
            ("Ctrl+K · Ctrl+F · F3", "Rechercher un réglage, une page ou une fonction"),
            ("↑ · ↓", "Parcourir les résultats de recherche"),
            ("Entrée", "Ouvrir le résultat sélectionné"),
            ("Échap", "Fermer la recherche ou la boîte de dialogue"),
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
        var transparency = AppUi.Button("Transparence", AppPagesModule.TransparencyGlyph, "Pp.Button", (_, _) => AppHost.Navigator.Navigate(AppPagesModule.TransparencyPageId));
        transparency.Margin = new Thickness(0, 0, 8, 0);
        links.Children.Add(transparency);
        links.Children.Add(AppUi.Button("Journal des modifications", AppPagesModule.JournalGlyph, "Pp.Button", (_, _) => AppHost.Navigator.Navigate(AppPagesModule.JournalPageId)));
        s.Children.Add(links);
        return AppUi.Card(s);
    }
}
