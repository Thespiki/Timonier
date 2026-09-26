using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;
using Timonier.Core.Settings;
using Timonier.UI.Controls;
using Timonier.UI.Services;
using static Timonier.Modules.Kiosk.KioskUi;

namespace Timonier.Modules.Kiosk;

/// <summary>
/// Page « Mode kiosque » : compatibilité, assistant en 4 étapes (compte, application, options, application),
/// état et retrait, limites. Tout ce qui modifie le système passe par les actions du broker.
/// </summary>
public sealed partial class KioskPage : UserControl, INavigationAware
{
    private readonly StackPanel _stack;
    private readonly Border _statusCard = new Border().Styled("Pp.Card");
    private readonly TextBlock _wizardAnchor;
    private readonly TextBlock _statusAnchor;
    private readonly SystemProfile _profile = AppHost.Profile;
    private readonly string? _edgePath = KioskRules.EdgePath();
    private object? _pendingParameter;
    private bool _loadedOnce;

    public KioskPage()
    {
        _stack = PageScaffold.Create(this, L("Mode kiosque"),
            L("Transformez ce PC en borne dédiée à une seule application, pour un compte à part, puis revenez en arrière en un clic."), KioskModule.Glyph);

        _stack.Children.Add(PageScaffold.Section(L("Compatibilité")));
        _stack.Children.Add(BuildCompatibility());

        _wizardAnchor = PageScaffold.Section(L("Assistant"));
        _stack.Children.Add(_wizardAnchor);
        _stack.Children.Add(BuildWizard());

        _statusAnchor = PageScaffold.Section(L("État et retrait"));
        _stack.Children.Add(_statusAnchor);
        _stack.Children.Add(_statusCard);

        _stack.Children.Add(PageScaffold.Section(L("Limites à connaître")));
        _stack.Children.Add(BuildLimits());

        _mode = _profile.SupportsAssignedAccess ? KioskModes.Store : KioskModes.Win32;
        ShowStep(0);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        await RefreshStatusAsync();
        await LoadAccountsAsync();
        ApplyPendingParameter();
    }

    public void OnNavigatedTo(object? parameter)
    {
        _pendingParameter = parameter;
        if (_loadedOnce) ApplyPendingParameter();
    }

    private void ApplyPendingParameter()
    {
        var p = _pendingParameter as string;
        _pendingParameter = null;
        switch (p)
        {
            case "section:status":
                _statusAnchor.BringIntoView();
                break;
            case "mode:edge" or "mode:win32" or "mode:store":
                var mode = p[5..];
                if (mode == KioskModes.Store && !_profile.SupportsAssignedAccess) break;
                if (mode == KioskModes.Edge && _edgePath is null) break;
                _mode = mode;
                if (_account is not null) ShowStep(1);
                _wizardAnchor.BringIntoView();
                break;
            case "section:wizard":
                _wizardAnchor.BringIntoView();
                break;
        }
    }

    // ================================================================== Compatibilité

