using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static PcPilot.Modules.Users.UsersUi;

namespace PcPilot.Modules.Users;

/// <summary>
/// Grille 7 × 24 (lundi → dimanche, heure locale). Cliquer ou faire glisser inverse les cases ; cliquer sur un jour ou une
/// heure inverse toute la ligne ou la colonne.
/// </summary>
internal sealed class HoursGrid : Grid
{
    private static readonly string[] Days = ["Lun", "Mar", "Mer", "Jeu", "Ven", "Sam", "Dim"];
    private readonly Border[] _cells = new Border[LogonHoursMap.Hours];
    private readonly bool[] _state = new bool[LogonHoursMap.Hours];
    private bool? _paint;

    public event Action? StateChanged;

    public HoursGrid()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        for (var h = 0; h < 24; h++) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 16 });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var r = 0; r < 7; r++) RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        Background = System.Windows.Media.Brushes.Transparent;
        Cursor = Cursors.Hand;

        for (var h = 0; h < 24; h++)
        {
            var label = Caption(h % 2 == 0 ? h.ToString(CultureInfo.InvariantCulture) : "");
            label.FontSize = 11;
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.Margin = new Thickness(2, 0, 0, 4);
            label.Tag = -100 - h;
            label.ToolTip = $"{h} h – {h + 1} h : inverser toute la colonne";
            SetColumn(label, h + 1);
            Children.Add(label);
        }
        for (var r = 0; r < 7; r++)
        {
            var day = Caption(Days[r]);
            day.VerticalAlignment = VerticalAlignment.Center;
            day.Tag = -10 - r;
            day.ToolTip = "Inverser toute la journée";
            SetRow(day, r + 1);
            Children.Add(day);
            for (var h = 0; h < 24; h++)
            {
                var index = IndexOf(r, h);
                var cell = new Border { CornerRadius = new CornerRadius(3), Margin = new Thickness(1.5), BorderThickness = new Thickness(1), Tag = index };
                cell.ToolTip = $"{FullDay(r)}, {h} h – {h + 1} h";
                SetRow(cell, r + 1);
                SetColumn(cell, h + 1);
                Children.Add(cell);
                _cells[index] = cell;
                Paint(index);
            }
        }
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += (_, _) => EndPaint();
        LostMouseCapture += (_, _) => _paint = null;
    }

    /// <summary>Index local (dimanche = 0, comme DayOfWeek) d'une ligne affichée (lundi = 0).</summary>
    private static int IndexOf(int row, int hour) => LogonHoursMap.LocalIndex((DayOfWeek)((row + 1) % 7), hour);

    private static string FullDay(int row) => CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetDayName((DayOfWeek)((row + 1) % 7));

    public bool[] State
    {
        get => (bool[])_state.Clone();
        set
        {
            Array.Copy(value, _state, LogonHoursMap.Hours);
            for (var i = 0; i < LogonHoursMap.Hours; i++) Paint(i);
            StateChanged?.Invoke();
        }
    }

    public int AllowedCount => _state.Count(b => b);

    private void Paint(int index)
    {
        var cell = _cells[index];
        if (cell is null) return;
        cell.SetResourceReference(Border.BackgroundProperty, _state[index] ? "Pp.Accent" : "Pp.ControlFill");
        cell.SetResourceReference(Border.BorderBrushProperty, _state[index] ? "Pp.Accent" : "Pp.ControlStroke");
    }

    private void Set(int index, bool value)
    {
        if (_state[index] == value) return;
        _state[index] = value;
        Paint(index);
        StateChanged?.Invoke();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled || e.OriginalSource is not FrameworkElement { Tag: int tag }) return;
        if (tag >= 0)
        {
            _paint = !_state[tag];
            Set(tag, _paint.Value);
            CaptureMouse();
        }
        else if (tag <= -100)
        {
            var h = -100 - tag;
            var target = !Enumerable.Range(0, 7).All(r => _state[IndexOf(r, h)]);
            for (var r = 0; r < 7; r++) Set(IndexOf(r, h), target);
        }
        else
        {
            var r = -10 - tag;
            var target = !Enumerable.Range(0, 24).All(h => _state[IndexOf(r, h)]);
            for (var h = 0; h < 24; h++) Set(IndexOf(r, h), target);
        }
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_paint is not { } value || e.LeftButton != MouseButtonState.Pressed) return;
        if (InputHitTest(e.GetPosition(this)) is FrameworkElement { Tag: int tag } && tag >= 0) Set(tag, value);
    }

    private void EndPaint()
    {
        _paint = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
}

