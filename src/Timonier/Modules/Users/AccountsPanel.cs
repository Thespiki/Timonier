using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Timonier.Core.Platform;
using Timonier.Core.Security;
using Timonier.UI.Services;
using static Timonier.Modules.Users.UsersUi;

namespace Timonier.Modules.Users;

/// <summary>
/// Liste des comptes locaux avec leurs actions. Les boutons reflètent les mêmes garde-fous que le broker
/// (qui revalide tout) : jamais d'action qui verrouillerait la session en cours ou le dernier administrateur.
/// </summary>
internal sealed class AccountsPanel : StackPanel
{
    private readonly StackPanel _list = new();
    private readonly CheckBox _showSystem;
    private readonly TextBlock _systemCount = Caption("");
    private readonly Button _create;
    private readonly Button _refresh;
    private readonly ProgressBar _busyBar = BusyBar();
    private List<LocalAccount>? _accounts;
    private string? _error;
    private bool _busy;

    /// <summary>Déclenché après une modification réussie (la page relit les comptes).</summary>
    public event Action? Changed;
    /// <summary>Demande de relecture sans modification (bouton Actualiser).</summary>
    public event Action? RefreshRequested;

    public TextBlock Heading { get; }

    public AccountsPanel()
    {
        _create = MakeButton(L("New account"),GlyphAdd, "Pp.AccentButton", async (_, _) => await CreateAsync());
        _refresh = MakeButton(L("Refresh"), GlyphRefresh, "Pp.SubtleButton", (_, _) => RefreshRequested?.Invoke());
        _refresh.ToolTip = L("Reload the account list");
        Children.Add(SectionHeader(L("Accounts"), out var heading, _busyBar, _refresh, _create));
        Heading = heading;

        _showSystem = new CheckBox { Margin = new Thickness(0, 0, 10, 0) }.Styled("Pp.ToggleSwitch");
        _showSystem.Checked += (_, _) => Render();
        _showSystem.Unchecked += (_, _) => Render();
        System.Windows.Automation.AutomationProperties.SetName(_showSystem, L("Show system accounts"));
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 8) };
        options.Children.Add(_showSystem);
        var label = Caption(L("Show Windows system accounts"));
        label.VerticalAlignment = VerticalAlignment.Center;
        options.Children.Add(label);
        _systemCount.VerticalAlignment = VerticalAlignment.Center;
        _systemCount.Margin = new Thickness(6, 0, 0, 0);
        options.Children.Add(_systemCount);
        Children.Add(options);

        Children.Add(_list);
        Children.Add(BuildToolsRow());
        ShowLoading();
    }

    // ------------------------------------------------------------------ États

    public void ShowLoading()
    {
        if (_accounts is not null) { SetBusy(true); return; }
        _list.Children.Clear();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        var bar = new ProgressBar { IsIndeterminate = true, Width = 140, Height = 3, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(bar);
        var t = Caption(L("Reading local accounts…"));
        t.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(t);
        _list.Children.Add(Card(row));
    }

    public void Update(List<LocalAccount>? accounts, string? error)
    {
        _accounts = accounts;
        _error = error;
        SetBusy(false);
        Render();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _busyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _create.IsEnabled = !busy;
        _refresh.IsEnabled = !busy;
        _list.IsEnabled = !busy;
    }

    private void Render()
    {
        _list.Children.Clear();
        if (_error is not null || _accounts is null)
        {
            _list.Children.Add(EmptyState(GlyphWarning, L("Couldn't read local accounts"), _error ?? L("Unknown error."),
                MakeButton(L("Try again"), GlyphRefresh, "Pp.Button", (_, _) => RefreshRequested?.Invoke())));
            return;
        }
        var system = _accounts.Count(a => a.IsSystemAccount);
        _systemCount.Text = system > 0 ? $"({system})" : "";
        var visible = _accounts.Where(a => _showSystem.IsChecked == true || !a.IsSystemAccount).ToList();
        var enabledAdmins = _accounts.Count(a => a.IsAdmin && a.Enabled);
        foreach (var a in visible) _list.Children.Add(BuildCard(a, enabledAdmins));
        if (visible.Count == 0)
            _list.Children.Add(EmptyState(GlyphUser, L("No local accounts"), L("This PC may only use domain or Microsoft Entra accounts.")));
    }

    // ------------------------------------------------------------------ Carte d'un compte

    private Border BuildCard(LocalAccount a, int enabledAdmins)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(Avatar(a));

        var body = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var titleRow = new WrapPanel();
        var name = Text(a.Name, "Pp.CardTitle");
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(0, 0, 8, 0);
        titleRow.Children.Add(name);
        if (a.FullName.Length > 0 && !string.Equals(a.FullName, a.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            var full = Caption(a.FullName);
            full.VerticalAlignment = VerticalAlignment.Center;
            full.Margin = new Thickness(0, 0, 10, 0);
            titleRow.Children.Add(full);
        }
        if (a.IsCurrent) titleRow.Children.Add(Badge(L("You"), "Pp.Info"));
        titleRow.Children.Add(a.IsAdmin ? Badge(L("Administrator"), "Pp.Warning") : Badge(LC("account type", "Standard user"), "Pp.Success"));
        if (!a.Enabled) titleRow.Children.Add(Badge(L("Off"), "Pp.Neutral"));
        if (a.LockedOut) titleRow.Children.Add(Badge(L("Locked"), "Pp.Danger"));
        if (a.MicrosoftAccount is not null) titleRow.Children.Add(Badge(L("Microsoft account"), "Pp.Info"));
        if (a.IsBuiltIn) titleRow.Children.Add(Badge(L("Built into Windows"), "Pp.Neutral"));
        else if (a.IsSystemAccount) titleRow.Children.Add(Badge(L("Setup leftover"),"Pp.Neutral"));
        if (a.HasLogonRestriction) titleRow.Children.Add(Badge(L("Limited hours"), "Pp.Info"));
        if (a.Enabled && a.PasswordNotRequired && a.MicrosoftAccount is null) titleRow.Children.Add(Badge(L("Blank password allowed"), "Pp.Warning"));
        body.Children.Add(titleRow);

        var details = new List<string>();
        if (a.MicrosoftAccount is not null) details.Add(a.MicrosoftAccount);
        details.Add(a.LastLogon is { } ll ? L("Last sign-in {0}", Format.Ago(ll)) : L("Never signed in"));
        if (a.PasswordLastSet is { } ps) details.Add(L("password set on {0}", Format.Day(ps)));
        var info = Caption(string.Join(" · ", details));
        info.Margin = new Thickness(0, 3, 0, 0);
        body.Children.Add(info);
        if (Describe(a) is { } desc)
        {
            var d = Caption(desc);
            d.Margin = new Thickness(0, 2, 0, 0);
            d.FontStyle = FontStyles.Italic;
            body.Children.Add(d);
        }

        var actions = BuildActions(a, enabledAdmins);
        if (actions.Children.Count > 0) body.Children.Add(actions);

        return Card(grid, new Thickness(16, 12, 16, 10));
    }

    private static Border Avatar(LocalAccount a)
    {
        var letter = a.DisplayName.Length > 0 ? char.ToUpper(a.DisplayName[0], CultureInfo.CurrentCulture).ToString() : "?";
        var t = new TextBlock
        {
            Text = letter, FontSize = 17, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, a.Enabled ? "Pp.AccentText" : "Pp.TextTertiary");
        return new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Child = t, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) }
            .Themed(Border.BackgroundProperty, a.Enabled ? "Pp.AccentSubtle" : "Pp.NeutralBackground");
    }

    private static string? Describe(LocalAccount a) => a.Rid switch
    {
        500 => L("Hidden administrator account created by Windows, with no UAC prompt: keep it disabled."),
        501 => L("Guest account: would allow signing in without a password. Keep it disabled."),
        503 => L("System account used by Windows (DefaultAccount)."),
        504 => L("Microsoft Defender Application Guard system account."),
        _ when LocalAccounts.IsSetupLeftover(a.Name) => L("Temporary account created during Windows setup; it can be deleted."),
        _ => null,
    };

    private WrapPanel BuildActions(LocalAccount a, int enabledAdmins)
    {
        var panel = new WrapPanel { Margin = new Thickness(-8, 6, 0, 0) };
        if (a.Rid is 503 or 504) return panel;

        var lastAdmin = a.IsAdmin && a.Enabled && enabledAdmins <= 1;
        var sessionReason = L("Not possible on the account you're signed in with.");
        var lastAdminReason = L("This is the last active administrator on this PC.");

        Button Add(string text, string glyph, Func<Task> run, string? disabledReason = null, string style = "Pp.SubtleButton")
        {
            var b = MakeButton(text, glyph, style, async (_, _) => { if (!_busy) await run(); });
            b.Margin = new Thickness(0, 0, 4, 0);
            if (disabledReason is not null) { b.IsEnabled = false; b.ToolTip = disabledReason; }
            panel.Children.Add(b);
            return b;
        }

        if (a.IsCurrent)
        {
            Add(L("Sign-in options"), GlyphKey, () => { OpenUri("ms-settings:signinoptions"); return Task.CompletedTask; })
                .ToolTip = L("Change your password, PIN or Windows Hello");
        }
        else if (a.Rid != 501)
        {
            string? reason = a.MicrosoftAccount is not null ? L("Microsoft account: the password is changed at account.microsoft.com.") : null;
            Add(L("Password…"), GlyphKey, () => ResetPasswordAsync(a), reason);
        }

        if (!a.IsBuiltIn && a.MicrosoftAccount is null)
            Add(L("Display name…"), "", () => RenameAsync(a));

        if (!a.IsBuiltIn)
        {
            if (a.IsAdmin)
                Add(L("Make standard"), GlyphUser, () => SetTypeAsync(a, admin: false), a.IsCurrent ? sessionReason : lastAdmin ? lastAdminReason : null);
            else
                Add(L("Make administrator"),GlyphAdmin, () => SetTypeAsync(a, admin: true));
        }

        if (a.LockedOut)
            Add(L("Unlock"), "", () => SetEnabledAsync(a, true));
        if (a.Enabled)
            Add(L("Disable"), "", () => SetEnabledAsync(a, false), a.IsCurrent ? sessionReason : lastAdmin ? lastAdminReason : null);
        else if (!a.IsBuiltInAdministrator && !a.IsGuest)
            Add(L("Enable"), "", () => SetEnabledAsync(a, true));

        if (!a.IsBuiltIn)
            Add(L("Remove"), GlyphDelete, () => DeleteAsync(a), a.IsCurrent ? sessionReason : lastAdmin ? lastAdminReason : null);

        if (!a.IsAdmin && !a.IsCurrent && !a.IsBuiltIn && a.Enabled)
        {
            Add(L("Hours"), GlyphClock, () => { NavigateRequested?.Invoke("section:hours", a.Sid); return Task.CompletedTask; });
            Add(L("Restrictions"),GlyphBlock, () => { NavigateRequested?.Invoke("section:restrictions", a.Sid); return Task.CompletedTask; });
        }
        return panel;
    }

    /// <summary>Demande à la page d'afficher une section pour un compte donné (section, SID).</summary>
    public event Action<string, string>? NavigateRequested;

    // ------------------------------------------------------------------ Actions

    private async Task RunAsync(string actionId, Dictionary<string, string> p)
    {
        SetBusy(true);
        try
        {
            var outcome = await UsersUi.RunAsync(actionId, p);
            if (outcome.Success)
            {
                UsersHealth.Invalidate();
                Changed?.Invoke();
                return;
            }
        }
        finally
        {
            if (_busy) SetBusy(false);
        }
    }

    private async Task SetEnabledAsync(LocalAccount a, bool enable)
    {
        if (!enable && !await AppHost.Dialogs.ConfirmAsync(L("Disable account"),
                L("“{0}” will no longer be able to sign in. Its files and settings are kept; you can enable it again at any time.", a.Name),
                L("Disable"), danger: true))
            return;
        await RunAsync("users.account.setenabled", new() { ["sid"] = a.Sid, ["enabled"] = enable ? "true" : "false" });
    }

    private async Task SetTypeAsync(LocalAccount a, bool admin)
    {
        if (!admin && !await AppHost.Dialogs.ConfirmAsync(L("Make the account standard"),
                L("“{0}” will no longer be able to install software or change PC settings without an administrator's password. The change takes effect at their next sign-in.", a.Name), L("Make standard")))
            return;
        if (admin && !await AppHost.Dialogs.ConfirmAsync(L("Make the account administrator"),
                L("“{0}” will have full control over this PC: installing software, changing other accounts, removing restrictions. Windows will ask you to confirm in an administrator window.", a.Name), L("Continue"), danger: true))
            return;
        await RunAsync("users.account.settype", new() { ["sid"] = a.Sid, ["type"] = admin ? "admin" : "standard" });
    }

    private async Task DeleteAsync(LocalAccount a)
    {
        if (!await AppHost.Dialogs.ConfirmAsync(L("Delete account"),
                L("The account “{0}” will be deleted and will no longer be able to sign in. Its profile folder (documents, desktop, pictures) stays on the disk: you can recover or delete it later from “User Profiles”.\n\nYou'll be asked to confirm again in an administrator window.", a.Name), L("Remove"), danger: true))
            return;
        await RunAsync("users.account.delete", new() { ["sid"] = a.Sid });
    }

    private async Task RenameAsync(LocalAccount a)
    {
        var value = await AppHost.Dialogs.PromptAsync(L("Display name"),
            L("Full name shown for “{0}” (sign-in screen, Start menu). The account name itself doesn't change.", a.Name),
            a.FullName, validate: v =>
            {
                try { AccountParams.FullName(new Dictionary<string, string> { ["fullName"] = v }); return null; }
                catch (ValidationException ex) { return ex.Message; }
            });
        if (value is null || value.Trim() == a.FullName) return;
        await RunAsync("users.account.setfullname", new() { ["sid"] = a.Sid, ["fullName"] = value.Trim() });
    }

    private async Task ResetPasswordAsync(LocalAccount a)
    {
        var first = new PasswordBox { MaxLength = AccountParams.MaxPassword, Margin = new Thickness(0, 4, 0, 10) };
        var second = new PasswordBox { MaxLength = AccountParams.MaxPassword, Margin = new Thickness(0, 4, 0, 10) };
        var error = Text("", "Pp.Caption").Themed(TextBlock.ForegroundProperty, "Pp.Danger");
        error.Visibility = Visibility.Collapsed;
        var content = new StackPanel { Width = 420 };
        content.Children.Add(Warn(L("Resetting a password makes this account lose access to its encrypted files (EFS), its passwords saved by Windows and its certificates. It's better to let the person change it themselves if they remember it.")));
        content.Children.Add(Caption(L("New password")));
        content.Children.Add(first);
        content.Children.Add(Caption(L("Confirm password")));
        content.Children.Add(second);
        content.Children.Add(error);
        try
        {
            while (await AppHost.Dialogs.ShowAsync(L("Password for “{0}”", a.Name), content, L("Reset"), L("Undo"), danger: true))
            {
                string? problem = first.Password.Length == 0 ? L("Enter a password.")
                    : first.Password != second.Password ? L("The two passwords don't match.")
                    : first.Password.Length > AccountParams.MaxPassword ? L("Password too long.")
                    : null;
                if (problem is null)
                {
                    await RunAsync("users.account.resetpassword", new() { ["sid"] = a.Sid, ["password"] = first.Password });
                    return;
                }
                error.Text = problem;
                error.Visibility = Visibility.Visible;
                content = Detach(content);
            }
        }
        finally
        {
            first.Clear();
            second.Clear();
        }
    }

    /// <summary>Retire le contenu de son ancien hôte de dialogue pour pouvoir le réafficher.</summary>
    private static StackPanel Detach(StackPanel content)
    {
        switch (content.Parent)
        {
            case Panel p: p.Children.Remove(content); break;
            case ContentControl c: c.Content = null; break;
            case Decorator d: d.Child = null; break;
        }
        return content;
    }

    private static Border Warn(string text)
    {
        var bar = Timonier.UI.Controls.PageScaffold.InfoBar(text, GlyphWarning, "Pp.InfoBar.Warning");
        bar.Margin = new Thickness(0, 0, 0, 12);
        return bar;
    }

    /// <summary>Boîte de dialogue de création d'un compte (nom, nom complet, mot de passe, type).</summary>
    public async Task CreateAsync()
    {
        if (_busy) return;
        var name = new TextBox { MaxLength = 20, Margin = new Thickness(0, 4, 0, 10) };
        var fullName = new TextBox { MaxLength = 64, Margin = new Thickness(0, 4, 0, 10) };
        var pwd = new PasswordBox { MaxLength = AccountParams.MaxPassword, Margin = new Thickness(0, 4, 0, 10) };
        var pwd2 = new PasswordBox { MaxLength = AccountParams.MaxPassword, Margin = new Thickness(0, 4, 0, 4) };
        var standard = new RadioButton { IsChecked = true, GroupName = "tmn-users-type", Margin = new Thickness(0, 6, 0, 4) };
        standard.Content = RadioLabel(L("Standard user (recommended)"), L("For a child or everyday use: can't install software or change the PC."));
        var admin = new RadioButton { GroupName = "tmn-users-type", Margin = new Thickness(0, 4, 0, 4) };
        admin.Content = RadioLabel(L("Administrator"), L("Full control of the PC, including other accounts."));
        var error = Text("", "Pp.Caption").Themed(TextBlock.ForegroundProperty, "Pp.Danger");
        error.Visibility = Visibility.Collapsed;
        error.Margin = new Thickness(0, 8, 0, 0);

        var content = new StackPanel { Width = 440 };
        content.Children.Add(Caption(L("Account name (1 to 20 characters: letters, digits, space, . _ -)")));
        content.Children.Add(name);
        content.Children.Add(Caption(L("Full name (optional, shown at sign-in)")));
        content.Children.Add(fullName);
        content.Children.Add(Caption(L("Password")));
        content.Children.Add(pwd);
        content.Children.Add(Caption(L("Confirm password")));
        content.Children.Add(pwd2);
        var hint = Caption(L("If left blank, anyone will be able to open this account. The person can then set up a PIN."));
        hint.FontStyle = FontStyles.Italic;
        content.Children.Add(hint);
        var typeTitle = Caption(L("Account type"));
        typeTitle.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(typeTitle);
        content.Children.Add(standard);
        content.Children.Add(admin);
        content.Children.Add(error);
        name.Loaded += (_, _) => name.Focus();

        try
        {
            while (await AppHost.Dialogs.ShowAsync(L("New local account"),content, L("Create"), L("Undo")))
            {
                string? problem = null;
                try
                {
                    Validate.LocalUserName(name.Text);
                    AccountParams.FullName(new Dictionary<string, string> { ["fullName"] = fullName.Text });
                    if (pwd.Password != pwd2.Password) problem = L("The two passwords don't match.");
                    else if (_accounts?.Any(a => string.Equals(a.Name, name.Text.Trim(), StringComparison.OrdinalIgnoreCase)) == true)
                        problem = LC("form validation", "An account with this name already exists.");
                }
                catch (ValidationException ex) { problem = ex.Message; }

                if (problem is null)
                {
                    if (pwd.Password.Length == 0 && !await AppHost.Dialogs.ConfirmAsync(L("Account without a password"),
                            L("Without a password, anyone with access to the PC will be able to open this account and read its files. Create it anyway?"),
                            L("Create without a password"), L("Go back")))
                    {
                        content = Detach(content);
                        continue;
                    }
                    var isAdmin = admin.IsChecked == true;
                    var p = new Dictionary<string, string>
                    {
                        ["name"] = name.Text.Trim(),
                        ["password"] = pwd.Password,
                        ["type"] = isAdmin ? "admin" : "standard",
                    };
                    if (fullName.Text.Trim().Length > 0) p["fullName"] = fullName.Text.Trim();
                    await RunAsync("users.account.create", p);
                    return;
                }
                error.Text = problem;
                error.Visibility = Visibility.Visible;
                content = Detach(content);
            }
        }
        finally
        {
            pwd.Clear();
            pwd2.Clear();
        }
    }

    private static StackPanel RadioLabel(string title, string text)
    {
        var s = new StackPanel { Margin = new Thickness(4, -2, 0, 0) };
        s.Children.Add(Text(title));
        s.Children.Add(Caption(text));
        return s;
    }

    // ------------------------------------------------------------------ Outils Windows

    private static WrapPanel BuildToolsRow()
    {
        var row = new WrapPanel { Margin = new Thickness(-8, 6, 0, 0) };
        void Add(string text, string glyph, Action run, string tip)
        {
            var b = MakeButton(text, glyph, "Pp.SubtleButton", (_, _) => run());
            b.ToolTip = tip;
            b.Margin = new Thickness(0, 0, 4, 0);
            row.Children.Add(b);
        }
        Add(L("Other users"),"", () => OpenUri("ms-settings:otherusers"), L("Settings › Accounts › Other users (Microsoft accounts, work accounts)"));
        Add(L("Sign-in options"), GlyphKey, () => OpenUri("ms-settings:signinoptions"), L("PIN, Windows Hello, your account password"));
        Add(L("User Profiles"), "", () => Launch(SystemTool.Rundll32, "sysdm.cpl,EditUserProfiles"), L("Delete an old account's profile folder"));
        Add("netplwiz", "", () => Launch(SystemTool.Netplwiz), L("Classic “User Accounts” tool"));
        return row;
    }
}