    private Border BuildCompatibility()
    {
        var p = _profile;
        var stack = new StackPanel();

        var header = Row(null, p.WindowsLabel.Length > 0 ? p.WindowsLabel : "Windows", L("Édition {0} · {1}", p.EditionLabel, (p.IsUserAdmin ? L("votre compte est administrateur") : L("votre compte n'est pas administrateur"))),
            Badge(p.SupportsAssignedAccess ? L("Accès attribué pris en charge") : L("Accès attribué indisponible"), p.SupportsAssignedAccess ? "Success" : "Warning"));
        var tile = GlyphTile("");
        tile.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(tile, Dock.Left);
        header.Children.Insert(header.Children.Count - 1, tile);
        stack.Children.Add(header);
        stack.Children.Add(Divider(new Thickness(0, 12, 0, 6)));

        stack.Children.Add(CompatRow("", L("Application du Store en plein écran"),
            p.SupportsAssignedAccess
                ? L("Accès attribué à application unique (Set-AssignedAccess) : l'application occupe tout l'écran, sans Bureau ni barre des tâches.")
                : L("L'accès attribué n'existe pas sur l'édition Famille : il faut Windows Professionnel, Entreprise ou Éducation."),
            p.SupportsAssignedAccess ? (L("Disponible"), "Success") : (L("Indisponible ici"), "Danger")));

        stack.Children.Add(CompatRow("", L("Microsoft Edge en mode kiosque"),
            _edgePath is null
                ? L("Microsoft Edge n'est pas installé pour tous les utilisateurs sur ce PC.")
                : L("Edge s'ouvre en plein écran sur votre site à la place du Bureau du compte kiosque (options officielles --kiosk d'Edge).")
                  + (p.SupportsAssignedAccess ? L(" L'assistant kiosque de Windows propose aussi Edge, via l'accès attribué.") : ""),
            _edgePath is null ? (L("Edge absent"), "Danger") : (L("Disponible"), "Success")));

        stack.Children.Add(CompatRow("", L("Application classique (.exe)"),
            L("Le programme choisi remplace le Bureau pour ce compte uniquement (stratégie « Interface utilisateur personnalisée »). C'est une alternative légère à Shell Launcher.")
            + (p.IsHomeEdition ? L(" Sur l'édition Famille, les stratégies utilisateur ne sont pas officiellement prises en charge : fonctionnement non garanti.") : ""),
            p.IsHomeEdition ? (L("Non garanti"), "Warning") : (L("Disponible"), "Success")));

        stack.Children.Add(CompatRow("", L("Shell Launcher et kiosque multi-applications"),
            p.SupportsShellLauncher
                ? L("Pris en charge par votre édition, mais configurés par fichier XML (Windows Configuration Designer, MDM/Intune) : Timonier ne les gère pas.")
                : L("Shell Launcher est réservé aux éditions Entreprise et Éducation ; le kiosque multi-applications nécessite une configuration XML ou MDM. Timonier ne les gère pas."),
            (L("Non géré"), "Neutral")));

        if (p.IsManaged)
        {
            var bar = PageScaffold.InfoBar(L("Ce PC est géré par une organisation (domaine, Microsoft Entra ou MDM) : ses stratégies peuvent remplacer ou bloquer la configuration de borne. Vérifiez avec votre service informatique."),
                "", "Pp.InfoBar.Warning");
            bar.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(bar);
        }
        if (!p.IsUserAdmin)
        {
            var bar = PageScaffold.InfoBar(L("Votre compte n'est pas administrateur : Windows demandera les identifiants d'un administrateur au moment d'appliquer."), "");
            bar.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(bar);
        }
        return PageScaffold.Card(stack);
    }

    private static DockPanel CompatRow(string glyph, string title, string description, (string Text, string Tone) badge)
    {
        var row = Row(glyph, title, description, Badge(badge.Text, badge.Tone));
        row.Margin = new Thickness(0, 8, 0, 4);
        return row;
    }

    // ================================================================== Limites

    private static Border BuildLimits()
    {
        var stack = new StackPanel();
        string[] items =
        [
            L("Ctrl+Alt+Suppr fonctionne toujours, par conception de Windows : c'est la sortie de secours. Pour quitter une session kiosque, appuyez sur Ctrl+Alt+Suppr puis choisissez « Se déconnecter »."),
            L("Testez d'abord : déconnectez-vous et ouvrez la session kiosque avant de laisser la borne en libre-service. Gardez toujours un compte administrateur dont vous connaissez le mot de passe."),
            L("Avec une application classique ou Edge comme interface, rien ne relance l'application si elle se ferme : l'écran reste noir jusqu'à la déconnexion. Choisissez une application prévue pour rester ouverte."),
            L("Les applications classiques (Win32) sous accès attribué et le kiosque multi-applications nécessitent une configuration XML (MDM, Intune, Windows Configuration Designer) : non gérés ici."),
            L("Windows Update continue d'installer les mises à jour et peut redémarrer le PC : planifiez les heures d'activité pour éviter un redémarrage pendant l'utilisation de la borne."),
            L("Les restrictions (Gestionnaire des tâches, Exécuter…) sont des stratégies utilisateur : elles gênent un utilisateur curieux, pas une personne déterminée ayant un accès physique au PC."),
        ];
        foreach (var i in items) stack.Children.Add(Bullet(i, "", "Pp.TextSecondary"));
        return PageScaffold.Card(stack);
    }

    // ================================================================== État et retrait

    private KioskState? _state;
    private AutologonInfo? _autologon;
    private readonly StackPanel _detected = new();