/// <summary>Section « Plages horaires » : choix d'un compte standard, grille, préréglages, enregistrement (admin).</summary>
internal sealed class LogonHoursPanel : StackPanel
{
    private readonly ComboBox _account = new() { MinWidth = 240, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock _summary = Caption("");
    private readonly HoursGrid _grid = new();
    private readonly Button _save;
    private readonly Button _revert;
    private readonly ProgressBar _busyBar = BusyBar();
    private readonly StackPanel _editor = new();
    private readonly Border _card;
    private readonly ContentControl _empty = new();
    private readonly TextBlock _dirty = Caption("");
    private List<LocalAccount> _eligible = [];
    private bool[] _saved = new bool[LogonHoursMap.Hours];
    private bool _busy, _suppress;

    public event Action? Changed;
    public event Action? CreateRequested;
    public TextBlock Heading { get; }

    public LogonHoursPanel()
    {
        Children.Add(SectionHeader("Plages horaires de connexion", out var heading));
        Heading = heading;
        Children.Add(_empty);

        _account.SelectionChanged += (_, _) => { if (!_suppress) LoadSelected(); };
        System.Windows.Automation.AutomationProperties.SetName(_account, "Compte");
        _grid.StateChanged += UpdateSummary;

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var pick = new StackPanel { Orientation = Orientation.Horizontal };
        var who = Caption("Compte");
        who.VerticalAlignment = VerticalAlignment.Center;
        who.Margin = new Thickness(0, 0, 10, 0);
        pick.Children.Add(who);
        pick.Children.Add(_account);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        pick.Children.Add(_summary);
        top.Children.Add(pick);
        _editor.Children.Add(top);

        var presets = new WrapPanel { Margin = new Thickness(-8, 0, 0, 10) };
        void Preset(string text, string tip, Func<int, int, bool> rule)
        {
            var b = MakeButton(text, null, "Pp.SubtleButton", (_, _) => ApplyPreset(rule));
            b.ToolTip = tip;
            b.Margin = new Thickness(0, 0, 4, 0);
            presets.Children.Add(b);
        }
        var label = Caption("Préréglages :");
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(8, 0, 4, 0);
        presets.Children.Add(label);
        Preset("Soirs de semaine", "Lundi au vendredi de 17 h à 20 h, samedi et dimanche de 9 h à 20 h",
            (dow, h) => dow is >= 1 and <= 5 ? h is >= 17 and < 20 : h is >= 9 and < 20);
        Preset("Journée", "Tous les jours de 8 h à 20 h", (_, h) => h is >= 8 and < 20);
        Preset("Tout autoriser", "Aucune restriction horaire", (_, _) => true);
        Preset("Tout effacer", "Point de départ pour dessiner vos propres plages", (_, _) => false);
        _editor.Children.Add(presets);

        _editor.Children.Add(_grid);

        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(44, 10, 0, 0) };
        legend.Children.Add(Swatch("Pp.Accent", "Pp.Accent"));
        legend.Children.Add(LegendText("Connexion autorisée"));
        legend.Children.Add(Swatch("Pp.ControlFill", "Pp.ControlStroke"));
        legend.Children.Add(LegendText("Bloquée"));
        var how = Caption("Cliquez ou faites glisser pour modifier ; cliquez sur un jour ou une heure pour toute la ligne.");
        how.VerticalAlignment = VerticalAlignment.Center;
        how.Margin = new Thickness(8, 0, 0, 0);
        legend.Children.Add(how);
        _editor.Children.Add(legend);

        _save = MakeButton("Enregistrer", GlyphAdmin, "Pp.AccentButton", async (_, _) => await SaveAsync());
        _save.ToolTip = "Nécessite les droits administrateur";
        _revert = MakeButton("Annuler les modifications", null, "Pp.SubtleButton", (_, _) => { _grid.State = _saved; });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(_save);
        _revert.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(_revert);
        actions.Children.Add(_busyBar);
        _dirty.VerticalAlignment = VerticalAlignment.Center;
        _dirty.Margin = new Thickness(12, 0, 0, 0);
        actions.Children.Add(_dirty);
        _editor.Children.Add(actions);

        _card = Card(_editor, new Thickness(18, 14, 18, 14));
        Children.Add(_card);
        Children.Add(BuildNotes());
        UpdateSummary();
    }

