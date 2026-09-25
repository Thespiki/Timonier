using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PcPilot.Core.Platform;
using PcPilot.UI.Controls;
using static PcPilot.Modules.Users.UsersUi;

namespace PcPilot.Modules.Users;

/// <summary>
/// Section « Restrictions du compte » : stratégies utilisateur écrites dans la ruche d'un compte standard (liste blanche),
/// lues et écrites par le broker (la ruche d'un autre compte n'est pas accessible sans droits administrateur).
/// </summary>
internal sealed class RestrictionsPanel : StackPanel
{
    private static readonly string[] Suggestions = ["powershell.exe", "pwsh.exe", "powershell_ise.exe", "mmc.exe"];

    private readonly ComboBox _account = new() { MinWidth = 240, Margin = new Thickness(0, 0, 12, 0) };
    private readonly Button _read;
    private readonly Button _save;
    private readonly Button _revert;
    private readonly ProgressBar _busyBar = BusyBar();
    private readonly TextBlock _status = Caption("");
    private readonly ContentControl _banner = new();
    private readonly ContentControl _empty = new();
    private readonly Border _card;
    private readonly StackPanel _rows = new();
    private readonly Dictionary<string, CheckBox> _toggles = [];
    private readonly ComboBox _cmd = new() { MinWidth = 230 };
    private readonly WrapPanel _apps = new() { Margin = new Thickness(32, 4, 0, 0) };
    private readonly TextBox _appInput = new() { Width = 220, MaxLength = 68 };
    private readonly TextBlock _appError = Caption("");
    private List<LocalAccount> _eligible = [];
    private Dictionary<string, string>? _loaded;
    private List<string> _appList = [];
    private string? _loadedSid;
    private bool _busy, _suppress;

    public TextBlock Heading { get; }