    private async Task RefreshStatusAsync()
    {
        _statusCard.Child = State("", L("Lecture de la configuration…"), busy: true);
        try
        {
            (_state, _autologon) = await Task.Run(() => (KioskState.Read(), AutologonInfo.Read()));
            RenderStatus();
        }
        catch (Exception ex)
        {
            Log.Error("Kiosk", "lecture de l'état", ex);
            _statusCard.Child = State("", L("Impossible de lire la configuration"), ex.Message);
        }
    }

    private void RenderStatus()
    {
        var stack = new StackPanel();
        var s = _state;
        var al = _autologon ?? new AutologonInfo(false, null, null, false);

        if (s is { IsConfigured: true })
        {
            var head = Row(null, L("Borne configurée pour « {0} »", s.User), KioskModes.Label(s.Mode), Badge(LC("state", "Active"), "Success",""));
            var tile = GlyphTile(KioskModule.Glyph, "Pp.SuccessBackground", "Pp.Success");
            tile.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(tile, Dock.Left);
            head.Children.Insert(head.Children.Count - 1, tile);
            stack.Children.Add(head);
            stack.Children.Add(Divider(new Thickness(0, 12, 0, 8)));
            stack.Children.Add(PageScaffold.KeyValue(L("Application"), TargetLabel(s)));
            var keys = s.RestrictionKeys.Select(k => KioskRestrictions.Get(k)?.Title).Where(t => t is not null).ToList();
            stack.Children.Add(PageScaffold.KeyValue(L("Restrictions du compte"), keys.Count == 0 ? L("Aucune") : string.Join(" · ", keys)));
            if (s.AppliedAt is { } at) stack.Children.Add(PageScaffold.KeyValue(L("Configurée le"), Format.Date(at)));
        }
        else
        {
            var head = Row(null, L("Aucune borne configurée par Timonier"),
                L("Utilisez l'assistant ci-dessus. Une borne créée avec les Paramètres de Windows peut être détectée avec « Vérifier dans Windows »."));
            var tile = GlyphTile(KioskModule.Glyph, "Pp.NeutralBackground", "Pp.Neutral");
            tile.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(tile, Dock.Left);
            head.Children.Insert(head.Children.Count - 1, tile);
            stack.Children.Add(head);
            stack.Children.Add(Divider(new Thickness(0, 12, 0, 8)));
        }

        stack.Children.Add(PageScaffold.KeyValue(L("Ouverture de session automatique"),
            al.Enabled ? L("Activée pour « {0} »", al.User) : L("Désactivée")));
        if (al.PlainPasswordInRegistry)
        {
            var bar = PageScaffold.InfoBar(L("Un mot de passe est stocké en clair dans le registre (valeur Winlogon « DefaultPassword »). Désactiver l'ouverture automatique le supprime."),
                "", "Pp.InfoBar.Warning");
            bar.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(bar);
        }

        stack.Children.Add(_detected);

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var remove = Button(L("Désactiver le mode kiosque"), "", "Pp.DangerButton", async (b, _) => await RemoveAsync((Button)b));
        remove.IsEnabled = s is { IsConfigured: true } || al.Enabled || _detectedAssigned;
        remove.Margin = new Thickness(0, 0, 8, 6);
        actions.Children.Add(remove);
        if (al.Enabled)
        {
            var clear = Button(L("Désactiver l'ouverture automatique"), "", "Pp.Button", async (b, _) => await ClearAutologonAsync((Button)b));
            clear.Margin = new Thickness(0, 0, 8, 6);
            actions.Children.Add(clear);
        }
        var check = Button(L("Vérifier dans Windows"), "", "Pp.Button", async (b, _) => await DetectAsync((Button)b));
        check.ToolTip = L("Lit la configuration d'accès attribué de Windows (nécessite les droits administrateur).");
        check.Margin = new Thickness(0, 0, 8, 6);
        actions.Children.Add(check);
        var settings = Button(L("Kiosque dans les Paramètres"), "", "Pp.SubtleButton", (_, _) => OpenAssignedAccessSettings());
        settings.Margin = new Thickness(0, 0, 8, 6);
        actions.Children.Add(settings);
        var refresh = IconButton("", L("Actualiser"), async (_, _) => await RefreshStatusAsync());
        refresh.Margin = new Thickness(0, 0, 0, 6);
        actions.Children.Add(refresh);
        stack.Children.Add(actions);

        _statusCard.Child = stack;
    }

