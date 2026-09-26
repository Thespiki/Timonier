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
        _stack = PageScaffold.Create(this, L("Kiosk mode"),
            L("Turn this PC into a kiosk dedicated to a single app, for a separate account, then undo it in one click."), KioskModule.Glyph);

        _stack.Children.Add(PageScaffold.Section(L("Compatibility")));
        _stack.Children.Add(BuildCompatibility());

        _wizardAnchor = PageScaffold.Section(L("Wizard"));
        _stack.Children.Add(_wizardAnchor);
        _stack.Children.Add(BuildWizard());

        _statusAnchor = PageScaffold.Section(L("Status and removal"));
        _stack.Children.Add(_statusAnchor);
        _stack.Children.Add(_statusCard);

        _stack.Children.Add(PageScaffold.Section(L("Limitations to know")));
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

        var header = Row(null, p.WindowsLabel.Length > 0 ? p.WindowsLabel : "Windows", L("{0} edition · {1}", p.EditionLabel, (p.IsUserAdmin ? L("your account is an administrator") : L("your account isn't an administrator"))),
            Badge(p.SupportsAssignedAccess ? L("Assigned access supported") : L("Assigned access unavailable"), p.SupportsAssignedAccess ? "Success" : "Warning"));
        var tile = GlyphTile("");
        tile.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(tile, Dock.Left);
        header.Children.Insert(header.Children.Count - 1, tile);
        stack.Children.Add(header);
        stack.Children.Add(Divider(new Thickness(0, 12, 0, 6)));

        stack.Children.Add(CompatRow("", L("Full-screen Store app"),
            p.SupportsAssignedAccess
                ? L("Single-app assigned access (Set-AssignedAccess): the app fills the whole screen, with no desktop or taskbar.")
                : L("Assigned access doesn't exist on the Home edition: you need Windows Pro, Enterprise or Education."),
            p.SupportsAssignedAccess ? (L("Available"), "Success") : (L("Unavailable here"), "Danger")));

        stack.Children.Add(CompatRow("", L("Microsoft Edge in kiosk mode"),
            _edgePath is null
                ? L("Microsoft Edge isn't installed for all users on this PC.")
                : L("Edge opens in full screen on your site instead of the kiosk account's desktop (Edge's official --kiosk options).")
                  + (p.SupportsAssignedAccess ? L(" The Windows kiosk wizard also offers Edge, through assigned access.") : ""),
            _edgePath is null ? (L("Edge not installed"), "Danger") : (L("Available"), "Success")));

        stack.Children.Add(CompatRow("", L("Classic app (.exe)"),
            L("The selected program replaces the desktop for this account only (“Custom User Interface” policy). It's a lightweight alternative to Shell Launcher.")
            + (p.IsHomeEdition ? L(" On the Home edition, user policies aren't officially supported: it may not work.") : ""),
            p.IsHomeEdition ? (L("Not guaranteed"), "Warning") : (L("Available"), "Success")));

        stack.Children.Add(CompatRow("", L("Shell Launcher and multi-app kiosk"),
            p.SupportsShellLauncher
                ? L("Supported by your edition, but configured through an XML file (Windows Configuration Designer, MDM/Intune): Timonier doesn't manage them.")
                : L("Shell Launcher is only available on Enterprise and Education editions; the multi-app kiosk requires an XML or MDM configuration. Timonier doesn't manage them."),
            (L("Not managed"), "Neutral")));

        if (p.IsManaged)
        {
            var bar = PageScaffold.InfoBar(L("This PC is managed by an organization (domain, Microsoft Entra or MDM): its policies may override or block the kiosk configuration. Check with your IT department."),
                "", "Pp.InfoBar.Warning");
            bar.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(bar);
        }
        if (!p.IsUserAdmin)
        {
            var bar = PageScaffold.InfoBar(L("Your account isn't an administrator: Windows will ask for an administrator's credentials when you apply."), "");
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
            L("Ctrl+Alt+Del always works, by Windows design: it's the emergency exit. To leave a kiosk session, press Ctrl+Alt+Del and choose “Sign out”."),
            L("Test first: sign out and sign in to the kiosk session before leaving the kiosk for public use. Always keep an administrator account whose password you know."),
            L("With a classic app or Edge as the shell, nothing restarts the app if it closes: the screen stays black until sign-out. Choose an app designed to stay open."),
            L("Classic (Win32) apps under assigned access and the multi-app kiosk require an XML configuration (MDM, Intune, Windows Configuration Designer): not managed here."),
            L("Windows Update keeps installing updates and may restart the PC: set active hours to avoid a restart while the kiosk is in use."),
            L("The restrictions (Task Manager, Run…) are user policies: they stop a curious user, not a determined person with physical access to the PC."),
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
        _statusCard.Child = State("", L("Reading the configuration…"), busy: true);
        try
        {
            (_state, _autologon) = await Task.Run(() => (KioskState.Read(), AutologonInfo.Read()));
            RenderStatus();
        }
        catch (Exception ex)
        {
            Log.Error("Kiosk", "lecture de l'état", ex);
            _statusCard.Child = State("", L("Couldn't read the configuration"), ex.Message);
        }
    }

    private void RenderStatus()
    {
        var stack = new StackPanel();
        var s = _state;
        var al = _autologon ?? new AutologonInfo(false, null, null, false);

        if (s is { IsConfigured: true })
        {
            var head = Row(null, L("Kiosk set up for “{0}”", s.User), KioskModes.Label(s.Mode), Badge(LC("state", "Active"), "Success",""));
            var tile = GlyphTile(KioskModule.Glyph, "Pp.SuccessBackground", "Pp.Success");
            tile.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(tile, Dock.Left);
            head.Children.Insert(head.Children.Count - 1, tile);
            stack.Children.Add(head);
            stack.Children.Add(Divider(new Thickness(0, 12, 0, 8)));
            stack.Children.Add(PageScaffold.KeyValue(L("App"), TargetLabel(s)));
            var keys = s.RestrictionKeys.Select(k => KioskRestrictions.Get(k)?.Title).Where(t => t is not null).ToList();
            stack.Children.Add(PageScaffold.KeyValue(L("Account restrictions"), keys.Count == 0 ? LC("feminine", "None") : string.Join(" · ", keys)));
            if (s.AppliedAt is { } at) stack.Children.Add(PageScaffold.KeyValue(L("Set up on"), Format.Date(at)));
        }
        else
        {
            var head = Row(null, L("No kiosk set up by Timonier"),
                L("Use the wizard above. A kiosk created with Windows Settings can be detected with “Check in Windows”."));
            var tile = GlyphTile(KioskModule.Glyph, "Pp.NeutralBackground", "Pp.Neutral");
            tile.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(tile, Dock.Left);
            head.Children.Insert(head.Children.Count - 1, tile);
            stack.Children.Add(head);
            stack.Children.Add(Divider(new Thickness(0, 12, 0, 8)));
        }

        stack.Children.Add(PageScaffold.KeyValue(L("Automatic sign-in"),
            al.Enabled ? L("On for “{0}”", al.User) : L("Disabled")));
        if (al.PlainPasswordInRegistry)
        {
            var bar = PageScaffold.InfoBar(L("A password is stored in plain text in the registry (Winlogon “DefaultPassword” value). Turning off automatic sign-in deletes it."),
                "", "Pp.InfoBar.Warning");
            bar.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(bar);
        }

        stack.Children.Add(_detected);

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var remove = Button(L("Turn off kiosk mode"), "", "Pp.DangerButton", async (b, _) => await RemoveAsync((Button)b));
        remove.IsEnabled = s is { IsConfigured: true } || al.Enabled || _detectedAssigned;
        remove.Margin = new Thickness(0, 0, 8, 6);
        actions.Children.Add(remove);
        if (al.Enabled)
        {
            var clear = Button(LC("short form", "Turn off automatic sign-in"), "", "Pp.Button", async (b, _) => await ClearAutologonAsync((Button)b));
            clear.Margin = new Thickness(0, 0, 8, 6);
            actions.Children.Add(clear);
        }
        var check = Button(L("Check in Windows"), "", "Pp.Button", async (b, _) => await DetectAsync((Button)b));
        check.ToolTip = L("Reads the Windows assigned access configuration (requires administrator rights).");
        check.Margin = new Thickness(0, 0, 8, 6);
        actions.Children.Add(check);
        var settings = Button(L("Kiosk in Settings"), "", "Pp.SubtleButton", (_, _) => OpenAssignedAccessSettings());
        settings.Margin = new Thickness(0, 0, 8, 6);
        actions.Children.Add(settings);
        var refresh = IconButton("", L("Refresh"), async (_, _) => await RefreshStatusAsync());
        refresh.Margin = new Thickness(0, 0, 0, 6);
        actions.Children.Add(refresh);
        stack.Children.Add(actions);

        _statusCard.Child = stack;
    }

    private static string TargetLabel(KioskState s) => s.Mode switch
    {
        KioskModes.Edge => L("Microsoft Edge on {0}", s.Target),
        KioskModes.Win32 => Path.GetFileName(s.Target ?? "") is { Length: > 0 } f ? $"{f} ({s.Target})" : s.Target ?? "",
        _ => s.Target ?? "",
    };

    private bool _detectedAssigned;

    private async Task DetectAsync(Button button)
    {
        button.IsEnabled = false;
        _detected.Children.Clear();
        _detected.Children.Add(State("", L("Reading the Windows configuration…"), busy: true));
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
                box.Children.Add(PageScaffold.InfoBar(L("Windows: no single-app assigned access is configured. An XML configuration (multi-app, MDM) isn't detected by this check."), ""));
            for (var i = 0; i < count; i++)
                box.Children.Add(PageScaffold.InfoBar(
                    L("Windows: assigned access active for “{0}” with the app {1} ({2}).", data.GetValueOrDefault($"aa{i}.user"), data.GetValueOrDefault($"aa{i}.app"), data.GetValueOrDefault($"aa{i}.aumid")),
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
            ? L("The account “{0}” will get its Windows desktop back the next time it signs in, and the restrictions set by Timonier will be removed (the original values are restored).", s.User)
            : L("Timonier has no saved kiosk: only the options checked below will be applied.")));
        var aa = new CheckBox
        {
            Content = L("Also remove the assigned access (single app) configured in Windows"),
            IsChecked = s?.AssignedAccess == true || _detectedAssigned, Margin = new Thickness(0, 14, 0, 0),
        };
        var auto = new CheckBox
        {
            Content = L("Turn off automatic sign-in and erase the saved password"),
            IsChecked = al?.Enabled == true, Margin = new Thickness(0, 8, 0, 0),
        };
        content.Children.Add(aa);
        content.Children.Add(auto);
        if (!await AppHost.Dialogs.ShowAsync(L("Turn off kiosk mode"), content, L("Disable"), L("Undo"), danger: true)) return;

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
        catch (Exception ex) { AppHost.Toasts.Show(L("Couldn't open Settings: {0}", ex.Message), ToastKind.Error); }
    }
}
