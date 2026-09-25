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
        _create = MakeButton("Nouveau compte", GlyphAdd, "Pp.AccentButton", async (_, _) => await CreateAsync());
        _refresh = MakeButton("Actualiser", GlyphRefresh, "Pp.SubtleButton", (_, _) => RefreshRequested?.Invoke());
        _refresh.ToolTip = "Relire la liste des comptes";
        Children.Add(SectionHeader("Comptes", out var heading, _busyBar, _refresh, _create));
        Heading = heading;

        _showSystem = new CheckBox { Margin = new Thickness(0, 0, 10, 0) }.Styled("Pp.ToggleSwitch");
        _showSystem.Checked += (_, _) => Render();
        _showSystem.Unchecked += (_, _) => Render();
        System.Windows.Automation.AutomationProperties.SetName(_showSystem, "Afficher les comptes techniques");
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 8) };
        options.Children.Add(_showSystem);
        var label = Caption("Afficher les comptes techniques de Windows");
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
        var t = Caption("Lecture des comptes locaux…");
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
            _list.Children.Add(EmptyState(GlyphWarning, "Impossible de lire les comptes locaux", _error ?? "Erreur inconnue.",
                MakeButton("Réessayer", GlyphRefresh, "Pp.Button", (_, _) => RefreshRequested?.Invoke())));
            return;
        }
        var system = _accounts.Count(a => a.IsSystemAccount);
        _systemCount.Text = system > 0 ? $"({system})" : "";
        var visible = _accounts.Where(a => _showSystem.IsChecked == true || !a.IsSystemAccount).ToList();
        var enabledAdmins = _accounts.Count(a => a.IsAdmin && a.Enabled);
        foreach (var a in visible) _list.Children.Add(BuildCard(a, enabledAdmins));
        if (visible.Count == 0)
            _list.Children.Add(EmptyState(GlyphUser, "Aucun compte local", "Ce PC n'utilise peut-être que des comptes de domaine ou Microsoft Entra."));
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
        if (a.IsCurrent) titleRow.Children.Add(Badge("Vous", "Pp.Info"));
        titleRow.Children.Add(a.IsAdmin ? Badge("Administrateur", "Pp.Warning") : Badge("Standard", "Pp.Success"));
        if (!a.Enabled) titleRow.Children.Add(Badge("Désactivé", "Pp.Neutral"));
        if (a.LockedOut) titleRow.Children.Add(Badge("Verrouillé", "Pp.Danger"));
        if (a.MicrosoftAccount is not null) titleRow.Children.Add(Badge("Compte Microsoft", "Pp.Info"));
        if (a.IsBuiltIn) titleRow.Children.Add(Badge("Intégré à Windows", "Pp.Neutral"));
        else if (a.IsSystemAccount) titleRow.Children.Add(Badge("Reliquat d'installation", "Pp.Neutral"));
        if (a.HasLogonRestriction) titleRow.Children.Add(Badge("Horaires limités", "Pp.Info"));
        if (a.Enabled && a.PasswordNotRequired && a.MicrosoftAccount is null) titleRow.Children.Add(Badge("Mot de passe vide autorisé", "Pp.Warning"));
        body.Children.Add(titleRow);

        var details = new List<string>();
        if (a.MicrosoftAccount is not null) details.Add(a.MicrosoftAccount);
        details.Add(a.LastLogon is { } ll ? "Dernière connexion " + Format.Ago(ll) : "Jamais connecté");
        if (a.PasswordLastSet is { } ps) details.Add("mot de passe défini le " + ps.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR")));
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
        500 => "Compte administrateur caché créé par Windows, sans invite UAC : à laisser désactivé.",
        501 => "Compte Invité : permettrait d'ouvrir une session sans mot de passe. À laisser désactivé.",
        503 => "Compte technique utilisé par Windows (DefaultAccount).",
        504 => "Compte technique de Microsoft Defender Application Guard.",
        _ when LocalAccounts.IsSetupLeftover(a.Name) => "Compte temporaire créé pendant l'installation de Windows ; il peut être supprimé.",
        _ => null,
    };

    private WrapPanel BuildActions(LocalAccount a, int enabledAdmins)
    {
        var panel = new WrapPanel { Margin = new Thickness(-8, 6, 0, 0) };
        if (a.Rid is 503 or 504) return panel;

        var lastAdmin = a.IsAdmin && a.Enabled && enabledAdmins <= 1;
        const string sessionReason = "Impossible sur le compte avec lequel vous êtes connecté.";
        const string lastAdminReason = "C'est le dernier administrateur actif de ce PC.";

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
            Add("Options de connexion", GlyphKey, () => { OpenUri("ms-settings:signinoptions"); return Task.CompletedTask; })
                .ToolTip = "Changer votre mot de passe, votre code PIN ou Windows Hello";
        }
        else if (a.Rid != 501)
        {
            string? reason = a.MicrosoftAccount is not null ? "Compte Microsoft : le mot de passe se change sur account.microsoft.com." : null;
            Add("Mot de passe…", GlyphKey, () => ResetPasswordAsync(a), reason);
        }

        if (!a.IsBuiltIn && a.MicrosoftAccount is null)
            Add("Nom affiché…", "", () => RenameAsync(a));

        if (!a.IsBuiltIn)
        {
            if (a.IsAdmin)
                Add("Rendre standard", GlyphUser, () => SetTypeAsync(a, admin: false), a.IsCurrent ? sessionReason : lastAdmin ? lastAdminReason : null);
            else
                Add("Rendre administrateur", GlyphAdmin, () => SetTypeAsync(a, admin: true));
        }

        if (a.LockedOut)
            Add("Déverrouiller", "", () => SetEnabledAsync(a, true));
        if (a.Enabled)
            Add("Désactiver", "", () => SetEnabledAsync(a, false), a.IsCurrent ? sessionReason : lastAdmin ? lastAdminReason : null);
        else if (!a.IsBuiltInAdministrator && !a.IsGuest)
            Add("Activer", "", () => SetEnabledAsync(a, true));

        if (!a.IsBuiltIn)
            Add("Supprimer", GlyphDelete, () => DeleteAsync(a), a.IsCurrent ? sessionReason : lastAdmin ? lastAdminReason : null);

        if (!a.IsAdmin && !a.IsCurrent && !a.IsBuiltIn && a.Enabled)
        {
            Add("Horaires", GlyphClock, () => { NavigateRequested?.Invoke("section:hours", a.Sid); return Task.CompletedTask; });
            Add("Restrictions", GlyphBlock, () => { NavigateRequested?.Invoke("section:restrictions", a.Sid); return Task.CompletedTask; });
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
        if (!enable && !await AppHost.Dialogs.ConfirmAsync("Désactiver le compte",
                $"« {a.Name} » ne pourra plus ouvrir de session. Ses fichiers et ses réglages sont conservés ; vous pourrez le réactiver à tout moment.",
                "Désactiver", danger: true))
            return;
        await RunAsync("users.account.setenabled", new() { ["sid"] = a.Sid, ["enabled"] = enable ? "true" : "false" });
    }

    private async Task SetTypeAsync(LocalAccount a, bool admin)
    {
        if (!admin && !await AppHost.Dialogs.ConfirmAsync("Rendre le compte standard",
                $"« {a.Name} » ne pourra plus installer de logiciels ni modifier les réglages du PC sans le mot de passe d'un administrateur. " +
                "Le changement prend effet à sa prochaine ouverture de session.", "Rendre standard"))
            return;
        if (admin && !await AppHost.Dialogs.ConfirmAsync("Rendre le compte administrateur",
                $"« {a.Name} » aura un contrôle total sur ce PC : installer des logiciels, modifier les autres comptes, retirer les restrictions. " +
                "Windows vous demandera de confirmer dans une fenêtre administrateur.", "Continuer", danger: true))
            return;
        await RunAsync("users.account.settype", new() { ["sid"] = a.Sid, ["type"] = admin ? "admin" : "standard" });
    }

    private async Task DeleteAsync(LocalAccount a)
    {
        if (!await AppHost.Dialogs.ConfirmAsync("Supprimer le compte",
                $"Le compte « {a.Name} » sera supprimé et ne pourra plus ouvrir de session. Son dossier de profil (documents, bureau, images) " +
                "reste sur le disque : vous pourrez le récupérer ou le supprimer ensuite depuis « Profils des utilisateurs ».\n\n" +
                "Une confirmation vous sera encore demandée dans une fenêtre administrateur.", "Supprimer", danger: true))
            return;
        await RunAsync("users.account.delete", new() { ["sid"] = a.Sid });
    }

    private async Task RenameAsync(LocalAccount a)
    {
        var value = await AppHost.Dialogs.PromptAsync("Nom affiché",
            $"Nom complet affiché pour « {a.Name} » (écran de connexion, menu Démarrer). Le nom du compte lui-même ne change pas.",
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
        content.Children.Add(Warn("Réinitialiser un mot de passe fait perdre à ce compte l'accès à ses fichiers chiffrés (EFS), à ses mots de passe " +
                                  "enregistrés par Windows et à ses certificats. Préférez que la personne le change elle-même si elle s'en souvient."));
        content.Children.Add(Caption("Nouveau mot de passe"));
        content.Children.Add(first);
        content.Children.Add(Caption("Confirmer le mot de passe"));
        content.Children.Add(second);
        content.Children.Add(error);
        try
        {
            while (await AppHost.Dialogs.ShowAsync($"Mot de passe de « {a.Name} »", content, "Réinitialiser", "Annuler", danger: true))
            {
                string? problem = first.Password.Length == 0 ? "Saisissez un mot de passe."
                    : first.Password != second.Password ? "Les deux mots de passe ne correspondent pas."
                    : first.Password.Length > AccountParams.MaxPassword ? "Mot de passe trop long."
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
        standard.Content = RadioLabel("Standard (recommandé)", "Pour un enfant ou l'usage quotidien : ne peut ni installer de logiciels ni modifier le PC.");
        var admin = new RadioButton { GroupName = "tmn-users-type", Margin = new Thickness(0, 4, 0, 4) };
        admin.Content = RadioLabel("Administrateur", "Contrôle total du PC, y compris des autres comptes.");
        var error = Text("", "Pp.Caption").Themed(TextBlock.ForegroundProperty, "Pp.Danger");
        error.Visibility = Visibility.Collapsed;
        error.Margin = new Thickness(0, 8, 0, 0);

        var content = new StackPanel { Width = 440 };
        content.Children.Add(Caption("Nom du compte (1 à 20 caractères : lettres, chiffres, espace, . _ -)"));
        content.Children.Add(name);
        content.Children.Add(Caption("Nom complet (facultatif, affiché à la connexion)"));
        content.Children.Add(fullName);
        content.Children.Add(Caption("Mot de passe"));
        content.Children.Add(pwd);
        content.Children.Add(Caption("Confirmer le mot de passe"));
        content.Children.Add(pwd2);
        var hint = Caption("Laissé vide, n'importe qui pourra ouvrir ce compte. La personne pourra ensuite définir un code PIN.");
        hint.FontStyle = FontStyles.Italic;
        content.Children.Add(hint);
        var typeTitle = Caption("Type de compte");
        typeTitle.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(typeTitle);
        content.Children.Add(standard);
        content.Children.Add(admin);
        content.Children.Add(error);
        name.Loaded += (_, _) => name.Focus();

        try
        {
            while (await AppHost.Dialogs.ShowAsync("Nouveau compte local", content, "Créer", "Annuler"))
            {
                string? problem = null;
                try
                {
                    Validate.LocalUserName(name.Text);
                    AccountParams.FullName(new Dictionary<string, string> { ["fullName"] = fullName.Text });
                    if (pwd.Password != pwd2.Password) problem = "Les deux mots de passe ne correspondent pas.";
                    else if (_accounts?.Any(a => string.Equals(a.Name, name.Text.Trim(), StringComparison.OrdinalIgnoreCase)) == true)
                        problem = "Un compte porte déjà ce nom.";
                }
                catch (ValidationException ex) { problem = ex.Message; }

                if (problem is null)
                {
                    if (pwd.Password.Length == 0 && !await AppHost.Dialogs.ConfirmAsync("Compte sans mot de passe",
                            "Sans mot de passe, toute personne ayant accès au PC pourra ouvrir ce compte et lire ses fichiers. Créer quand même ?",
                            "Créer sans mot de passe", "Revenir"))
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
        Add("Autres utilisateurs", "", () => OpenUri("ms-settings:otherusers"), "Paramètres › Comptes › Autres utilisateurs (comptes Microsoft, comptes professionnels)");
        Add("Options de connexion", GlyphKey, () => OpenUri("ms-settings:signinoptions"), "Code PIN, Windows Hello, mot de passe de votre compte");
        Add("Profils des utilisateurs", "", () => Launch(SystemTool.Rundll32, "sysdm.cpl,EditUserProfiles"), "Supprimer le dossier de profil d'un ancien compte");
        Add("netplwiz", "", () => Launch(SystemTool.Netplwiz), "Outil classique « Comptes d'utilisateurs »");
        return row;
    }
}