    public RestrictionsPanel()
    {
        Children.Add(SectionHeader("Restrictions du compte", out var heading));
        Heading = heading;
        Children.Add(_empty);

        var content = new StackPanel();
        var top = new WrapPanel();
        var who = Caption("Compte");
        who.VerticalAlignment = VerticalAlignment.Center;
        who.Margin = new Thickness(0, 0, 10, 0);
        top.Children.Add(who);
        _account.SelectionChanged += async (_, _) => { if (!_suppress) await OnAccountChangedAsync(); };
        System.Windows.Automation.AutomationProperties.SetName(_account, "Compte à restreindre");
        top.Children.Add(_account);
        _read = MakeButton("Lire l'état actuel", GlyphAdmin, "Pp.Button", async (_, _) => await ReadAsync());
        _read.ToolTip = "Lit les restrictions dans le profil de ce compte (droits administrateur requis)";
        top.Children.Add(_read);
        top.Children.Add(_busyBar);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(12, 0, 0, 0);
        top.Children.Add(_status);
        content.Children.Add(top);
        _banner.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(_banner);

        content.Children.Add(Divider(new Thickness(0, 12, 0, 6)));
        BuildRows();
        content.Children.Add(_rows);

        _save = MakeButton("Enregistrer les restrictions", GlyphAdmin, "Pp.AccentButton", async (_, _) => await SaveAsync());
        _revert = MakeButton("Rétablir", null, "Pp.SubtleButton", (_, _) => ApplyLoaded());
        _revert.Margin = new Thickness(8, 0, 0, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(_save);
        actions.Children.Add(_revert);
        content.Children.Add(actions);

        _card = Card(content, new Thickness(18, 14, 18, 14));
        Children.Add(_card);
        Children.Add(PageScaffold.InfoBar(
            "Ces restrictions sont des stratégies Windows appliquées à l'ouverture de session du compte. Elles complètent un compte " +
            "standard (qui ne peut déjà ni installer de logiciels ni modifier le système) mais ne remplacent pas une supervision : " +
            "un utilisateur averti peut contourner la liste d'applications interdites. Le compte ne peut pas les retirer lui-même.",
            GlyphInfo));
        SetEditable(false);
    }

    // ------------------------------------------------------------------ Construction

    private void BuildRows()
    {
        var profile = AppHost.Profile;
        foreach (var d in UserRestrictions.All)
        {
            if (d.Key == UserRestrictions.DisallowRunKey)
            {
                _rows.Children.Add(Divider(new Thickness(0, 4, 0, 4)));
                _rows.Children.Add(SettingRow(d.Glyph, d.Title, d.Description, new FrameworkElement(), d.Note));
                _rows.Children.Add(BuildAppEditor());
                continue;
            }
            if (d.Key == "DisableCMD")
            {
                _cmd.Items.Add(new ComboBoxItem { Content = "Autorisée", Tag = "0" });
                _cmd.Items.Add(new ComboBoxItem { Content = "Bloquée (scripts .bat autorisés)", Tag = "2" });
                _cmd.Items.Add(new ComboBoxItem { Content = "Bloquée, scripts compris", Tag = "1" });
                _cmd.SelectedIndex = 0;
                _cmd.SelectionChanged += (_, _) => UpdateDirty();
                System.Windows.Automation.AutomationProperties.SetName(_cmd, d.Title);
                _rows.Children.Add(SettingRow(d.Glyph, d.Title, d.Description, _cmd, d.Note));
                continue;
            }
            var toggle = new CheckBox().Styled("Pp.ToggleSwitch");
            System.Windows.Automation.AutomationProperties.SetName(toggle, d.Title);
            toggle.Checked += (_, _) => UpdateDirty();
            toggle.Unchecked += (_, _) => UpdateDirty();
            toggle.ToolTip = "Activé = restriction appliquée (fonction bloquée pour ce compte)";
            _toggles[d.Key] = toggle;
            var note = d.Note;
            if (d.EnterpriseOrEducationOnly && !SupportsStorePolicy(profile))
                note = d.Note + $" Votre édition ({profile.EditionLabel}) l'ignore.";
            _rows.Children.Add(SettingRow(d.Glyph, d.Title, d.Description, toggle, note));
        }
    }

    private static bool SupportsStorePolicy(SystemProfile p) =>
        p.Edition is EditionFamily.Enterprise or EditionFamily.Education or EditionFamily.IoTEnterprise;

    private StackPanel BuildAppEditor()
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var input = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 2, 0, 0) };
        var prompt = Caption("Programme (ex. jeu.exe)");
        prompt.VerticalAlignment = VerticalAlignment.Center;
        prompt.Margin = new Thickness(0, 0, 10, 0);
        input.Children.Add(prompt);
        _appInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddApp(_appInput.Text); e.Handled = true; } };
        System.Windows.Automation.AutomationProperties.SetName(_appInput, "Nom du programme à interdire");
        input.Children.Add(_appInput);
        var add = MakeButton("Ajouter", GlyphAdd, "Pp.Button", (_, _) => AddApp(_appInput.Text));
        add.Margin = new Thickness(8, 0, 0, 0);
        input.Children.Add(add);
        _appError.VerticalAlignment = VerticalAlignment.Center;
        _appError.Margin = new Thickness(10, 0, 0, 0);
        _appError.SetResourceReference(TextBlock.ForegroundProperty, "Pp.Danger");
        input.Children.Add(_appError);
        s.Children.Add(input);

        var suggest = new WrapPanel { Margin = new Thickness(24, 6, 0, 0) };
        var label = Caption("Suggestions :");
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(8, 0, 4, 0);
        suggest.Children.Add(label);
        foreach (var name in Suggestions)
        {
            var b = MakeButton(name, null, "Pp.SubtleButton", (_, _) => AddApp(name));
            b.ToolTip = "Ajouter " + name;
            suggest.Children.Add(b);
        }
        s.Children.Add(suggest);
        s.Children.Add(_apps);
        return s;
    }

    private void RenderApps()
    {
        _apps.Children.Clear();
        if (_appList.Count == 0)
        {
            var none = Caption("Aucun programme interdit.");
            none.Margin = new Thickness(0, 4, 0, 0);
            _apps.Children.Add(none);
            return;
        }
        foreach (var app in _appList)
        {
            var remove = new Button { ToolTip = "Retirer " + app, Padding = new Thickness(4, 0, 2, 0), Margin = new Thickness(4, 0, 0, 0) }.Styled("Pp.SubtleButton");
            remove.Content = Icon("", 10);
            System.Windows.Automation.AutomationProperties.SetName(remove, "Retirer " + app);
            var captured = app;
            remove.Click += (_, _) => { _appList.Remove(captured); RenderApps(); UpdateDirty(); };
            var chip = new StackPanel { Orientation = Orientation.Horizontal };
            var t = Text(app, "Pp.BadgeText");
            t.VerticalAlignment = VerticalAlignment.Center;
            chip.Children.Add(t);
            chip.Children.Add(remove);
            _apps.Children.Add(new Border { Child = chip, Margin = new Thickness(0, 4, 6, 0), Padding = new Thickness(10, 2, 2, 2) }
                .Styled("Pp.Badge").Themed(Border.BackgroundProperty, "Pp.NeutralBackground"));
        }
    }

    private void AddApp(string raw)
    {
        var name = raw.Trim().Trim('"');
        if (name.Length > 0 && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        if (!UserRestrictions.IsValidExeName(name)) { _appError.Text = "Nom invalide : indiquez seulement le fichier, par exemple jeu.exe."; return; }
        if (_appList.Count >= UserRestrictions.MaxBlockedApps) { _appError.Text = $"{UserRestrictions.MaxBlockedApps} programmes au maximum."; return; }
        _appError.Text = "";
        if (!_appList.Contains(name, StringComparer.OrdinalIgnoreCase)) _appList.Add(name);
        _appInput.Text = "";
        RenderApps();
        UpdateDirty();
    }

    // ------------------------------------------------------------------ Données

    public void Update(List<LocalAccount>? accounts, string? preferSid = null)
    {
        var previous = preferSid ?? (_account.SelectedItem as ComboBoxItem)?.Tag as string;
        _eligible = accounts?.Where(a => !a.IsAdmin && !a.IsCurrent && !a.IsBuiltIn && !a.IsSystemAccount).ToList() ?? [];
        _suppress = true;
        _account.Items.Clear();
        foreach (var a in _eligible)
            _account.Items.Add(new ComboBoxItem { Content = a.DisplayName == a.Name ? a.Name : $"{a.DisplayName} ({a.Name})", Tag = a.Sid });
        var index = _eligible.FindIndex(a => a.Sid == previous);
        _account.SelectedIndex = _eligible.Count == 0 ? -1 : Math.Max(0, index);
        _suppress = false;

        if (accounts is not null && _eligible.Count == 0)
        {
            _empty.Content = EmptyState(GlyphBlock, "Aucun compte standard à restreindre",
                "Les restrictions s'appliquent à un compte standard autre que le vôtre. Un administrateur pourrait les retirer lui-même.");
            _card.Visibility = Visibility.Collapsed;
            return;
        }
        _empty.Content = null;
        _card.Visibility = Visibility.Visible;
        if (Selected?.Sid != _loadedSid) _ = OnAccountChangedAsync();
    }

    public void Select(string sid)
    {
        var index = _eligible.FindIndex(a => a.Sid == sid);
        if (index >= 0) _account.SelectedIndex = index;
    }

    private LocalAccount? Selected => _account.SelectedIndex >= 0 && _account.SelectedIndex < _eligible.Count ? _eligible[_account.SelectedIndex] : null;

    private async Task OnAccountChangedAsync()
    {
        _loaded = null;
        _loadedSid = null;
        _appList = [];
        ApplyLoaded();
        SetEditable(false);
        if (Selected is null) return;
        if (AppHost.Broker.IsRunning)
        {
            await ReadAsync();
            return;
        }
        _status.Text = "";
        _banner.Content = PageScaffold.InfoBar(
            "Les restrictions sont stockées dans le profil de ce compte, que seul un administrateur peut lire. Cliquez sur « Lire " +
            "l'état actuel » pour les afficher et les modifier.", GlyphAdmin);
    }

    private async Task ReadAsync()
    {
        if (Selected is not { } a || _busy) return;
        SetBusy(true);
        _status.Text = "Lecture du profil…";
        try
        {
            var outcome = await UsersUi.RunAsync("users.restrictions.get", new() { ["sid"] = a.Sid }, toast: false);
            if (Selected?.Sid != a.Sid) return;
            if (!outcome.Success)
            {
                _status.Text = "";
                if (!outcome.Cancelled) AppHost.Toasts.ShowOutcome(outcome);
                return;
            }
            ShowData(a, outcome.Data);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowData(LocalAccount a, Dictionary<string, string>? data)
    {
        if (data is null || data.GetValueOrDefault("profile") == "missing")
        {
            _loaded = null;
            _loadedSid = a.Sid;
            _status.Text = "";
            _banner.Content = PageScaffold.InfoBar(UserRestrictions.ProfileMissingMessage, GlyphWarning, "Pp.InfoBar.Warning");
            SetEditable(false);
            return;
        }
        _loaded = data;
        _loadedSid = a.Sid;
        _banner.Content = null;
        var active = UserRestrictions.All.Count(d => d.Key != UserRestrictions.DisallowRunKey && data.GetValueOrDefault(d.Key, "0") != "0");
        var apps = UserRestrictions.ParseAppListSafe(data.GetValueOrDefault("DisallowRunList"));
        _status.Text = (active == 0 && apps.Count == 0 ? "Aucune restriction active" : $"{active + (apps.Count > 0 ? 1 : 0)} restriction(s) active(s)")
                       + (data.GetValueOrDefault("profile") == "loaded" ? " · session ouverte" : "");
        ApplyLoaded();
        SetEditable(true);
    }

    private void ApplyLoaded()
    {
        _suppress = true;
        foreach (var (key, toggle) in _toggles) toggle.IsChecked = _loaded?.GetValueOrDefault(key, "0") is { } v && v != "0";
        var cmd = _loaded?.GetValueOrDefault("DisableCMD", "0") ?? "0";
        _cmd.SelectedIndex = cmd switch { "2" => 1, "1" => 2, _ => 0 };
        _appList = UserRestrictions.ParseAppListSafe(_loaded?.GetValueOrDefault("DisallowRunList"));
        _appError.Text = "";
        RenderApps();
        _suppress = false;
        UpdateDirty();
    }

    private Dictionary<string, string> Current()
    {
        var d = new Dictionary<string, string>();
        foreach (var (key, toggle) in _toggles) d[key] = toggle.IsChecked == true ? "1" : "0";
        d["DisableCMD"] = (_cmd.SelectedItem as ComboBoxItem)?.Tag as string ?? "0";
        d["DisallowRunList"] = string.Join("|", _appList);
        return d;
    }

    private void UpdateDirty()
    {
        if (_suppress) return;
        var dirty = _loaded is not null && Current().Any(kv => _loaded.GetValueOrDefault(kv.Key, kv.Key == "DisallowRunList" ? "" : "0") != kv.Value);
        _save.IsEnabled = dirty && !_busy;
        _revert.IsEnabled = dirty && !_busy;
    }

    private void SetEditable(bool editable)
    {
        _rows.IsEnabled = editable && !_busy;
        _rows.Opacity = editable ? 1 : 0.6;
        // La stratégie Store n'est modifiable que là où Windows l'applique (ou pour la retirer si elle existe déjà).
        if (_toggles.TryGetValue("RemoveWindowsStore", out var store))
            store.IsEnabled = SupportsStorePolicy(AppHost.Profile) || store.IsChecked == true;
        UpdateDirty();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _busyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _account.IsEnabled = !busy;
        _read.IsEnabled = !busy;
        _rows.IsEnabled = !busy && _loaded is not null;
        UpdateDirty();
    }

    private async Task SaveAsync()
    {
        if (Selected is not { } a || _loaded is null || _busy) return;
        var p = Current();
        p["sid"] = a.Sid;
        SetBusy(true);
        _status.Text = "Enregistrement…";
        try
        {
            var outcome = await UsersUi.RunAsync("users.restrictions.set", p);
            if (outcome.Success && Selected?.Sid == a.Sid) ShowData(a, outcome.Data);
            else _status.Text = "";
        }
        finally
        {
            SetBusy(false);
        }
    }
}
