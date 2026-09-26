using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Timonier.Core.Localization;
using static Timonier.Modules.Users.UsersUi;

namespace Timonier.Modules.Users;

/// <summary>
/// Grille 7 × 24 (lundi → dimanche, heure locale). Cliquer ou faire glisser inverse les cases ; cliquer sur un jour ou une
/// heure inverse toute la ligne ou la colonne.
/// </summary>
internal sealed class HoursGrid : Grid
{
    /// <summary>Jour abrégé de la ligne affichée (lundi = 0) dans la culture de l'interface (« Lun », « Mon »…).</summary>
    private static string ShortDay(int row)
    {
        var name = Loc.Culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((row + 1) % 7)).TrimEnd('.');
        return name.Length > 0 ? char.ToUpper(name[0], Loc.Culture) + name[1..] : name;
    }
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
            label.ToolTip = L("{0}:00 – {1}:00: invert the whole column", h, h + 1);
            SetColumn(label, h + 1);
            Children.Add(label);
        }
        for (var r = 0; r < 7; r++)
        {
            var day = Caption(ShortDay(r));
            day.VerticalAlignment = VerticalAlignment.Center;
            day.Tag = -10 - r;
            day.ToolTip = L("Invert the whole day");
            SetRow(day, r + 1);
            Children.Add(day);
            for (var h = 0; h < 24; h++)
            {
                var index = IndexOf(r, h);
                var cell = new Border { CornerRadius = new CornerRadius(3), Margin = new Thickness(1.5), BorderThickness = new Thickness(1), Tag = index };
                cell.ToolTip = L("{0}, {1}:00 – {2}:00", FullDay(r), h, h + 1);
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

    private static string FullDay(int row) => Loc.Culture.DateTimeFormat.GetDayName((DayOfWeek)((row + 1) % 7));

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
        Children.Add(SectionHeader(L("Sign-in hours"), out var heading));
        Heading = heading;
        Children.Add(_empty);

        _account.SelectionChanged += (_, _) => { if (!_suppress) LoadSelected(); };
        System.Windows.Automation.AutomationProperties.SetName(_account, L("Account"));
        _grid.StateChanged += UpdateSummary;

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var pick = new StackPanel { Orientation = Orientation.Horizontal };
        var who = Caption(L("Account"));
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
        var label = Caption(L("Presets:"));
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(8, 0, 4, 0);
        presets.Children.Add(label);
        Preset(L("Weekday evenings"), L("Monday to Friday from 5 PM to 8 PM, Saturday and Sunday from 9 AM to 8 PM"),
            (dow, h) => dow is >= 1 and <= 5 ? h is >= 17 and < 20 : h is >= 9 and < 20);
        Preset(L("Daytime"), L("Every day from 8 AM to 8 PM"), (_, h) => h is >= 8 and < 20);
        Preset(L("Allow all"), L("No time restrictions"), (_, _) => true);
        Preset(L("Clear all"), L("Starting point for drawing your own time slots"), (_, _) => false);
        _editor.Children.Add(presets);

        _editor.Children.Add(_grid);

        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(44, 10, 0, 0) };
        legend.Children.Add(Swatch("Pp.Accent", "Pp.Accent"));
        legend.Children.Add(LegendText(L("Sign-in allowed")));
        legend.Children.Add(Swatch("Pp.ControlFill", "Pp.ControlStroke"));
        legend.Children.Add(LegendText(LC("feminine", "Blocked")));
        var how = Caption(L("Click or drag to change; click a day or an hour for the whole row."));
        how.VerticalAlignment = VerticalAlignment.Center;
        how.Margin = new Thickness(8, 0, 0, 0);
        legend.Children.Add(how);
        _editor.Children.Add(legend);

        _save = MakeButton(L("Save"), GlyphAdmin, "Pp.AccentButton", async (_, _) => await SaveAsync());
        _save.ToolTip = L("Requires administrator rights");
        _revert = MakeButton(L("Discard changes"), null, "Pp.SubtleButton", (_, _) => { _grid.State = _saved; });
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
        s.Children.Add(Timonier.UI.Controls.PageScaffold.InfoBar(
            L("Windows refuses any new sign-in outside the allowed time slots, but doesn't close a session that's already open when the time limit arrives. For strict supervision, combine this with guided access, Microsoft Family Safety screen time or your own supervision."), GlyphInfo));
        s.Children.Add(Timonier.UI.Controls.PageScaffold.InfoBar(
            L("Windows stores these time slots in Coordinated Universal Time: Timonier converts them from your current time zone ({0}). When daylight saving time starts or ends, they shift by one hour: come back and save them again.", utc)
            + (LogonHoursMap.OffsetHasMinutes() ? L(" Your time zone has a non-whole-hour offset: time slots are rounded to the hour.") : ""),
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
            if (!a.Enabled) label = L("{0} — disabled", label);
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
            _empty.Content = EmptyState(GlyphClock, L("No standard account to supervise"),
                L("Time slots apply to standard accounts (for example a child's), never to your own account or to an administrator, who could remove them themselves."),
                MakeButton(L("Create a standard account"), GlyphAdd, "Pp.AccentButton", (_, _) => CreateRequested?.Invoke()));
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
            LogonHoursMap.Hours => L("No restrictions: sign-in allowed at any time"),
            0 => L("No hours allowed: the account will no longer be able to sign in"),
            _ => LP(allowed, "{0} hour allowed per week", "{0} hours allowed per week"),
        };
        var dirty = !_grid.State.SequenceEqual(_saved);
        _dirty.Text = dirty ? L("Unsaved changes") : "";
        _save.IsEnabled = dirty && !_busy && Selected is not null;
        _revert.IsEnabled = dirty && !_busy;
    }

    private async Task SaveAsync()
    {
        if (Selected is not { } a || _busy) return;
        var state = _grid.State;
        if (state.All(b => !b) && !await AppHost.Dialogs.ConfirmAsync(L("Block all hours"),
                L("“{0}” will no longer be able to sign in at all, at any time. Continue?", a.Name), L("Block all"), danger: true))
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