    private static Border Swatch(string fill, string stroke) =>
        new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }
            .Themed(Border.BackgroundProperty, fill).Themed(Border.BorderBrushProperty, stroke);

    private static TextBlock LegendText(string text)
    {
        var t = Caption(text);
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Margin = new Thickness(0, 0, 16, 0);
        return t;
    }

    private static StackPanel BuildNotes()
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        var utc = "UTC" + (offset >= TimeSpan.Zero ? "+" : "−") + offset.ToString(offset.Minutes == 0 ? "%h" : @"h\:mm", CultureInfo.InvariantCulture);
        var s = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        s.Children.Add(PcPilot.UI.Controls.PageScaffold.InfoBar(
            "Windows refuse toute nouvelle ouverture de session en dehors des plages autorisées, mais ne ferme pas une session déjà " +
            "ouverte quand l'heure limite arrive. Pour un encadrement strict, combinez avec l'accès guidé, le temps d'écran de " +
            "Famille Microsoft ou votre propre supervision.", GlyphInfo));
        s.Children.Add(PcPilot.UI.Controls.PageScaffold.InfoBar(
            $"Windows enregistre ces plages en heure universelle : PC Pilot les convertit depuis votre fuseau actuel ({utc}). " +
            "Au passage à l'heure d'été ou d'hiver, elles se décalent d'une heure : revenez les enregistrer à nouveau." +
            (LogonHoursMap.OffsetHasMinutes() ? " Votre fuseau a un décalage non entier : les plages sont arrondies à l'heure." : ""),
            GlyphClock));
        return s;
    }

    // ------------------------------------------------------------------ Données

    public void Update(List<LocalAccount>? accounts, string? preferSid = null)
    {
        var previous = preferSid ?? (_account.SelectedItem as ComboBoxItem)?.Tag as string;
        _eligible = accounts?.Where(a => !a.IsAdmin && !a.IsCurrent && !a.IsBuiltIn && !a.IsSystemAccount).ToList() ?? [];
        _suppress = true;
        _account.Items.Clear();
        foreach (var a in _eligible)
        {
            var label = a.DisplayName == a.Name ? a.Name : $"{a.DisplayName} ({a.Name})";
            if (!a.Enabled) label += " — désactivé";
            _account.Items.Add(new ComboBoxItem { Content = label, Tag = a.Sid });
        }
        var index = _eligible.FindIndex(a => a.Sid == previous);
        _account.SelectedIndex = _eligible.Count == 0 ? -1 : Math.Max(0, index);
        _suppress = false;

        if (accounts is null)
        {
            _empty.Content = null;
            _editor.IsEnabled = false;
            return;
        }
        if (_eligible.Count == 0)
        {
            _empty.Content = EmptyState(GlyphClock, "Aucun compte standard à encadrer",
                "Les plages horaires s'appliquent aux comptes standard (par exemple celui d'un enfant), jamais à votre propre compte " +
                "ni à un administrateur, qui pourrait les retirer lui-même.",
                MakeButton("Créer un compte standard", GlyphAdd, "Pp.AccentButton", (_, _) => CreateRequested?.Invoke()));
            _card.Visibility = Visibility.Collapsed;
            return;
        }
        _empty.Content = null;
        _card.Visibility = Visibility.Visible;
        _editor.IsEnabled = true;
        LoadSelected();
    }

    public void Select(string sid)
    {
        var index = _eligible.FindIndex(a => a.Sid == sid);
        if (index >= 0) _account.SelectedIndex = index;
    }

    private LocalAccount? Selected => _account.SelectedIndex >= 0 && _account.SelectedIndex < _eligible.Count ? _eligible[_account.SelectedIndex] : null;

    private void LoadSelected()
    {
        if (Selected is not { } a) return;
        _saved = LogonHoursMap.FromUtcBitmap(a.LogonHours, LogonHoursMap.CurrentOffsetHours());
        _grid.State = _saved;
    }

    private void ApplyPreset(Func<int, int, bool> rule)
    {
        var state = new bool[LogonHoursMap.Hours];
        for (var dow = 0; dow < 7; dow++)
            for (var h = 0; h < 24; h++) state[dow * 24 + h] = rule(dow, h);
        _grid.State = state;
    }

    private void UpdateSummary()
    {
        var allowed = _grid.AllowedCount;
        _summary.Text = allowed switch
        {
            LogonHoursMap.Hours => "Aucune restriction : connexion possible à toute heure",
            0 => "Aucune heure autorisée : le compte ne pourra plus se connecter",
            _ => $"{allowed} h autorisées par semaine",
        };
        var dirty = !_grid.State.SequenceEqual(_saved);
        _dirty.Text = dirty ? "Modifications non enregistrées" : "";
        _save.IsEnabled = dirty && !_busy && Selected is not null;
        _revert.IsEnabled = dirty && !_busy;
    }

    private async Task SaveAsync()
    {
        if (Selected is not { } a || _busy) return;
        var state = _grid.State;
        if (state.All(b => !b) && !await AppHost.Dialogs.ConfirmAsync("Bloquer toutes les heures",
                $"« {a.Name} » ne pourra plus ouvrir de session du tout, à aucun moment. Continuer ?", "Tout bloquer", danger: true))
            return;
        var bitmap = LogonHoursMap.ToUtcBitmap(state, LogonHoursMap.CurrentOffsetHours());
        _busy = true;
        _busyBar.Visibility = Visibility.Visible;
        _account.IsEnabled = false;
        _grid.IsEnabled = false;
        UpdateSummary();
        try
        {
            var outcome = await UsersUi.RunAsync("users.logonhours.set", new() { ["sid"] = a.Sid, ["hours"] = LogonHoursMap.ToHex(bitmap) });
            if (outcome.Success)
            {
                _saved = state;
                Changed?.Invoke();
            }
        }
        finally
        {
            _busy = false;
            _busyBar.Visibility = Visibility.Collapsed;
            _account.IsEnabled = true;
            _grid.IsEnabled = true;
            UpdateSummary();
        }
    }
}