    private static string TargetLabel(KioskState s) => s.Mode switch
    {
        KioskModes.Edge => L("Microsoft Edge sur {0}", s.Target),
        KioskModes.Win32 => Path.GetFileName(s.Target ?? "") is { Length: > 0 } f ? $"{f} ({s.Target})" : s.Target ?? "",
        _ => s.Target ?? "",
    };

    private bool _detectedAssigned;

    private async Task DetectAsync(Button button)
    {
        button.IsEnabled = false;
        _detected.Children.Clear();
        _detected.Children.Add(State("", L("Lecture de la configuration de Windows…"), busy: true));
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync("kiosk.status");
            _detected.Children.Clear();
            if (!outcome.Success)
            {
                if (!outcome.Cancelled) AppHost.Toasts.ShowOutcome(outcome);
                return;
            }
            var data = outcome.Data ?? [];
            var count = int.TryParse(data.GetValueOrDefault("aa.count"), out var c) ? c : 0;
            _detectedAssigned = count > 0;
            var box = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (count == 0)
                box.Children.Add(PageScaffold.InfoBar(L("Windows : aucun accès attribué à application unique n'est configuré. Une configuration XML (multi-applications, MDM) n'est pas détectée par cette vérification."), ""));
            for (var i = 0; i < count; i++)
                box.Children.Add(PageScaffold.InfoBar(
                    L("Windows : accès attribué actif pour « {0} » avec l'application {1} ({2}).", data.GetValueOrDefault($"aa{i}.user"), data.GetValueOrDefault($"aa{i}.app"), data.GetValueOrDefault($"aa{i}.aumid")),
                    "", "Pp.InfoBar.Success"));
            _detected.Children.Add(box);
            if (_detectedAssigned) RenderStatus();
        }
        finally { button.IsEnabled = true; }
    }

    private async Task RemoveAsync(Button button)
    {
        var s = _state;
        var al = _autologon;
        var content = new StackPanel();
        content.Children.Add(Text(s is { IsConfigured: true }
            ? L("Le compte « {0} » retrouvera son Bureau Windows à sa prochaine connexion et les restrictions posées par Timonier seront retirées (les valeurs d'origine sont restaurées).", s.User)
            : L("Timonier n'a pas de borne enregistrée : seules les options cochées ci-dessous seront appliquées.")));
        var aa = new CheckBox
        {
            Content = L("Supprimer aussi l'accès attribué (application unique) configuré dans Windows"),
            IsChecked = s?.AssignedAccess == true || _detectedAssigned, Margin = new Thickness(0, 14, 0, 0),
        };
        var auto = new CheckBox
        {
            Content = L("Désactiver l'ouverture de session automatique et effacer le mot de passe mémorisé"),
            IsChecked = al?.Enabled == true, Margin = new Thickness(0, 8, 0, 0),
        };
        content.Children.Add(aa);
        content.Children.Add(auto);
        if (!await AppHost.Dialogs.ShowAsync(L("Désactiver le mode kiosque"), content, L("Désactiver"), L("Annuler"), danger: true)) return;

        button.IsEnabled = false;
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync("kiosk.remove", new Dictionary<string, string>
            {
                ["assignedAccess"] = aa.IsChecked == true ? "true" : "false",
                ["autologon"] = auto.IsChecked == true ? "true" : "false",
            });
            AppHost.Toasts.ShowOutcome(outcome);
            if (outcome.Success)
            {
                _detectedAssigned = false;
                _detected.Children.Clear();
                SettingsStore.Update(st =>
                {
                    foreach (var k in st.ModuleData.Keys.Where(k => k.StartsWith("kiosk.", StringComparison.Ordinal)).ToList()) st.ModuleData.Remove(k);
                });
            }
        }
        finally
        {
            button.IsEnabled = true;
            await RefreshStatusAsync();
        }
    }

    private async Task ClearAutologonAsync(Button button)
    {
        button.IsEnabled = false;
        try
        {
            var outcome = await AppHost.Engine.RunActionAsync("kiosk.autologon.clear");
            AppHost.Toasts.ShowOutcome(outcome);
        }
        finally
        {
            button.IsEnabled = true;
            await RefreshStatusAsync();
        }
    }

    private static void OpenAssignedAccessSettings()
    {
        try { ProcessRunner.OpenSettingsUri("ms-settings:assignedaccess"); }
        catch (Exception ex) { AppHost.Toasts.Show(L("Impossible d'ouvrir les Paramètres : {0}", ex.Message), ToastKind.Error); }
    }
}
